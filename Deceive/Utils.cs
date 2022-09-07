using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Deceive;

internal static class Utils
{
    internal static string DeceiveVersion
    {
        get
        {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            if (version is null)
                return "v0.0.0";
            return "v" + version.Major + "." + version.Minor + "." + version.Build;
        }
    }

    /**
     * Asynchronously checks if the current version of Deceive is the latest version.
     * If not, and the user has not dismissed the message before, an alert is shown.
     */
    public static async Task CheckForUpdatesAsync()
    {
        try
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Deceive", DeceiveVersion));

            var response =
                await httpClient.GetAsync("https://api.github.com/repos/molenzwiebel/Deceive/releases/latest");
            var content = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<JsonNode>(content);
            var latestVersion = release?["tag_name"]?.ToString();

            // If failed to fetch or already latest or newer, return.
            if (latestVersion is null)
                return;
            var githubVersion = new Version(latestVersion.Replace("v", ""));
            var assemblyVersion = new Version(DeceiveVersion.Replace("v", ""));
            // Earlier = -1, Same = 0, Later = 1
            if (assemblyVersion.CompareTo(githubVersion) != -1)
                return;

            // Check if we have shown this before.
            var latestShownVersion = Persistence.GetPromptedUpdateVersion();

            // If we have, return.
            if (string.IsNullOrEmpty(latestShownVersion) && latestShownVersion == latestVersion)
                return;

            // Show a message and record the latest shown.
            Persistence.SetPromptedUpdateVersion(latestVersion);

            var result = MessageBox.Show(
                $"There is a new version of Deceive available: {latestVersion}. You are currently using Deceive {DeceiveVersion}. " +
                "Deceive updates usually fix critical bugs or adapt to changes by Riot, so it is recommended that you install the latest version.\n\n" +
                "Press OK to visit the download page, or press Cancel to continue. Don't worry, we won't bother you with this message again if you press cancel.",
                StartupHandler.DeceiveTitle,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1
            );

            if (result is DialogResult.OK)
                // Open the url in the browser.
                Process.Start(release?["html_url"]?.ToString()!);
        }
        catch
        {
            // Ignored.
        }
    }

    private static IEnumerable<Process> GetProcesses()
    {
        var riotCandidates = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Where(process => process.Id != Process.GetCurrentProcess().Id).ToList();
        riotCandidates.AddRange(Process.GetProcessesByName("LeagueClient"));
        riotCandidates.AddRange(Process.GetProcessesByName("LoR"));
        riotCandidates.AddRange(Process.GetProcessesByName("VALORANT-Win64-Shipping"));
        riotCandidates.AddRange(Process.GetProcessesByName("RiotClientServices"));
        return riotCandidates;
    }

    // Return the currently running Riot Client process, or null if none are running.
    public static Process GetRiotClientProcess() => Process.GetProcessesByName("RiotClientServices").FirstOrDefault();

    // Checks if there is a running LCU/LoR/VALORANT/RC or Deceive instance.
    public static bool IsClientRunning() => GetProcesses().Any();

    // Kills the running LCU/LoR/VALORANT/RC or Deceive instance, if applicable.
    public static void KillProcesses()
    {
        foreach (var process in GetProcesses())
        {
            process.Refresh();
            if (process.HasExited)
                continue;
            process.Kill();
            process.WaitForExit();
        }
    }

    // Checks for any installed Riot Client configuration,
    // and returns the path of the client if it does. Else, returns null.
    public static string? GetRiotClientPath()
    {
        // Find the RiotClientInstalls file.
        var installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Riot Games/RiotClientInstalls.json");
        if (!File.Exists(installPath))
            return null;

        try
        {
            // occasionally this deserialization may error, because the RC occasionally corrupts its own
            // configuration file (wtf riot?). we will return null in that case, which will cause a prompt
            // telling the user to launch a game normally once
            var data = JsonSerializer.Deserialize<JsonNode>(File.ReadAllText(installPath));
            var rcPaths = new List<string?> { data?["rc_default"]?.ToString(), data?["rc_live"]?.ToString(), data?["rc_beta"]?.ToString() };

            return rcPaths.FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }
}
