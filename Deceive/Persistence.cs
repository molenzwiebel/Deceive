using System;
using System.IO;
using System.Threading.Tasks;

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
    internal static Task<string> GetPromptedUpdateVersionAsync() => File.Exists(UpdateVersionPath) ? File.ReadAllTextAsync(UpdateVersionPath) : Task.FromResult(string.Empty);

    internal static Task SetPromptedUpdateVersionAsync(string version) => File.WriteAllTextAsync(UpdateVersionPath, version);

    // Configured launch option.
    internal static async Task<LaunchGame> GetDefaultLaunchGameAsync()
    {
        if (!File.Exists(DefaultLaunchGamePath))
            return LaunchGame.Prompt;

        var contents = await File.ReadAllTextAsync(DefaultLaunchGamePath);
        if (!Enum.TryParse(contents, true, out LaunchGame launchGame))
            launchGame = LaunchGame.Prompt;

        return launchGame;
    }

    internal static Task SetDefaultLaunchGameAsync(LaunchGame game) => File.WriteAllTextAsync(DefaultLaunchGamePath, game.ToString());
}
