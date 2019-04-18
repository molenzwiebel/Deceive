using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Deceive
{
    class MainClass
    {
        private static readonly Dictionary<string, Tuple<string, int>> REGIONS = new Dictionary<string, Tuple<string, int>>
        {
            ["BR"] = new Tuple<string, int>("chat.br.lol.riotgames.com", 5223),
            ["EUNE"] = new Tuple<string, int>("chat.eun1.lol.riotgames.com", 5223),
            ["EUW"] = new Tuple<string, int>("chat.euw1.lol.riotgames.com", 5223),
            ["JP"] = new Tuple<string, int>("chat.jp1.lol.riotgames.com", 5223),
            ["LA1"] = new Tuple<string, int>("chat.la1.lol.riotgames.com", 5223),
            ["LA2"] = new Tuple<string, int>("chat.la2.lol.riotgames.com", 5223),
            ["NA"] = new Tuple<string, int>("chat.na2.lol.riotgames.com", 5223),
            ["OC1"] = new Tuple<string, int>("chat.oc1.lol.riotgames.com", 5223),
            ["RU"] = new Tuple<string, int>("chat.ru.lol.riotgames.com", 5223),
            ["TEST"] = new Tuple<string, int>("chat.na2.lol.riotgames.com", 5223),
            ["TR"] = new Tuple<string, int>("chat.tr.lol.riotgames.com", 5223),
            ["PBE"] = new Tuple<string, int>("chat.pbe1.lol.riotgames.com", 5223)
        };

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
            var port = ((IPEndPoint) listener.LocalEndpoint).Port;

            // Step 2: Find original system.yaml, patch our localhost proxy in, and save it somewhere.
            var leaguePath = Utils.GetLCUPath();
            var contents = File.ReadAllText(Utils.GetSystemYamlPath());
            contents = contents.Replace("allow_self_signed_cert: false", "allow_self_signed_cert: true");
            contents = contents.Replace("chat_port: 5223", "chat_port: " + port);
            contents = new Regex("chat_host: .*?\t?\n").Replace(contents, "chat_host: localhost\n");

            // Write this to the league install folder and not the appdata folder.
            // This is because league segfaults if you give it an override path with unicode characters,
            // such as some users with a special character in their Windows user name may have.
            // We put it in the Config folder since the new patcher will nuke any non-league files in the install root.
            var yamlPath = Path.Combine(Path.GetDirectoryName(leaguePath), "Config", "deceive-system.yaml");
            File.WriteAllText(yamlPath, contents);

            // Step 3: Start league and wait for a connect.
            var startArgs = new ProcessStartInfo
            {
                FileName = leaguePath,
                Arguments = "--system-yaml-override=\"" + yamlPath + "\"",
                UseShellExecute = false
            };
            Process.Start(startArgs);
            var incoming = listener.AcceptTcpClient();

            // Step 4: Connect sockets.
            var sslIncoming = new SslStream(incoming.GetStream());
            var cert = new X509Certificate2(Properties.Resources.certificates);
            sslIncoming.AuthenticateAsServer(cert);

            var regionDetails = REGIONS[Utils.GetLCURegion()];
            var outgoing = new TcpClient(regionDetails.Item1, regionDetails.Item2);
            var sslOutgoing = new SslStream(outgoing.GetStream());
            sslOutgoing.AuthenticateAsClient(regionDetails.Item1);

            // Step 5: All sockets are now connected, start tray icon.
            var mainController = new MainController();
            mainController.StartThreads(sslIncoming, sslOutgoing);
            Application.EnableVisualStyles();
            Application.Run(mainController);
        }
    }
}
