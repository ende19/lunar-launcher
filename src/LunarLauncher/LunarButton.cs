using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LunarLauncher;

/// <summary>
/// The launcher's main call to action: a gold button with Marcellus capitals, matching the gold of the logo.
/// "Secondary" is a quiet outlined variant for dialogs.
/// </summary>
internal sealed class LunarButton : Control
{
    private bool hover;
    private bool pressed;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Secondary { get; set; }

    public LunarButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        AccessibleName = Text;
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var scale = DeviceDpi / 96f;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.Rounded(rect, 10 * scale);

        Color textColor;
        if (Secondary)
        {
            using var fill = new SolidBrush(hover && Enabled ? Color.FromArgb(40, 167, 139, 250) : Color.FromArgb(18, 255, 255, 255));
            g.FillPath(fill, path);
            using var edge = new Pen(Enabled ? Theme.Border : Color.FromArgb(60, Theme.Border));
            g.DrawPath(edge, path);
            textColor = Enabled ? Theme.Text : Theme.Muted;
        }
        else if (!Enabled)
        {
            using var fill = new SolidBrush(Color.FromArgb(150, 52, 40, 82));
            g.FillPath(fill, path);
            using var edge = new Pen(Color.FromArgb(90, 120, 100, 170));
            g.DrawPath(edge, path);
            textColor = Theme.Muted;
        }
        else
        {
            var top = pressed ? Theme.GoldDeep : hover ? Color.FromArgb(255, 250, 228) : Theme.Gold;
            var bottom = pressed ? ColorTranslator.FromHtml("#B9812F") : Theme.GoldDeep;
            using var fill = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), top, bottom, LinearGradientMode.Vertical);
            g.FillPath(fill, path);
            // Thin highlight along the top edge, like the gilded strokes of the logo.
            using var shine = new Pen(Color.FromArgb(hover ? 200 : 140, 255, 248, 225), 1);
            g.DrawLine(shine, 10 * scale, 1.5f, Width - 10 * scale, 1.5f);
            using var edge = new Pen(Color.FromArgb(200, 140, 96, 32));
            g.DrawPath(edge, path);
            textColor = Theme.GoldInk;
        }

        var size = Secondary ? 10.5f : 15f;
        using var font = new Font(Theme.Display, size, FontStyle.Regular, GraphicsUnit.Point);
        var label = Secondary ? Text : Text.ToUpperInvariant();
        var spacing = Secondary ? 0.5f * scale : 3f * scale;
        var measured = Theme.MeasureSpaced(g, label, font, spacing);
        Theme.DrawSpaced(g, label, font, textColor, new PointF((Width - measured.Width) / 2f, (Height - measured.Height) / 2f), spacing);

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(Secondary ? Theme.PrimaryHover : Theme.GoldInk) { DashStyle = DashStyle.Dot };
            using var inner = Theme.Rounded(RectangleF.Inflate(rect, -4 * scale, -4 * scale), 7 * scale);
            g.DrawPath(focus, inner);
        }
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
}
