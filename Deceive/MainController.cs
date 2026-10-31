using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Deceive.Native;

namespace Deceive;

internal class MainController
{
    internal MainController() => LoadStatus();

    private TrayIcon? TrayIcon { get; set; }
    public bool Enabled { get; set; } = true;
    public string Status { get; set; } = null!;
    private string StatusFile { get; } = Path.Combine(Persistence.DataDir, "status");
    public bool ConnectToMuc { get; set; } = true;
    private bool SentIntroductionText { get; set; } = false;
    private CancellationTokenSource? _shutdownToken;

    /// Shows the tray icon and runs the message loop on the calling thread until the user quits.
    internal void Run()
    {
        TrayIcon = new TrayIcon(StartupHandler.DeceiveTitle, BuildTrayMenu);
        TrayIcon.ShowBalloonTip(StartupHandler.DeceiveTitle,
            "Deceive is currently masking your status. Right-click the tray icon for more options.", 5000);
        NativeWindow.RunMessageLoop();
    }

    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(30);

    // Touched from the accept loop, connection error handlers, and tray/chat commands; always lock.
    private List<ProxiedConnection> Connections { get; } = new();

    private ProxiedConnection[] ConnectionsSnapshot()
    {
        lock (Connections)
            return Connections.ToArray();
    }

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
                var incoming = await server.AcceptTcpClientAsync();

                // no need to shutdown, we received a new request
                Interlocked.Exchange(ref _shutdownToken, null)?.Cancel();

