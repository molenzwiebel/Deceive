using System;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Text;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Deceive;

internal class ProxiedConnection
{
    private MainController MainController { get; set; }

    private const string RosterQuery = "<query xmlns='jabber:iq:riotgames:roster'>";
    private static ReadOnlySpan<byte> RosterQueryUtf8 => "<query xmlns='jabber:iq:riotgames:roster'>"u8;
    private static ReadOnlySpan<byte> FakePlayerJidUtf8 => "41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net"u8;

    private const string FakePlayerRosterItem =
        "<item jid='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' name='&#9;Deceive Active!' subscription='both' puuid='41c322a1-b328-495b-a004-5ccd3e45eae8'>" +
        "<group priority='9999'>Deceive</group>" +
        "<state>online</state>" +
        "<id name='&#9;Deceive Active!' tagline='...'/>" +
        "<lol name='&#9;Deceive Active!'/>" +
        "<platforms><riot name='&#9;Deceive Active' tagline='...'/></platforms>" +
        "</item>";

    private SslStream Incoming { get; set; } = null!;
    private SslStream Outgoing { get; set; } = null!;
    private SemaphoreSlim IncomingWriteLock { get; } = new(1, 1);
    private SemaphoreSlim OutgoingWriteLock { get; } = new(1, 1);
    private volatile bool _connected = true;
    private int _closed;
    private bool Connected => _connected;
    private string LastPresence { get; set; } = null!; // we resend this if the state changes
    private bool InsertedFakePlayer { get; set; } = false;
    private bool SentFakePlayerPresence { get; set; } = false;
    private string? ValorantVersion { get; set; } = null;

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

    // RC -> server. Rewrites our presence and intercepts messages to the fake Deceive player.
    private async Task IncomingLoopAsync()
    {
        try
        {
            var reader = new XmppStanzaReader(Incoming);
            var passthrough = new PassthroughBatch();
            while (Connected && await reader.FillAsync())
            {
                while (reader.TryReadUnit(out var unit))
                {
                    // If this is a presence stanza, rewrite it.
                    if (unit.IsElement("presence"u8) && MainController.Enabled)
                    {
                        await FlushToServerAsync(passthrough);
                        var content = Encoding.UTF8.GetString(unit.Span);
                        TrafficLog.Write("<!--RC TO SERVER ORIGINAL-->", content);
                        await PossiblyRewriteAndResendPresenceAsync(content, MainController.Status);
                    }
                    else if (unit.Contains(FakePlayerJidUtf8))
                    {
                        // Don't send anything involving our fake user to chat servers.
                        await FlushToServerAsync(passthrough);
                        var content = Encoding.UTF8.GetString(unit.Span);
                        TrafficLog.Write("<!--RC TO SERVER REMOVED-->", content);
                        await MainController.HandleChatMessage(content);
                    }
                    else
                    {
                        passthrough.Add(unit);
                    }
                }

                // Must be flushed before the next FillAsync, which reuses the reader's buffer.
                await FlushToServerAsync(passthrough);

                if (InsertedFakePlayer && !SentFakePlayerPresence)
                    await SendFakePlayerPresenceAsync();
            }
        }
        catch (Exception e)
        {
            if (Connected)
            {
                Trace.WriteLine("Incoming errored.");
                Trace.WriteLine(e);
            }
        }
        finally
        {
            Trace.WriteLine("Incoming closed.");
            OnConnectionErrored();
        }
    }

    // Server -> RC. Inserts the fake Deceive player into the roster.
    private async Task OutgoingLoopAsync()
    {
        try
        {
            var reader = new XmppStanzaReader(Outgoing);
            var passthrough = new PassthroughBatch();
            while (Connected && await reader.FillAsync())
            {
                while (reader.TryReadUnit(out var unit))
                {
                    if (!InsertedFakePlayer && unit.Contains(RosterQueryUtf8))
                    {
                        await FlushToClientAsync(passthrough);
                        InsertedFakePlayer = true;

                        // The unit is a complete stanza, so decoding it can never split a multibyte character.
                        var content = Encoding.UTF8.GetString(unit.Span);
                        TrafficLog.Write("<!--SERVER TO RC ORIGINAL-->", content);
                        content = content.Insert(content.IndexOf(RosterQuery, StringComparison.Ordinal) + RosterQuery.Length, FakePlayerRosterItem);
                        await WriteToClientAsync(Encoding.UTF8.GetBytes(content));
                        TrafficLog.Write("<!--DECEIVE TO RC-->", content);
                    }
                    else
                    {
                        passthrough.Add(unit);
                    }
                }

                // Must be flushed before the next FillAsync, which reuses the reader's buffer.
                await FlushToClientAsync(passthrough);
            }
        }
        catch (Exception e)
        {
            if (Connected)
            {
                Trace.WriteLine("Outgoing errored.");
                Trace.WriteLine(e);
            }
        }
        finally
        {
            Trace.WriteLine("Outgoing closed.");
            OnConnectionErrored();
        }
    }

    private async ValueTask FlushToServerAsync(PassthroughBatch batch)
    {
        if (batch.IsEmpty)
            return;

        var bytes = batch.Take();
        await WriteToServerAsync(bytes);

        if (TrafficLog.Enabled)
        {
            var content = Encoding.UTF8.GetString(bytes.Span);
            // don't log anything that contains a JWT
            if (!content.Contains("token>")) TrafficLog.Write("<!--RC TO SERVER-->", content);
        }
    }

