using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Management;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Deceive
{
    class Utils
    {
        public static readonly string DATA_DIR = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deceive");
        private static Regex AUTH_TOKEN_REGEX = new Regex("\"--remoting-auth-token=(.+?)\"");
        private static Regex PORT_REGEX = new Regex("\"--app-port=(\\d+?)\"");
        private static string CONFIG_PATH = Path.Combine(DATA_DIR, "lcuPath");

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
         * Gets the path to the most recent system.yaml file in the current league installation.
         */
        public static string GetSystemYamlPath()
        {
            var league = GetLCUPath();
            if (league == null)
                return null;

            var releases = Path.GetDirectoryName(league) + "/RADS/projects/league_client/releases";

            // Old patcher has the system.yaml in RADS/projects/league_client/releases/<version>/deploy
            // New patcher has it in the install root.
            // If releases doesn't exist, we assume it is something in the root.
            if (!Directory.Exists(releases))
            {
                return Path.Combine(Path.GetDirectoryName(league), "system.yaml");
            }

            var last = Directory.GetDirectories(releases).Select(x => {
                try
                {
                    // Convert 0.0.0.29 to 29.
                    return new { Path = x, Version = int.Parse(Path.GetFileName(x).Replace(".", "")) };
                }
                catch
                {
                    return new { Path = x, Version = -1 };
                }
            }).OrderBy(x => x.Version).Last().Path;

            // Some times the patcher leaves the folders empty, without removing the actual folders.
            // As an extra sanity check, check if the file exists and default back to the root system yaml.
            var fullPath = last + "/deploy/system.yaml";
            if (!File.Exists(fullPath))
            {
                return Path.Combine(Path.GetDirectoryName(league), "system.yaml");
            }

            return fullPath;
        }

        /**
         * Either gets the LCU path from the saved properties, from registry, or by prompting the user (in case all goes wrong).
         */
        public static string GetLCUPath()
        {
            string path;
            string initialDirectory = "C:\\Riot Games\\League of Legends";

            if (File.Exists(CONFIG_PATH))
                path = File.ReadAllText(CONFIG_PATH);
            else
            {
                object registry = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Riot Games\\RADS", "LocalRootFolder", "");
                if (registry == null)
                {
                    path = initialDirectory;
                }
                else
                {
                    path = registry.ToString();
                    // Remove "RADS" from the string's end
                    path = path.Remove(path.Length - 4) + "LeagueClient.exe";
                }
            }

            while (!IsValidLCUPath(path))
            {
                // Notify that the path is invalid.
                MessageBox.Show(
                    "Could not find the League client at " + path + ". Please select the location of 'LeagueClient.exe' manually.",
                    "LCU not found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Exclamation
                );

                // Ask for new path.
                CommonOpenFileDialog dialog = new CommonOpenFileDialog();
                dialog.Title = "Select LeagueClient.exe location.";
                dialog.InitialDirectory = initialDirectory;
                dialog.EnsureFileExists = true;
                dialog.EnsurePathExists = true;
                dialog.DefaultFileName = "LeagueClient";
                dialog.DefaultExtension = "exe";
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
                if (String.IsNullOrEmpty(path))
                    return false;

                string folder = Path.GetDirectoryName(path);
                return File.Exists(folder + "\\LeagueClient.exe") && Directory.Exists(folder + "\\Config") && Directory.Exists(folder + "\\Logs");
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
                if (!IsValidLCUPath(p.MainModule.FileName))
                    continue;

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

        // Checks if the current client has a Riot Client configuration,
        // and returns the path of the client if it does. Else, returns null.
        public static string GetRiotClientPath()
        {
            // Find the RiotClientInstalls file.
            var installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games/RiotClientInstalls.json");
            if (!File.Exists(installPath)) return null;

            // Ensure it has a list of installed clients.
            JsonObject data = (JsonObject) SimpleJson.DeserializeObject(File.ReadAllText(installPath));
            if (data["associated_client"] == null || !(data["associated_client"] is JsonObject)) return null;

            // Find the directory of the client we're attempting to launch.
            var baseDir = Path.GetDirectoryName(GetLCUPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // For every entry, see if it matches after normalization.
            // We need to normalize since the client is inconsistent with direction of slashes and trailing slashes.
            foreach (var entry in (JsonObject) data["associated_client"])
            {
                var normalizedPath = Path.GetFullPath(entry.Key).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (normalizedPath == baseDir)
                {
                    return (string)entry.Value;
                }
            }

            return "";
        }
    }
}
