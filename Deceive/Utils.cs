using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
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
            if (!string.IsNullOrEmpty(latestShownVersion) && latestShownVersion == latestVersion)
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
        var riotCandidates = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
            .Where(process => process.Id != Process.GetCurrentProcess().Id).ToList();
        riotCandidates.AddRange(Process.GetProcessesByName("LeagueClient"));
        riotCandidates.AddRange(Process.GetProcessesByName("LoR"));
        riotCandidates.AddRange(Process.GetProcessesByName("VALORANT-Win64-Shipping"));
        riotCandidates.AddRange(Process.GetProcessesByName("RiotClientServices"));
        return riotCandidates;
    }

    // Return the currently running Riot Client process, or null if none are running.
    public static Process? GetRiotClientProcess() => Process.GetProcessesByName("RiotClientServices").FirstOrDefault();

    // Checks if there is a running LCU/LoR/VALORANT/RC or Deceive instance.
    public static bool IsClientRunning() => GetProcesses().Any();

    // Kills the running LCU/LoR/VALORANT/RC or Deceive instance, if applicable.
    public static void KillProcesses()
    {
        try
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
        catch (Win32Exception ex)
        {
            // thank you C# and your horrible win32 ecosystem integration, I have no clue if this is correct
            if (ex.NativeErrorCode == -2147467259 || ex.ErrorCode == -2147467259 || ex.ErrorCode == 5 ||
                ex.NativeErrorCode == 5)
            {
                // ERROR_ACCESS_DENIED
                MessageBox.Show(
                    "Deceive could not stop existing Riot processes because it does not have the right permissions. Please relaunch this application as an administrator and try again.",
                    StartupHandler.DeceiveTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1
                );
                Environment.Exit(0);
            }

            throw ex;
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
            var rcPaths = new List<string?>
                { data?["rc_default"]?.ToString(), data?["rc_live"]?.ToString(), data?["rc_beta"]?.ToString() };

            return rcPaths.FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }

    // Returns a certificate for deceive-localhost.molenzwiebel.xyz, either from cache or by downloading
    // the current one from the server. The returned certificate will be valid for at least 20 days.
    public static async Task<X509Certificate2?> GetProxyCertificateAsync()
    {
        var cachedCert = Persistence.GetCachedCertificate();
        if (cachedCert is not null && cachedCert.NotAfter > DateTime.Now.AddDays(20))
        {
            Trace.WriteLine($"Cached certificate is valid until {cachedCert.NotAfter}, using cached certificate.");
            return cachedCert;
        }

        try
        {
            Trace.WriteLine("Cached certificate is missing or expiring soon, downloading new certificate.");
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Deceive", DeceiveVersion));

            var response = await httpClient.GetAsync("https://mln.cx/deceive/localhost.pfx");
            response.EnsureSuccessStatusCode();
            var certBytes = await response.Content.ReadAsByteArrayAsync();
            var cert = new X509Certificate2(certBytes);
            Persistence.SetCachedCertificate(certBytes);
            return cert;
        }
        catch (Exception ex)
        {
            // something went wrong, let's just return null and inform the user
            Trace.WriteLine($"Failed to download certificate: {ex}");
            return null;
        }
    }

    private static bool DeceiveLocalhostResolves()
    {
        try
        {
            var addresses = System.Net.Dns.GetHostAddresses(ConfigProxy.LocalhostDomain);
            if (addresses.Any(addr => addr.ToString() == "127.0.0.1"))
                return true;
        }
        catch
        {
            // intentionally empty
        }
        return false;
    }

    // Check if deceive-localhost.molenzwiebel.xyz is resolving to 127.0.0.1, and offer
    // the user to relaunch to install the necessary hosts file entry if not.
    public static void EnsureLocalhostResolution()
    {
        if (DeceiveLocalhostResolves())
            return;

        var result = MessageBox.Show(
            "Your machine is failing to resolve some required domains. You will need to switch DNS servers or add an entry to your hosts file. Please see the Deceive FAQ for more information. Deceive will not work until this issue is resolved. Would you like to open the FAQ now?",
            StartupHandler.DeceiveTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1
        );

        if (result is DialogResult.Yes)
        {
            Process.Start("https://github.com/molenzwiebel/Deceive#FAQ");
        }

        Environment.Exit(0);
    }
}
