using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Deceive
{
    class Utils
    {
        public static readonly string DATA_DIR = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deceive");

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
            var contents = File.ReadAllText(config);
            var matches = new Regex("region: \"(.+?)\"").Match(contents);

            return matches.Groups[1].Value;
        }

        /**
         * Gets the path to the most recent system.yaml file in the current league installation.
         */
        public static string GetSystemYamlPath()
        {
            var league = GetLCUPath();
            var releases = Path.GetDirectoryName(league) + "/RADS/projects/league_client/releases";
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
            return last + "/deploy/system.yaml";
        }

        /**
         * Either gets the LCU path from the saved properties, or by prompting the user.
         */
        public static string GetLCUPath()
        {
            string configPath = Path.Combine(DATA_DIR, "lcuPath");
            string path = File.Exists(configPath) ? File.ReadAllText(configPath) : "C:/Riot Games/League of Legends/LeagueClient.exe";

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
                dialog.InitialDirectory = "C:\\Riot Games\\League of Legends";
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
            File.WriteAllText(configPath, path);

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
                return File.Exists(folder + "/LeagueClient.exe") && Directory.Exists(folder + "/Config") && Directory.Exists(folder + "/Logs");
            }
            catch
            {
                return false;
            }
        }

        // Checks if there is a running LCU instance.
        public static bool IsLCURunning()
        {
            Process[] lcuCandidates = Process.GetProcessesByName("LeagueClient");
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUx")).ToArray();
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUxRender")).ToArray();

            return lcuCandidates.Length > 0;
        }

        // Kills the running LCU instance, if applicable.
        public static void KillLCU()
        {
            Process[] lcuCandidates = Process.GetProcessesByName("LeagueClient");
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUx")).ToArray();
            lcuCandidates = lcuCandidates.Concat(Process.GetProcessesByName("LeagueClientUxRender")).ToArray();

            foreach (Process lcu in lcuCandidates)
            {
                lcu.Refresh();
                if (!lcu.HasExited)
                {
                    lcu.Kill();
                    lcu.WaitForExit();
                }
            }
        }
    }
}
