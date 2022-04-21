using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Deceive;

internal static class Persistence
{
    internal static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Deceive");

    internal static readonly string UpdateVersionPath = Path.Combine(DataDir, "updateVersionPrompted");
    internal static readonly string DefaultLaunchGamePath = Path.Combine(DataDir, "launchGame");

    static Persistence()
    {
        if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
    }

    // Prompted update version.
    internal static string? GetPromptedUpdateVersion()
    {
        return File.Exists(UpdateVersionPath) ? File.ReadAllText(UpdateVersionPath) : null;
    }

    internal static void SetPromptedUpdateVersion(string version)
    {
        File.WriteAllText(UpdateVersionPath, version);
    }

    // Configured launch option.
    internal static LaunchGame GetDefaultLaunchGame()
    {
        if (!File.Exists(DefaultLaunchGamePath)) return LaunchGame.Prompt;

        var contents = File.ReadAllText(DefaultLaunchGamePath);
        if (!Enum.TryParse(contents, true, out LaunchGame launchGame))
        {
            launchGame = LaunchGame.Prompt;
        }

        return launchGame;
    }

    internal static void SetDefaultLaunchGame(LaunchGame game)
    {
        File.WriteAllText(DefaultLaunchGamePath, game.ToString());
    }
}
