namespace Deceive;

/// <summary>
///     Which game to automatically launch when Deceive is started.
/// </summary>
public enum LaunchGame
{
    /// <summary>
    ///     Attempt to start League of Legends.
    /// </summary>
    LoL,

    /// <summary>
    ///     Attempt to start Legends of Runeterra.
    /// </summary>
    LoR,

    /// <summary>
    ///     Attempt to start VALORANT.
    /// </summary>
    VALORANT,

    /// <summary>
    ///     Attempt to launch the Riot Client.
    /// </summary>
    RiotClient,

    /// <summary>
    ///     Display a dialog asking which game to launch.
    /// </summary>
    Prompt,

    /// <summary>
    ///     Automatically pick which game to launch, using either the configured
    ///     default launch method or prompting, depending on previous runs.
    /// </summary>
    Auto
}
