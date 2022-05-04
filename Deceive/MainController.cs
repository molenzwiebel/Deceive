using System;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Deceive.Properties;

namespace Deceive;

internal class MainController : ApplicationContext
{
    internal MainController()
    {
        TrayIcon = new NotifyIcon
        {
            Icon = Resources.DeceiveIcon,
            Visible = true,
            BalloonTipTitle = StartupHandler.DeceiveTitle,
            BalloonTipText = "Deceive is currently masking your status. Right-click the tray icon for more options."
        };
        TrayIcon.ShowBalloonTip(5000);

        LoadStatus();
        UpdateTray();
    }

    private NotifyIcon TrayIcon { get; }
    private bool Enabled { get; set; } = true;
    private string Status { get; set; } = null!;
    private string StatusFile { get; } = Path.Combine(Persistence.DataDir, "status");
    private bool ConnectToMuc { get; set; } = true;
    private bool InsertedFakePlayer { get; set; }
    private bool SentFakePlayerPresence { get; set; }
    private bool SentIntroductionText { get; set; }
    private string? ValorantVersion { get; set; }

    private SslStream Incoming { get; set; } = null!;
    private SslStream Outgoing { get; set; } = null!;
    private bool Connected { get; set; }
    private string LastPresence { get; set; } = null!; // we resend this if the state changes

    private ToolStripMenuItem EnabledMenuItem { get; set; } = null!;
    private ToolStripMenuItem ChatStatus { get; set; } = null!;
    private ToolStripMenuItem OfflineStatus { get; set; } = null!;
    private ToolStripMenuItem MobileStatus { get; set; } = null!;

    internal event EventHandler? ConnectionErrored;

    private void UpdateTray()
    {
        var aboutMenuItem = new ToolStripMenuItem(StartupHandler.DeceiveTitle) { Enabled = false };

        EnabledMenuItem = new ToolStripMenuItem("Enabled", null, async (_, _) =>
        {
            Enabled = !Enabled;
            await UpdateStatusAsync(Enabled ? Status : "chat");
            await SendMessageFromFakePlayerAsync(Enabled ? "Deceive is now enabled." : "Deceive is now disabled.");
            UpdateTray();
        }) { Checked = Enabled };

        var mucMenuItem = new ToolStripMenuItem("Enable lobby chat", null, (_, _) =>
        {
            ConnectToMuc = !ConnectToMuc;
            UpdateTray();
        }) { Checked = ConnectToMuc };

        ChatStatus = new ToolStripMenuItem("Online", null, async (_, _) =>
        {
            await UpdateStatusAsync(Status = "chat");
            Enabled = true;
            UpdateTray();
        }) { Checked = Status.Equals("chat") };

        OfflineStatus = new ToolStripMenuItem("Offline", null, async (_, _) =>
        {
            await UpdateStatusAsync(Status = "offline");
            Enabled = true;
            UpdateTray();
        }) { Checked = Status.Equals("offline") };

        MobileStatus = new ToolStripMenuItem("Mobile", null, async (_, _) =>
        {
            await UpdateStatusAsync(Status = "mobile");
            Enabled = true;
            UpdateTray();
        }) { Checked = Status.Equals("mobile") };

        var typeMenuItem = new ToolStripMenuItem("Status Type", null, ChatStatus, OfflineStatus, MobileStatus);

        var restartWithDifferentGameItem = new ToolStripMenuItem("Restart and launch a different game", null, (_, _) =>
        {
            var result = MessageBox.Show(
                "Restart Deceive to launch a different game? This will also stop related games if they are running.",
                StartupHandler.DeceiveTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1
            );

            if (result is not DialogResult.Yes)
                return;

            Utils.KillProcesses();
            Thread.Sleep(2000);

            Persistence.SetDefaultLaunchGame(LaunchGame.Prompt);
            Process.Start(Application.ExecutablePath);
            Environment.Exit(0);
        });

        var quitMenuItem = new ToolStripMenuItem("Quit", null, (_, _) =>
        {
            var result = MessageBox.Show(
                "Are you sure you want to stop Deceive? This will also stop related games if they are running.",
                StartupHandler.DeceiveTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1
            );

            if (result is not DialogResult.Yes)
                return;

            Utils.KillProcesses();
            SaveStatus();
            Application.Exit();
        });

        TrayIcon.ContextMenuStrip = new ContextMenuStrip();

#if DEBUG
        var closeIn = new ToolStripMenuItem("Close incoming", null, (_, _) => { Incoming.Close(); });
        var closeOut = new ToolStripMenuItem("Close outgoing", null, (_, _) => { Outgoing.Close(); });
        var createFakePlayer = new ToolStripMenuItem("Resend fake player", null, async (_, _) => { await SendFakePlayerPresenceAsync(); });
        var sendTestMsg = new ToolStripMenuItem("Send message", null, async (_, _) => { await SendMessageFromFakePlayerAsync("Test"); });

        TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[]
        {
            aboutMenuItem, EnabledMenuItem, typeMenuItem, mucMenuItem, closeIn, closeOut, createFakePlayer, sendTestMsg, restartWithDifferentGameItem, quitMenuItem
        });
#else
        TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[] { aboutMenuItem, EnabledMenuItem, typeMenuItem, mucMenuItem, restartWithDifferentGameItem, quitMenuItem });
