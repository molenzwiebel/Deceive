using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using Deceive.Properties;

namespace Deceive
{
    internal class MainController : ApplicationContext
    {
        private NotifyIcon TrayIcon { get; }
        private bool Enabled { get; set; } = true;
        private string Status { get; set; } = null!;
        private string StatusFile { get; } = Path.Combine(Utils.DataDir, "status");
        private bool ConnectToMuc { get; set; } = true;
        private bool CreatedFakePlayer { get; set; }
        private bool SentIntroductionText { get; set; }

        private SslStream Incoming { get; set; } = null!;
        private SslStream Outgoing { get; set; } = null!;
        private bool Connected { get; set; }
        private string LastPresence { get; set; } = null!; // we resend this if the state changes

        private ToolStripMenuItem EnabledMenuItem { get; set; } = null!;
        private ToolStripMenuItem ChatStatus { get; set; } = null!;
        private ToolStripMenuItem OfflineStatus { get; set; } = null!;
        private ToolStripMenuItem MobileStatus { get; set; } = null!;

        internal event EventHandler? ConnectionErrored;

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

        private void UpdateTray()
        {
            var aboutMenuItem = new ToolStripMenuItem(StartupHandler.DeceiveTitle)
            {
                Enabled = false
            };

            EnabledMenuItem = new ToolStripMenuItem("Enabled", null, (_, _) =>
            {
                Enabled = !Enabled;
                UpdateStatus(Enabled ? Status : "chat");
                SendMessageFromFakePlayer(Enabled ? "Deceive is now enabled." : "Deceive is now disabled.");
                UpdateTray();
            })
            {
                Checked = Enabled
            };

            var mucMenuItem = new ToolStripMenuItem("Enable lobby chat", null, (_, _) =>
            {
                ConnectToMuc = !ConnectToMuc;
                UpdateTray();
            })
            {
                Checked = ConnectToMuc
            };

            ChatStatus = new ToolStripMenuItem("Online", null, (_, _) =>
            {
                UpdateStatus(Status = "chat");
                Enabled = true;
                UpdateTray();
            })
            {
                Checked = Status.Equals("chat")
            };

            OfflineStatus = new ToolStripMenuItem("Offline", null, (_, _) =>
            {
                UpdateStatus(Status = "offline");
                Enabled = true;
                UpdateTray();
            })
            {
                Checked = Status.Equals("offline")
            };

            MobileStatus = new ToolStripMenuItem("Mobile", null, (_, _) =>
            {
                UpdateStatus(Status = "mobile");
                Enabled = true;
                UpdateTray();
            })
            {
                Checked = Status.Equals("mobile")
            };

            var typeMenuItem = new ToolStripMenuItem("Status Type", null, ChatStatus, OfflineStatus, MobileStatus);

            var quitMenuItem = new ToolStripMenuItem("Quit", null, (_, _) =>
            {
                var result = MessageBox.Show(
                    "Are you sure you want to stop Deceive? This will also stop related games if they are running.",
                    StartupHandler.DeceiveTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if (result != DialogResult.Yes) return;

                Utils.KillProcesses();
                SaveStatus();
                Application.Exit();
            });

            TrayIcon.ContextMenuStrip = new ContextMenuStrip();

#if DEBUG
            var closeIn = new ToolStripMenuItem("Close incoming", null, (_, _) => { Incoming.Close(); });
            var closeOut = new ToolStripMenuItem("Close outgoing", null, (_, _) => { Outgoing.Close(); });
            var createFakePlayer = new ToolStripMenuItem("Resend fake player", null, (_, _) => { CreateFakePlayer(); });
            var sendTestMsg = new ToolStripMenuItem("Send message", null, (_, _) => { SendMessageFromFakePlayer("Test"); });

            TrayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[] { aboutMenuItem, EnabledMenuItem, typeMenuItem, mucMenuItem, closeIn, closeOut, createFakePlayer, sendTestMsg, quitMenuItem });
#else
            _trayIcon.ContextMenuStrip.Items.AddRange(new ToolStripItem[] {aboutMenuItem, _enabledMenuItem, typeMenuItem, mucMenuItem, quitMenuItem});
#endif
        }

        public void StartThreads(SslStream incoming, SslStream outgoing)
        {
            Incoming = incoming;
            Outgoing = outgoing;
            Connected = true;
            CreatedFakePlayer = false;

            new Thread(IncomingLoop).Start();
            new Thread(OutgoingLoop).Start();
        }

