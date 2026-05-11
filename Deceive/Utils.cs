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
            {
                var htmlUrl = release?["html_url"]?.ToString();
                if (!string.IsNullOrEmpty(htmlUrl) && htmlUrl.StartsWith("https://github.com/", StringComparison.Ordinal))
                    Process.Start(htmlUrl);
            }
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

    private const string ExpectedCertDomain = "deceive-localhost.molenzwiebel.xyz";

    // Returns a certificate for deceive-localhost.molenzwiebel.xyz, either from cache or by downloading
    // the current one from the server. The returned certificate will be valid for at least 20 days.
    public static async Task<X509Certificate2?> GetProxyCertificateAsync()
    {
        var cachedCert = Persistence.GetCachedCertificate();
        if (cachedCert is not null && cachedCert.NotAfter > DateTime.Now.AddDays(20))
        {
            if (!ValidateProxyCertificate(cachedCert))
            {
                Trace.WriteLine("Cached certificate failed domain validation, discarding.");
                Persistence.DeleteCachedCertificate();
            }
            else
            {
                Trace.WriteLine($"Cached certificate is valid until {cachedCert.NotAfter}, using cached certificate.");
                return cachedCert;
            }
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

            if (!ValidateProxyCertificate(cert))
            {
                Trace.WriteLine("Downloaded certificate failed domain validation, refusing to use it.");
                return null;
            }

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

    private static bool ValidateProxyCertificate(X509Certificate2 cert)
    {
        var san = cert.Extensions["2.5.29.17"]?.Format(false) ?? string.Empty;
        var cn = cert.GetNameInfo(X509NameType.DnsName, false);
        return cn.Equals(ExpectedCertDomain, StringComparison.OrdinalIgnoreCase) ||
               san.Contains(ExpectedCertDomain, StringComparison.OrdinalIgnoreCase);
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
            "Your machine is failing to resolve some required domains. This can be fixed by switching to a different DNS server or by " +
            "letting Deceive add a manual hosts entry for you. If you press Yes, Deceive will attempt to automatically fix this for you by " + 
            "editing your hosts file (requires administrator permissions). If you press No, Deceive will exit. See the FAQ on GitHub for more details.",
            StartupHandler.DeceiveTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1
        );

        if (result is not DialogResult.Yes)
        {
            Environment.Exit(0);
        }

        var processInfo = new ProcessStartInfo(Assembly.GetEntryAssembly()!.Location)
        {
            Arguments = "--update-hosts=true",
            UseShellExecute = true,
            Verb = "runas" // ask for admin
        };
        // wait for the process to exit, so that we can check if the issue is fixed
        var process = Process.Start(processInfo);
        process?.WaitForExit();

        if (DeceiveLocalhostResolves())
            return;
        
        MessageBox.Show(
            "Deceive was unable to fix your DNS resolution issue. Please try switching to a different DNS server (like Cloudflare's 1.1.1.1 or Google's " +
            "8.8.8.8). If that doesn't work, please contact the creator through GitHub (https://github.com/molenzwiebel/Deceive) or Discord for further assistance.",
            StartupHandler.DeceiveTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error,
            MessageBoxDefaultButton.Button1
        );
        Environment.Exit(0);
    }
}
