using System;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Deceive;

internal class ProxiedConnection
{
    private MainController MainController { get; set; }

    private SslStream Incoming { get; set; } = null!;
    private SslStream Outgoing { get; set; } = null!;
    private bool Connected { get; set; } = true;
    private string LastPresence { get; set; } = null!; // we resend this if the state changes
    private bool InsertedFakePlayer { get; set; } = false;
    private bool SentFakePlayerPresence { get; set; } = false;
    private string? ValorantVersion { get; set; } = null;
    private MemoryStream? RosterBuffer { get; set; } = null;
    private string PresenceBuffer { get; set; } = "";

    internal event EventHandler? ConnectionErrored;

    internal ProxiedConnection(MainController main, SslStream incoming, SslStream outgoing)
    {
        MainController = main;
        Incoming = incoming;
        Outgoing = outgoing;
    }

    public void Start()
    {
        Task.Run(IncomingLoopAsync);
        Task.Run(OutgoingLoopAsync);
    }

    private async Task IncomingLoopAsync()
    {
        try
        {
            int byteCount;
            var bytes = new byte[8192 * 2];

            do
            {
                byteCount = await Incoming.ReadAsync(bytes, 0, bytes.Length);

                var content = Encoding.UTF8.GetString(bytes, 0, byteCount);

                // If this is possibly a presence stanza, rewrite it.
                if (content.Contains("<presence") && MainController.Enabled)
                {
                    Trace.WriteLine("<!--RC TO SERVER ORIGINAL-->" + content);
                    await PossiblyRewriteAndResendPresenceAsync(content, MainController.Status);
                }
                else if (content.Contains("41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net"))
                {
                    await MainController.HandleChatMessage(content);

                    //Don't send anything involving our fake user to chat servers
                    Trace.WriteLine("<!--RC TO SERVER REMOVED-->" + content);
                }
                else
                {
                    await Outgoing.WriteAsync(bytes, 0, byteCount);
                    // don't log anything that contains a JWT
                    if (!content.Contains("token>")) Trace.WriteLine("<!--RC TO SERVER-->" + content);
                }

                if (InsertedFakePlayer && !SentFakePlayerPresence)
                    await SendFakePlayerPresenceAsync();
            } while (byteCount != 0 && Connected);
        }
        catch (Exception e)
        {
            Trace.WriteLine("Incoming errored.");
            Trace.WriteLine(e);
        }
        finally
        {
            Trace.WriteLine("Incoming closed.");
            OnConnectionErrored();
        }
    }

