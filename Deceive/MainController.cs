using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Deceive.Properties;
using WebSocketSharp;

namespace Deceive
{
    internal class MainController : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private bool _enabled = true;
        private string _status;
        private WebSocket _ws;
        private readonly string _statusFile = Path.Combine(Utils.DataDir, "status");

        private SslStream _incoming;
        private SslStream _outgoing;
        private string _lastPresence; // we resend this if the state changes

        internal MainController()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = Resources.deceive,
                Visible = true,
                BalloonTipTitle = StartupHandler.DeceiveTitle,
                BalloonTipText = "Deceive is currently masking your status. Right-Click the tray icon for more options."
            };
            _trayIcon.ShowBalloonTip(5000);
            LoadStatus();
            SetupMenuItems();
            InitLcuStatus();
        }

        private async void InitLcuStatus()
        {
            while (true)
            {
                if ((_ws = Utils.MonitorChatStatusChange(_status, _enabled)) == null)
                {
                    // LCU is not ready yet. Wait for a bit.
                    await Task.Delay(3000);
                    
                } else return;
            }
        }

        private void SetupMenuItems()
        {
            var aboutMenuItem = new MenuItem(StartupHandler.DeceiveTitle)
            {
                Enabled = false
            };

            var enabledMenuItem = new MenuItem("Enabled", (a, e) =>
            {
                _enabled = !_enabled;
                UpdateStatus(_enabled ? _status : "chat");
                SetupMenuItems();
            })
            {
                Checked = _enabled
            };

            var offlineStatus = new MenuItem("Offline", (a, e) =>
            {
                UpdateStatus(_status = "offline");
                _enabled = true;
                SetupMenuItems();
            })
            {
                Checked = _status.Equals("offline")
            };

            var mobileStatus = new MenuItem("Mobile", (a, e) =>
            {
                UpdateStatus(_status = "mobile");
                _enabled = true;
                SetupMenuItems();
            })
            {
                Checked = _status.Equals("mobile")
            };

            var typeMenuItem = new MenuItem("Status Type", new[] { offlineStatus, mobileStatus });

            var quitMenuItem = new MenuItem("Quit", (a, b) =>
            {
                var result = MessageBox.Show(
                    "Are you sure you want to stop Deceive? This will also stop League if it is running.",
                    StartupHandler.DeceiveTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if (result != DialogResult.Yes) return;

                Utils.KillClients();
                SaveStatus();
                Application.Exit();
            });

            _trayIcon.ContextMenu = new ContextMenu(new[] { aboutMenuItem, enabledMenuItem, typeMenuItem, quitMenuItem });
        }

        public void StartThreads(SslStream incoming, SslStream outgoing)
        {
            _incoming = incoming;
            _outgoing = outgoing;

            new Thread(IncomingLoop).Start();
            new Thread(OutgoingLoop).Start();
        }

        private void IncomingLoop()
        {
            try
            {
                int byteCount;
                var bytes = new byte[2048];

                do
                {
                    byteCount = _incoming.Read(bytes, 0, bytes.Length);

                    var content = Encoding.UTF8.GetString(bytes, 0, byteCount);
                    // If this is possibly a presence stanza, rewrite it.
                    if (content.Contains("<presence") && _enabled)
                    {
                        PossiblyRewriteAndResendPresence(content, _status);
                    }
                    else
                    {
                        _outgoing.Write(bytes, 0, byteCount);
                    }
                } while (byteCount != 0);
            }
            finally
            {
                Trace.WriteLine(@"Incoming closed.");
                SaveStatus();
                Application.Exit();
            }
        }

        private void OutgoingLoop()
        {
            try
            {
                int byteCount;
                var bytes = new byte[2048];

                do
                {
                    byteCount = _outgoing.Read(bytes, 0, bytes.Length);
                    _incoming.Write(bytes, 0, byteCount);
                } while (byteCount != 0);

                Trace.WriteLine(@"Outgoing closed.");
            }
            catch
            {
                Trace.WriteLine(@"Outgoing errored.");
                SaveStatus();
                Application.Exit();
            }
        }

        private void PossiblyRewriteAndResendPresence(string content, string targetStatus)
        {
            try
            {
                var xml = XDocument.Load(new StringReader(content));
                
                var presence = xml.Element("presence");
                if (presence != null && presence.Attribute("to") == null)
                {
                    _lastPresence = content;
                    presence.Element("show").Value = targetStatus;

                    if (targetStatus != "chat")
                    {
                        presence.Element("status")?.Remove();
                        presence.Element("games")?.Element("league_of_legends")?.Remove();
                    }

                    content = presence.ToString();
                }
                _outgoing.Write(Encoding.UTF8.GetBytes(content));
            }
            catch
            {
                Trace.WriteLine(@"Error rewriting presence. Sending the raw value.");
                _outgoing.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        private void UpdateStatus(string newStatus)
        {
            if (string.IsNullOrEmpty(_lastPresence)) return;

            PossiblyRewriteAndResendPresence(_lastPresence, newStatus);
            _ws.Close();
            Utils.SendStatusToLcu(newStatus);
            _ws = Utils.MonitorChatStatusChange(newStatus, _enabled);
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
    }
}
