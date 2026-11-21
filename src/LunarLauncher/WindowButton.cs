using System.Drawing.Drawing2D;

namespace LunarLauncher;

/// <summary>Minimize and close for the frameless windows. Glyphs are drawn as lines, not text, so they stay centred.</summary>
internal sealed class WindowButton : Control
{
    public enum Kind { Minimize, Close }

    private readonly Kind kind;
    private bool hover;

    public WindowButton(Kind kind)
    {
        this.kind = kind;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
        AccessibleName = kind == Kind.Close ? "Chiudi" : "Riduci a icona";
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (hover)
        {
            using var fill = new SolidBrush(kind == Kind.Close ? Color.FromArgb(220, 196, 43, 28) : Color.FromArgb(36, 255, 255, 255));
            g.FillRectangle(fill, ClientRectangle);
        }
        var scale = DeviceDpi / 96f;
        var half = 5f * scale;
        var cx = Width / 2f;
        var cy = Height / 2f;
        using var pen = new Pen(hover ? Theme.Text : Theme.TextSecondary, 1.2f * scale);
        if (kind == Kind.Close)
        {
            g.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
            g.DrawLine(pen, cx - half, cy + half, cx + half, cy - half);
        }
        else
        {
            g.DrawLine(pen, cx - half, cy, cx + half, cy);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
}
