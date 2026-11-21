using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LunarLauncher;

/// <summary>
/// Lunar Roleplay social links, bottom right of the launcher. An empty URL is a placeholder: the icon shows
/// "In arrivo" and does nothing. Fill in the URLs here when the pages exist.
/// </summary>
internal static class SocialLinks
{
    public sealed record Link(string Name, string? Icon, string Url);

    public static readonly Link[] All =
    [
        new("Sito web", null, ""),
        new("Discord", "social-discord.png", ""),
        new("YouTube", "social-youtube.png", ""),
        new("TikTok", "social-tiktok.png", ""),
        new("Instagram", "social-instagram.png", ""),
    ];
}

/// <summary>A small monochrome social icon that turns gold on hover. Icons from Simple Icons (CC0).</summary>
internal sealed class SocialButton : Control
{
    private readonly SocialLinks.Link link;
    private readonly Image? icon;
    private bool hover;

    public SocialButton(SocialLinks.Link link, ToolTip tips)
    {
        this.link = link;
        icon = link.Icon == null ? null : Theme.LoadImage(link.Icon);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        AccessibleName = link.Name;
        AccessibleRole = AccessibleRole.Link;
        tips.SetToolTip(this, link.Url.Length > 0 ? link.Name : link.Name + " · in arrivo");
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (link.Url.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(link.Url) { UseShellExecute = true }); }
        catch (Exception) { /* No browser available: nothing else to do. */ }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var color = hover ? Theme.Gold : Theme.TextSecondary;
        var size = Math.Min(Width, Height) * 0.78f;
        var box = new RectangleF((Width - size) / 2, (Height - size) / 2, size, size);

        if (icon == null)
        {
            DrawGlobe(g, box, color);
            return;
        }
        // The icons are white; scale their colour channels to tint them.
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(
        [
            [color.R / 255f, 0, 0, 0, 0],
            [0, color.G / 255f, 0, 0, 0],
            [0, 0, color.B / 255f, 0, 0],
            [0, 0, 0, 1, 0],
            [0, 0, 0, 0, 1],
        ]));
        g.DrawImage(icon, Rectangle.Round(box), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, attributes);
    }

    /// <summary>Website: a simple line globe.</summary>
    private void DrawGlobe(Graphics g, RectangleF box, Color color)
    {
        box.Inflate(-box.Width * 0.06f, -box.Height * 0.06f);
        using var pen = new Pen(color, Math.Max(1.4f, box.Width / 12f));
        g.DrawEllipse(pen, box);
        var meridian = new RectangleF(box.X + box.Width * 0.27f, box.Y, box.Width * 0.46f, box.Height);
        g.DrawEllipse(pen, meridian);
        g.DrawLine(pen, box.X, box.Y + box.Height / 2, box.Right, box.Y + box.Height / 2);
        var inset = box.Width * 0.1f;
        g.DrawLine(pen, box.X + inset, box.Y + box.Height * 0.27f, box.Right - inset, box.Y + box.Height * 0.27f);
        g.DrawLine(pen, box.X + inset, box.Y + box.Height * 0.73f, box.Right - inset, box.Y + box.Height * 0.73f);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) icon?.Dispose();
        base.Dispose(disposing);
    }
}
