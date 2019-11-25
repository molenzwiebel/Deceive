using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Deceive.Properties;
using WebSocketSharp;

namespace Deceive
{
    class Utils
    {
        public static readonly string DATA_DIR = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deceive");
        private static readonly Regex AUTH_TOKEN_REGEX = new Regex("\"--remoting-auth-token=(.+?)\"");
        private static readonly Regex PORT_REGEX = new Regex("\"--app-port=(\\d+?)\"");
        private static readonly string CONFIG_PATH = Path.Combine(DATA_DIR, "lcuPath");

        static Utils()
        {
            if (!Directory.Exists(DATA_DIR)) Directory.CreateDirectory(DATA_DIR);
        }

        /**
         * Finds the current region as defined in the local league settings.
         */
        public static string GetLCURegion()
        {
            var league = GetLCUPath();
            var config = Path.GetDirectoryName(league) + "/Config/LeagueClientSettings.yaml";

            // If we're unlucky, we read this file while league has it locked. Copy it over so we can read it.
            var copy = Path.Combine(DATA_DIR, "LeagueClientSettings.yaml");
            File.Copy(config, copy);
            var contents = File.ReadAllText(copy);
            File.Delete(copy);

            var matches = new Regex("region: \"(.+?)\"").Match(contents);

            return matches.Groups[1].Value;
        }

        /**
         * Gets the path to the system.yaml file in the current league installation.
         */
        public static string GetSystemYamlPath()
        {
            var league = GetLCUPath();
            if (league == null)
                return null;

            // New patcher has it in the install root.
            return Path.Combine(Path.GetDirectoryName(league), "system.yaml");
        }

        /**
         * Either gets the LCU path from the saved properties, from registry, or by prompting the user (in case all goes wrong).
         */
        public static string GetLCUPath()
        {
            string path;
            const string initialDirectory = "C:\\Riot Games\\League of Legends";

            if (File.Exists(CONFIG_PATH))
                path = File.ReadAllText(CONFIG_PATH);
            else
            {
                object registry = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Riot Games, Inc\\League of Legends", "Location", "");
                if (registry == null)
                {
                    path = initialDirectory + "\\LeagueClient.exe";
                }
                else
                {
                    path = registry + "\\LeagueClient.exe";
                }
            }

            while (!IsValidLCUPath(path))
            {
                // Notify that the path is invalid.
                MessageBox.Show(
                    "Could not find the League client at " + path + ". Please select the location of 'LeagueClient.exe' manually.",
                    Resources.DeceiveTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation
                );

                // Ask for new path.
                CommonOpenFileDialog dialog = new CommonOpenFileDialog
                {
                    Title = "Select LeagueClient.exe location.",
                    InitialDirectory = initialDirectory,
                    EnsureFileExists = true,
                    EnsurePathExists = true,
                    DefaultFileName = "LeagueClient",
                    DefaultExtension = "exe"
                };
                dialog.Filters.Add(new CommonFileDialogFilter("Executables", ".exe"));
                dialog.Filters.Add(new CommonFileDialogFilter("All Files", ".*"));
                if (dialog.ShowDialog() == CommonFileDialogResult.Cancel)
                {
                    // User wants to cancel. Exit
                    return null;
                }

                path = dialog.FileName;
            }

            // Store choice so we don't have to ask for it again.
            File.WriteAllText(CONFIG_PATH, path);

            return path;
        }

        /**
         * Checks if the provided path is most likely a path where the LCU is installed.
         */
        private static bool IsValidLCUPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                    return false;

