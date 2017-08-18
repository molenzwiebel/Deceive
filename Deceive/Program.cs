using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

namespace Deceive
{
    class MainClass
    {
        public static void Main(string[] args)
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
            var contents = File.ReadAllText(Utils.GetSystemYamlPath());
            contents = contents.Replace("allow_self_signed_cert: false", "allow_self_signed_cert: true");
            contents = contents.Replace("chat_port: 5223", "chat_port: " + port);
            contents = new Regex("chat_host: .*?\t?\n").Replace(contents, "chat_host: localhost\n");

            var yamlPath = Path.Combine(Utils.DATA_DIR, "system.yaml");
            File.WriteAllText(yamlPath, contents);

            // Step 3: Start league and wait for a connect.
            var startArgs = new ProcessStartInfo
            {
                FileName = Utils.GetLCUPath(),
                Arguments = "--system-yaml-override=" + yamlPath,
                UseShellExecute = false
            };
            Process.Start(startArgs);

            listener.AcceptSocket();
            Console.WriteLine("Got connection.");
            Console.Read();
        }
    }
}
