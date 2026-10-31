using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Deceive;

internal static class StartupHandler
{
    public static string DeceiveTitle => "Deceive " + Utils.DeceiveVersion;

    // See CommandLineOptions for the accepted arguments.
    [STAThread]
    public static int Main(string[] argv)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        Trace.Listeners.Add(new ConsoleTraceListener());

        if (!CommandLineOptions.TryParse(argv, out var options, out var error))
        {
            MessageBox.Show((error is null ? "" : error + "\n\n") + CommandLineOptions.Usage, DeceiveTitle, MessageBoxButtons.OK,
                error is null ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            return error is null ? 0 : 1;
        }

        Trace.WriteLine($"{DeceiveTitle} starting with arguments:\n" +
                        $"LaunchGame: {options.Game}\n" +
                        $"GamePatchline: {options.GamePatchline}\n" +
                        $"RiotClientParams: {options.RiotClientParams}\n" +
                        $"GameParams: {options.GameParams}");

        try
        {
            var mainController = StartDeceiveAsync(options.Game, options.GamePatchline, options.RiotClientParams, options.GameParams)
                .GetAwaiter().GetResult();

            // Loop infinitely and handle window messages/tray icon, on the main thread.
            mainController?.Run();
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

        return 0;
    }

    /// Actual main function. Wrapped into a separate function so we can catch exceptions.
    private static async Task<MainController?> StartDeceiveAsync(LaunchGame game, string gamePatchline, string? riotClientParams, string? gameParams)
    {
        // Refuse to do anything if the client is already running, unless we're specifically
        // allowing that through League/RC's --allow-multiple-clients.
        if (Utils.IsClientRunning() && !(riotClientParams?.Contains("allow-multiple-clients") ?? false))
        {
            var result = MessageBox.Show(
                "The Riot Client is currently running. In order to mask your online status, the Riot Client needs to be started by Deceive. " +
                "Do you want Deceive to stop the Riot Client and games launched by it, so that it can restart with the proper configuration?",
                DeceiveTitle,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1
            );

            if (result is not DialogResult.OK)
                return null;
            Utils.KillProcesses();
            await Task.Delay(2000); // Riot Client takes a while to die
        }

        try
        {
            File.WriteAllText(Path.Combine(Persistence.DataDir, "debug.log"), string.Empty);
            Trace.Listeners.Add(new TextWriterTraceListener(Path.Combine(Persistence.DataDir, "debug.log")));
            Debug.AutoFlush = true;
            Trace.WriteLine(DeceiveTitle);
            Trace.WriteLine($"Chat traffic logging: {(TrafficLog.Enabled ? "on" : "off")}");
        }
        catch
        {
            // ignored; just don't save logs if file is already being accessed
        }

        // if we can't resolve deceive-localhost.molenzwiebel.xyz to 127.0.0.1, boom
        Utils.EnsureLocalhostResolution();

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

            return null;
        }

        // If launching "auto", use the persisted launch game (which defaults to prompt).
        if (game is LaunchGame.Auto)
            game = Persistence.GetDefaultLaunchGame();

        // If prompt, display dialog.
        if (game is LaunchGame.Prompt)
        {
            game = GamePrompt.Show();
        }

        // If we don't have a concrete game by now, the user has cancelled and nothing we can do.
        if (game is LaunchGame.Prompt or LaunchGame.Auto)
            return null;

        var launchProduct = game switch
        {
            LaunchGame.LoL => "league_of_legends",
            LaunchGame.TeamfightTactics => "teamfighttactics",
            LaunchGame.LoR => "bacon",
            LaunchGame.VALORANT => "valorant",
            LaunchGame.Lion => "lion",
            LaunchGame.RiotClient => null,
            var x => throw new Exception("Unexpected LaunchGame: " + x)
        };

        // Step 3: Start proxy web server for clientconfig
        var proxyServer = new ConfigProxy(port);
        
        // Step 4: fetch certificate for MITM
        var serverCertificate = await Utils.GetProxyCertificateAsync();
        if (serverCertificate is null)
        {
            MessageBox.Show(
                "Deceive was unable to obtain a necessary security certificate for intercepting and modifying the chat connection. This normally happens when there's " +
                "a problem with the server that provides the certificate, but it can also be caused by network issues on your end. Please check if there's a new version" +
                " of Deceive available, check your network connection, or contact the creator through GitHub (https://github.com/molenzwiebel/Deceive) or Discord.",
                DeceiveTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1
            );

            return null;
        }

        // Step 5: Launch Riot Client (+game)
        var startArgs = new ProcessStartInfo { FileName = riotClientPath, Arguments = $"--client-config-url=\"http://127.0.0.1:{proxyServer.ConfigPort}\"" };

        if (launchProduct is not null)
            startArgs.Arguments += $" --launch-product={launchProduct} --launch-patchline={gamePatchline} --allow-direct-launch";

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

        // Step 6: Get chat server and port for this player by listening to event from ConfigProxy.
        var servingClients = false;
        proxyServer.PatchedChatServer += (_, args) =>
        {
            Trace.WriteLine($"The original chat server details were {args.ChatHost}:{args.ChatPort}");

            // Step 7: Start serving incoming connections and proxy them!
            if (servingClients)
                return;
            servingClients = true;
            if (args.ChatHost is not null)
            {
                mainController.StartServingClients(listener, serverCertificate, args.ChatHost, args.ChatPort);
            }
        };

        return mainController;
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