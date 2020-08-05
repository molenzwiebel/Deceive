using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        private readonly NotifyIcon _trayIcon;
        private bool _enabled = true;
        private string _status;
        private readonly string _statusFile = Path.Combine(Utils.DataDir, "status");
        private bool _connectToMuc = true;
        private bool _createdFakePlayer = false;
        private bool _sentIntroductionText = false;

        private LCUOverlay _overlay;
        private WindowFollower _follower;

        private SslStream _incoming;
        private SslStream _outgoing;
        private bool _connected;
        private string _lastPresence; // we resend this if the state changes

        internal event EventHandler ConnectionErrored;

        internal MainController(bool createOverlay)
        {
            _trayIcon = new NotifyIcon
            {
                Icon = Resources.DeceiveIcon,
                Visible = true,
                BalloonTipTitle = StartupHandler.DeceiveTitle,
                BalloonTipText = "Deceive is currently masking your status. Right-Click the tray icon for more options."
            };
            _trayIcon.ShowBalloonTip(5000);

            // Create overlay and start following the LCU with it.
            if (createOverlay) CreateOverlay();

            LoadStatus();
            UpdateTray();
        }

        private async void CreateOverlay()
        {
            while (true)
            {
                var process = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
                if (process == null)
                {
                    await Task.Delay(5000);
                    continue;
                }

                _overlay = new LCUOverlay();
                _overlay.Show();
                _follower = new WindowFollower(_overlay, process);
                _follower.StartFollowing();
                return;
            }
        }

        private void UpdateTray()
        {
            var aboutMenuItem = new MenuItem(StartupHandler.DeceiveTitle)
            {
                Enabled = false
            };

            var enabledMenuItem = new MenuItem("Enabled", (a, e) =>
            {
                _enabled = !_enabled;
                UpdateStatus(_enabled ? _status : "chat");
                UpdateTray();
            })
            {
                Checked = _enabled
            };

            var overlayMenuItem = new MenuItem("Show status overlay", (a, e) =>
            {
                if (_overlay == null)
                {
                    CreateOverlay();
                }
                else
                {
                    _follower.Dispose();
                    _overlay.Close();
                    _overlay = null;
                }

                UpdateTray();
            })
            {
                Checked = _overlay != null
            };

            var mucMenuItem = new MenuItem("Enable lobby chat", (a, e) =>
            {
                _connectToMuc = !_connectToMuc;
                UpdateTray();
            })
            {
                Checked = _connectToMuc
            };

            var chatStatus = new MenuItem("Chat", (a, e) =>
            {
                UpdateStatus(_status = "chat");
                _enabled = true;
                UpdateTray();
            })
            {
                Checked = _status.Equals("chat")
            };

            var offlineStatus = new MenuItem("Offline", (a, e) =>
            {
                UpdateStatus(_status = "offline");
                _enabled = true;
                UpdateTray();
            })
            {
                Checked = _status.Equals("offline")
            };

            var mobileStatus = new MenuItem("Mobile", (a, e) =>
            {
                UpdateStatus(_status = "mobile");
                _enabled = true;
                UpdateTray();
            })
            {
                Checked = _status.Equals("mobile")
            };

            var typeMenuItem = new MenuItem("Status Type", new[] {chatStatus, offlineStatus, mobileStatus});

            var quitMenuItem = new MenuItem("Quit", (a, b) =>
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

#if DEBUG
            var closeIn = new MenuItem("Close incoming", (a, e) => { _incoming.Close(); });
            var closeOut = new MenuItem("Close outgoing", (a, e) => { _outgoing.Close(); });
            var sendTestMsg = new MenuItem("Send message", (a, e) => { SendMessageFromFakePlayer("Test"); });

            _trayIcon.ContextMenu = new ContextMenu(new[] {aboutMenuItem, enabledMenuItem, typeMenuItem, overlayMenuItem, mucMenuItem, closeIn, closeOut, sendTestMsg, quitMenuItem});
#else
            _trayIcon.ContextMenu = new ContextMenu(new[] {aboutMenuItem, enabledMenuItem, typeMenuItem, overlayMenuItem, mucMenuItem, quitMenuItem});
#endif
            _overlay?.UpdateStatus(_status, _enabled);
        }

        public void StartThreads(SslStream incoming, SslStream outgoing)
        {
            _incoming = incoming;
            _outgoing = outgoing;
            _connected = true;
            _createdFakePlayer = false;

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
                    byteCount = _incoming.Read(bytes, 0, bytes.Length);

                    var content = Encoding.UTF8.GetString(bytes, 0, byteCount);
                    Trace.WriteLine("<!--FROM RC-->" + content);

                    // If this is possibly a presence stanza, rewrite it.
                    if (content.Contains("<presence") && _enabled)
                    {
                        PossiblyRewriteAndResendPresence(content, _status);
                    }
                    else
                    {
                        _outgoing.Write(bytes, 0, byteCount);
                    }
                } while (byteCount != 0 && _connected);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
            }
            finally
            {
                Trace.WriteLine(@"Incoming closed.");
                SaveStatus();
                if (_connected) OnConnectionErrored();
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
                    byteCount = _outgoing.Read(bytes, 0, bytes.Length);
                    Trace.WriteLine("<!--TO RC-->" + Encoding.UTF8.GetString(bytes, 0, byteCount));
                    _incoming.Write(bytes, 0, byteCount);
                } while (byteCount != 0 && _connected);

                Trace.WriteLine(@"Outgoing closed.");
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
                Trace.WriteLine(@"Outgoing errored.");
                SaveStatus();
                if (_connected) OnConnectionErrored();
            }
        }

        private void PossiblyRewriteAndResendPresence(string content, string targetStatus)
        {
            try
            {
                _lastPresence = content;
                var wrappedContent = "<xml>" + content + "</xml>";
                var xml = XDocument.Load(new StringReader(wrappedContent));

                if (xml.Root == null) return;
                if (xml.Root.HasElements == false) return;

                foreach (var presence in xml.Root.Elements())
                {
                    if (presence.Name != "presence") continue;
                    if (presence.Attribute("to") != null)
                    {
                        if (_connectToMuc) continue;
                        presence.Remove();
                    }

                    if (!_createdFakePlayer)
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
                var xws = new XmlWriterSettings {OmitXmlDeclaration = true, Encoding = Encoding.UTF8, ConformanceLevel = ConformanceLevel.Fragment};
                using (var xw = XmlWriter.Create(sb, xws))
                {
                    foreach (var xElement in xml.Root.Elements())
                    {
                        xElement.WriteTo(xw);
                    }
                }

                _outgoing.Write(Encoding.UTF8.GetBytes(sb.ToString()));
                Trace.WriteLine("<!--DECEIVE-->" + sb);
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
                Trace.WriteLine(@"Error rewriting presence.");
            }
        }
        
        private async void CreateFakePlayer()
        {
            _createdFakePlayer = true;

            await Task.Delay(5000);
            
            var subscriptionMessage =
                $"<iq from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' to='RC-1' id='fake-player' type='set'>" +
                $"<query xmlns='jabber:iq:riotgames:roster'>" +
                $"<item jid='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net' name='&#9;Deceive Active!' subscription='both' puuid='41c322a1-b328-495b-a004-5ccd3e45eae8'>" +
                $"<group priority='10000000'>Deceive</group>" +
                $"<id name='&#9;Deceive Active!' tagline=''/><lol name='&#9;Deceive Active!'/>" +
                $"</item>" +
                $"</query>" +
                $"</iq>";

            var presenceMessage =
                $"<presence from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-1' to='a' id='fake-player-2'>" +
                $"<games>" +
                $"<keystone><st>chat</st><s.t>1596626820587</s.t><m>Appearing offline.</m><s.p>keystone</s.p></keystone>" +
                $"<league_of_legends><st>chat</st><s.r>EUW1</s.r><s.t>1596628992873</s.t><s.p>league_of_legends</s.p><s.c>live</s.c><m>Appearing offline.</m><p>{{&quot;pty&quot;:true}}</p></league_of_legends>" +
                $"<valorant><st>chat</st><s.t>1596630374381</s.t><s.d/><s.l/><m/><s.a/><p>eyJpc1ZhbGlkIjp0cnVlLCJzZXNzaW9uTG9vcFN0YXRlIjoiSU5HQU1FIiwicGFydHlPd25lclNlc3Npb25Mb29wU3RhdGUiOiJJTkdBTUUiLCJjdXN0b21HYW1lTmFtZSI6IiIsImN1c3RvbUdhbWVUZWFtIjoiIiwicGFydHlPd25lck1hdGNoTWFwIjoiL0dhbWUvTWFwcy9UcmlhZC9UcmlhZCIsInBhcnR5T3duZXJNYXRjaEN1cnJlbnRUZWFtIjoiQmx1ZSIsInBhcnR5T3duZXJNYXRjaFNjb3JlQWxseVRlYW0iOjEwMCwicGFydHlPd25lck1hdGNoU2NvcmVFbmVteVRlYW0iOjMsInBhcnR5T3duZXJQcm92aXNpb25pbmdGbG93IjoiTWF0Y2htYWtpbmciLCJwcm92aXNpb25pbmdGbG93IjoiTWF0Y2htYWtpbmciLCJtYXRjaE1hcCI6Ii9HYW1lL01hcHMvVHJpYWQvVHJpYWQiLCJwYXJ0eUlkIjoiMDAwMDAwMDAtMDAwMC0wMDAwLTAwMDAtMDAwMDAwMDAwMDEiLCJpc1BhcnR5T3duZXIiOnRydWUsInBhcnR5TmFtZSI6IlBpbmF0UGFydHkiLCJwYXJ0eVN0YXRlIjoiREVGQVVMVCIsInBhcnR5QWNjZXNzaWJpbGl0eSI6IkNMT1NFRCIsIm1heFBhcnR5U2l6ZSI6MTAwMCwicXVldWVJZCI6InVucmF0ZWQiLCJwYXJ0eUxGTSI6ZmFsc2UsInBhcnR5Q2xpZW50VmVyc2lvbiI6ImEiLCJwYXJ0eVNpemUiOi0xLCJwYXJ0eVZlcnNpb24iOjE1OTY2MzI5Nzc4NzMsInF1ZXVlRW50cnlUaW1lIjoiMjAyMC4wNC4yMC0yMy40Ny4xOSIsInBsYXllckNhcmRJZCI6IjhiMGMxNDkyLTQ1OTQtZjgxNS1jZDA0LWQzOGExYzJlM2Y4NiIsInBsYXllclRpdGxlSWQiOiJmZDdiMDQwNi00NmU1LTVhZTYtNWUzYi01OTgyMTMxZTdjZDgiLCJhbGciOiJIUzI1NiJ9</p><s.p>valorant</s.p></valorant>" +
                $"<bacon><st>chat</st><s.r>europe</s.r><s.d></s.d><m></m><s.l>bacon_availability_online</s.l><p>e</p><s.p>bacon</s.p><s.c>live</s.c><s.a></s.a><s.t>1596633825489</s.t></bacon>" +
                $"</games>" +
                $"<show>chat</show>" +
                $"</presence>";
            
            var bytes = Encoding.UTF8.GetBytes(subscriptionMessage);
            _incoming.Write(bytes, 0, bytes.Length);

            await Task.Delay(200);

            bytes = Encoding.UTF8.GetBytes(presenceMessage);
            _incoming.Write(bytes, 0, bytes.Length);

            await Task.Delay(5000);

            if (!_sentIntroductionText)
            {
                _sentIntroductionText = true;
                
                SendMessageFromFakePlayer("Welcome! Deceive is running and you are currently appearing " + _status + ". Despite what the game client may indicate, you are appearing offline to your friends unless you manually disable Deceive.");
                await Task.Delay(200);
                SendMessageFromFakePlayer("If you want to invite others while being offline, you may need to disable Deceive for them to accept. You can enable Deceive again as soon as they are in your lobby.");
                await Task.Delay(200);
                SendMessageFromFakePlayer("To enable or disable Deceive, or to configure other settings, find Deceive in your tray icons.");
                await Task.Delay(200);
                SendMessageFromFakePlayer("Have fun!");
            }
        }

        private void SendMessageFromFakePlayer(string message)
        {
            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");

            var chatMessage =
                $"<message from='41c322a1-b328-495b-a004-5ccd3e45eae8@eu1.pvp.net/RC-1' to='a' stamp='{stamp}' id='fake-{stamp}' type='chat'><body>{message}</body></message>";
            
            var bytes = Encoding.UTF8.GetBytes(chatMessage);
            _incoming.Write(bytes, 0, bytes.Length);
        }

        private void UpdateStatus(string newStatus)
        {
            if (string.IsNullOrEmpty(_lastPresence)) return;

            PossiblyRewriteAndResendPresence(_lastPresence, newStatus);

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
            if (File.Exists(_statusFile)) _status = File.ReadAllText(_statusFile) == "mobile" ? "mobile" : "offline";
            else _status = "offline";
        }

        private void SaveStatus()
        {
            File.WriteAllText(_statusFile, _status);
        }

        private void OnConnectionErrored()
        {
            _connected = false;
            ConnectionErrored?.Invoke(this, EventArgs.Empty);
        }
    }
}