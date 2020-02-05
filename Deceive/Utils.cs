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
         * Either gets the LCU path from the saved properties, from registry, or by prompting the user (in case all goes wrong).
         * SOON NOT NEEDED AS CHAT IS PROXIED TO RIOT CLIENT
         */
        public static string GetLcuPath()
        {
            string path;
            const string initialDirectory = "C:\\Riot Games\\League of Legends";

            if (File.Exists(ConfigPath))
                path = File.ReadAllText(ConfigPath);
            else
            {
                var registry =
                    Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\WOW6432Node\\Riot Games, Inc\\League of Legends",
                        "Location", "");
                if (registry == null)
                {
                    path = initialDirectory + "\\LeagueClient.exe";
                }
                else
                {
                    path = registry + "\\LeagueClient.exe";
                }
            }

            while (!IsValidLcuPath(path))
            {
                // Notify that the path is invalid.
                MessageBox.Show(
                    "Could not find the League client at " + path +
                    ". Please select the location of 'LeagueClient.exe' manually.",
                    StartupHandler.DeceiveTitle,
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
            File.WriteAllText(ConfigPath, path);

            return path;
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

        /**
         * Checks if the provided path is most likely a path where the LCU is installed.
         * SOON NOT NEEDED AS CHAT IS PROXIED TO RIOT CLIENT
         */
        private static bool IsValidLcuPath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return false;
                var folder = Path.GetDirectoryName(path);
                return File.Exists(folder + "\\LeagueClient.exe") && Directory.Exists(folder + "\\Config") &&
                       File.Exists(folder + "\\system.yaml");
            }
            catch
            {
                return false;
            }
        }

        /**
         * SOON NOT NEEDED AS CHAT IS PROXIED TO RIOT CLIENT
         */
        public static void InitPathWithRunningLcu()
        {
            // Find the LeagueClientUx process.
            foreach (var p in GetLeagueProcesses())
            {
                try
                {
                    if (!IsValidLcuPath(p.MainModule?.FileName))
                        continue;
                }
                catch
                {
                    var result = MessageBox.Show(
                        "League is currently running in admin mode. In order to proceed Deceive also needs to be elevated. Do you want Deceive to restart in admin mode?",
                        StartupHandler.DeceiveTitle,
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

                        System.Diagnostics.Process.Start(currentProcessInfo);
                        Environment.Exit(0);
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                }

                File.WriteAllText(ConfigPath, p.MainModule?.FileName);
                return;
            }
        }

        private static Process[] GetLeagueProcesses()
        {
            var lcuCandidates = Process.GetProcessesByName("LeagueClient");
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUx")).ToArray();
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUxRender")).ToArray();
            return lcuCandidates;
        }

        // Checks if there is a running LCU or RC instance.
        public static bool IsClientRunning()
        {
            return GetLeagueProcesses().Length > 0 || Process.GetProcessesByName("RiotClientServices").Length > 0;
        }

        // Kills the running LCU or RC instance, if applicable.
        public static void KillClients()
        {
            IEnumerable<Process> candidates = GetLeagueProcesses();
            candidates = candidates.Concat(Process.GetProcessesByName("RiotClientServices"));

            foreach (var lcu in candidates)
            {
                lcu.Refresh();
                if (lcu.HasExited) continue;
                lcu.Kill();
                lcu.WaitForExit();
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

        /**
         * Finds the current region as defined in RiotClientSettings.yaml
         */
        public static string GetServerRegion()
        {
            var riotClientSettings =
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Riot Games/Riot Client/Config/RiotClientSettings.yaml");

            // If we're unlucky, we read this file while league has it locked. Copy it over so we can read it.
            var copy = Path.Combine(DataDir, "RiotClientSettings.yaml");
            File.Copy(riotClientSettings, copy);
            var contents = File.ReadAllText(copy);
            File.Delete(copy);

            var yaml = new YamlStream();
            yaml.Load(new StringReader(contents));
            var root = yaml.Documents[0].RootNode;
            return root["install"]["globals"]["region"].ToString();
        }
    }
}