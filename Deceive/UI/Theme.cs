using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Deceive.UI;

/// <summary>
///     Central palette, typography and drawing helpers for Deceive's custom dark UI.
/// </summary>
internal static class Theme
{
    // Backdrop gradient, top to bottom.
    internal static readonly Color BackdropTop = Color.FromArgb(0x14, 0x16, 0x1F);
    internal static readonly Color BackdropBottom = Color.FromArgb(0x0A, 0x0B, 0x11);

    internal static readonly Color WindowBorder = Color.FromArgb(0x2A, 0x2F, 0x3D);
    internal static readonly Color Surface = Color.FromArgb(0x1A, 0x1E, 0x28);
    internal static readonly Color SurfaceTop = Color.FromArgb(0x20, 0x25, 0x31);
    internal static readonly Color SurfaceBorder = Color.FromArgb(0x2E, 0x34, 0x44);

    internal static readonly Color TextPrimary = Color.FromArgb(0xEC, 0xEE, 0xF4);
    internal static readonly Color TextSecondary = Color.FromArgb(0x8B, 0x93, 0xA7);
    internal static readonly Color TextMuted = Color.FromArgb(0x7A, 0x82, 0x96);

    internal static readonly Color Danger = Color.FromArgb(0xE8, 0x3D, 0x4A);

    private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();
    private static readonly Dictionary<string, Image> ImageCache = new Dictionary<string, Image>();

    /// <summary>
    ///     Fetches (and caches) a Segoe UI font. Falls back gracefully when the requested
    ///     weight is not installed, which keeps things sane on stripped-down Windows installs.
    /// </summary>
    internal static Font GetFont(float size, FontStyle style = FontStyle.Regular, bool semibold = false)
    {
        var family = semibold ? "Segoe UI Semibold" : "Segoe UI";
        var key = family + "|" + size.ToString("0.##") + "|" + (int)style;

        if (FontCache.TryGetValue(key, out var cached))
            return cached;

        Font font;
        try
        {
            font = new Font(family, size, style, GraphicsUnit.Point);
            // Font construction silently substitutes when the family is missing; detect that
            // and fall back to bold Segoe UI so the hierarchy still reads correctly.
            if (!font.Name.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                font.Dispose();
                font = new Font("Segoe UI", size, semibold ? style | FontStyle.Bold : style, GraphicsUnit.Point);
            }
        }
        catch (ArgumentException)
        {
            font = new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }

        FontCache[key] = font;
        return font;
    }

    /// <summary>
    ///     Loads one of the embedded game logos by file name (without extension).
    /// </summary>
    internal static Image? GetLogo(string name)
    {
        if (ImageCache.TryGetValue(name, out var cached))
            return cached;

        var assembly = Assembly.GetExecutingAssembly();
        var resource = Array.Find(assembly.GetManifestResourceNames(),
            candidate => candidate.EndsWith("games." + name + ".png", StringComparison.OrdinalIgnoreCase));

        if (resource is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(resource);
        if (stream is null)
            return null;

        // Copy into memory so the image does not keep the manifest stream alive.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        var image = Image.FromStream(buffer);
        ImageCache[name] = image;
        return image;
    }

    internal static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();

        return path;
    }

    internal static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Max(0f, Math.Min(1f, amount));
        return Color.FromArgb(
            from.A + (int)((to.A - from.A) * amount),
            from.R + (int)((to.R - from.R) * amount),
            from.G + (int)((to.G - from.G) * amount),
            from.B + (int)((to.B - from.B) * amount)
        );
    }

    internal static Color WithAlpha(Color color, int alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>
    ///     Paints the window backdrop gradient into a child control, aligned as if the control
    ///     were transparent. Lets rounded corners blend seamlessly with the form behind them.
    /// </summary>
    internal static void PaintBackdrop(Graphics g, Rectangle local, Point originInForm, Size formSize)
    {
        if (formSize.Height <= 0)
        {
            g.Clear(BackdropBottom);
            return;
        }

        var full = new Rectangle(-originInForm.X, -originInForm.Y, Math.Max(1, formSize.Width), formSize.Height);
        using var brush = new LinearGradientBrush(full, BackdropTop, BackdropBottom, LinearGradientMode.Vertical);
        g.FillRectangle(brush, local);
    }

    /// <summary>
    ///     Draws text with extra letter spacing, character by character. GDI has no tracking
    ///     support, and the wordmark needs it to read as a logo rather than a label.
    /// </summary>
    internal static int DrawTracked(Graphics g, string text, Font font, Color color, int x, int y, float tracking)
    {
        var cursor = (float)x;
        foreach (var character in text)
        {
            var glyph = character.ToString();
            var size = TextRenderer.MeasureText(g, glyph, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, glyph, font, new Point((int)Math.Round(cursor), y), color, TextFormatFlags.NoPadding);
            cursor += size.Width + tracking;
        }

        return (int)Math.Round(cursor - x - tracking);
    }

    /// <summary>
    ///     Draws a soft outward glow around a rounded rectangle by stroking it repeatedly
    ///     with widening, fading pens. Cheap enough for hover animation at this size.
    /// </summary>
    internal static void DrawGlow(Graphics g, RectangleF bounds, float radius, Color color, float strength, int layers = 5)
    {
        if (strength <= 0.01f)
            return;

        for (var i = layers; i >= 1; i--)
        {
            var inflate = i * 1.6f;
            var alpha = (int)(strength * 46f / i);
            if (alpha <= 0)
                continue;

            var outline = RectangleF.Inflate(bounds, inflate, inflate);
            using var path = RoundedRect(outline, radius + inflate);
            using var pen = new Pen(WithAlpha(color, Math.Min(alpha, 255)), 2.2f);
            g.DrawPath(pen, path);
        }
    }
}
