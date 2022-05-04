using System;
using System.IO;

namespace Deceive;

internal static class Persistence
{
    internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deceive");

    private static readonly string UpdateVersionPath = Path.Combine(DataDir, "updateVersionPrompted");
    private static readonly string DefaultLaunchGamePath = Path.Combine(DataDir, "launchGame");

    static Persistence()
    {
        if (!Directory.Exists(DataDir))
            Directory.CreateDirectory(DataDir);
    }

    // Prompted update version.
    internal static string GetPromptedUpdateVersionAsync() => File.Exists(UpdateVersionPath) ? File.ReadAllText(UpdateVersionPath) : string.Empty;

    internal static void SetPromptedUpdateVersionAsync(string version) => File.WriteAllText(UpdateVersionPath, version);

    // Configured launch option.
    internal static LaunchGame GetDefaultLaunchGameAsync()
    {
        if (!File.Exists(DefaultLaunchGamePath))
            return LaunchGame.Prompt;

        var contents = File.ReadAllText(DefaultLaunchGamePath);
        if (!Enum.TryParse(contents, true, out LaunchGame launchGame))
            launchGame = LaunchGame.Prompt;

        return launchGame;
    }

    internal static void SetDefaultLaunchGame(LaunchGame game) => File.WriteAllText(DefaultLaunchGamePath, game.ToString());
}
