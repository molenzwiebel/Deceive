using System.Drawing;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

namespace Deceive
{
    public class MainController : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private bool enabled = true;
        private string status = "offline";

        private SslStream incoming;
        private SslStream outgoing;
        private string lastPresence; // we resend this if the state changes

        public MainController()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = Properties.Resources.deceive,
                Visible = true,
                BalloonTipTitle = "Deceive",
                BalloonTipText = "Deceive is currently masking your status. Right-Click the tray icon for more options."
            };
            trayIcon.ShowBalloonTip(5000);
            SetupMenuItems();
        }

        private void SetupMenuItems()
        {
            var aboutMenuItem = new MenuItem("Deceive v1.0");
            aboutMenuItem.Enabled = false;

            var enabledMenuItem = new MenuItem("Enabled", (a, e) =>
            {
                enabled = !enabled;
                UpdateStatus(enabled ? status : "online");
                SetupMenuItems();
            });
            enabledMenuItem.Checked = enabled;

            var offlineStatus = new MenuItem("Offline", (a, e) =>
            {
                UpdateStatus(status = "offline");
                SetupMenuItems();
            });
            offlineStatus.Checked = status.Equals("offline");

            var mobileStatus = new MenuItem("Mobile", (a, e) =>
            {
                UpdateStatus(status = "mobile");
                SetupMenuItems();
            });
            mobileStatus.Checked = status.Equals("mobile");

            var typeMenuItem = new MenuItem("Status Type", new MenuItem[] { offlineStatus, mobileStatus });

            var quitMenuItem = new MenuItem("Quit", (a, b) =>
            {
                var result = MessageBox.Show(
                    "Are you sure you want to stop Deceive? This will also stop League if it is running.",
                    "Deceive",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if (result != DialogResult.Yes) return;

                Utils.KillLCU();
                Application.Exit();
            });

            trayIcon.ContextMenu = new ContextMenu(new MenuItem[] { aboutMenuItem, enabledMenuItem, typeMenuItem, quitMenuItem });
        }

        public void StartThreads(SslStream incoming, SslStream outgoing)
        {
            this.incoming = incoming;
            this.outgoing = outgoing;

            new Thread(() => this.IncomingLoop()).Start();
            new Thread(() => this.OutgoingLoop()).Start();
        }

        private void IncomingLoop()
        {
            var byteCount = 0;
            var bytes = new byte[2048];

            do
            {
                byteCount = this.incoming.Read(bytes, 0, bytes.Length);

                var content = Encoding.UTF8.GetString(bytes, 0, byteCount);

                // If this is possibly a presence stanza, rewrite it.
                if (content.Contains("<presence") && this.enabled)
                {
                    this.PossiblyRewriteAndResendPresence(content, this.status);
                } else
                {
                    this.outgoing.Write(bytes, 0, byteCount);
                }
            } while (byteCount != 0);

            System.Console.WriteLine("Incoming closed.");
            Application.Exit();
        }

        private void OutgoingLoop()
        {
            var byteCount = 0;
            var bytes = new byte[2048];

            do
            {
                byteCount = this.outgoing.Read(bytes, 0, bytes.Length);
                this.incoming.Write(bytes, 0, byteCount);
            } while (byteCount != 0);

            System.Console.WriteLine("Outgoing closed.");
        }

        private void PossiblyRewriteAndResendPresence(string content, string targetStatus)
        {
            try
            {
                var xml = new XmlDocument();
                xml.LoadXml(content);

                var presence = xml["presence"];
                if (presence != null && presence.Attributes["to"] == null)
                {
                    this.lastPresence = content;
                    presence["show"].InnerText = targetStatus;

                    if (targetStatus != "online")
                    {
                        var status = new XmlDocument();
                        status.LoadXml(presence["status"].InnerText);
                        status["body"]["statusMsg"].InnerText = "";
                        status["body"]["gameStatus"].InnerText = "outOfGame";

                        presence["status"].InnerText = status.OuterXml;
                    }

                    content = presence.OuterXml;
                }

                this.outgoing.Write(Encoding.UTF8.GetBytes(content));
            }
            catch
            {
                System.Console.WriteLine("Error rewriting presence. Sending the raw value.");
                this.outgoing.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        private void UpdateStatus(string status)
        {
            if (string.IsNullOrEmpty(this.lastPresence)) return;

            this.PossiblyRewriteAndResendPresence(this.lastPresence, status);
        }
    }
}
