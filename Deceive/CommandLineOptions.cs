using System;

namespace Deceive;

/// Parses the same command line that System.CommandLine.DragonFruit used to accept:
///   Deceive [game] [--game-patchline value] [--riot-client-params value] [--game-params value]
/// Options accept both "--name value" and "--name=value".
internal sealed class CommandLineOptions
{
    internal const string Usage =
        "Usage: Deceive [lol|teamfighttactics|lor|valorant|lion|riotclient|prompt|auto] [options]\n\n" +
        "Options:\n" +
        "  --game-patchline <patchline>    The patchline to be used for launching the game (default: live).\n" +
        "  --riot-client-params <params>   Any extra parameters to be passed to the Riot Client.\n" +
        "  --game-params <params>          Any extra parameters to be passed to the launched game.";

    internal LaunchGame Game { get; private set; } = LaunchGame.Auto;
    internal string GamePatchline { get; private set; } = "live";
    internal string? RiotClientParams { get; private set; }
    internal string? GameParams { get; private set; }

    /// Returns false when the program should not continue. In that case <paramref name="error" /> is null
    /// if help was requested, or contains the reason parsing failed.
    internal static bool TryParse(string[] args, out CommandLineOptions options, out string? error)
    {
        options = new CommandLineOptions();
        error = null;
        var gameSeen = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-h" or "--help" or "-?" or "/?")
                return false;

            if (!arg.StartsWith('-'))
            {
                if (gameSeen || !Enum.TryParse(arg, true, out LaunchGame game) || !Enum.IsDefined(game))
                {
                    error = $"Unrecognized argument '{arg}'.";
                    return false;
                }

                options.Game = game;
                gameSeen = true;
                continue;
            }

            string name;
            string? value;
            var equals = arg.IndexOf('=');
            if (equals > 0)
            {
                name = arg[..equals];
                value = arg[(equals + 1)..];
            }
            else
            {
                name = arg;
                // The value may itself start with dashes (e.g. --riot-client-params "--allow-multiple-clients").
                value = i + 1 < args.Length ? args[++i] : null;
            }

            if (value is null)
            {
                error = $"Missing value for option '{name}'.";
                return false;
            }

            switch (name.ToLowerInvariant())
            {
                case "--game-patchline":
                    options.GamePatchline = value;
                    break;
                case "--riot-client-params":
                    options.RiotClientParams = value;
                    break;
                case "--game-params":
                    options.GameParams = value;
                    break;
                default:
                    error = $"Unrecognized option '{name}'.";
                    return false;
            }
        }

        return true;
    }
}
