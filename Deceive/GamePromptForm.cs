using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Deceive.Properties;
using Deceive.UI;

namespace Deceive;

/// <summary>
///     Borderless, owner-drawn launcher picker. Every tile is a <see cref="GameCard" />,
///     so the whole window is painted by us rather than by the Win32 common controls.
/// </summary>
internal sealed class GamePromptForm : Form
{
    private const int WmNcLButtonDown = 0xA1;
    private const int HtCaption = 0x2;
    private const int CsDropShadow = 0x20000;

    private static readonly Color LolAccent = Color.FromArgb(0xC8, 0xAA, 0x6E);
    private static readonly Color LorAccent = Color.FromArgb(0xE8, 0x91, 0x2D);
    private static readonly Color ValorantAccent = Color.FromArgb(0xFF, 0x46, 0x55);
    private static readonly Color LionAccent = Color.FromArgb(0xC4, 0xF5, 0x46);
    private static readonly Color TftAccent = Color.FromArgb(0xF2, 0xC1, 0x4E);
    private static readonly Color RiotAccent = Color.FromArgb(0xE8, 0x37, 0x3E);

    internal static LaunchGame SelectedGame = LaunchGame.Auto;

    private readonly float _scale;
    private readonly Timer _fadeIn;
    private readonly ToggleSwitch _remember;

    private readonly int _padding;
    private readonly int _titleBarHeight;
    private readonly int _footerY;

