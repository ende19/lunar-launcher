using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace LunarLauncher;

/// <summary>Rounded progress bar: a violet-to-gold fill on a dark track.</summary>
internal sealed class ProgressStrip : Control
{
    private double value;

    public ProgressStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 8;
    }

    /// <summary>0 to 1.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => value;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (clamped == this.value) return;
            this.value = clamped;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var radius = Height / 2f;
        using (var track = Theme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), radius))
        using (var brush = new SolidBrush(Color.FromArgb(160, 8, 5, 18)))
        using (var edge = new Pen(Color.FromArgb(80, 167, 139, 250)))
        {
            g.FillPath(brush, track);
            g.DrawPath(edge, track);
        }
        var width = (float)((Width - 1) * value);
        if (width < Height) return;
        using var fill = Theme.Rounded(new RectangleF(0, 0, width, Height - 1), radius);
        using var gradient = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(1, Width), Height), Theme.Primary, Theme.Gold, LinearGradientMode.Horizontal);
        g.FillPath(gradient, fill);
    }
}
