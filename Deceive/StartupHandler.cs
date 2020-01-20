using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using Deceive.Properties;
using YamlDotNet.RepresentationModel;

namespace Deceive
{
    internal static class StartupHandler
    {
        internal static string DeceiveTitle => "Deceive " + Resources.DeceiveVersion;

        [STAThread]
        private static void Main()
        {
            try
            {
                StartDeceive();
            }
            catch (Exception ex)
            {
                // Show some kind of message so that Deceive doesn't just disappear.
                MessageBox.Show(
                    "Deceive encountered an error and couldn't properly initialize itself. Please contact the creator through GitHub (https://github.com/molenzwiebel/deceive) or Discord.\n\n" + ex,
                    DeceiveTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );
            }
        }

        /**
         * Actual main function. Wrapped into a separate function so we can catch exceptions.
         */
        private static void StartDeceive()
        {
            // We are supposed to launch league, so if it's already running something is going wrong.
            if (Utils.IsClientRunning())
            {
                var result = MessageBox.Show(
                    "League or the Riot Client is currently running. In order to mask your online status, League and the Riot Client needs to be started by Deceive. Do you want Deceive to stop League and the Riot Client, so that it can restart it with the proper configuration?",
                    DeceiveTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if (result != DialogResult.Yes) return;
                Utils.KillClients();
                Thread.Sleep(2000); // Riot Client takes a while to die
            }

            // Step 0: Check for updates in the background.
            Utils.CheckForUpdates();

            // Step 1: Open a port for our chat proxy, so we can patch chat port into clientconfig.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Step 2: Find the Riot Client.
            var riotClientPath = Utils.GetRiotClientPath();

            // If the riot client doesn't exist, the user is either severely outdated or has a bugged install.
            if (riotClientPath == null)
            {
                MessageBox.Show(
                    "Deceive was unable to find the path to the Riot Launcher. If you have League installed and it is working properly, please file a bug report through GitHub (https://github.com/molenzwiebel/deceive) or Discord.",
                    DeceiveTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );

                return;
            }

            // Step 3: Find the old config URL and start proxy web server (likely https://clientconfig.rpg.riotgames.com)
            var riotYamlContents = File.ReadAllText(Utils.GetSystemYamlPath(riotClientPath));
            var riotYaml = new YamlStream();
            riotYaml.Load(new StringReader(riotYamlContents));
            var root = riotYaml.Documents[0].RootNode;
            var oldConfigUrl = root["region_data"][root["default_region"].ToString()]["servers"]["client_config"]["client_config_url"].ToString();
            var proxyServer = new ConfigProxy(oldConfigUrl, port);

            // Step 4: Start the Riot Client and wait for a connect.
            var startArgs = new ProcessStartInfo
            {
                FileName = riotClientPath,
                Arguments = "--client-config-url=\"http://localhost:" + proxyServer.ConfigPort + "\" --launch-product=league_of_legends --launch-patchline=live"
            };
            Process.Start(startArgs);
            
            // Step 5: Get chat server and port for this player by listening to event from ConfigProxy.
            string chatHost = null;
            var chatPort = 0;
            proxyServer.PatchedChatServer += (sender, args) =>
            {
                chatHost = args.ChatHost;
                chatPort = args.ChatPort;
            };
            
            var incoming = listener.AcceptTcpClient();
                
            // Step 6: Connect sockets.
            var sslIncoming = new SslStream(incoming.GetStream());
            var cert = new X509Certificate2(Resources.certificates);
            sslIncoming.AuthenticateAsServer(cert);
            
            if (chatHost == null)
            {
                MessageBox.Show(
                    "Deceive was unable to find Riot's chat server, please try again later. If this issue persists and you can connect to chat normally without Deceive, please file a bug report through GitHub (https://github.com/molenzwiebel/deceive) or Discord.",
                    DeceiveTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );

                return;
            }
            
            var outgoing = new TcpClient(chatHost, chatPort);
            var sslOutgoing = new SslStream(outgoing.GetStream());
            sslOutgoing.AuthenticateAsClient(chatHost);

            // Step 7: All sockets are now connected, start tray icon.
            var mainController = new MainController();
            mainController.StartThreads(sslIncoming, sslOutgoing);
            Application.EnableVisualStyles();
            Application.Run(mainController);
        }
    }
}