    internal GamePromptForm()
    {
        using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            _scale = graphics.DpiX / 96f;

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        _padding = S(26);
        _titleBarHeight = S(42);

        // All measurements are scaled by hand, so keep WinForms from scaling them again.
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;
        BackColor = Theme.BackdropBottom;
        ForeColor = Theme.TextPrimary;
        Font = Theme.GetFont(9f);
        Text = StartupHandler.DeceiveTitle;
        Icon = Resources.DeceiveIcon;

        var contentWidth = S(672);
        ClientSize = new Size(contentWidth + _padding * 2, S(572));

        BuildTitleBar();
        var gridBottom = BuildCards(contentWidth);

        _footerY = gridBottom + S(26);
        _remember = BuildRememberToggle(contentWidth);

        _fadeIn = new Timer { Interval = 15 };
        _fadeIn.Tick += OnFadeTick;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ClassStyle |= CsDropShadow;
            return parameters;
        }
    }

    /// <summary>Scales a design-time (96 DPI) measurement to the current display.</summary>
    private int S(float value) => (int)Math.Round(value * _scale);

    private void BuildTitleBar()
    {
        var buttonSize = new Size(S(30), S(24));
        var top = (_titleBarHeight - buttonSize.Height) / 2;

        var close = new WindowButton
        {
            Glyph = WindowGlyph.Close,
            HoverColor = Theme.Danger,
            UiScale = _scale,
            Size = buttonSize,
            Location = new Point(ClientSize.Width - _padding - buttonSize.Width, top)
        };
        close.Click += (_, _) => Close();

        var minimize = new WindowButton
        {
            Glyph = WindowGlyph.Minimize,
            UiScale = _scale,
            Size = buttonSize,
            Location = new Point(close.Left - buttonSize.Width - S(4), top)
        };
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;

        Controls.Add(close);
        Controls.Add(minimize);
    }

    /// <summary>Lays out the four game tiles plus a Teamfight Tactics / Riot Client row.</summary>
    /// <returns>The Y coordinate directly below the last tile.</returns>
    private int BuildCards(int contentWidth)
    {
        var gap = S(14);
        var cardWidth = (contentWidth - gap) / 2;
        var cardHeight = S(116);
        var top = _titleBarHeight + S(76);

        AddCard(Theme.GetLogo("lol"), LolAccent, "League of Legends", 1, LaunchGame.LoL,
            new Rectangle(_padding, top, cardWidth, cardHeight));

        AddCard(Theme.GetLogo("lor"), LorAccent, "Legends of Runeterra", 2, LaunchGame.LoR,
            new Rectangle(_padding + cardWidth + gap, top, cardWidth, cardHeight));

        AddCard(Theme.GetLogo("valorant"), ValorantAccent, "VALORANT", 3, LaunchGame.VALORANT,
            new Rectangle(_padding, top + cardHeight + gap, cardWidth, cardHeight));

        AddCard(Theme.GetLogo("lion"), LionAccent, "2XKO", 4, LaunchGame.Lion,
            new Rectangle(_padding + cardWidth + gap, top + cardHeight + gap, cardWidth, cardHeight));

        var wideTop = top + (cardHeight + gap) * 2;
        var wideHeight = cardHeight;
        // The Riot Client wordmark is far wider than the game logos, so it gets extra
        // padding to stop it from shouting over the games above it.
        AddCard(Theme.GetLogo("tft"), TftAccent, "Teamfight Tactics", 5, LaunchGame.TeamfightTactics,
            new Rectangle(_padding, wideTop, cardWidth, wideHeight));

        AddCard(Theme.GetLogo("riot_client"), RiotAccent, "Riot Client", 6, LaunchGame.RiotClient,
            new Rectangle(_padding + cardWidth + gap, wideTop, cardWidth, wideHeight));

        return wideTop + wideHeight;
    }

    private void AddCard(Image? logo, Color accent, string title, int hotkey, LaunchGame game, Rectangle bounds,
        float artworkPaddingX = 0.16f)
    {
        var card = new GameCard
        {
            Logo = logo,
            Accent = accent,
            Title = title,
            Hotkey = hotkey,
            ArtworkPaddingX = artworkPaddingX,
            UiScale = _scale,
            Bounds = bounds,
            TabIndex = hotkey,
            AccessibleName = "Launch " + title
        };

        card.Click += (_, _) => HandleLaunchChoice(game);
        Controls.Add(card);
    }

    private ToggleSwitch BuildRememberToggle(int contentWidth)
    {
        var toggle = new ToggleSwitch
        {
            Text = "Remember my choice and skip this screen",
            Accent = RiotAccent,
            UiScale = _scale,
            Bounds = new Rectangle(_padding, _footerY, contentWidth / 2, S(24)),
            TabIndex = 7,
            AccessibleName = "Remember my choice and skip this screen"
        };

        Controls.Add(toggle);
        return toggle;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        Opacity = 0;
        _fadeIn.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // WinForms selects the first tile once the window is shown, which makes it look
        // pre-picked. Clear it so the window opens calm; any arrow or Tab press focuses a tile.
        ActiveControl = null;

        ForceToForeground();
    }

    /// <summary>
    ///     By the time this form is shown, StartupHandler has already done several synchronous
    ///     steps (DNS resolution, update checks, etc.), so enough time has usually passed since
    ///     process launch that Windows denies us the foreground. When that happens the window is
    ///     created behind whatever else is on screen and never activates until the user clicks it
    ///     directly. SetForegroundWindow is normally restricted to whichever process already owns
    ///     the foreground, so we briefly attach our input queue to that thread's, which is the
    ///     documented way around the restriction.
    /// </summary>
    private void ForceToForeground()
    {
        var foregroundWindow = GetForegroundWindow();
        var foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        var currentThreadId = GetCurrentThreadId();

        var attached = foregroundThreadId != currentThreadId &&
                        AttachThreadInput(currentThreadId, foregroundThreadId, true);
        try
        {
            // Punch through the z-order directly too, in case activation is still denied.
            // Stay above other topmost windows too; the picker is modal and short-lived.
            TopMost = true;

            SetForegroundWindow(Handle);
            Activate();
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        var next = Opacity + 0.12;
        if (next >= 1)
        {
            Opacity = 1;
            _fadeIn.Stop();
            return;
        }

        Opacity = next;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        // Let the whole backdrop act as a title bar, since there is no system caption.
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        // Only stay above other topmost windows while we are the active window.
        TopMost = false;
        base.OnDeactivate(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
            case Keys.D1 or Keys.NumPad1:
                HandleLaunchChoice(LaunchGame.LoL);
                return true;
            case Keys.D2 or Keys.NumPad2:
                HandleLaunchChoice(LaunchGame.LoR);
                return true;
            case Keys.D3 or Keys.NumPad3:
                HandleLaunchChoice(LaunchGame.VALORANT);
                return true;
            case Keys.D4 or Keys.NumPad4:
                HandleLaunchChoice(LaunchGame.Lion);
                return true;
            case Keys.D5 or Keys.NumPad5:
                HandleLaunchChoice(LaunchGame.TeamfightTactics);
                return true;
            case Keys.D6 or Keys.NumPad6:
                HandleLaunchChoice(LaunchGame.RiotClient);
                return true;
            case Keys.Right or Keys.Down:
                SelectNextControl(ActiveControl, true, true, true, true);
                return true;
            case Keys.Left or Keys.Up:
                SelectNextControl(ActiveControl, false, true, true, true);
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void HandleLaunchChoice(LaunchGame game)
    {
        if (_remember.Checked)
            Persistence.SetDefaultLaunchGame(game);

        SelectedGame = game;
        DialogResult = DialogResult.OK;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Theme.PaintBackdrop(g, ClientRectangle, Point.Empty, ClientSize);
        PaintHeaderGlow(g);
        PaintTitleBar(g);
        PaintHeading(g);
        PaintFooter(g);

        using var border = new Pen(Theme.WindowBorder);
        g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    /// <summary>Soft red bloom behind the header, so the window has a focal point.</summary>
    internal void PaintBackdropExtras(Graphics g) => PaintHeaderGlow(g);

    private void PaintHeaderGlow(Graphics g)
    {
        var radius = S(340);
        var glow = new Rectangle(ClientSize.Width / 2 - radius, -radius + S(50), radius * 2, radius * 2);

        using var path = new GraphicsPath();
        path.AddEllipse(glow);

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Theme.WithAlpha(RiotAccent, 46),
            SurroundColors = new[] { Color.FromArgb(0, RiotAccent) }
        };

        g.FillEllipse(brush, glow);
    }

    private void PaintTitleBar(Graphics g)
    {
        if (Icon is not null)
        {
            var size = S(16);
            g.DrawIcon(Icon, new Rectangle(_padding, (_titleBarHeight - size) / 2, size, size));
        }

        var textTop = (_titleBarHeight - S(14)) / 2;
        var wordmarkWidth = Theme.DrawTracked(g, "DECEIVE", Theme.GetFont(8.5f * _scale, semibold: true),
            Theme.TextPrimary, _padding + S(24), textTop, 1.4f * _scale);

        TextRenderer.DrawText(g, Utils.DeceiveVersion, Theme.GetFont(8f * _scale),
            new Point(_padding + S(24) + wordmarkWidth + S(9), textTop + S(1)), Theme.TextMuted, TextFormatFlags.NoPadding);
    }

    private void PaintHeading(Graphics g)
    {
        var top = _titleBarHeight + S(18);

        TextRenderer.DrawText(g, "Which game would you like to launch?", Theme.GetFont(14f * _scale, semibold: true),
            new Point(_padding, top), Theme.TextPrimary, TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, "Deceive starts it with your status masked, so you appear offline to everyone.",
            Theme.GetFont(8.75f * _scale), new Point(_padding, top + S(26)), Theme.TextSecondary, TextFormatFlags.NoPadding);
    }

    private void PaintFooter(Graphics g)
    {
        var dividerY = _footerY - S(20);
        var right = ClientSize.Width - _padding;

        using (var brush = new LinearGradientBrush(
                   new Rectangle(_padding, dividerY, right - _padding, 1),
                   Theme.SurfaceBorder, Color.FromArgb(0, Theme.SurfaceBorder), LinearGradientMode.Horizontal))
            g.FillRectangle(brush, _padding, dividerY, right - _padding, 1);

        var hint = "1\u20136 to launch  \u00B7  Esc to cancel";
        var font = Theme.GetFont(8f * _scale);
        var size = TextRenderer.MeasureText(g, hint, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, hint, font,
            new Point(right - size.Width, _footerY + (S(24) - size.Height) / 2), Theme.TextMuted, TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fadeIn.Tick -= OnFadeTick;
            _fadeIn.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
