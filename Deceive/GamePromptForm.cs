using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Deceive
{
    internal partial class GamePromptForm : Form
    {
        internal static LaunchGame SelectedGame = LaunchGame.Auto;

        internal GamePromptForm() => InitializeComponent();

        private void OnFormLoad(object sender, EventArgs e) => Text = StartupHandler.DeceiveTitle;

        private async void OnLoLLaunch(object sender, EventArgs e) => await HandleLaunchChoiceAsync(LaunchGame.LoL);

        private async void OnLoRLaunch(object sender, EventArgs e) => await HandleLaunchChoiceAsync(LaunchGame.LoR);

        private async void OnValorantLaunch(object sender, EventArgs e) =>
            await HandleLaunchChoiceAsync(LaunchGame.VALORANT);

        private async void OnRiotClientLaunch(object sender, EventArgs e) =>
            await HandleLaunchChoiceAsync(LaunchGame.RiotClient);

        private async Task HandleLaunchChoiceAsync(LaunchGame game)
        {
            if (checkboxRemember.Checked)
                await Persistence.SetDefaultLaunchGameAsync(game);

            SelectedGame = game;
            DialogResult = DialogResult.OK;
        }
    }
}