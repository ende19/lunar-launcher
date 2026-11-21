using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;

namespace LunarLauncher;

/// <summary>
/// "Accedi con [Discord logo]" in Discord's Blurple button style
/// (https://docs.discord.com/developers/discord-social-sdk/design-guidelines/branding-guidelines#buttons):
/// Blurple background, white text, the official white Discord logo unaltered, with spacing between the text,
/// the logo and the edges. The logo is Discord's own asset (discord.com/branding), rendered to PNG.
/// </summary>
internal sealed class DiscordButton : Control
{
    private static readonly Image Logo = LoadLogo();
    private bool hover;
    private bool pressed;

    public DiscordButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Text = "Accedi con";
        Font = new Font("Segoe UI Semibold", 10f);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        AccessibleName = "Accedi con Discord";
        AccessibleRole = AccessibleRole.PushButton;
        TabStop = true;
    }

    private int LogoHeight => (int)Math.Round(Font.Height * 0.62);
    private int LogoWidth => (int)Math.Round(LogoHeight * (double)Logo.Width / Logo.Height);
    private int Gap => (int)Math.Round(Font.Height * 0.4);
    private int SidePadding => (int)Math.Round(Font.Height * 0.85);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(SidePadding * 2 + text.Width + Gap + LogoWidth, (int)Math.Round(Font.Height * 2.1));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var fill = !Enabled ? Theme.Border : pressed || hover ? Theme.DiscordHover : Theme.Discord;
        using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), (int)Math.Round(Font.Height * 0.3)))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
        }

        var text = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        var content = text.Width + Gap + LogoWidth;
        var x = (Width - content) / 2;
        var textTop = (Height - text.Height) / 2;
        TextRenderer.DrawText(g, Text, Font, new Point(x, textTop), Color.White, fill, TextFormatFlags.NoPadding);

        // Bottom of the logo on the text baseline, so the type sits on the same line as the wordmark.
        var family = Font.FontFamily;
        var ascent = Font.Size * family.GetCellAscent(Font.Style) / family.GetEmHeight(Font.Style) * g.DpiY / 72f;
        var baseline = textTop + (int)Math.Round(ascent);
        g.DrawImage(Logo, new Rectangle(x + text.Width + Gap, baseline - LogoHeight, LogoWidth, LogoHeight));

        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Color.White) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(pen, 3, 3, Width - 7, Height - 7);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
        base.OnKeyUp(e);
    }

    private static Image LoadLogo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("discord-logo-white.png")
            ?? throw new InvalidOperationException("Discord logo resource missing.");
        return new Bitmap(stream);
    }
}