        private void IncomingLoop()
        {
            try
            {
                int byteCount;
                var bytes = new byte[8192];

                do
                {
                    byteCount = Incoming.Read(bytes, 0, bytes.Length);

                    var content = Encoding.UTF8.GetString(bytes, 0, byteCount);

                    // If this is possibly a presence stanza, rewrite it.
                    if (content.Contains("<presence") && Enabled)
                    {
                        PossiblyRewriteAndResendPresence(content, Status);
                        Trace.WriteLine("<!--RC TO SERVER ORIGINAL-->" + content);
                    }
                    else if (content.Contains("41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net"))
                    {
                        if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(content, "offline", CompareOptions.IgnoreCase) >= 0)
                        {
                            if (!Enabled) SendMessageFromFakePlayer("Deceive is now enabled.");
                            OfflineStatus.PerformClick();
                        }
                        else if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(content, "mobile", CompareOptions.IgnoreCase) >= 0)
                        {
                            if (!Enabled) SendMessageFromFakePlayer("Deceive is now enabled.");
                            MobileStatus.PerformClick();
                        }
                        else if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(content, "online", CompareOptions.IgnoreCase) >= 0)
                        {
                            if (!Enabled) SendMessageFromFakePlayer("Deceive is now enabled.");
                            ChatStatus.PerformClick();
                        }
                        else if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(content, "enable", CompareOptions.IgnoreCase) >= 0)
                        {
                            if (Enabled) SendMessageFromFakePlayer("Deceive is already enabled.");
                            else EnabledMenuItem.PerformClick();
                        }
                        else if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(content, "disable", CompareOptions.IgnoreCase) >= 0)
                        {
                            if (!Enabled) SendMessageFromFakePlayer("Deceive is already disabled.");
                            else EnabledMenuItem.PerformClick();
                        }
                        else if (CultureInfo.InvariantCulture.CompareInfo.IndexOf(content, "status", CompareOptions.IgnoreCase) >= 0)
                        {
                            if (Status == "chat")
                                SendMessageFromFakePlayer("You are appearing online.");
                            else
                                SendMessageFromFakePlayer("You are appearing " + Status + ".");
                        }

