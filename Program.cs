using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Drawing;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using WebSocketSharp;

namespace AppearOffline
{
    class Program : ApplicationContext
    {
        static string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppearOffline");
        static WebSocket socket;
        static Tuple<string, string> lockfileContents;

        static string status = "offline";
        static bool enabled = true;

        private NotifyIcon trayIcon;

        Program()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Shield,
                Visible = true,
                BalloonTipTitle = "Appear Offline",
                BalloonTipText = "Appear Offline will run in the background. Right-Click the shield tray icon for more options."
            };
            trayIcon.ShowBalloonTip(5000);
            SetupMenuItems();
        }

        private void SetupMenuItems()
        {
            var aboutMenuItem = new MenuItem("Appear Offline v0.1");
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
                UpdateStatus("online"); // make sure the user is offline before we quit
                Application.Exit();
            });

            trayIcon.ContextMenu = new ContextMenu(new MenuItem[] { aboutMenuItem, enabledMenuItem, typeMenuItem, quitMenuItem });
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

            string lcuPath = GetLCUPath();
            if (lcuPath == null) return;
            string lolDir = Path.GetDirectoryName(lcuPath) + "\\";

            StartWatching(lolDir);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Program());
        }

        /**
         * Sets the user to the specified status, forcing their gameStatus to be outOfGame.
         * Does nothing if league isn't currently running.
         */
        static void UpdateStatus(string stat)
        {
            if (socket == null) return;
            UpdateMeStatus(lockfileContents, "{ \"availability\": \"" + stat + "\", \"lol\": { \"gameStatus\": \"outOfGame\" } }");
        }

        /**
         * Called when League is started. Opens the socket and starts listening for events.
         */
        static void OnLeagueStart(Tuple<string, string> lockfileInfo)
        {
            // Update status in case the user was already logged in.
            lockfileContents = lockfileInfo;
            UpdateStatus(status);

            socket = new WebSocket("wss://127.0.0.1:" + lockfileInfo.Item1 + "/", "wamp");
            socket.SetCredentials("riot", lockfileInfo.Item2, true);
            socket.SslConfiguration.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            socket.SslConfiguration.ServerCertificateValidationCallback = (a, b, c, d) => true;
            socket.OnMessage += (sender, e) =>
            {
                if (!enabled) return;

                var data = SimpleJson.DeserializeObject<JsonArray>(e.Data);
                if ((long)data[0] == 8 && ((string)data[1]).Equals("OnJsonApiEvent"))
                {
                    // Only monitor chat endpoint.
                    var ev = (JsonObject)data[2];
                    if (!ev.ContainsKey("uri") || !ev.ContainsKey("data")) return;

                    var uri = (string)ev["uri"];
                    if (uri.Equals("/lol-login/v1/session"))
                    {
                        // The user might have just logged in, update their status.
                        UpdateStatus(status);
                        return;
                    }

                    if (!uri.Equals("/lol-chat/v1/me")) return;

                    // Only act if we aren't offline yet.
                    var me = (JsonObject)ev["data"];
                    var lol = (JsonObject)me["lol"];
                    if (((string)me["availability"]).Equals(status) && ((string)lol["gameStatus"]).Equals("outOfGame")) return;

                    UpdateStatus(status);
                }
            };
            socket.Connect();
            // Subscribe to Json API events.
            socket.Send("[5,\"OnJsonApiEvent\"]");
        }

        /**
         * Called when League is terminated. Closes the socket.
         */
        static void OnLeagueStop()
        {
            socket.Close();
            socket = null;   
        }

        /**
         * Loads a tuple of (port, auth token) from the specified lockfile.
         */
        static Tuple<string, string> LoadLockfile(string leagueLocation)
        {
            // The lockfile is locked (heh), so we copy it first.
            File.Copy(leagueLocation + "lockfile", leagueLocation + "lockfile-temp");
            var contents = File.ReadAllText(leagueLocation + "lockfile-temp", Encoding.UTF8).Split(':');
            File.Delete(leagueLocation + "lockfile-temp");
            return new Tuple<string, string>(contents[2], contents[3]);
        }

        /**
         * Configures a FileSystemWatcher to watch the specified league location for changes to the lockfile.
         * This will immediately call OnLeagueStart if League was already running.
         */
        static void StartWatching(string leagueLocation)
        {
            var watcher = new FileSystemWatcher();
            watcher.Path = leagueLocation;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastAccess;
            watcher.Filter = "lockfile";

            watcher.Created += (o, e) =>
            {
                // Lockfile was created, connect to the socket.
                OnLeagueStart(LoadLockfile(leagueLocation));
            };

            watcher.Deleted += (o, e) =>
            {
                // Lockfile was deleted, close the socket.
                OnLeagueStop();
            };

            watcher.EnableRaisingEvents = true;

            // Check if we launched while league was already active.
            if (File.Exists(leagueLocation + "lockfile"))
            {
                OnLeagueStart(LoadLockfile(leagueLocation));
            }
        }

        /**
         * Uses a raw TCP socket to PUT the new /lol-chat/v1/me status.
         */
        static void UpdateMeStatus(Tuple<string, string> connectionInfo, string payload)
        {
            using (var client = new TcpClient("127.0.0.1", int.Parse(connectionInfo.Item1)))
            using (var stream = new SslStream(client.GetStream(), true, (a, b, c, d) => true))
            {
                stream.AuthenticateAsClient("127.0.0.1", null, System.Security.Authentication.SslProtocols.Tls12, false);

                var bytes = Encoding.UTF8.GetBytes(payload);
                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + connectionInfo.Item2));

                var request =
                    "PUT /lol-chat/v1/me HTTP/1.1\r\n"
                    + "Host: 127.0.0.1:" + connectionInfo.Item1 + "\r\n"
                    + "Authorization: Basic " + auth + "\r\n"
                    + "Connection: close\r\n"
                    + "Content-Type: application/json\r\n"
                    + "Content-Length: " + bytes.Length + "\r\n"
                    + "\r\n";

                stream.Write(Encoding.UTF8.GetBytes(request));
                stream.Write(bytes);
            }
        }

        /**
         * Either gets the LCU path from the saved properties, or by prompting the user.
         */
        static string GetLCUPath()
        {
            string configPath = Path.Combine(dataDir, "lcuPath");
            string path = File.Exists(configPath) ? File.ReadAllText(configPath) : "C:/Riot Games/League of Legends/LeagueClient.exe";
            bool valid = IsValidLCUPath(path);

            while (!valid)
            {
                // Notify that the path is invalid.
                MessageBox.Show(
                    "Could not find the League client at " + path + ". Please select the location of 'LeagueClient.exe' manually.",
                    "LCU not found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation
                );

                // Ask for new path.
                CommonOpenFileDialog dialog = new CommonOpenFileDialog();
                dialog.Title = "Select LeagueClient.exe location.";
                dialog.InitialDirectory = "C:\\Riot Games\\League of Legends";
                dialog.EnsureFileExists = true;
                dialog.EnsurePathExists = true;
                dialog.DefaultFileName = "LeagueClient";
                dialog.DefaultExtension = "exe";
                dialog.Filters.Add(new CommonFileDialogFilter("Executables", ".exe"));
                dialog.Filters.Add(new CommonFileDialogFilter("All Files", ".*"));
                if (dialog.ShowDialog() == CommonFileDialogResult.Cancel)
                {
                    // User wants to cancel. Exit
                    return null;
                }

                path = dialog.FileName;
                valid = IsValidLCUPath(path);
            }

            // Store choice so we don't have to ask for it again.
            File.WriteAllText(configPath, path);

            return path;
        }

        /**
         * Checks if the provided path is most likely a path where the LCU is installed.
         */
        static bool IsValidLCUPath(string path)
        {
            string folder = Path.GetDirectoryName(path);
            return File.Exists(path) && Directory.Exists(folder + "/RADS") && Directory.Exists(folder + "/RADS/projects/league_client");
        }
    }
}