#endif
    }

    public void StartThreads(SslStream incoming, SslStream outgoing)
    {
        Incoming = incoming;
        Outgoing = outgoing;
        Connected = true;
        InsertedFakePlayer = false;
        SentFakePlayerPresence = false;

        Task.Run(IncomingLoopAsync);
        Task.Run(OutgoingLoopAsync);
    }

    private async Task IncomingLoopAsync()
    {
        try
        {
            int byteCount;
            var bytes = new byte[8192];

            do
            {
                byteCount = await Incoming.ReadAsync(bytes, 0, bytes.Length);

                var content = Encoding.UTF8.GetString(bytes, 0, byteCount);

                // If this is possibly a presence stanza, rewrite it.
                if (content.Contains("<presence") && Enabled)
                {
                    Trace.WriteLine("<!--RC TO SERVER ORIGINAL-->" + content);
                    await PossiblyRewriteAndResendPresenceAsync(content, Status);
                }
                else if (content.Contains("41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net"))
                {
                    if (content.ToLower().Contains("offline"))
                    {
                        if (!Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
                        OfflineStatus.PerformClick();
                    }
                    else if (content.ToLower().Contains("mobile"))
                    {
                        if (!Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
                        MobileStatus.PerformClick();
                    }
                    else if (content.ToLower().Contains("online"))
                    {
                        if (!Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
                        ChatStatus.PerformClick();
                    }
                    else if (content.ToLower().Contains("enable"))
                    {
                        if (Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is already enabled.");
                        else
                            EnabledMenuItem.PerformClick();
                    }
                    else if (content.ToLower().Contains("disable"))
                    {
                        if (!Enabled)
                            await SendMessageFromFakePlayerAsync("Deceive is already disabled.");
                        else
                            EnabledMenuItem.PerformClick();
                    }
                    else if (content.ToLower().Contains("status"))
                    {
                        if (Status == "chat")
                            await SendMessageFromFakePlayerAsync("You are appearing online.");
                        else
                            await SendMessageFromFakePlayerAsync("You are appearing " + Status + ".");
                    }
                    else if (content.ToLower().Contains("help"))
                    {
                        await SendMessageFromFakePlayerAsync("You can send the following messages to quickly change Deceive settings: online/offline/mobile/enable/disable/status");
                    }

                    //Don't send anything involving our fake user to chat servers
                    Trace.WriteLine("<!--RC TO SERVER REMOVED-->" + content);
                }
                else
                {
                    await Outgoing.WriteAsync(bytes, 0, byteCount);
                    Trace.WriteLine("<!--RC TO SERVER-->" + content);
                }

                if (InsertedFakePlayer && !SentFakePlayerPresence)
                    await SendFakePlayerPresenceAsync();

                if (!SentIntroductionText)
                    await SendIntroductionTextAsync();
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
            SaveStatus();
            if (Connected)
                OnConnectionErrored();
        }
    }

    private async Task OutgoingLoopAsync()
    {
        try
        {
            int byteCount;
            var bytes = new byte[8192];

            do
            {
                byteCount = await Outgoing.ReadAsync(bytes, 0, bytes.Length);
                var content = Encoding.UTF8.GetString(bytes, 0, byteCount);

                // Insert fake player into roster
                const string roster = "<query xmlns='jabber:iq:riotgames:roster'>";
                if (!InsertedFakePlayer && content.Contains(roster))
                {
                    InsertedFakePlayer = true;
                    Trace.WriteLine("<!--SERVER TO RC ORIGINAL-->" + content);
                    content = content.Insert(content.IndexOf(roster, StringComparison.Ordinal) + roster.Length,
                        "<item jid='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' name='&#9;Deceive Active!' subscription='both' puuid='41c322a1-b328-495b-a004-5ccd3e45eae8'>" +
                        "<group priority='9999'>Deceive</group>" +
                        "<id name='&#9;Deceive Active!' tagline=''/><lol name='&#9;Deceive Active!'/>" +
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
            SaveStatus();
            if (Connected)
                OnConnectionErrored();
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
                    if (ConnectToMuc)
                        continue;
                    presence.Remove();
                }

                if (targetStatus != "chat" || presence.Element("games")?.Element("league_of_legends")?.Element("st")?.Value != "dnd")
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

                // Extracts current VALORANT version from the logs, so that we can show a fake
                // player with the proper version and avoid "Version Mismatch" from being shown.
                //
                // This isn't technically necessary, but people keep coming in and asking whether
                // the scary red text means Deceive doesn't work, so might as well do this and
                // get a slightly better user experience.
                if (ValorantVersion is null)
                {
                    var valorantBase64 = presence.Element("games")?.Element("valorant")?.Element("p")?.Value;
                    if (valorantBase64 is not null)
                    {
                        var valorantPresence = Encoding.UTF8.GetString(Convert.FromBase64String(valorantBase64));
                        var valorantJson = JsonSerializer.Deserialize<JsonNode>(valorantPresence);
                        ValorantVersion = valorantJson?["partyClientVersion"]?.GetValue<string>();
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
            var xws = new XmlWriterSettings { OmitXmlDeclaration = true, Encoding = Encoding.UTF8, ConformanceLevel = ConformanceLevel.Fragment, Async = true };
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
            Encoding.UTF8.GetBytes($"{{\"isValid\":true,\"partyId\":\"00000000-0000-0000-0000-000000000000\",\"partyClientVersion\":\"{ValorantVersion ?? "unknown"}\"}}")
        );

        var randomStanzaId = Guid.NewGuid();
        var unixTimeMilliseconds = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        var presenceMessage =
            $"<presence from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Deceive' id='b-{randomStanzaId}'>" +
            "<games>" +
            $"<keystone><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>keystone</s.p></keystone>" +
            $"<league_of_legends><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>league_of_legends</s.p><p>{{&quot;pty&quot;:true}}</p></league_of_legends>" + // No Region s.r keeps it in the main "League" category rather than "Other Servers" in every region with "Group Games & Servers" active
            $"<valorant><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.p>valorant</s.p><p>{valorantPresence}</p></valorant>" +
            $"<bacon><st>chat</st><s.t>{unixTimeMilliseconds}</s.t><s.l>bacon_availability_online</s.l><s.p>bacon</s.p></bacon>" +
            "</games>" +
            "<show>chat</show>" +
            "</presence>";

        var bytes = Encoding.UTF8.GetBytes(presenceMessage);
        await Incoming.WriteAsync(bytes, 0, bytes.Length);
        Trace.WriteLine("<!--DECEIVE TO RC-->" + presenceMessage);
    }

    private async Task SendIntroductionTextAsync()
    {
        if (!InsertedFakePlayer)
            return;
        SentIntroductionText = true;
        await SendMessageFromFakePlayerAsync("Welcome! Deceive is running and you are currently appearing " + Status +
                                             ". Despite what the game client may indicate, you are appearing offline to your friends unless you manually disable Deceive.");
        await Task.Delay(200);
        await SendMessageFromFakePlayerAsync(
            "If you want to invite others while being offline, you may need to disable Deceive for them to accept. You can enable Deceive again as soon as they are in your lobby.");
        await Task.Delay(200);
        await SendMessageFromFakePlayerAsync("To enable or disable Deceive, or to configure other settings, find Deceive in your tray icons.");
        await Task.Delay(200);
        await SendMessageFromFakePlayerAsync("Have fun!");
    }

    private async Task SendMessageFromFakePlayerAsync(string message)
    {
        var stamp = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss.fff");

        var chatMessage =
            $"<message from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Deceive' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>{message}</body></message>";

        var bytes = Encoding.UTF8.GetBytes(chatMessage);
        await Incoming.WriteAsync(bytes, 0, bytes.Length);
        Trace.WriteLine("<!--DECEIVE TO RC-->" + chatMessage);
    }

    private async Task UpdateStatusAsync(string newStatus)
    {
        if (string.IsNullOrEmpty(LastPresence))
            return;

        await PossiblyRewriteAndResendPresenceAsync(LastPresence, newStatus);

        if (newStatus == "chat")
            await SendMessageFromFakePlayerAsync("You are now appearing online.");
        else
            await SendMessageFromFakePlayerAsync("You are now appearing " + newStatus + ".");
    }

    private void LoadStatus()
    {
        if (File.Exists(StatusFile))
            Status = File.ReadAllText(StatusFile) == "mobile" ? "mobile" : "offline";
        else
            Status = "offline";
    }

    private void SaveStatus() => File.WriteAllText(StatusFile, Status);

    private void OnConnectionErrored()
    {
        Connected = false;
        ConnectionErrored?.Invoke(this, EventArgs.Empty);
    }
}
