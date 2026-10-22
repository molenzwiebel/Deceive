using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Deceive.UI;

/// <summary>
///     A large, owner-drawn launch tile: game artwork on a dark card that lights up
///     in the game's own accent colour on hover, focus or keyboard selection.
/// </summary>
internal sealed class GameCard : Control
{
    private const float CornerRadius = 12f;

    private readonly Timer _animation;
    private float _hover;
    private float _hoverTarget;
    private bool _pressed;

    internal GameCard()
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

    /// <summary>Artwork drawn in the centre of the card.</summary>
    internal Image? Logo { get; set; }

    /// <summary>Colour the card lights up with. Usually taken from the artwork itself.</summary>
    internal Color Accent { get; set; } = Color.FromArgb(0xC8, 0xAA, 0x6E);

    /// <summary>Fallback text drawn when no artwork is available.</summary>
    internal string Title { get; set; } = string.Empty;

    /// <summary>Digit shown in the corner badge, matching the keyboard shortcut. 0 hides it.</summary>
    internal int Hotkey { get; set; }

    /// <summary>Breathing room left and right of the artwork, as a fraction of the card width.</summary>
    internal float ArtworkPaddingX { get; set; } = 0.16f;

    /// <summary>Breathing room above and below the artwork, as a fraction of the card height.</summary>
    internal float ArtworkPaddingY { get; set; } = 0.22f;

    /// <summary>UI scale factor, so the card stays proportional on high-DPI displays.</summary>
    internal float UiScale { get; set; } = 1f;

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var delta = _hoverTarget - _hover;
        if (Math.Abs(delta) < 0.02f)
        {
            _hover = _hoverTarget;
            _animation.Stop();
        }
        else
        {
            _hover += delta * 0.22f;
        }

