using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Security;
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
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            try
            {
                StartDeceive(args);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                // Show some kind of message so that Deceive doesn't just disappear.
                MessageBox.Show(
                    "Deceive encountered an error and couldn't properly initialize itself. " +
                    "Please contact the creator through GitHub (https://github.com/molenzwiebel/deceive) or Discord.\n\n" + ex,
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
            // We are supposed to launch league, so if it's already running something is going wrong.
            if (Utils.IsClientRunning() && cmdArgs.All(x => x.ToLower() != "--allow-multiple-clients"))
            {
                var result = MessageBox.Show(
                    "The Riot Client is currently running. In order to mask your online status, the Riot Client needs to be started by Deceive. " +
                    "Do you want Deceive to stop the Riot Client and games launched by it, so that it can restart with the proper configuration?",
                    DeceiveTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if (result != DialogResult.Yes) return;
                Utils.KillProcesses();
                Thread.Sleep(2000); // Riot Client takes a while to die
            }

            try
            {
                File.WriteAllText(Path.Combine(Utils.DataDir, "debug.log"), string.Empty);
                Debug.Listeners.Add(new TextWriterTraceListener(Path.Combine(Utils.DataDir, "debug.log")));
                Debug.AutoFlush = true;
                Trace.WriteLine(DeceiveTitle);
            }
            catch
            {
                // ignored; just don't save logs if file is already being accessed
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
                    "Deceive was unable to find the path to the Riot Client. If you have the game installed and it is working properly, " +
                    "please file a bug report through GitHub (https://github.com/molenzwiebel/deceive) or Discord.",
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
            var overlayEnabled = cmdArgs.All(x => x.ToLower() != "--no-overlay");
            var game = "league_of_legends";
            if (cmdArgs.Any(x => x.ToLower() == "lor"))
            {
                overlayEnabled = false;
                game = "bacon";
            }

            if (cmdArgs.Any(x => x.ToLower() == "valorant"))
            {
                overlayEnabled = false;
                game = "valorant";
            }

            var startArgs = new ProcessStartInfo
            {
                FileName = riotClientPath,
                Arguments = $"--client-config-url=\"http://127.0.0.1:{proxyServer.ConfigPort}\" --launch-product={game} --launch-patchline=live"
            };
            if (cmdArgs.Any(x => x.ToLower() == "--allow-multiple-clients")) startArgs.Arguments += " --allow-multiple-clients";
            var riotClient = Process.Start(startArgs);
            // Kill Deceive when Riot Client has exited, so no ghost Deceive exists.
            if (riotClient != null)
            {
                riotClient.EnableRaisingEvents = true;
                riotClient.Exited += (sender, args) =>
                {
                    Trace.WriteLine("Exiting on Riot Client exit.");
                    Environment.Exit(0);
                };
            }

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
            var cert = new X509Certificate2(Resources.Certificate);
            sslIncoming.AuthenticateAsServer(cert);

            if (chatHost == null)
            {
                MessageBox.Show(
                    "Deceive was unable to find Riot's chat server, please try again later. " +
                    "If this issue persists and you can connect to chat normally without Deceive, " +
                    "please file a bug report through GitHub (https://github.com/molenzwiebel/deceive) or Discord.",
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
            var mainController = new MainController(overlayEnabled);
            mainController.StartThreads(sslIncoming, sslOutgoing);
            mainController.ConnectionErrored += (sender, args) =>
            {
                Trace.WriteLine("Trying to reconnect.");
                sslIncoming.Close();
                sslOutgoing.Close();
                incoming.Close();
                outgoing.Close();

                incoming = listener.AcceptTcpClient();
                sslIncoming = new SslStream(incoming.GetStream());
                sslIncoming.AuthenticateAsServer(cert);
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
                            "Unable to reconnect to the chat server. Please check your internet connection." +
                            "If this issue persists and you can connect to chat normally without Deceive, " +
                            "please file a bug report through GitHub (https://github.com/molenzwiebel/deceive) or Discord.",
                            DeceiveTitle,
                            MessageBoxButtons.RetryCancel,
                            MessageBoxIcon.Error,
                            MessageBoxDefaultButton.Button1
                        );
                        if (result == DialogResult.Cancel)
                        {
                            Environment.Exit(0);
                        }
                    }
                }

                sslOutgoing = new SslStream(outgoing.GetStream());
                sslOutgoing.AuthenticateAsClient(chatHost);
                mainController.StartThreads(sslIncoming, sslOutgoing);
            };
            Application.EnableVisualStyles();
            Application.Run(mainController);
        }

        [HandleProcessCorruptedStateExceptions, SecurityCritical]
        private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            //Log all unhandled exceptions
            Trace.WriteLine(e.ExceptionObject as Exception);
            Trace.WriteLine(Environment.StackTrace);
        }
    }
}