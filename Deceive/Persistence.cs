using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;

namespace Deceive;

internal static class Persistence
{
    internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deceive");

    private static readonly string UpdateVersionPath = Path.Combine(DataDir, "updateVersionPrompted");
    private static readonly string DefaultLaunchGamePath = Path.Combine(DataDir, "launchGame");
    private static readonly string CachedCertPath = Path.Combine(DataDir, "localhostCert.pfx");
    private static readonly string StartupStatusPath = Path.Combine(DataDir, "startupStatus");
    private static readonly string IntroductionShownPath = Path.Combine(DataDir, "introductionShown");

    static Persistence()
    {
        if (!Directory.Exists(DataDir))
            Directory.CreateDirectory(DataDir);
    }

    // Prompted update version.
    internal static string GetPromptedUpdateVersion() => File.Exists(UpdateVersionPath) ? File.ReadAllText(UpdateVersionPath) : string.Empty;

    internal static void SetPromptedUpdateVersion(string version) => File.WriteAllText(UpdateVersionPath, version);

    // Configured launch option.
    internal static LaunchGame GetDefaultLaunchGame()
    {
        if (!File.Exists(DefaultLaunchGamePath))
            return LaunchGame.Prompt;

        var contents = File.ReadAllText(DefaultLaunchGamePath);
        if (!Enum.TryParse(contents, true, out LaunchGame launchGame))
            launchGame = LaunchGame.Prompt;

        return launchGame;
    }

    internal static void SetDefaultLaunchGame(LaunchGame game) => File.WriteAllText(DefaultLaunchGamePath, game.ToString());
    
    // Cached deceive-localhost.molenzwiebel.xyz certificate
    internal static X509Certificate2? GetCachedCertificate()
    {
        if (!File.Exists(CachedCertPath))
            return null;

        try
        {
            var contents = File.ReadAllBytes(CachedCertPath);
            return new X509Certificate2(contents);
        }
        catch
        {
            // If we fail to load the cert for any reason, just return null and grab a new one.
            return null;
        }
    }
    
    internal static void SetCachedCertificate(byte[] certBytes) => File.WriteAllBytes(CachedCertPath, certBytes);
    
    // Startup status: "chat", "offline", "mobile", or "last" (remember last session).
    internal static string GetStartupStatus() => File.Exists(StartupStatusPath) ? File.ReadAllText(StartupStatusPath) : "last";
    internal static void SetStartupStatus(string status) => File.WriteAllText(StartupStatusPath, status);

    // Whether the introduction/welcome text has already been shown to the user.
    internal static bool GetHasShownIntroduction() => File.Exists(IntroductionShownPath);
    internal static void SetHasShownIntroduction() => File.WriteAllText(IntroductionShownPath, string.Empty);
}