                // Handshakes run per connection, so one slow or stuck client can't block the others.
                _ = Task.Run(() => HandleIncomingAsync(incoming, serverCert, chatHost, chatPort));
            }
            catch (Exception e)
            {
                Trace.WriteLine("Failed to accept incoming connection.");
                Trace.WriteLine(e);
            }
        }
    }

    private async Task HandleIncomingAsync(TcpClient incoming, X509Certificate2 serverCert, string chatHost, int chatPort)
    {
        SslStream? sslIncoming = null;
        SslStream? sslOutgoing = null;
        try
        {
            // Chat stanzas are small and latency-sensitive; don't let Nagle delay them.
            incoming.NoDelay = true;
            sslIncoming = new SslStream(incoming.GetStream());
            using (var timeout = new CancellationTokenSource(HandshakeTimeout))
                await sslIncoming.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = serverCert }, timeout.Token);

            var outgoing = await ConnectToChatServerAsync(chatHost, chatPort);
            outgoing.NoDelay = true;
            sslOutgoing = new SslStream(outgoing.GetStream());
            using (var timeout = new CancellationTokenSource(HandshakeTimeout))
                await sslOutgoing.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = chatHost }, timeout.Token);

            var proxiedConnection = new ProxiedConnection(this, sslIncoming, sslOutgoing);
            proxiedConnection.ConnectionErrored += (_, _) =>
            {
                Trace.WriteLine("Disconnected incoming connection.");
                bool noConnectionsLeft;
                lock (Connections)
                {
                    Connections.Remove(proxiedConnection);
                    noConnectionsLeft = Connections.Count == 0;
                }

                if (noConnectionsLeft)
                    _ = Task.Run(ShutdownIfNoReconnect);
            };

            lock (Connections)
                Connections.Add(proxiedConnection);

            // Subscribe before starting, so an immediate disconnect is never missed.
            proxiedConnection.Start();

            if (!SentIntroductionText && !Persistence.GetHasShownIntroduction())
            {
                SentIntroductionText = true;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(10_000);
                    await SendIntroductionTextAsync();
                });
            }
        }
        catch (Exception e)
        {
            Trace.WriteLine("Failed to handle incoming connection.");
            Trace.WriteLine(e);
            sslIncoming?.Dispose();
            sslOutgoing?.Dispose();
            incoming.Dispose();
        }
    }

    private static async Task<TcpClient> ConnectToChatServerAsync(string chatHost, int chatPort)
    {
        while (true)
        {
            var client = new TcpClient();
            try
            {
                using var timeout = new CancellationTokenSource(HandshakeTimeout);
                await client.ConnectAsync(chatHost, chatPort, timeout.Token);
                return client;
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException)
            {
                client.Dispose();
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
    }

    private List<TrayMenuItem> BuildTrayMenu()
    {
        var currentStartup = Persistence.GetStartupStatus();

        var items = new List<TrayMenuItem>
        {
            new(StartupHandler.DeceiveTitle, enabled: false),
            new("Enabled", () => Fire(ToggleEnabledAsync), Enabled),
            new("Status Type", null, false, true,
                new TrayMenuItem("Online", () => Fire(() => SetStatusAsync("chat")), Status == "chat"),
                new TrayMenuItem("Offline", () => Fire(() => SetStatusAsync("offline")), Status == "offline"),
                new TrayMenuItem("Mobile", () => Fire(() => SetStatusAsync("mobile")), Status == "mobile")),
            new("Default Status on Startup", null, false, true,
                new TrayMenuItem("Online", () => Persistence.SetStartupStatus("chat"), currentStartup == "chat"),
                new TrayMenuItem("Offline", () => Persistence.SetStartupStatus("offline"), currentStartup == "offline"),
                new TrayMenuItem("Mobile", () => Persistence.SetStartupStatus("mobile"), currentStartup == "mobile"),
                new TrayMenuItem("Remember Last", () => Persistence.SetStartupStatus("last"), currentStartup == "last")),
            new("Enable lobby chat", () => ConnectToMuc = !ConnectToMuc, ConnectToMuc),
            new("Log chat traffic (for bug reports)", ToggleTrafficLogging, TrafficLog.Enabled),
        };

#if DEBUG
        items.Add(new TrayMenuItem("Send message", () => Fire(() => SendMessageFromFakePlayerAsync("Test"))));
#endif

        items.Add(new TrayMenuItem("Restart and launch a different game", RestartWithDifferentGame));
        items.Add(new TrayMenuItem("Quit", Quit));
        return items;
    }

    private static void ToggleTrafficLogging()
    {
        if (TrafficLog.Enabled)
        {
            TrafficLog.Enabled = false;
            return;
        }

        var result = MessageBox.Show(
            "This writes all chat traffic to debug.log, including your private messages and friends list. " +
            "Only enable it while reproducing a bug, and review the log before sharing it with anyone.\n\n" +
            "Traffic from before you enable this is not logged, so you may need to restart Deceive to capture the problem. " +
            "The setting is remembered until you turn it off.",
            StartupHandler.DeceiveTitle,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2
        );

        if (result is DialogResult.OK)
            TrafficLog.Enabled = true;
    }

    private static void RestartWithDifferentGame()
    {
        var result = MessageBox.Show(
            "Restart Deceive to launch a different game? This will also stop related games if they are running.",
            StartupHandler.DeceiveTitle,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1
        );

        if (result is not DialogResult.OK)
            return;

        Utils.KillProcesses();
        Thread.Sleep(2000);

        Persistence.SetDefaultLaunchGame(LaunchGame.Prompt);
        Process.Start(Environment.ProcessPath!);
        Environment.Exit(0);
    }

    private void Quit()
    {
        var result = MessageBox.Show(
            "Are you sure you want to stop Deceive? This will also stop related games if they are running.",
            StartupHandler.DeceiveTitle,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1
        );

        if (result is not DialogResult.OK)
            return;

        Utils.KillProcesses();
        SaveStatus();
        TrayIcon?.Close();
    }

    private async Task ToggleEnabledAsync()
    {
        Enabled = !Enabled;
        await UpdateStatusAsync(Enabled ? Status : "chat");
        await SendMessageFromFakePlayerAsync(Enabled ? "Deceive is now enabled." : "Deceive is now disabled.");
    }

    private async Task SetStatusAsync(string status)
    {
        Status = status;
        await UpdateStatusAsync(status);
        Enabled = true;
    }

    // Runs an async menu action without blocking the UI thread, logging any failure.
    private static async void Fire(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
        }
    }

    public async Task HandleChatMessage(string content)
    {
        var lower = content.ToLowerInvariant();
        if (lower.Contains("offline"))
        {
            if (!Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
            await SetStatusAsync("offline");
        }
        else if (lower.Contains("mobile"))
        {
            if (!Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
            await SetStatusAsync("mobile");
        }
        else if (lower.Contains("online"))
        {
            if (!Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is now enabled.");
            await SetStatusAsync("chat");
        }
        else if (lower.Contains("enable"))
        {
            if (Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is already enabled.");
            else
                await ToggleEnabledAsync();
        }
        else if (lower.Contains("disable"))
        {
            if (!Enabled)
                await SendMessageFromFakePlayerAsync("Deceive is already disabled.");
            else
                await ToggleEnabledAsync();
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
        }
    }

    private async Task SendIntroductionTextAsync()
    {
        Persistence.SetHasShownIntroduction();
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
        foreach (var connection in ConnectionsSnapshot())
            await connection.SendMessageFromFakePlayerAsync(message);
    }

    private async Task UpdateStatusAsync(string newStatus)
    {
        foreach (var connection in ConnectionsSnapshot())
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
        var token = new CancellationTokenSource();
        var existing = Interlocked.CompareExchange(ref _shutdownToken, token, null);
        if (existing is not null)
        {
            // A shutdown timer is already running.
            token.Dispose();
            return;
        }

        try
        {
            await Task.Delay(60_000, token.Token);
        }
        catch (OperationCanceledException)
        {
            // A new connection came in.
            return;
        }

        Trace.WriteLine("Received no new connections after 60s, shutting down.");
        Environment.Exit(0);
    }

    private void SaveStatus() => File.WriteAllText(StatusFile, Status);
}
