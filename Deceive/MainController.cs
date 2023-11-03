using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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
    public bool Enabled { get; set; } = true;
    public string Status { get; set; } = null!;
    private string StatusFile { get; } = Path.Combine(Persistence.DataDir, "status");
    public bool ConnectToMuc { get; set; } = true;
    private bool SentIntroductionText { get; set; } = false;
    private CancellationTokenSource? ShutdownToken { get; set; } = null;

    public ToolStripMenuItem EnabledMenuItem { get; set; } = null!;
    public ToolStripMenuItem ChatStatus { get; set; } = null!;
    public ToolStripMenuItem OfflineStatus { get; set; } = null!;
    public ToolStripMenuItem MobileStatus { get; set; } = null!;

    private List<ProxiedConnection> Connections { get; } = new();

    public void StartServingClients(TcpListener server, string chatHost, int chatPort)
    {
        Task.Run(() => ServeClientsAsync(server, chatHost, chatPort));
    }

    private async Task ServeClientsAsync(TcpListener server, string chatHost, int chatPort)
    {
        var cert = new X509Certificate2(Resources.Certificate);

        while (true)
        {
            try
            {
                // no need to shutdown, we received a new request
                ShutdownToken?.Cancel();
                ShutdownToken = null;

                var incoming = await server.AcceptTcpClientAsync();
                var sslIncoming = new SslStream(incoming.GetStream());
                await sslIncoming.AuthenticateAsServerAsync(cert);

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
            aboutMenuItem, EnabledMenuItem, typeMenuItem, mucMenuItem, sendTestMsg, restartWithDifferentGameItem, quitMenuItem
        });
#else
        TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[] { aboutMenuItem, EnabledMenuItem, typeMenuItem, mucMenuItem, restartWithDifferentGameItem, quitMenuItem });
#endif
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
        if (File.Exists(StatusFile))
            Status = File.ReadAllText(StatusFile) == "mobile" ? "mobile" : "offline";
        else
            Status = "offline";
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
