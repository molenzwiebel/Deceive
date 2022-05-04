using System;
using System.Windows.Forms;

namespace Deceive;

internal partial class GamePromptForm : Form
{
    internal static LaunchGame SelectedGame = LaunchGame.Auto;

    internal GamePromptForm() => InitializeComponent();

    private void OnFormLoad(object sender, EventArgs e) => Text = StartupHandler.DeceiveTitle;

    private void OnLoLLaunch(object sender, EventArgs e) => HandleLaunchChoiceAsync(LaunchGame.LoL);

    private void OnLoRLaunch(object sender, EventArgs e) => HandleLaunchChoiceAsync(LaunchGame.LoR);

    private void OnValorantLaunch(object sender, EventArgs e) => HandleLaunchChoiceAsync(LaunchGame.VALORANT);

    private void OnRiotClientLaunch(object sender, EventArgs e) => HandleLaunchChoiceAsync(LaunchGame.RiotClient);

    private void HandleLaunchChoiceAsync(LaunchGame game)
    {
        if (checkboxRemember.Checked)
            Persistence.SetDefaultLaunchGame(game);

        SelectedGame = game;
        DialogResult = DialogResult.OK;
    }
}