        Invalidate();
    }

    private void AnimateTo(float target)
    {
        _hoverTarget = target;
        if (!_animation.Enabled)
            _animation.Start();
    }

    private void UpdateHoverTarget() => AnimateTo(ClientRectangle.Contains(PointToClient(MousePosition)) || Focused ? 1f : 0f);

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        AnimateTo(1f);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _pressed = false;
        UpdateHoverTarget();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        AnimateTo(1f);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        UpdateHoverTarget();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
            return;

        Focus();
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Enter or Keys.Space))
            return;

        _pressed = true;
        Invalidate();
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is not (Keys.Enter or Keys.Space))
            return;

        _pressed = false;
        Invalidate();
        e.Handled = true;
        OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var form = FindForm();
        var origin = form is null ? Point.Empty : form.PointToClient(PointToScreen(Point.Empty));
        Theme.PaintBackdrop(g, ClientRectangle, origin, form is null ? ClientSize : form.ClientSize);
        
        // The form paints its header bloom on top of the backdrop; repeat it here or the card
        // rectangles show up as visible seams where the bloom is.
        if (form is GamePromptForm prompt)
        {
            var state = g.Save();
            g.TranslateTransform(-origin.X, -origin.Y);
            prompt.PaintBackdropExtras(g);
            g.Restore(state);
        }

        var inset = _pressed ? 2f * UiScale : 0f;
        var body = new RectangleF(
            1f * UiScale + inset,
            1f * UiScale + inset,
            Width - 2f * UiScale - inset * 2f,
            Height - 2f * UiScale - inset * 2f
        );

        var radius = CornerRadius * UiScale;

        Theme.DrawGlow(g, body, radius, Accent, _hover * (_pressed ? 0.5f : 1f));

        using (var path = Theme.RoundedRect(body, radius))
        {
            var top = Theme.Blend(Theme.SurfaceTop, Accent, _hover * 0.14f);
            var bottom = Theme.Blend(Theme.Surface, Accent, _hover * 0.06f);
            if (_pressed)
            {
                top = Theme.Blend(top, Color.Black, 0.18f);
                bottom = Theme.Blend(bottom, Color.Black, 0.18f);
            }

            using (var fill = new LinearGradientBrush(Rectangle.Round(body), top, bottom, LinearGradientMode.Vertical))
                g.FillPath(fill, path);

            using var pen = new Pen(Theme.Blend(Theme.SurfaceBorder, Accent, _hover * 0.85f), (1f + _hover * 0.6f) * UiScale);
            g.DrawPath(pen, path);
        }

        DrawAccentBar(g, body);
        DrawArtwork(g, body);
        DrawHotkeyBadge(g, body);
        DrawLaunchHint(g, body);

        if (Focused)
            DrawFocusRing(g, body, radius);
    }

    private void DrawAccentBar(Graphics g, RectangleF body)
    {
        var height = body.Height * (0.22f + _hover * 0.42f);
        var bar = new RectangleF(
            body.X + 1f * UiScale,
            body.Y + (body.Height - height) / 2f,
            3.5f * UiScale,
            height
        );

        using var path = Theme.RoundedRect(bar, 2f * UiScale);
        using var brush = new SolidBrush(Theme.WithAlpha(Accent, (int)(110 + _hover * 145)));
        g.FillPath(brush, path);
    }

    private void DrawArtwork(Graphics g, RectangleF body)
    {
        var box = RectangleF.Inflate(body, -body.Width * ArtworkPaddingX, -body.Height * ArtworkPaddingY);

        if (Logo is null)
        {
            TextRenderer.DrawText(g, Title, Theme.GetFont(15f * UiScale, semibold: true), Rectangle.Round(body),
                Theme.TextPrimary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var ratio = Math.Min(box.Width / Logo.Width, box.Height / Logo.Height);
        // Slight lift on hover, so the artwork feels like it is coming forward.
        ratio *= 1f + _hover * 0.035f;

        var width = Logo.Width * ratio;
        var height = Logo.Height * ratio;
        var target = new RectangleF(
            body.X + (body.Width - width) / 2f,
            body.Y + (body.Height - height) / 2f,
            width,
            height
        );

        // Idle artwork sits slightly dimmed; hovering brings it to full strength.
        var opacity = 0.82f + _hover * 0.18f;
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity };
        attributes.SetColorMatrix(matrix);

        g.DrawImage(Logo, Rectangle.Round(target), 0, 0, Logo.Width, Logo.Height, GraphicsUnit.Pixel, attributes);
    }

    private void DrawHotkeyBadge(Graphics g, RectangleF body)
    {
        if (Hotkey <= 0)
            return;

        var size = 18f * UiScale;
        var badge = new RectangleF(body.X + 9f * UiScale, body.Y + 9f * UiScale, size, size);

        using (var path = Theme.RoundedRect(badge, 5f * UiScale))
        {
            using var brush = new SolidBrush(Theme.WithAlpha(Color.Black, (int)(60 + _hover * 60)));
            g.FillPath(brush, path);

            using var pen = new Pen(Theme.WithAlpha(Accent, (int)(35 + _hover * 120)), 1f * UiScale);
            g.DrawPath(pen, path);
        }

        var color = Theme.Blend(Theme.TextMuted, Accent, _hover);
        TextRenderer.DrawText(g, Hotkey.ToString(), Theme.GetFont(7.5f * UiScale, semibold: true), Rectangle.Round(badge),
            color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawLaunchHint(Graphics g, RectangleF body)
    {
        if (_hover < 0.05f)
            return;

        var text = "LAUNCH \u203A";
        var font = Theme.GetFont(7.5f * UiScale, semibold: true);
        var size = TextRenderer.MeasureText(g, text, font);
        var bounds = new Rectangle(
            (int)(body.Right - size.Width - 12f * UiScale),
            (int)(body.Bottom - size.Height - 8f * UiScale),
            size.Width,
            size.Height
        );

        TextRenderer.DrawText(g, text, font, bounds, Theme.WithAlpha(Accent, (int)(_hover * 235)), TextFormatFlags.NoPadding);
    }

    private void DrawFocusRing(Graphics g, RectangleF body, float radius)
    {
        var ring = RectangleF.Inflate(body, -3f * UiScale, -3f * UiScale);
        using var path = Theme.RoundedRect(ring, Math.Max(2f, radius - 3f * UiScale));
        using var pen = new Pen(Theme.WithAlpha(Accent, 150), 1.2f * UiScale) { DashStyle = DashStyle.Dot };
        g.DrawPath(pen, path);
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
