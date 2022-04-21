using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Deceive;

public partial class GamePromptForm : Form
{
    public static LaunchGame SelectedGame = LaunchGame.Auto;

    public GamePromptForm()
    {
        InitializeComponent();
    }

    private void OnFormLoad(object sender, EventArgs e)
    {
        Text = StartupHandler.DeceiveTitle;
    }

    private void OnLoLLaunch(object sender, EventArgs e)
    {
        HandleLaunchChoice(LaunchGame.LoL);
    }

    private void OnLoRLaunch(object sender, EventArgs e)
    {
        HandleLaunchChoice(LaunchGame.LoR);
    }

    private void OnValorantLaunch(object sender, EventArgs e)
    {
        HandleLaunchChoice(LaunchGame.VALORANT);
    }

    private void OnRiotClientLaunch(object sender, EventArgs e)
    {
        HandleLaunchChoice(LaunchGame.RiotClient);
    }

    private void HandleLaunchChoice(LaunchGame game)
    {
        if (checkboxRemember.Checked)
        {
            Persistence.SetDefaultLaunchGame(game);
        }

        SelectedGame = game;
        DialogResult = DialogResult.OK;
    }
}