    private async Task OutgoingLoopAsync()
    {
        try
        {
            int byteCount;
            var bytes = new byte[8192 * 2];

            do
            {
                byteCount = await Outgoing.ReadAsync(bytes, 0, bytes.Length);
                var content = Encoding.UTF8.GetString(bytes, 0, byteCount);

                // Insert fake player into roster
                const string roster = "<query xmlns='jabber:iq:riotgames:roster'>";

                // Capture the roster for friend tracking. It can span multiple reads, so we buffer
                // the raw bytes (decoding per-chunk would corrupt multi-byte characters split across
                // a read boundary) until the closing </query> arrives, then decode it all at once.
                if (RosterBuffer is null && content.Contains(roster))
                    RosterBuffer = new MemoryStream();
                if (RosterBuffer is not null)
                {
                    RosterBuffer.Write(bytes, 0, byteCount);
                    var buffered = Encoding.UTF8.GetString(RosterBuffer.ToArray());
                    var openIndex = buffered.IndexOf(roster, StringComparison.Ordinal);
                    var closed = openIndex >= 0 && buffered.IndexOf("</query>", openIndex, StringComparison.Ordinal) >= 0;
                    if (closed || RosterBuffer.Length > 8 * 1024 * 1024)
                    {
                        if (closed)
                            MainController.HandleRosterContent(buffered);
                        RosterBuffer.Dispose();
                        RosterBuffer = null;
                    }
                }

                if (!InsertedFakePlayer && content.Contains(roster))
                {
                    InsertedFakePlayer = true;
                    Trace.WriteLine("<!--SERVER TO RC ORIGINAL-->" + content);
                    content = content.Insert(content.IndexOf(roster, StringComparison.Ordinal) + roster.Length,
                        "<item jid='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' name='&#9;Deceive Active!' subscription='both' puuid='41c322a1-b328-495b-a004-5ccd3e45eae8'>" +
                        "<group priority='9999'>Deceive</group>" +
                        "<state>online</state>" +
                        "<id name='&#9;Deceive Active!' tagline='...'/>" +
                        "<lol name='&#9;Deceive Active!'/>" +
                        "<platforms><riot name='&#9;Deceive Active' tagline='...'/></platforms>" +
                        "</item>");
                    var contentBytes = Encoding.UTF8.GetBytes(content);
                    await Incoming.WriteAsync(contentBytes, 0, contentBytes.Length);
                    Trace.WriteLine("<!--DECEIVE TO RC-->" + content);
                }
                else
                {
                    await Incoming.WriteAsync(bytes, 0, byteCount);
                    Trace.WriteLine("<!--SERVER TO RC-->" + content);
                }

                // Observe friend presences (for status change notifications) without altering the
                // forwarded data. Presences can span multiple reads (especially the burst at login),
                // so they are reassembled into complete stanzas before being parsed.
                if (content.Contains("<presence") || PresenceBuffer.Length > 0)
                    ObserveFriendPresence(content);
            } while (byteCount != 0 && Connected);
        }
        catch (Exception e)
        {
            Trace.WriteLine("Outgoing errored.");
            Trace.WriteLine(e);
        }
        finally
        {
            Trace.WriteLine("Outgoing closed.");
            OnConnectionErrored();
        }
    }

    // Reassembles the (possibly fragmented) server-to-client stream into complete <presence>
    // stanzas and hands each one to the controller. Any trailing partial stanza is kept in the
    // buffer for the next read.
    private void ObserveFriendPresence(string content)
    {
        PresenceBuffer += content;
        if (PresenceBuffer.Length > 4 * 1024 * 1024)
        {
            // Runaway guard: a presence stanza should never be this large.
            PresenceBuffer = "";
            return;
        }

        var searchStart = 0;
        while (true)
        {
            var open = PresenceBuffer.IndexOf("<presence", searchStart, StringComparison.Ordinal);
            if (open < 0)
            {
                // Keep a trailing fragment only if it could be the start of "<presence".
                var lt = PresenceBuffer.LastIndexOf('<');
                var tail = lt >= 0 ? PresenceBuffer.Substring(lt) : "";
                PresenceBuffer = "<presence".StartsWith(tail, StringComparison.Ordinal) ? tail : "";
                return;
            }

            var tagEnd = PresenceBuffer.IndexOf('>', open);
            if (tagEnd < 0)
            {
                PresenceBuffer = PresenceBuffer.Substring(open); // incomplete start tag
                return;
            }

            int stanzaEnd;
            if (PresenceBuffer[tagEnd - 1] == '/')
            {
                stanzaEnd = tagEnd + 1; // self-closing <presence .../>
            }
            else
            {
                const string closeTag = "</presence>";
                var close = PresenceBuffer.IndexOf(closeTag, tagEnd, StringComparison.Ordinal);
                if (close < 0)
                {
                    PresenceBuffer = PresenceBuffer.Substring(open); // stanza not complete yet
                    return;
                }

                stanzaEnd = close + closeTag.Length;
            }

            var stanza = PresenceBuffer.Substring(open, stanzaEnd - open);
            MainController.HandleFriendPresenceContent(stanza);
            searchStart = stanzaEnd;
        }
    }

