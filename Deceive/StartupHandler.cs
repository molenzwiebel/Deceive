using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Deceive;

internal static class StartupHandler
{
    public static string DeceiveTitle => "Deceive " + Utils.DeceiveVersion;

    // Arguments are parsed through System.CommandLine.DragonFruit.
    /// <param name="args">The game to be launched, or automatically determined if not passed.</param>
    /// <param name="gamePatchline">The patchline to be used for launching the game.</param>
    /// <param name="riotClientParams">Any extra parameters to be passed to the Riot Client.</param>
    /// <param name="gameParams">Any extra parameters to be passed to the launched game.</param>
    [STAThread]
    public static async Task Main(LaunchGame args = LaunchGame.Auto, string gamePatchline = "live", string? riotClientParams = null, string? gameParams = null)
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
                "Please contact the creator through GitHub (https://github.com/molenzwiebel/Deceive) or Discord.\n\n" + ex,
                DeceiveTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1
            );
        }
    }

    /// Actual main function. Wrapped into a separate function so we can catch exceptions.
    private static async Task StartDeceiveAsync(LaunchGame game, string gamePatchline, string? riotClientParams, string? gameParams)
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

            if (result is not DialogResult.Yes)
                return;
            Utils.KillProcesses();
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
        var riotClientPath = Utils.GetRiotClientPath();

        // If the riot client doesn't exist, the user is either severely outdated or has a bugged install.
        if (riotClientPath is null)
        {
            MessageBox.Show(
                "Deceive was unable to find the path to the Riot Client. Usually this can be resolved by launching any Riot Games game once, then launching Deceive again. " +
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
            game = Persistence.GetDefaultLaunchGame();

        // If prompt, display dialog.
        if (game is LaunchGame.Prompt)
        {
            new GamePromptForm().ShowDialog();
            game = GamePromptForm.SelectedGame;
        }

        // If we don't have a concrete game by now, the user has cancelled and nothing we can do.
        if (game is LaunchGame.Prompt or LaunchGame.Auto)
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
        var startArgs = new ProcessStartInfo { FileName = riotClientPath, Arguments = $"--client-config-url=\"http://127.0.0.1:{proxyServer.ConfigPort}\"" };

        if (launchProduct is not null)
            startArgs.Arguments += $" --launch-product={launchProduct} --launch-patchline={gamePatchline}";

        if (riotClientParams is not null)
            startArgs.Arguments += $" {riotClientParams}";

        if (gameParams is not null)
            startArgs.Arguments += $" -- {gameParams}";

        Trace.WriteLine($"About to launch Riot Client with parameters:\n{startArgs.Arguments}");
        var riotClient = Process.Start(startArgs);
        // Kill Deceive when Riot Client has exited, so no ghost Deceive exists.
        if (riotClient is not null)
        {
            ListenToRiotClientExit(riotClient);
        }

        var mainController = new MainController();

        // Step 5: Get chat server and port for this player by listening to event from ConfigProxy.
        var servingClients = false;
        proxyServer.PatchedChatServer += (_, args) =>
        {
            Trace.WriteLine($"The original chat server details were {args.ChatHost}:{args.ChatPort}");

            // Step 6: Start serving incoming connections and proxy them!
            if (servingClients)
                return;
            servingClients = true;
            mainController.StartServingClients(listener, args.ChatHost, args.ChatPort);
        };

        // Loop infinitely and handle window messages/tray icon.
        Application.Run(mainController);
    }

    private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Log all unhandled exceptions
        Trace.WriteLine(e.ExceptionObject as Exception);
        Trace.WriteLine(Environment.StackTrace);
    }

    private static void ListenToRiotClientExit(Process riotClientProcess)
    {
        riotClientProcess.EnableRaisingEvents = true;
        riotClientProcess.Exited += async (sender, e) =>
        {
            Trace.WriteLine("Detected Riot Client exit.");
            await Task.Delay(3000); // wait for a bit to ensure this is not a relaunch triggered by the RC

            var newProcess = Utils.GetRiotClientProcess();
            if (newProcess is not null)
            {
                Trace.WriteLine("A new Riot Client process spawned, monitoring that for exits.");
                ListenToRiotClientExit(newProcess);
            }
            else
            {
                Trace.WriteLine("No new clients spawned after waiting, killing ourselves.");
                Environment.Exit(0);
            }
        };
    }
}
