using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Deceive.Properties;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using YamlDotNet.RepresentationModel;

namespace Deceive
{
    internal static class Utils
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        internal static readonly string DataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deceive");

        private static readonly string ConfigPath = Path.Combine(DataDir, "lcuPath");

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
                HttpClient.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("Deceive", Resources.DeceiveVersion));

                var response =
                    await HttpClient.GetAsync("https://api.github.com/repos/molenzwiebel/deceive/releases/latest");
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
            var installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Riot Games/RiotClientInstalls.json");
            if (!File.Exists(installPath)) return null;

            var data = (JsonObject) SimpleJson.DeserializeObject(File.ReadAllText(installPath));
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
            return File.Exists(Path.Combine(Path.GetDirectoryName(path), "system.yaml"))
                ? Path.Combine(Path.GetDirectoryName(path), "system.yaml")
                : null;
        }
    }
}