using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommandLine;
using CommandLine.Text;
using Deceive.Properties;

namespace Deceive
{
    internal class Options
    {
        [Option('p', "game-patchline", Default = "live", HelpText = "The patchline to launch RC with.")]
        public string GamePatchline { get; set; }

        [Option("riot-client-params", HelpText = "Additional parameters to be passed to the Riot Client.")]
        public string? RiotClientParams { get; set; }

        [Option("game-params", HelpText = "Additional parameters to be passed to the game launched by Deceive.")]
        public string? GameParams { get; set; }

        [Value(0, Default = LaunchGame.Auto, HelpText = "Which game to launch.")]
        public LaunchGame Game { get; set; }
    }

    internal static class StartupHandler
    {
        public static string DeceiveTitle => "Deceive " + Utils.DeceiveVersion;

        [STAThread]
        public static void Main(string[] args)
        {
            var parser = new Parser(with => with.CaseInsensitiveEnumValues = true);
            var result = parser.ParseArguments<Options>(args);
            result.WithParsed(parsedArgs =>
                {
                    DeceiveMain(parsedArgs.Game, parsedArgs.GamePatchline, parsedArgs.RiotClientParams, parsedArgs.GameParams).Wait();
                })
                .WithNotParsed(errs =>
                {
                    var helpText = HelpText.AutoBuild(result, h =>
                    {
                        h.AddEnumValuesToHelpText = true;
                        return h;
                    });

                    Trace.WriteLine(helpText);
                });
        }

        // Arguments are parsed through System.CommandLine.DragonFruit.
        /// <param name="args">The game to be launched, or automatically determined if not passed.</param>
        /// <param name="gamePatchline">The patchline to be used for launching the game.</param>
        /// <param name="riotClientParams">Any extra parameters to be passed to the Riot Client.</param>
        /// <param name="gameParams">Any extra parameters to be passed to the launched game.</param>
        public static async Task DeceiveMain(LaunchGame args = LaunchGame.Auto, string gamePatchline = "live",
            string? riotClientParams = null, string? gameParams = null)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            Application.EnableVisualStyles();
            try
            {
                await StartDeceiveAsync(args, gamePatchline, riotClientParams, gameParams);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                // Show some kind of message so that Deceive doesn't just disappear.
                MessageBox.Show(
                    "Deceive encountered an error and couldn't properly initialize itself. " +
                    "Please contact the creator through GitHub (https://github.com/molenzwiebel/Deceive) or Discord.\n\n" +
                    ex,
                    DeceiveTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );
            }
        }

        /// Actual main function. Wrapped into a separate function so we can catch exceptions.
        private static async Task StartDeceiveAsync(LaunchGame game, string gamePatchline, string? riotClientParams,
            string? gameParams)
        {
            // Refuse to do anything if the client is already running, unless we're specifically
            // allowing that through League/RC's --allow-multiple-clients.
            if (Utils.IsClientRunning() && !(riotClientParams?.Contains("allow-multiple-clients") ?? false))
            {
                var result = MessageBox.Show(
                    "The Riot Client is currently running. In order to mask your online status, the Riot Client needs to be started by Deceive. " +
                    "Do you want Deceive to stop the Riot Client and games launched by it, so that it can restart with the proper configuration?",
                    DeceiveTitle,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if (result != DialogResult.Yes)
                    return;
                await Utils.KillProcesses();
                await Task.Delay(2000); // Riot Client takes a while to die
            }

            try
            {
                File.WriteAllText(Path.Combine(Persistence.DataDir, "debug.log"), string.Empty);
                Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(Persistence.DataDir, "debug.log")));
                Debug.AutoFlush = true;
                Trace.WriteLine(DeceiveTitle);
            }
            catch
            {
                // ignored; just don't save logs if file is already being accessed
            }

            // Step 0: Check for updates in the background.
            _ = Utils.CheckForUpdatesAsync();

            // Step 1: Open a port for our chat proxy, so we can patch chat port into clientconfig.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Trace.WriteLine($"Chat proxy listening on port {port}");

            // Step 2: Find the Riot Client.
            var riotClientPath = await Utils.GetRiotClientPath();

            // If the riot client doesn't exist, the user is either severely outdated or has a bugged install.
            if (riotClientPath is null)
            {
                MessageBox.Show(
                    "Deceive was unable to find the path to the Riot Client. Usually this can be resolved by launching any Riot Games game once, then launching Deceive again." +
                    "If this does not resolve the issue, please file a bug report through GitHub (https://github.com/molenzwiebel/Deceive) or Discord.",
                    DeceiveTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );

                return;
            }

            // If launching "auto", use the persisted launch game (which defaults to prompt).
            if (game is LaunchGame.Auto)
                game = await Persistence.GetDefaultLaunchGameAsync();

