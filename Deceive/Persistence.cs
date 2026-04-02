using System;
using System.IO;

namespace Deceive;

internal static class Persistence
{
    internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deceive");

    private static readonly string UpdateVersionPath = Path.Combine(DataDir, "updateVersionPrompted");
    private static readonly string DefaultLaunchGamePath = Path.Combine(DataDir, "launchGame");
    private static readonly string StartupStatusPath = Path.Combine(DataDir, "startupStatus");

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

    // Startup status: "chat", "offline", "mobile", or "last" (remember last session).
    internal static string GetStartupStatus() => File.Exists(StartupStatusPath) ? File.ReadAllText(StartupStatusPath) : "last";

    internal static void SetStartupStatus(string status) => File.WriteAllText(StartupStatusPath, status);

    // Kill switch mode: launch the game normally without any chat proxy interception.
    private static readonly string KillSwitchModePath = Path.Combine(DataDir, "killSwitchMode");
    internal static bool GetKillSwitchMode() => File.Exists(KillSwitchModePath) && File.ReadAllText(KillSwitchModePath) == "true";
    internal static void SetKillSwitchMode(bool enabled) => File.WriteAllText(KillSwitchModePath, enabled ? "true" : "false");
}
