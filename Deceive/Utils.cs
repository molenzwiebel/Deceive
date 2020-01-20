using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Deceive.Properties;
using System.Net.Http;
using WebSocketSharp;

namespace Deceive
{
    internal static class Utils
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deceive");
        private static readonly Regex AuthTokenRegex = new Regex("\"--remoting-auth-token=(.+?)\"");
        private static readonly Regex PortRegex = new Regex("\"--app-port=(\\d+?)\"");

        static Utils()
        {
            if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
        }

        /**
         * Asynchronously checks if the current version of Deceive is the latest version.
         * If not, and the user has not dismissed the message before, an alert is shown.
         */
        public static async void CheckForUpdates()
        {
            try
            {
                HttpClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("Deceive", Resources.DeceiveVersion));

                var response = await HttpClient.GetAsync("https://api.github.com/repos/molenzwiebel/deceive/releases/latest");
                var content = await response.Content.ReadAsStringAsync();
                dynamic release = SimpleJson.DeserializeObject(content);
                var latestVersion = release["tag_name"];

                // If failed to fetch or already latest, return.
                if (latestVersion == null) return;
                if (latestVersion == Resources.DeceiveVersion) return;

                // Check if we have shown this before.
                var persistencePath = Path.Combine(DataDir, "updateVersionPrompted");
                var latestShownVersion = File.Exists(persistencePath) ? File.ReadAllText(persistencePath) : "";

                // If we have, return.
                if (latestShownVersion == latestVersion) return;

                // Show a message and record the latest shown.
                File.WriteAllText(persistencePath, latestVersion);

                var result = MessageBox.Show(
                    $"There is a new version of Deceive available: {latestVersion}. You are currently using Deceive {Resources.DeceiveVersion}. Deceive updates usually fix critical bugs or adapt to changes by Riot, so it is recommended that you install the latest version.\n\nPress OK to visit the download page, or press Cancel to continue. Don't worry, we won't bother you with this message again if you press cancel.",
                    StartupHandler.DeceiveTitle,
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1
                );

                if (result == DialogResult.OK)
                {
                    // Open the url in the browser.
                    Process.Start(release["html_url"]);
                }
            }
            catch
            {
                // Ignored.
            }
        }

        private static Process[] GetRiotProcesses()
        {
            var riotCandidates = Process.GetProcessesByName("LeagueClient");
            riotCandidates = riotCandidates.Concat(Process.GetProcessesByName("LeagueClientUx")).ToArray();
            riotCandidates = riotCandidates.Concat(Process.GetProcessesByName("LeagueClientUxRender")).ToArray();
            riotCandidates = riotCandidates.Concat(Process.GetProcessesByName("RiotClientServices")).ToArray();
            return riotCandidates;
        }

        // Checks if there is a running LCU or RC instance.
        public static bool IsClientRunning()
        {
            return GetRiotProcesses().Length > 0;
        }

        // Kills the running LCU or RC instance, if applicable.
        public static void KillClients()
        {
            IEnumerable<Process> candidates = GetRiotProcesses();
            foreach (var process in candidates)
            {
                process.Refresh();
                if (process.HasExited) continue;
                process.Kill();
                process.WaitForExit();
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
        
        /**
         * Tries to get path to the system.yaml file for given path.
         */
        public static string GetSystemYamlPath(string path)
        {
            if (path == null) return null;
            return File.Exists(Path.Combine(Path.GetDirectoryName(path), "system.yaml")) ? Path.Combine(Path.GetDirectoryName(path), "system.yaml") : null;
        }

        //Class for storing LCU API port and auth token
        private class LcuApiPortToken
        {
            internal LcuApiPortToken(string port, string token)
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
                    if (commandLine == null) return null;
                    var port = PortRegex.Match(commandLine).Groups[1].Value;
                    var token = AuthTokenRegex.Match(commandLine).Groups[1].Value;
                    return new LcuApiPortToken(port, token);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.Message);
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
        internal static void SendStatusToLcu(string status)
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
        
        public static WebSocket MonitorChatStatusChange(string status, bool enabled)
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
                    var json = (JsonArray) SimpleJson.DeserializeObject(e.Data);
                    if ((long) json[0] != 8) return;
                    var statusJson = (JsonObject)((JsonObject) json[2])[0];
                    if (!statusJson.ContainsKey("availability")) return;
                    var availability = (string) statusJson["availability"]; 
                    if (availability == "dnd" || availability == status) return;
                    SendStatusToLcu(status);
                    if (!enabled) ws.Close();
                };
                ws.Connect();
                ws.Send("[5, \"OnJsonApiEvent_lol-chat_v1_me\"]");
                return ws;
            }

            return null;
        }
    }
}
