using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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
        // Hidden control used to marshal work back onto the UI thread from the
        // background proxy loops (tray rebuilds, balloon tips, ...).
        UiInvoker = new Control();
        _ = UiInvoker.Handle; // force handle creation on the UI thread

        TrayIcon = new NotifyIcon
        {
            Icon = Resources.DeceiveIcon,
            Visible = true,
            BalloonTipTitle = StartupHandler.DeceiveTitle,
            BalloonTipText = "Deceive is currently masking your status. Right-click the tray icon for more options."
        };
        TrayIcon.ShowBalloonTip(5000);

        FriendNotificationsEnabled = Persistence.GetFriendNotificationsEnabled();
        foreach (var friend in Persistence.GetTrackedFriends())
            TrackedFriends.Add(friend);

        LoadStatus();
        UpdateTray();
    }

    private NotifyIcon TrayIcon { get; }
    private Control UiInvoker { get; }
    public bool Enabled { get; set; } = true;
    public string Status { get; set; } = null!;
    private string StatusFile { get; } = Path.Combine(Persistence.DataDir, "status");
    public bool ConnectToMuc { get; set; } = true;
    private bool SentIntroductionText { get; set; } = false;
    private CancellationTokenSource? ShutdownToken { get; set; } = null;

    private ToolStripMenuItem EnabledMenuItem { get; set; } = null!;
    private ToolStripMenuItem ChatStatus { get; set; } = null!;
    private ToolStripMenuItem OfflineStatus { get; set; } = null!;
    private ToolStripMenuItem MobileStatus { get; set; } = null!;

    // Friend status tracking. All access is guarded by FriendLock.
    private readonly object FriendLock = new();
    private bool FriendNotificationsEnabled;
    private readonly HashSet<string> TrackedFriends = new(StringComparer.OrdinalIgnoreCase); // normalized "name#tag" Riot IDs
    private readonly Dictionary<string, string> FriendNamesByPuuid = new(); // puuid -> "name#tag"
    private readonly Dictionary<string, string> FriendLastStatus = new(); // puuid -> last seen presence status
    private readonly Dictionary<string, string> FriendRosterState = new(); // puuid -> online/offline from roster (display fallback)

    private const string FakePlayerPuuid = "41c322a1-b328-495b-a004-5ccd3e45eae8";

    private List<ProxiedConnection> Connections { get; } = new();

    public void StartServingClients(TcpListener server, X509Certificate2 serverCert, string chatHost, int chatPort)
    {
        Task.Run(() => ServeClientsAsync(server, serverCert, chatHost, chatPort));
    }

    private async Task ServeClientsAsync(TcpListener server, X509Certificate2 serverCert, string chatHost, int chatPort)
    {
        while (true)
        {
            try
            {
                // no need to shutdown, we received a new request
                ShutdownToken?.Cancel();
                ShutdownToken = null;

                var incoming = await server.AcceptTcpClientAsync();
                var sslIncoming = new SslStream(incoming.GetStream());
                await sslIncoming.AuthenticateAsServerAsync(serverCert);

                TcpClient outgoing;
                while (true)
                {
                    try
                    {
                        outgoing = new TcpClient(chatHost, chatPort);
                        break;
                    }
                    catch (SocketException e)
                    {
                        Trace.WriteLine(e);
                        var result = MessageBox.Show(
                            "Unable to connect to the chat server. Please check your internet connection. " +
                            "If this issue persists and you can connect to chat normally without Deceive, " +
                            "please file a bug report through GitHub (https://github.com/molenzwiebel/Deceive) or Discord.",
                            StartupHandler.DeceiveTitle,
                            MessageBoxButtons.RetryCancel,
                            MessageBoxIcon.Error,
                            MessageBoxDefaultButton.Button1
                        );
                        if (result == DialogResult.Cancel)
                            Environment.Exit(0);
                    }
                }

                var sslOutgoing = new SslStream(outgoing.GetStream());
                await sslOutgoing.AuthenticateAsClientAsync(chatHost);

                var proxiedConnection = new ProxiedConnection(this, sslIncoming, sslOutgoing);
                proxiedConnection.Start();
                proxiedConnection.ConnectionErrored += (_, _) =>
                {
                    Trace.WriteLine("Disconnected incoming connection.");
                    Connections.Remove(proxiedConnection);

                    if (Connections.Count == 0)
                    {
                        Task.Run(ShutdownIfNoReconnect);
                    }
                };
                Connections.Add(proxiedConnection);

                if (!SentIntroductionText)
                {
                    SentIntroductionText = true;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(10_000);
                        await SendIntroductionTextAsync();
                    });
                }
            } catch (Exception e)
            {
                Trace.WriteLine("Failed to handle incoming connection.");
                Trace.WriteLine(e);
            }
        }
    }

    private void UpdateTray()
    {
        var aboutMenuItem = new ToolStripMenuItem(StartupHandler.DeceiveTitle) { Enabled = false };

        EnabledMenuItem = new ToolStripMenuItem("Enabled", null, async (_, _) =>
        {
            Enabled = !Enabled;
            await UpdateStatusAsync(Enabled ? Status : "chat");
            await SendMessageFromFakePlayerAsync(Enabled ? "Deceive is now enabled." : "Deceive is now disabled.");
            UpdateTray();
        })
        { Checked = Enabled };

        var mucMenuItem = new ToolStripMenuItem("Enable lobby chat", null, (_, _) =>
        {
            ConnectToMuc = !ConnectToMuc;
            UpdateTray();
        })
        { Checked = ConnectToMuc };

        ChatStatus = new ToolStripMenuItem("Online", null, async (_, _) =>
        {
            await UpdateStatusAsync(Status = "chat");
            Enabled = true;
            UpdateTray();
        })
        { Checked = Status.Equals("chat") };

        OfflineStatus = new ToolStripMenuItem("Offline", null, async (_, _) =>
        {
            await UpdateStatusAsync(Status = "offline");
            Enabled = true;
            UpdateTray();
        })
        { Checked = Status.Equals("offline") };

        MobileStatus = new ToolStripMenuItem("Mobile", null, async (_, _) =>
        {
            await UpdateStatusAsync(Status = "mobile");
            Enabled = true;
            UpdateTray();
        })
        { Checked = Status.Equals("mobile") };

        var typeMenuItem = new ToolStripMenuItem("Status Type", null, ChatStatus, OfflineStatus, MobileStatus);
        
        var currentStartup = Persistence.GetStartupStatus();
        var startupOnline = new ToolStripMenuItem("Online", null, (_, _) =>
        {
            Persistence.SetStartupStatus("chat");
            UpdateTray();
        })
        { Checked = currentStartup == "chat" };

        var startupOffline = new ToolStripMenuItem("Offline", null, (_, _) =>
        {
            Persistence.SetStartupStatus("offline");
            UpdateTray();
        })
        { Checked = currentStartup == "offline" };

        var startupMobile = new ToolStripMenuItem("Mobile", null, (_, _) =>
        {
            Persistence.SetStartupStatus("mobile");
            UpdateTray();
        })
        { Checked = currentStartup == "mobile" };

        var startupLast = new ToolStripMenuItem("Remember Last", null, (_, _) =>
        {
            Persistence.SetStartupStatus("last");
            UpdateTray();
        })
        { Checked = currentStartup == "last" };

        var startupStatusMenuItem = new ToolStripMenuItem("Default Status on Startup", null, startupOnline, startupOffline, startupMobile, startupLast);

        var friendNotificationsMenuItem = new ToolStripMenuItem("Friend Notifications...", null, (_, _) => OpenFriendNotificationsForm());

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
        var sendTestMsg = new ToolStripMenuItem("Send message", null, async (_, _) => { await SendMessageFromFakePlayerAsync("Test"); });

        TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[]
        {
            aboutMenuItem, EnabledMenuItem, typeMenuItem, startupStatusMenuItem, friendNotificationsMenuItem, mucMenuItem, sendTestMsg, restartWithDifferentGameItem, quitMenuItem
        });
