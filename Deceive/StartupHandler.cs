using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;
using Deceive.Properties;

namespace Deceive
{
    internal static class StartupHandler
    {
        internal static string DeceiveTitle => "Deceive " + Utils.DeceiveVersion;

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                StartDeceive(args);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
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
        private static void StartDeceive(string[] cmdArgs)
        {
            File.WriteAllText(Path.Combine(Utils.DataDir, "debug.log"), string.Empty);
            var traceListener = new TextWriterTraceListener(Path.Combine(Utils.DataDir, "debug.log"));
            Debug.Listeners.Add(traceListener);
            Debug.AutoFlush = true;

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
            var port = ((IPEndPoint) listener.LocalEndpoint).Port;

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

            // Step 3: Start proxy web server for clientconfig
            var proxyServer = new ConfigProxy("https://clientconfig.rpg.riotgames.com", port);

            // Step 4: Start the Riot Client and wait for a connect.
            var isLoL = true;
            var game = "league_of_legends";
            if (cmdArgs.Any(x => x.ToLower() == "lor"))
            {
                isLoL = false;
                game = "bacon";
            }

            if (cmdArgs.Any(x => x.ToLower() == "valorant"))
            {
                isLoL = false;
                game = "valorant";
            }

            var startArgs = new ProcessStartInfo
            {
                FileName = riotClientPath,
                Arguments = "--client-config-url=\"http://127.0.0.1:" + proxyServer.ConfigPort + "\" --launch-product=" + game + " --launch-patchline=live"
            };
            var riotClient = Process.Start(startArgs);

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
            var mainController = new MainController(isLoL);
            mainController.StartThreads(sslIncoming, sslOutgoing);
            Application.EnableVisualStyles();
            Application.Run(mainController);

            // Kill Deceive when Riot Client has exited, so no ghost Deceive exists.
            riotClient?.WaitForExit();
            Environment.Exit(0);
        }
    }
}