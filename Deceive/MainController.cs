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

            _trayIcon.ContextMenu = new ContextMenu(new[] {aboutMenuItem, enabledMenuItem, typeMenuItem, overlayMenuItem, mucMenuItem, closeIn, closeOut, quitMenuItem});
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

        private void UpdateStatus(string newStatus)
        {
            if (string.IsNullOrEmpty(_lastPresence)) return;

            PossiblyRewriteAndResendPresence(_lastPresence, newStatus);
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