#else
        TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[] { aboutMenuItem, EnabledMenuItem, typeMenuItem, startupStatusMenuItem, friendNotificationsMenuItem, mucMenuItem, restartWithDifferentGameItem, quitMenuItem });
#endif
    }

    // Opens the modal window that lets the user toggle notifications and pick which friends to
    // track. A dedicated window (rather than a tray submenu) is used because a roster can contain
    // hundreds of friends, which would never fit in a context menu.
    private void OpenFriendNotificationsForm()
    {
        List<KeyValuePair<string, string>> friends; // "name#tag" -> current status
        HashSet<string> tracked;
        bool enabled;
        lock (FriendLock)
        {
            friends = FriendNamesByPuuid
                .Select(pair => new KeyValuePair<string, string>(pair.Value, ResolveDisplayStatus(pair.Key)))
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            tracked = new HashSet<string>(TrackedFriends, StringComparer.OrdinalIgnoreCase);
            enabled = FriendNotificationsEnabled;
        }

        using var form = new FriendNotificationsForm(friends, tracked, enabled, SetFriendNotificationsEnabled, SetFriendTracked);
        form.ShowDialog();
    }

    // Best-known current status for display: a live presence status if we've seen one this session,
    // otherwise the online/offline state from the roster. Must be called while holding FriendLock.
    private string ResolveDisplayStatus(string puuid)
    {
        if (FriendLastStatus.TryGetValue(puuid, out var status))
            return status;
        if (FriendRosterState.TryGetValue(puuid, out var rosterState))
            return rosterState;
        return "Offline";
    }

    internal void SetFriendNotificationsEnabled(bool value)
    {
        lock (FriendLock)
        {
            FriendNotificationsEnabled = value;
            Persistence.SetFriendNotificationsEnabled(value);
        }
    }

    internal void SetFriendTracked(string riotId, bool tracked)
    {
        lock (FriendLock)
        {
            var changed = tracked ? TrackedFriends.Add(riotId) : TrackedFriends.Remove(riotId);
            if (changed)
                Persistence.SetTrackedFriends(TrackedFriends);
        }
    }

    public async Task HandleChatMessage(string content)
    {
        var body = (ExtractMessageBody(content) ?? content).Trim();
        var lower = body.ToLowerInvariant();

        // Friend tracking commands. Checked before the generic keyword matches below so that,
        // for example, "/tracking" isn't swallowed by the "/track" prefix check.
        if (lower is "/tracking" or "tracking" or "/tracked" or "tracked" or "/list" or "list")
        {
            await SendTrackedFriendsListAsync();
        }
        else if (lower.StartsWith("/untrack") || lower.StartsWith("untrack "))
        {
            await HandleTrackCommandAsync(body, track: false);
        }
        else if (lower.StartsWith("/track") || lower.StartsWith("track "))
        {
            await HandleTrackCommandAsync(body, track: true);
        }
        else if (lower.Contains("offline"))
        {
            if (!Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
            OfflineStatus.PerformClick();
        }
        else if (lower.Contains("mobile"))
        {
            if (!Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
            MobileStatus.PerformClick();
        }
        else if (lower.Contains("online"))
        {
            if (!Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
            ChatStatus.PerformClick();
        }
        else if (lower.Contains("enable"))
        {
            if (Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is already enabled.");
            else
                EnabledMenuItem.PerformClick();
        }
        else if (lower.Contains("disable"))
        {
            if (!Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is already disabled.");
            else
                EnabledMenuItem.PerformClick();
        }
        else if (lower.Contains("status"))
        {
            if (Status == "chat")
                await SendMessageFromFakePlayerAsync("You are appearing online.");
            else
                await SendMessageFromFakePlayerAsync("You are appearing " + Status + ".");
        }
        else if (lower.Contains("help"))
        {
            await SendMessageFromFakePlayerAsync("You can send the following messages to quickly change Deceive settings: online/offline/mobile/enable/disable/status");
            await SendMessageFromFakePlayerAsync(
                "Friend notifications: /track <name#tag> to be notified whenever that friend's status changes, /untrack <name#tag> to stop, /tracking to list tracked friends.");
        }
    }

    private async Task SendIntroductionTextAsync()
    {
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
        foreach (var connection in Connections)
            await connection.SendMessageFromFakePlayerAsync(message);
    }

    #region Friend status notifications

    private async Task HandleTrackCommandAsync(string body, bool track)
    {
        var riotId = ParseCommandArgument(body);
        if (riotId is null)
        {
            await SendMessageFromFakePlayerAsync(track
                ? "Usage: /track <name#tag> to get notified whenever that friend's status changes (e.g. when they finish a game)."
                : "Usage: /untrack <name#tag>.");
            return;
        }

        bool changed;
        bool known;
        lock (FriendLock)
        {
            known = FriendNamesByPuuid.Values.Any(value => string.Equals(value, riotId, StringComparison.OrdinalIgnoreCase));
            changed = track ? TrackedFriends.Add(riotId) : TrackedFriends.Remove(riotId);
            if (changed)
                Persistence.SetTrackedFriends(TrackedFriends);
        }

        if (track)
        {
            if (!changed)
                await SendMessageFromFakePlayerAsync($"Already tracking {riotId}.");
            else if (known)
                await SendMessageFromFakePlayerAsync($"Now tracking {riotId}. You'll be notified whenever their status changes.");
            else
                await SendMessageFromFakePlayerAsync($"Now tracking {riotId}. Note: no friend with that Riot ID is loaded yet, double-check the spelling (name#tag).");
        }
        else
        {
            await SendMessageFromFakePlayerAsync(changed ? $"No longer tracking {riotId}." : $"{riotId} wasn't being tracked.");
        }
    }

    private async Task SendTrackedFriendsListAsync()
    {
        List<string> tracked;
        lock (FriendLock)
            tracked = TrackedFriends.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();

        await SendMessageFromFakePlayerAsync(tracked.Count == 0
            ? "You aren't tracking any friends. Use /track <name#tag> to start."
            : "Currently tracking: " + string.Join(", ", tracked));
    }

    // Parses the argument of a "/command argument" message, stripping surrounding quotes.
    private static string? ParseCommandArgument(string body)
    {
        var spaceIndex = body.IndexOf(' ');
        if (spaceIndex < 0)
            return null;

        var argument = body.Substring(spaceIndex + 1).Trim();
        if (argument.Length >= 2 && argument[0] == '"' && argument[argument.Length - 1] == '"')
            argument = argument.Substring(1, argument.Length - 2).Trim();

        return argument.Length == 0 ? null : argument;
    }

    private static string? ExtractMessageBody(string content)
    {
        const string open = "<body>";
        const string close = "</body>";
        var start = content.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += open.Length;
        var end = content.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? null : WebUtility.HtmlDecode(content.Substring(start, end - start));
    }

    // Parses the roster stanza sent by the server to learn the puuid -> "name#tag" mapping so that
    // we can resolve incoming friend presences (which only carry the puuid) to a readable Riot ID.
    internal void HandleRosterContent(string content)
    {
        // Extract just the <query>...</query> element. The buffered content also contains the
        // enclosing (and unclosed, when split) <iq> wrapper plus any trailing stanzas, which would
        // otherwise make the document fail to parse.
        const string openMarker = "<query xmlns='jabber:iq:riotgames:roster'>";
        const string closeMarker = "</query>";
        var start = content.IndexOf(openMarker, StringComparison.Ordinal);
        if (start < 0)
            return;
        var end = content.IndexOf(closeMarker, start, StringComparison.Ordinal);
        if (end < 0)
            return;
        var queryXml = content.Substring(start, end - start + closeMarker.Length);

        try
        {
            var xml = XDocument.Load(new StringReader("<xml>" + queryXml + "</xml>"));
            var count = 0;
            foreach (var item in xml.Descendants().Where(element => element.Name.LocalName == "item"))
            {
                var jid = item.Attribute("jid")?.Value;
                if (string.IsNullOrEmpty(jid))
                    continue;

                var puuid = jid!.Split('@')[0];
                if (puuid == FakePlayerPuuid)
                    continue;

                var riotId = ExtractRiotId(item);
                if (riotId is null)
                    continue;

                var state = item.Elements().FirstOrDefault(element => element.Name.LocalName == "state")?.Value;
                lock (FriendLock)
                {
                    FriendNamesByPuuid[puuid] = riotId;
                    if (!string.IsNullOrEmpty(state))
                        FriendRosterState[puuid] = MapRosterState(state!);
                }

                count++;
            }

            Trace.WriteLine($"Parsed {count} friends from roster for friend tracking.");
        }
        catch (Exception e)
        {
            Trace.WriteLine("Failed to parse roster for friend tracking.");
            Trace.WriteLine(e);
        }
    }

    private static string MapRosterState(string state) => state.ToLowerInvariant() switch
    {
        "offline" => "Offline",
        "mobile" => "Mobile",
        _ => "Online"
    };

    private static string? ExtractRiotId(XElement item)
    {
        var id = item.Elements().FirstOrDefault(element => element.Name.LocalName == "id");
        var name = id?.Attribute("name")?.Value;
        var tagline = id?.Attribute("tagline")?.Value;
        if (!string.IsNullOrWhiteSpace(name))
            return string.IsNullOrWhiteSpace(tagline) ? name!.Trim() : name!.Trim() + "#" + tagline!.Trim();

        var lolName = item.Elements().FirstOrDefault(element => element.Name.LocalName == "lol")?.Attribute("name")?.Value;
        if (!string.IsNullOrWhiteSpace(lolName))
            return lolName!.Trim();

        var attributeName = item.Attribute("name")?.Value;
        return string.IsNullOrWhiteSpace(attributeName) ? null : attributeName!.Trim();
    }

    // Observes incoming friend presences and notifies the user when a tracked friend's status
    // changes. The very first presence seen for a friend in a session is recorded silently so that
    // we don't fire a burst of notifications right after logging in.
    internal void HandleFriendPresenceContent(string content)
    {
        XDocument xml;
        try
        {
            xml = XDocument.Load(new StringReader("<xml>" + content + "</xml>"));
        }
        catch
        {
            return; // partial or non-XML chunk; ignore
        }

        if (xml.Root is null)
            return;

        foreach (var presence in xml.Root.Elements().Where(element => element.Name.LocalName == "presence"))
        {
            var from = presence.Attribute("from")?.Value;
            if (string.IsNullOrEmpty(from))
                continue;

            var puuid = from!.Split('@')[0];
            if (puuid == FakePlayerPuuid)
                continue;

            var status = ComputeFriendStatus(presence);

            string? riotId;
            string? previous;
            bool tracked;
            bool enabled;
            lock (FriendLock)
            {
                FriendNamesByPuuid.TryGetValue(puuid, out riotId);
                FriendLastStatus.TryGetValue(puuid, out previous);
                FriendLastStatus[puuid] = status;
                enabled = FriendNotificationsEnabled;
                tracked = riotId is not null && TrackedFriends.Contains(riotId);
            }

            if (!enabled || !tracked)
                continue;
            if (previous is null || previous == status)
                continue; // first sighting this session, or just a heartbeat with no real change

            NotifyFriendStatusChanged(riotId!, status);
        }
    }

    // Reduces a presence stanza to the human-readable status shown in the friends list.
    private static string ComputeFriendStatus(XElement presence)
    {
        if (presence.Attribute("type")?.Value == "unavailable")
            return "Offline";

        var games = presence.Elements().FirstOrDefault(element => element.Name.LocalName == "games");
        var lol = games?.Elements().FirstOrDefault(element => element.Name.LocalName == "league_of_legends");
        var payload = lol?.Elements().FirstOrDefault(element => element.Name.LocalName == "p")?.Value;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                var gameStatus = JsonSerializer.Deserialize<JsonNode>(payload!)?["gameStatus"]?.GetValue<string>();
                switch (gameStatus)
                {
                    case "inGame":
                        return "In Game";
                    case "championSelect":
                        return "In Champion Select";
                    case "inQueue":
                        return "In Queue";
                    case "spectating":
                        return "Spectating";
                    case null or "" or "outOfGame":
                        break; // fall through to the availability-based status
                    default:
                        if (gameStatus!.StartsWith("hosting_", StringComparison.Ordinal))
                            return "In Lobby";
                        break;
                }
            }
            catch
            {
                // malformed payload; fall through to the availability-based status
            }
        }

        var show = presence.Elements().FirstOrDefault(element => element.Name.LocalName == "show")?.Value;
        return show switch
        {
            "away" => "Away",
            "dnd" => "Busy",
            "mobile" => "Mobile",
            _ => "Online"
        };
    }

    private void NotifyFriendStatusChanged(string riotId, string status)
    {
        var message = $"{riotId} is now {status}.";
        Trace.WriteLine("Friend status notification: " + message);

        PostToUi(() =>
        {
            try
            {
                TrayIcon.BalloonTipTitle = "Deceive: Friend status changed";
                TrayIcon.BalloonTipText = message;
                TrayIcon.ShowBalloonTip(5000);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
            }
        });

        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch
        {
            // no audio device / sound disabled; ignore
        }

        _ = SendMessageFromFakePlayerAsync(message);
    }

    // Marshals an action onto the UI thread that owns the tray icon.
    private void PostToUi(Action action)
    {
        if (UiInvoker.IsHandleCreated && UiInvoker.InvokeRequired)
            UiInvoker.BeginInvoke(action);
        else
            action();
    }

    #endregion

    private async Task UpdateStatusAsync(string newStatus)
    {
        foreach (var connection in Connections)
            await connection.UpdateStatusAsync(newStatus);

        if (newStatus == "chat")
            await SendMessageFromFakePlayerAsync("You are now appearing online.");
        else
            await SendMessageFromFakePlayerAsync("You are now appearing " + newStatus + ".");
    }

    private void LoadStatus()
    {
        var startupStatus = Persistence.GetStartupStatus();

        if (startupStatus is "chat" or "offline" or "mobile")
        {
            Status = startupStatus;
            return;
        }

        if (!File.Exists(StatusFile))
        {
            Status = "offline";
            return;
        }

        // "last" or unrecognized: use the saved session status.
        var saved = File.ReadAllText(StatusFile);
        Status = saved switch
        {
            "chat" => "chat",
            "mobile" => "mobile",
            _ => "offline"
        };
    }

    private async Task ShutdownIfNoReconnect()
    {
        if (ShutdownToken == null)
            ShutdownToken = new CancellationTokenSource();
        await Task.Delay(60_000, ShutdownToken.Token);

        Trace.WriteLine("Received no new connections after 60s, shutting down.");
        Environment.Exit(0);
    }

    private void SaveStatus() => File.WriteAllText(StatusFile, Status);
}
