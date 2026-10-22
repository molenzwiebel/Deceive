using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Deceive.UI;

/// <summary>
///     A sliding on/off switch with a label, replacing the stock checkbox.
/// </summary>
internal sealed class ToggleSwitch : Control
{
    private readonly Timer _animation;
    private float _knob;
    private bool _checked;
    private bool _hovered;

    internal ToggleSwitch()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable |
            ControlStyles.SupportsTransparentBackColor,
            true
        );

        TabStop = true;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;

        _animation = new Timer { Interval = 15 };
        _animation.Tick += OnAnimationTick;
    }

    internal Color Accent { get; set; } = Color.FromArgb(0xE8, 0x3D, 0x4A);

    /// <summary>UI scale factor, so the switch stays proportional on high-DPI displays.</summary>
    internal float UiScale { get; set; } = 1f;

    internal bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;

            _checked = value;
            if (!_animation.Enabled)
                _animation.Start();

            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal event EventHandler? CheckedChanged;

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var target = _checked ? 1f : 0f;
        var delta = target - _knob;

        if (Math.Abs(delta) < 0.02f)
        {
            _knob = target;
            _animation.Stop();
        }
        else
        {
            _knob += delta * 0.3f;
        }

        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        Focus();
        Checked = !Checked;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not Keys.Space)
            return;

        Checked = !Checked;
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var form = FindForm();
        var origin = form is null ? Point.Empty : form.PointToClient(PointToScreen(Point.Empty));
        Theme.PaintBackdrop(g, ClientRectangle, origin, form is null ? ClientSize : form.ClientSize);
        
        if (form is GamePromptForm prompt)
        {
            var state = g.Save();
            g.TranslateTransform(-origin.X, -origin.Y);
            prompt.PaintBackdropExtras(g);
            g.Restore(state);
        }

        var trackWidth = 34f * UiScale;
        var trackHeight = 18f * UiScale;
        var track = new RectangleF(1f, (Height - trackHeight) / 2f, trackWidth, trackHeight);

        using (var path = Theme.RoundedRect(track, trackHeight / 2f))
        {
            var off = _hovered ? Color.FromArgb(0x30, 0x36, 0x46) : Color.FromArgb(0x28, 0x2D, 0x3B);
            using var brush = new SolidBrush(Theme.Blend(off, Accent, _knob));
            g.FillPath(brush, path);

            using var pen = new Pen(Theme.Blend(Theme.SurfaceBorder, Theme.Blend(Accent, Color.White, 0.2f), _knob), 1f * UiScale);
            g.DrawPath(pen, path);
        }

        var knobSize = trackHeight - 5f * UiScale;
        var travel = track.Width - knobSize - 2.5f * UiScale;
        var knob = new RectangleF(
            track.X + 2.5f * UiScale + travel * _knob,
            track.Y + (track.Height - knobSize) / 2f,
            knobSize,
            knobSize
        );

        using (var brush = new SolidBrush(Theme.Blend(Color.FromArgb(0x9A, 0xA2, 0xB4), Color.White, _knob)))
            g.FillEllipse(brush, knob);

        var textBounds = new Rectangle(
            (int)(track.Right + 10f * UiScale),
            0,
            Width - (int)(track.Right + 10f * UiScale),
            Height
        );

        var textColor = _checked ? Theme.TextPrimary : _hovered ? Theme.TextSecondary : Theme.TextMuted;
        TextRenderer.DrawText(g, Text, Theme.GetFont(8.75f * UiScale), textBounds, textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        if (!Focused)
            return;

        var ring = new RectangleF(track.X - 3f * UiScale, track.Y - 3f * UiScale, track.Width + 6f * UiScale, track.Height + 6f * UiScale);
        using var ringPath = Theme.RoundedRect(ring, ring.Height / 2f);
        using var ringPen = new Pen(Theme.WithAlpha(Accent, 130), 1.2f * UiScale) { DashStyle = DashStyle.Dot };
        g.DrawPath(ringPen, ringPath);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Tick -= OnAnimationTick;
            _animation.Dispose();
        }

        base.Dispose(disposing);
    }
}