                        //Don't send anything involving our fake user to chat servers
                        Trace.WriteLine("<!--RC TO SERVER REMOVED-->" + content);
                    }
                    else
                    {
                        Outgoing.Write(bytes, 0, byteCount);
                        Trace.WriteLine("<!--RC TO SERVER-->" + content);
                    }
                } while (byteCount != 0 && Connected);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
            }
            finally
            {
                Trace.WriteLine("Incoming closed.");
                SaveStatus();
                if (Connected) OnConnectionErrored();
            }
        }

        private void OutgoingLoop()
        {
            try
            {
                int byteCount;
                var bytes = new byte[8192];

                do
                {
                    byteCount = Outgoing.Read(bytes, 0, bytes.Length);
                    Incoming.Write(bytes, 0, byteCount);
                    Trace.WriteLine("<!--SERVER TO RC-->" + Encoding.UTF8.GetString(bytes, 0, byteCount));
                } while (byteCount != 0 && Connected);

                Trace.WriteLine("Outgoing closed.");
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
                Trace.WriteLine("Outgoing errored.");
                SaveStatus();
                if (Connected) OnConnectionErrored();
            }
        }

        private void PossiblyRewriteAndResendPresence(string content, string targetStatus)
        {
            try
            {
                LastPresence = content;
                var wrappedContent = "<xml>" + content + "</xml>";
                var xml = XDocument.Load(new StringReader(wrappedContent));

                if (xml.Root == null) return;
                if (xml.Root.HasElements == false) return;

                foreach (var presence in xml.Root.Elements())
                {
                    if (presence.Name != "presence") continue;
                    if (presence.Attribute("to") != null)
                    {
                        if (ConnectToMuc) continue;
                        presence.Remove();
                    }

                    if (!CreatedFakePlayer)
                    {
                        CreateFakePlayer();
                    }

                    if (targetStatus != "chat" || presence.Element("games")?.Element("league_of_legends")?.Element("st")?.Value != "dnd")
                    {
                        presence.Element("show")?.ReplaceNodes(targetStatus);
                        presence.Element("games")?.Element("league_of_legends")?.Element("st")?.ReplaceNodes(targetStatus);
                    }

                    if (targetStatus == "chat") continue;
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

                    //Remove Legends of Runeterra presence
                    presence.Element("games")?.Element("bacon")?.Remove();

                    //Remove VALORANT presence
                    presence.Element("games")?.Element("valorant")?.Remove();
                }

                var sb = new StringBuilder();
                var xws = new XmlWriterSettings { OmitXmlDeclaration = true, Encoding = Encoding.UTF8, ConformanceLevel = ConformanceLevel.Fragment };
                using (var xw = XmlWriter.Create(sb, xws))
                {
                    foreach (var xElement in xml.Root.Elements())
                    {
                        xElement.WriteTo(xw);
                    }
                }

                Outgoing.Write(Encoding.UTF8.GetBytes(sb.ToString()));
                Trace.WriteLine("<!--DECEIVE TO SERVER-->" + sb);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
                Trace.WriteLine("Error rewriting presence.");
            }
        }

        private async void CreateFakePlayer()
        {
            CreatedFakePlayer = true;

            const string subscriptionMessage =
                "<iq from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' id='fake-player' type='set'>" +
                "<query xmlns='jabber:iq:riotgames:roster'>" +
                "<item jid='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' name='&#9;Deceive Active!' subscription='both' puuid='41c322a1-b328-495b-a004-5ccd3e45eae8'>" +
                "<group priority='9999'>Deceive</group>" +
                "<id name='&#9;Deceive Active!' tagline=''/> <lol name='&#9;Deceive Active!'/>" +
                "</item>" +
                "</query>" +
                "</iq>";

            const string presenceMessage =
                "<presence from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Deceive' id='fake-player-2'>" +
                "<games>" +
                "<keystone><st>chat</st><s.p>keystone</s.p></keystone>" +
                "<league_of_legends><st>chat</st><s.p>league_of_legends</s.p><p>{&quot;pty&quot;:true}</p></league_of_legends>" + // No Region s.r keeps it in the main "League" category rather than "Other Servers" in every region with "Group Games & Servers" active 
                "<valorant><st>chat</st><s.p>valorant</s.p><p>ewoJImlzVmFsaWQiOiB0cnVlLAoJInBhcnR5SWQiOiAiMDAwMDAwMDAtMDAwMC0wMDAwLTAwMDAtMDAwMDAwMDAwMDAwIiwKCSJwYXJ0eUNsaWVudFZlcnNpb24iOiAicmVsZWFzZS0wMS4wNS1zaGlwcGluZy0xMC00NjAxMjkiCn0=</p></valorant>" +
                "<bacon><st>chat</st><s.l>bacon_availability_online</s.l><s.p>bacon</s.p><s.t>1596633825489</s.t></bacon>" + // Timestamp needed or it will show offline
                "</games>" +
                "<show>chat</show>" +
                "</presence>";

            var bytes = Encoding.UTF8.GetBytes(subscriptionMessage);
            Incoming.Write(bytes, 0, bytes.Length);
            Trace.WriteLine("<!--DECEIVE TO RC-->" + subscriptionMessage);

            await Task.Delay(200);

            bytes = Encoding.UTF8.GetBytes(presenceMessage);
            Incoming.Write(bytes, 0, bytes.Length);
            Trace.WriteLine("<!--DECEIVE TO RC-->" + presenceMessage);


            await Task.Delay(10000);

            if (SentIntroductionText) return;
            SentIntroductionText = true;

            SendMessageFromFakePlayer("Welcome! Deceive is running and you are currently appearing " + Status +
                                      ". Despite what the game client may indicate, you are appearing offline to your friends unless you manually disable Deceive.");
            await Task.Delay(200);
            SendMessageFromFakePlayer(
                "If you want to invite others while being offline, you may need to disable Deceive for them to accept. You can enable Deceive again as soon as they are in your lobby.");
            await Task.Delay(200);
            SendMessageFromFakePlayer("To enable or disable Deceive, or to configure other settings, find Deceive in your tray icons.");
            await Task.Delay(200);
            SendMessageFromFakePlayer("Have fun!");
        }

        private void SendMessageFromFakePlayer(string message)
        {
            var stamp = DateTime.UtcNow.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss.fff");

            var chatMessage =
                $"<message from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-Deceive' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>{message}</body></message>";

            var bytes = Encoding.UTF8.GetBytes(chatMessage);
            Incoming.Write(bytes, 0, bytes.Length);
            Trace.WriteLine("<!--DECEIVE TO RC-->" + chatMessage);
        }

        private void UpdateStatus(string newStatus)
        {
            if (string.IsNullOrEmpty(LastPresence)) return;

            PossiblyRewriteAndResendPresence(LastPresence, newStatus);

            if (newStatus == "chat")
            {
                SendMessageFromFakePlayer("You are now appearing online.");
            }
            else
            {
                SendMessageFromFakePlayer("You are now appearing " + newStatus + ".");
            }
        }

        private void LoadStatus()
        {
            if (File.Exists(StatusFile)) Status = File.ReadAllText(StatusFile) == "mobile" ? "mobile" : "offline";
            else Status = "offline";
        }

        private void SaveStatus()
        {
            File.WriteAllText(StatusFile, Status);
        }

        private void OnConnectionErrored()
        {
            Connected = false;
            ConnectionErrored?.Invoke(this, EventArgs.Empty);
        }
    }
}