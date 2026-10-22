using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Deceive.UI;

internal enum WindowGlyph
{
    Close,
    Minimize
}

/// <summary>
///     Caption button for the custom title bar. Draws its glyph with vectors so it
///     stays crisp at any scale, unlike a font-based glyph.
/// </summary>
internal sealed class WindowButton : Control
{
    private bool _hovered;
    private bool _pressed;

    internal WindowButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true
        );

        TabStop = false;
        BackColor = Color.Transparent;
    }

    internal WindowGlyph Glyph { get; set; } = WindowGlyph.Close;

    /// <summary>Background colour when the pointer is over the button.</summary>
    internal Color HoverColor { get; set; } = Color.FromArgb(0x2A, 0x30, 0x3E);

    /// <summary>UI scale factor, so the glyph stays proportional on high-DPI displays.</summary>
    internal float UiScale { get; set; } = 1f;

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
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

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

        if (_hovered)
        {
            var body = new RectangleF(0, 0, Width, Height);
            using var path = Theme.RoundedRect(body, 6f * UiScale);
            using var brush = new SolidBrush(_pressed ? Theme.Blend(HoverColor, Color.Black, 0.25f) : HoverColor);
            g.FillPath(brush, path);
        }

        var color = _hovered ? Color.White : Theme.TextSecondary;
        using var pen = new Pen(color, 1.3f * UiScale) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        var center = new PointF(Width / 2f, Height / 2f);
        var arm = 4.5f * UiScale;

        if (Glyph is WindowGlyph.Close)
        {
            g.DrawLine(pen, center.X - arm, center.Y - arm, center.X + arm, center.Y + arm);
            g.DrawLine(pen, center.X + arm, center.Y - arm, center.X - arm, center.Y + arm);
        }
        else
        {
            g.DrawLine(pen, center.X - arm, center.Y, center.X + arm, center.Y);
        }
    }
}
