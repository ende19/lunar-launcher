using System.ComponentModel;

namespace LunarLauncher;

/// <summary>Flat progress bar in the Lunar colours (the stock ProgressBar ignores custom colours).</summary>
internal sealed class ProgressStrip : Control
{
    private double value;

    public ProgressStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
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
        using var track = new SolidBrush(Theme.Surface);
        using var fill = new SolidBrush(Theme.Primary);
        e.Graphics.FillRectangle(track, ClientRectangle);
        var width = (int)Math.Round(ClientSize.Width * value);
        if (width > 0) e.Graphics.FillRectangle(fill, 0, 0, width, ClientSize.Height);
    }
}