    private async Task PossiblyRewriteAndResendPresenceAsync(string content, string targetStatus)
    {
        try
        {
            LastPresence = content;
            var wrappedContent = "<xml>" + content + "</xml>";
            var xml = XDocument.Load(new StringReader(wrappedContent));

            if (xml.Root is null)
                return;
            if (xml.Root.HasElements is false)
                return;

            foreach (var presence in xml.Root.Elements())
            {
                if (presence.Name != "presence")
                    continue;
                if (presence.Attribute("to") is not null)
                {
                    if (MainController.ConnectToMuc)
                        continue;
                    presence.Remove();
                }

                if (targetStatus != "chat" ||
                    presence.Element("games")?.Element("league_of_legends")?.Element("st")?.Value != "dnd")
                {
                    presence.Element("show")?.ReplaceNodes(targetStatus);
                    presence.Element("games")?.Element("league_of_legends")?.Element("st")?.ReplaceNodes(targetStatus);
                }

                if (targetStatus == "chat")
                    continue;
                presence.Element("status")?.Remove();

                if (targetStatus == "mobile")
                {
                    presence.Element("games")?.Element("league_of_legends")?.Element("p")?.Remove();
                    presence.Element("games")?.Element("league_of_legends")?.Element("m")?.Remove();
                }
                else
                {
                    presence.Element("games")?.Element("league_of_legends")?.Remove();
                }

                // Remove Legends of Runeterra presence
                presence.Element("games")?.Element("bacon")?.Remove();

                // Remove 2XKO presence
                presence.Element("games")?.Element("lion")?.Remove();

                // Remove Riot Client presence
                presence.Element("games")?.Element("keystone")?.Remove();
                presence.Element("games")?.Element("riot_client")?.Remove();

                // Extracts current VALORANT from the user's own presence, so that we can show a fake
                // player with the proper version and avoid "Version Mismatch" from being shown.
                //
                // This isn't technically necessary, but people keep coming in and asking whether
                // the scary red text means Deceive doesn't work, so might as well do this and
                // get a slightly better user experience.
                if (ValorantVersion is null)
                {
                    var valorantBase64 = presence.Element("games")?.Element("valorant")?.Element("p")?.Value;
                    if (!string.IsNullOrWhiteSpace(valorantBase64))
                    {
                        var valorantPresence = Encoding.UTF8.GetString(Convert.FromBase64String(valorantBase64));
                        var valorantJson = JsonSerializer.Deserialize<JsonNode>(valorantPresence);
                        ValorantVersion = valorantJson?["partyPresenceData"]?["partyClientVersion"]?.GetValue<string>();
                        Trace.WriteLine("Found VALORANT version: " + ValorantVersion);
                        // only resend
                        if (InsertedFakePlayer && ValorantVersion is not null)
                            await SendFakePlayerPresenceAsync();
                    }
                }

                // Remove VALORANT presence
                presence.Element("games")?.Element("valorant")?.Remove();
            }

            var sb = new StringBuilder();
            var xws = new XmlWriterSettings
            {
                OmitXmlDeclaration = true, Encoding = Encoding.UTF8, ConformanceLevel = ConformanceLevel.Fragment,
                Async = true
            };
            using (var xw = XmlWriter.Create(sb, xws))
            {
                foreach (var xElement in xml.Root.Elements())
                    xElement.WriteTo(xw);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            await Outgoing.WriteAsync(bytes, 0, bytes.Length);
            Trace.WriteLine("<!--DECEIVE TO SERVER-->" + sb);
        }
        catch (Exception e)
        {
            Trace.WriteLine(e);
            Trace.WriteLine("Error rewriting presence.");
        }
    }

    private async Task SendFakePlayerPresenceAsync()
    {
        SentFakePlayerPresence = true;
        // VALORANT requires a recent version to not display "Version Mismatch"
        var valorantPresence = Convert.ToBase64String(
            Encoding.UTF8.GetBytes("""
                                              {
                                               "isValid": true,
                                               "isIdle": false,
                                               "queueId": "competitive",
                                               "provisioningFlow": "Invalid",
                                               "partyId": "00000000-0000-0000-0000-000000000000",
                                               "partySize": 1,
                                               "maxPartySize": 5,
                                               "partyOwnerMatchScoreAllyTeam": 0,
                                               "partyOwnerMatchScoreEnemyTeam": 0,
                                               "premierPresenceData":
                                               {
                                                   "rosterId": "",
                                                   "rosterName": "Deceive is active. Ignore any version mismatch warnings.",
                                                   "rosterTag": "Deceive Active!",
                                                   "rosterType": "VCT",
                                                   "division": 0,
                                                   "score": 0,
                                                   "plating": 0,
                                                   "showAura": false,
                                                   "showTag": true,
                                                   "showPlating": false
                                               },
                                               "matchPresenceData":
                                               {
                                                   "sessionLoopState": "MENUS",
                                                   "provisioningFlow": "Invalid",
                                                   "matchMap": "",
                                                   "queueId": "competitive"
                                               },
                                               "partyPresenceData":
                                               {
                                                   "partyId": "00000000-0000-0000-0000-000000000000",
                                                   "isPartyOwner": true,
                                                   "partyState": "DEFAULT",
                                                   "partyAccessibility": "CLOSED",
                                                   "partyLFM": false,
                                                   "partyClientVersion": "{VERSION}",
                                                   "partyVersion": 1768830115681,
                                                   "partySize": 1,
                                                   "queueEntryTime": "0001.01.01-00.00.00",
                                                   "isPartyCrossPlayEnabled": false,
                                                   "isPlayerCrossPlayEnabled": false,
                                                   "partyPrecisePlatformTypes": 1,
                                                   "customGameName": "Deceive Active!",
                                                   "customGameTeam": "",
                                                   "maxPartySize": 5,
                                                   "tournamentId": "",
                                                   "rosterId": "",
                                                   "partyOwnerSessionLoopState": "MENUS",
                                                   "partyOwnerMatchMap": "",
                                                   "partyOwnerProvisioningFlow": "Invalid",
                                                   "partyOwnerMatchScoreAllyTeam": 0,
                                                   "partyOwnerMatchScoreEnemyTeam": 0
                                               },
                                               "playerPresenceData":
                                               {
                                                   "playerCardId": "893deca1-4123-9c1f-2985-aa9de74cb512",
                                                   "playerTitleId": "e3ca05a4-4e44-9afe-3791-7d96ca8f71fa",
                                                   "accountLevel": 999,
                                                   "competitiveTier": 0,
                                                   "leaderboardPosition": 0
                                               }
                                              }
                                   """.Replace("{VERSION}", ValorantVersion ?? "unknown"))
        );

        var randomStanzaId = Guid.NewGuid();
        var unixTimeMilliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        var presenceMessage =
            $"<presence from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Deceive' id='b-{randomStanzaId}'>" +
            "<games>" +
            $"<keystone><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>keystone</s.p><pty/></keystone>" +
            $"<league_of_legends><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>league_of_legends</s.p><s.c>live</s.c><p>{{&quot;pty&quot;:true}}</p></league_of_legends>" + // No Region s.r keeps it in the main "League" category rather than "Other Servers" in every region with "Group Games & Servers" active
            $"<valorant><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>valorant</s.p><s.r>PC</s.r><p>{valorantPresence}</p><pty/></valorant>" +
            $"<bacon><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.l>bacon_availability_online</s.l><s.p>bacon</s.p></bacon>" +
            "</games>" +
            "<show>chat</show>" +
            "<platform>riot</platform>" +
            "<status/>" +
            "</presence>";

        var bytes = Encoding.UTF8.GetBytes(presenceMessage);
        await Incoming.WriteAsync(bytes, 0, bytes.Length);
        Trace.WriteLine("<!--DECEIVE TO RC-->" + presenceMessage);
    }

    public async Task SendMessageFromFakePlayerAsync(string message)
    {
        if (!InsertedFakePlayer || !Connected)
            return;

        var stamp = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss.fff");

        var chatMessage =
            $"<message from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Deceive' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>{message}</body></message>";

        var bytes = Encoding.UTF8.GetBytes(chatMessage);
        await Incoming.WriteAsync(bytes, 0, bytes.Length);
        Trace.WriteLine("<!--DECEIVE TO RC-->" + chatMessage);
    }

    public async Task UpdateStatusAsync(string newStatus)
    {
        if (string.IsNullOrEmpty(LastPresence) || !Connected)
            return;

        await PossiblyRewriteAndResendPresenceAsync(LastPresence, newStatus);
    }

    private void OnConnectionErrored()
    {
        Connected = false;
        ConnectionErrored?.Invoke(this, EventArgs.Empty);
    }
}