                string folder = Path.GetDirectoryName(path);
                return File.Exists(folder + "\\LeagueClient.exe") && Directory.Exists(folder + "\\Config") && File.Exists(folder + "\\system.yaml");
            }
            catch
            {
                return false;
            }
        }

        private static Process[] GetLeagueProcesses()
        {
            Process[] lcuCandidates = Process.GetProcessesByName("LeagueClient");
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUx")).ToArray();
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUxRender")).ToArray();
            return lcuCandidates;
        }

        public static void InitPathWithRunningLCU()
        {
            // Find the LeagueClientUx process.
            foreach (var p in GetLeagueProcesses())
            {
                try
                {
                    if (!IsValidLCUPath(p.MainModule.FileName))
                        continue;
                }
                catch
                {
                    var result = MessageBox.Show(
                        "League is currently running in admin mode. In order to proceed Deceive also needs to be elevated. Do you want Deceive to restart in admin mode?",
                        Resources.DeceiveTitle,
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button1
                    );

                    if (result == DialogResult.Yes)
                    {
                        var currentProcessInfo = new ProcessStartInfo
                        {
                            UseShellExecute = true,
                            WorkingDirectory = Environment.CurrentDirectory,
                            FileName = Assembly.GetEntryAssembly().Location,
                            Verb = "runas"
                        };

                        Process.Start(currentProcessInfo);
                        Environment.Exit(0);
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                }

                File.WriteAllText(CONFIG_PATH, p.MainModule.FileName);
                return;
            }
        }

        // Checks if there is a running LCU instance.
        public static bool IsLCURunning()
        {
            return GetLeagueProcesses().Length > 0 || Process.GetProcessesByName("RiotClientServices").Length > 0;
        }

        // Kills the running LCU instance, if applicable.
        public static void KillLCU()
        {
            IEnumerable<Process> candidates = GetLeagueProcesses();
            candidates = candidates.Concat(Process.GetProcessesByName("RiotClientServices"));

            foreach (Process lcu in candidates)
            {
                lcu.Refresh();
                if (!lcu.HasExited)
                {
                    lcu.Kill();
                    lcu.WaitForExit();
                }
            }
        }

        // Checks for any installed Riot Client configuration,
        // and returns the path of the client if it does. Else, returns null.
        public static string GetRiotClientPath()
        {
            // Find the RiotClientInstalls file.
            var installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games/RiotClientInstalls.json");
            if (!File.Exists(installPath)) return null;

            var data = (JsonObject)SimpleJson.DeserializeObject(File.ReadAllText(installPath));
            var rcPaths = new List<string>();
            
            if (data.ContainsKey("rc_default")) rcPaths.Add(data["rc_default"].ToString());
            if (data.ContainsKey("rc_live")) rcPaths.Add(data["rc_live"].ToString());
            if (data.ContainsKey("rc_beta")) rcPaths.Add(data["rc_beta"].ToString());

            return rcPaths.FirstOrDefault(File.Exists);
        }

        //Class for storing LCU API port and auth token
        private class LcuApiPortToken
        {
            public LcuApiPortToken(string port, string token)
            {
                Port = port;
                Token = token;
            }

            public string Port { get; }

            public string Token { get; }
        }

        //Reads LCU API port and auth token from LCU command line
        private static LcuApiPortToken GetApiPortAndToken(Process process)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id))
            using (var objects = searcher.Get())
            {
                var commandLine = (string)objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"];
                try
                {
                    var port = PORT_REGEX.Match(commandLine).Groups[1].Value;
                    var token = AUTH_TOKEN_REGEX.Match(commandLine).Groups[1].Value;
                    return new LcuApiPortToken(port, token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            return null;
        }

        /*
         * Sends our masked availability to LCU for display to local player instead of showing normal status.
         * LCU will only display availability, so still shows 'Creating Normal Game' or 'In Game'.
         * This happens only locally, since Deceive masks whole presence with 'gameStatus' as 'outOfGame'.
         * If we passed this (whole presence) too LCU just overrides it.
         */
        private static void SendStatusToLcu(string status)
        {
            foreach (var process in Process.GetProcessesByName("LeagueClientUx"))
            {
                var apiAuth = GetApiPortAndToken(process);
                if (apiAuth == null) return;
                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:" + apiAuth.Token));
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.ServerCertificateValidationCallback = (send, certificate, chain, sslPolicyErrors) => true;
                using (var client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.Authorization, "Basic " + auth);
                    var body = "{\"availability\": \"" + status + "\"}";
                    client.UploadString(new Uri("https://127.0.0.1:" + apiAuth.Port + "/lol-chat/v1/me"), "PUT", body);
                }
            }
        }
        
        public static WebSocket MonitorChatStatusChange(string status)
        {
            foreach (var process in Process.GetProcessesByName("LeagueClientUx"))
            {
                var apiAuth = GetApiPortAndToken(process);
                if (apiAuth == null) return null;
                var ws = new WebSocket($"wss://127.0.0.1:{apiAuth.Port}/", "wamp");
                ws.SetCredentials("riot", apiAuth.Token, true);
                ws.SslConfiguration.EnabledSslProtocols = SslProtocols.Tls12;
                ws.SslConfiguration.ServerCertificateValidationCallback = (send, certificate, chain, sslPolicyErrors) => true;
                ws.OnMessage += (s, e) =>
                {
                    if (!e.IsText) return;
                    SendStatusToLcu(status);
                };
                ws.Connect();
                ws.Send("[5, \"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");
                return ws;
            }

            return null;
        }
    }
}
