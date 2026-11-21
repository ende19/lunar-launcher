using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace LunarLauncher;

/// <summary>Lunar Roleplay palette (night sky, violet glow and gold, from the Lunar logo), fonts and images.</summary>
internal static class Theme
{
    // Night sky
    public static readonly Color NightTop = ColorTranslator.FromHtml("#170C2E");
    public static readonly Color Night = ColorTranslator.FromHtml("#0A0614");
    public static readonly Color Bg = Night;
    public static readonly Color Surface = ColorTranslator.FromHtml("#1C1430");
    public static readonly Color Border = ColorTranslator.FromHtml("#3A2B5E");
    public static readonly Color Card = Color.FromArgb(214, 20, 13, 38);
    public static readonly Color CardEdge = Color.FromArgb(120, 167, 139, 250);

    // Violet
    public static readonly Color Primary = ColorTranslator.FromHtml("#8B5CF6");
    public static readonly Color PrimaryHover = ColorTranslator.FromHtml("#A78BFA");
    public static readonly Color PrimaryDark = ColorTranslator.FromHtml("#7C3AED");
    public static readonly Color Glow = ColorTranslator.FromHtml("#7C3AED");

    // Gold
    public static readonly Color Gold = ColorTranslator.FromHtml("#F4D594");
    public static readonly Color GoldDeep = ColorTranslator.FromHtml("#D49A45");
    public static readonly Color GoldInk = ColorTranslator.FromHtml("#2A1640");

    // Text
    public static readonly Color Text = ColorTranslator.FromHtml("#F5F3FF");
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#B9B1D3");
    public static readonly Color Muted = ColorTranslator.FromHtml("#7F769C");
    public static readonly Color Danger = ColorTranslator.FromHtml("#F87171");
    public static readonly Color Success = ColorTranslator.FromHtml("#4ADE80");

    // Discord Blurple (discord.com/branding), for the sign-in button only; the hover shade is the button's pressed state.
    public static readonly Color Discord = ColorTranslator.FromHtml("#5865F2");
    public static readonly Color DiscordHover = ColorTranslator.FromHtml("#4752C4");

    private static readonly PrivateFontCollection Fonts = LoadFonts();

    /// <summary>Marcellus (SIL OFL), the serif that echoes the Lunar logo. Draw it with GDI+ (Graphics.DrawString).</summary>
    public static FontFamily Display => Fonts.Families.Length > 0 ? Fonts.Families[0] : FontFamily.GenericSerif;

    public static Image LoadImage(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Resource {name} missing.");
        return new Bitmap(stream);
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // Typographic layout that still counts spaces, which GenericTypographic alone measures as zero width.
    private static readonly StringFormat Typographic = CreateTypographic();

    private static StringFormat CreateTypographic()
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return format;
    }

    /// <summary>Draws text with extra spacing between letters, for display headings.</summary>
    public static void DrawSpaced(Graphics g, string text, Font font, Color color, PointF origin, float spacing)
    {
        using var brush = new SolidBrush(color);
        var x = origin.X;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            g.DrawString(s, font, brush, x, origin.Y, Typographic);
            x += g.MeasureString(s, font, PointF.Empty, Typographic).Width + spacing;
        }
    }

    public static SizeF MeasureSpaced(Graphics g, string text, Font font, float spacing)
    {
        float width = 0, height = 0;
        foreach (var ch in text)
        {
            var size = g.MeasureString(ch.ToString(), font, PointF.Empty, Typographic);
            width += size.Width + spacing;
            height = Math.Max(height, size.Height);
        }
        return new SizeF(Math.Max(0, width - spacing), height);
    }

    private static PrivateFontCollection LoadFonts()
    {
        var fonts = new PrivateFontCollection();
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Marcellus-Regular.ttf");
            if (stream == null) return fonts;
            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            // GDI+ keeps reading the font from this memory for the life of the process, so it is never freed.
            var memory = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, memory, data.Length);
            fonts.AddMemoryFont(memory, data.Length);
        }
        catch (Exception) { /* Falls back to the generic serif. */ }
        return fonts;
    }
}
