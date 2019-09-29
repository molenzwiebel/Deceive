using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using YamlDotNet.RepresentationModel;

namespace Deceive
{
    class MainClass
    {
        [STAThread]
        public static void Main(string[] args)
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
                    "Deceive",
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
            if (Utils.IsLCURunning())
            {
                Utils.InitPathWithRunningLCU();

                var result = MessageBox.Show(
                    "League is currently running. In order to mask your online status, League needs to be started by Deceive. Do you want Deceive to stop League, so that it can restart it with the proper configuration?",
                    "Deceive",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                if (result != DialogResult.Yes) return;
                Utils.KillLCU();
            }

            // Step 1: Open a port for our proxy, so we can patch the port number into the system yaml.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            // Step 2: Find original system.yaml, patch our localhost proxy in, and save it somewhere.
            // At the same time, also parse the system.yaml to get the original chat server locations.
            var sysYamlPath = Utils.GetSystemYamlPath();
            if (sysYamlPath == null) // If this is null, it means we canceled something that required manual user input. Just exit.
                return;

            var contents = File.ReadAllText(sysYamlPath);

            // Load the stream
            var yaml = new YamlStream();
            yaml.Load(new StringReader(contents));

            contents = contents.Replace("allow_self_signed_cert: false", "allow_self_signed_cert: true");
            contents = contents.Replace("chat_port: 5223", "chat_port: " + port);
            contents = new Regex("chat_host: .*?\t?\n").Replace(contents, "chat_host: localhost\n");

            // Write this to the league install folder and not the appdata folder.
            // This is because league segfaults if you give it an override path with unicode characters,
            // such as some users with a special character in their Windows user name may have.
            // We put it in the Config folder since the new patcher will nuke any non-league files in the install root.
            var leaguePath = Utils.GetLCUPath();
            var yamlPath = Path.Combine(Path.GetDirectoryName(leaguePath), "Config", "deceive-system.yaml");
            File.WriteAllText(yamlPath, contents);

            // Step 3: Either launch Riot Client or launch League, depending on local configuration.
            var riotClientPath = Utils.GetRiotClientPath();

            ProcessStartInfo startArgs;
            if (riotClientPath != null)
            {
                startArgs = new ProcessStartInfo
                {
                    FileName = riotClientPath,
                    Arguments = "--priority-launch-pid=12345 --priority-launch-path=\"" + leaguePath + "\" -- --system-yaml-override=\"" + yamlPath + "\"",
                };
            }
            else
            {
                startArgs = new ProcessStartInfo
                {
                    FileName = leaguePath,
                    Arguments = "--system-yaml-override=\"" + yamlPath + "\"",
                };
            }

            // Step 4: Start the process and wait for a connect.
            Process.Start(startArgs);
            var incoming = listener.AcceptTcpClient();

            // Step 5: Connect sockets.
            var sslIncoming = new SslStream(incoming.GetStream());
            var cert = new X509Certificate2(Properties.Resources.certificates);
            sslIncoming.AuthenticateAsServer(cert);

            // Find the chat information of the original system.yaml for that region.
            var regionDetails = yaml.Documents[0].RootNode["region_data"][Utils.GetLCURegion()]["servers"]["chat"];
            var chatHost = regionDetails["chat_host"].ToString();
            var chatPort = int.Parse(regionDetails["chat_port"].ToString());

            var outgoing = new TcpClient(chatHost, chatPort);
            var sslOutgoing = new SslStream(outgoing.GetStream());
            sslOutgoing.AuthenticateAsClient(chatHost);

            // Step 6: All sockets are now connected, start tray icon.
            var mainController = new MainController();
            mainController.StartThreads(sslIncoming, sslOutgoing);
            Application.EnableVisualStyles();
            Application.Run(mainController);
        }
    }
}