    private async ValueTask FlushToClientAsync(PassthroughBatch batch)
    {
        if (batch.IsEmpty)
            return;

        var bytes = batch.Take();
        await WriteToClientAsync(bytes);
        if (TrafficLog.Enabled)
            TrafficLog.Write("<!--SERVER TO RC-->", Encoding.UTF8.GetString(bytes.Span));
    }

    // SslStream does not support concurrent writes, and both the proxy loops and the tray/chat commands
    // write to the same streams, so every write goes through a per-direction lock.
    private async ValueTask WriteToServerAsync(ReadOnlyMemory<byte> bytes)
    {
        await OutgoingWriteLock.WaitAsync();
        try
        {
            await Outgoing.WriteAsync(bytes);
        }
        finally
        {
            OutgoingWriteLock.Release();
        }
    }

    private async ValueTask WriteToClientAsync(ReadOnlyMemory<byte> bytes)
    {
        await IncomingWriteLock.WaitAsync();
        try
        {
            await Incoming.WriteAsync(bytes);
        }
        finally
        {
            IncomingWriteLock.Release();
        }
    }

    /// Tracks a run of consecutive pass-through units so they are forwarded in a single write, straight from the
    /// reader's buffer. Units returned by the reader are contiguous, so a run is just a start and an end offset.
    private sealed class PassthroughBatch
    {
        private byte[]? _buffer;
        private int _start;
        private int _end;

        internal bool IsEmpty => _buffer is null;

        internal void Add(XmppUnit unit)
        {
            if (_buffer is null || _buffer != unit.Buffer || _end != unit.Offset)
            {
                // Callers flush before anything non-contiguous, so this only starts a new run.
                Debug.Assert(_buffer is null, "Pass-through units must be contiguous.");
                _buffer = unit.Buffer;
                _start = unit.Offset;
            }

            _end = unit.Offset + unit.Length;
        }

        internal ReadOnlyMemory<byte> Take()
        {
            var memory = _buffer.AsMemory(_start, _end - _start);
            _buffer = null;
            return memory;
        }
    }

    private async Task PossiblyRewriteAndResendPresenceAsync(string content, string targetStatus)
    {
        try
        {
            var wrappedContent = "<xml>" + content + "</xml>";
            var xml = XDocument.Load(new StringReader(wrappedContent));

            if (xml.Root is null)
                return;
            if (xml.Root.HasElements is false)
                return;

            // Remember our broadcast presence so it can be re-sent when the status changes. Directed presences
            // (e.g. to a lobby chat room) must not overwrite it, or status changes would only reach that room.
            if (xml.Root.Elements("presence").Any(p => p.Attribute("to") is null))
                LastPresence = content;

            // Snapshot the elements: removing one while enumerating Elements() would end the loop early
            // and forward any later presences in this batch unmodified.
            foreach (var presence in xml.Root.Elements().ToList())
            {
                if (presence.Name != "presence")
                    continue;
                if (presence.Attribute("to") is not null)
                {
                    if (MainController.ConnectToMuc)
                        continue;
                    presence.Remove();
                    continue;
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

                // Remove Teamfight Tactics presence
                presence.Element("games")?.Element("teamfighttactics")?.Remove();

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
                        var valorantJson = JsonNode.Parse(valorantPresence);
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
                OmitXmlDeclaration = true, Encoding = Encoding.UTF8, ConformanceLevel = ConformanceLevel.Fragment
            };
            using (var xw = XmlWriter.Create(sb, xws))
            {
                foreach (var xElement in xml.Root.Elements())
                    xElement.WriteTo(xw);
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            if (bytes.Length > 0)
                await WriteToServerAsync(bytes);
            TrafficLog.Write("<!--DECEIVE TO SERVER-->", sb.ToString());
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
        await WriteToClientAsync(bytes);
        TrafficLog.Write("<!--DECEIVE TO RC-->", presenceMessage);
    }

    public async Task SendMessageFromFakePlayerAsync(string message)
    {
        if (!InsertedFakePlayer || !Connected)
            return;

        var stamp = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss.fff");

        var chatMessage =
            $"<message from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Deceive' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>{message}</body></message>";

        var bytes = Encoding.UTF8.GetBytes(chatMessage);
        await WriteToClientAsync(bytes);
        TrafficLog.Write("<!--DECEIVE TO RC-->", chatMessage);
    }

    public async Task UpdateStatusAsync(string newStatus)
    {
        if (string.IsNullOrEmpty(LastPresence) || !Connected)
            return;

        await PossiblyRewriteAndResendPresenceAsync(LastPresence, newStatus);
    }

    // Called by both loops when they end. Closes both sides exactly once: disposing the streams unblocks
    // the other loop's pending read, and makes sure we never keep a half-dead connection to the chat server open.
    private void OnConnectionErrored()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        _connected = false;
        try
        {
            Incoming.Dispose();
        }
        catch (Exception e)
        {
            Trace.WriteLine(e);
        }

        try
        {
            Outgoing.Dispose();
        }
        catch (Exception e)
        {
            Trace.WriteLine(e);
        }

        ConnectionErrored?.Invoke(this, EventArgs.Empty);
    }
}