            // If prompt, display dialog.
            if (game is LaunchGame.Prompt)
            {
                new GamePromptForm().ShowDialog();
                game = GamePromptForm.SelectedGame;
            }

            // If we don't have a concrete game by now, the user has cancelled and nothing we can do.
            if (game is LaunchGame.Prompt || game is LaunchGame.Auto)
                return;

            var launchProduct = game switch
            {
                LaunchGame.LoL => "league_of_legends",
                LaunchGame.LoR => "bacon",
                LaunchGame.VALORANT => "valorant",
                LaunchGame.RiotClient => null,
                var x => throw new Exception("Unexpected LaunchGame: " + x)
            };

            // Step 3: Start proxy web server for clientconfig
            var proxyServer = new ConfigProxy(port);

            // Step 4: Launch Riot Client (+game)
            var startArgs = new ProcessStartInfo
            {
                FileName = riotClientPath,
                Arguments = $"--client-config-url=\"http://127.0.0.1:{proxyServer.ConfigPort}\""
            };

            if (launchProduct != null)
                startArgs.Arguments += $" --launch-product={launchProduct} --launch-patchline={gamePatchline}";

            if (riotClientParams != null)
                startArgs.Arguments += $" {riotClientParams}";

            if (gameParams != null)
                startArgs.Arguments += $" -- {gameParams}";

            Trace.WriteLine($"About to launch Riot Client with parameters:\n{startArgs.Arguments}");
            var riotClient = Process.Start(startArgs);
            // Kill Deceive when Riot Client has exited, so no ghost Deceive exists.
            if (riotClient != null)
            {
                riotClient.EnableRaisingEvents = true;
                riotClient.Exited += async (a, b) =>
                {
                    Trace.WriteLine("Exiting on Riot Client exit.");
                    await Task.Delay(3000); // in case of restart, let us kill ourselves elsewhere
                    Environment.Exit(0);
                };
            }

            // Step 5: Get chat server and port for this player by listening to event from ConfigProxy.
            string? chatHost = null;
            var chatPort = 0;
            proxyServer.PatchedChatServer += (_, args) =>
            {
                chatHost = args.ChatHost;
                chatPort = args.ChatPort;
                Trace.WriteLine($"The original chat server details were {chatHost}:{chatPort}");
            };

            Trace.WriteLine("Waiting for client to connect to chat server...");
            var incoming = await listener.AcceptTcpClientAsync();
            Trace.WriteLine("Client connected!");

            // Step 6: Connect sockets.
            var sslIncoming = new SslStream(incoming.GetStream());
            var cert = new X509Certificate2(Resources.Certificate);
            await sslIncoming.AuthenticateAsServerAsync(cert);

            if (chatHost is null)
            {
                MessageBox.Show(
                    "Deceive was unable to find Riot's chat server, please try again later. " +
                    "If this issue persists and you can connect to chat normally without Deceive, " +
                    "please file a bug report through GitHub (https://github.com/molenzwiebel/Deceive) or Discord.",
                    DeceiveTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );
                return;
            }

            var outgoing = new TcpClient(chatHost, chatPort);
            var sslOutgoing = new SslStream(outgoing.GetStream());
            await sslOutgoing.AuthenticateAsClientAsync(chatHost);

            // Step 7: All sockets are now connected, start tray icon.
            var mainController = new MainController();
            mainController.StartThreads(sslIncoming, sslOutgoing);
            mainController.ConnectionErrored += async (a, b) =>
            {
                Trace.WriteLine("Trying to reconnect.");
                sslIncoming.Close();
                sslOutgoing.Close();
                incoming.Close();
                outgoing.Close();

                incoming = await listener.AcceptTcpClientAsync();
                sslIncoming = new SslStream(incoming.GetStream());
                await sslIncoming.AuthenticateAsServerAsync(cert);
                while (true)
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
                            "please file a bug report through GitHub (https://github.com/molenzwiebel/Deceive) or Discord.",
                            DeceiveTitle,
                            MessageBoxButtons.RetryCancel,
                            MessageBoxIcon.Error,
                            MessageBoxDefaultButton.Button1
                        );
                        if (result == DialogResult.Cancel)
                            Environment.Exit(0);
                    }

                sslOutgoing = new SslStream(outgoing.GetStream());
                await sslOutgoing.AuthenticateAsClientAsync(chatHost);
                mainController.StartThreads(sslIncoming, sslOutgoing);
            };
            
            Trace.WriteLine("Pre-run");
            Application.Run(mainController);
        }

        private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // Log all unhandled exceptions
            Trace.WriteLine(e.ExceptionObject as Exception);
            Trace.WriteLine(Environment.StackTrace);
        }
    }
}