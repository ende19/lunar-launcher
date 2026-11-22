using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Net.Sockets;

namespace LunarLauncher;

/// <summary>
/// Game server status in the title bar: a green dot when the server answers, a red one when it does not.
/// The check is a plain TCP connect to the game port (the ArcaneMP server listens there on TCP as well as
/// UDP), so it needs nothing from the server and sends no data.
/// </summary>
internal sealed class ServerStatus : Control
{
    public enum State { Checking, Online, Offline }

    private static readonly Color Online = Color.FromArgb(52, 211, 153);
    private static readonly Color Offline = Color.FromArgb(248, 113, 113);

    private State state = State.Checking;

    public ServerStatus()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI Semibold", 9f);
        AccessibleRole = AccessibleRole.StaticText;
        AccessibleName = Label;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public State Current
    {
        get => state;
        set
        {
            if (state == value) return;
            state = value;
            AccessibleName = Label;
            Invalidate();
        }
    }

    private string Label => state switch
    {
        State.Online => "Server online",
        State.Offline => "Server offline",
        _ => "Controllo server…",
    };

    /// <summary>True when a TCP connection to the game port opens within the timeout.</summary>
    public static async Task<bool> ProbeAsync(string host, int port, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            return true;
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException && !cancel.IsCancellationRequested)
        {
            return false;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText("Controllo server…", Font);
        return new Size(Dot + Gap + text.Width + Pad * 2, Math.Max(text.Height + 8, Dot + 12));
    }

    private int Dot => (int)Math.Round(Font.Height * 0.5);
    private int Gap => (int)Math.Round(Font.Height * 0.45);
    private int Pad => (int)Math.Round(Font.Height * 0.7);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var text = TextRenderer.MeasureText(g, Label, Font, Size.Empty, TextFormatFlags.NoPadding);
        var width = Pad * 2 + Dot + Gap + text.Width;
        var pill = new RectangleF(0.5f, 0.5f, Math.Min(width, Width) - 1, Height - 1);
        using (var path = Theme.Rounded(pill, pill.Height / 2))
        using (var fill = new SolidBrush(Color.FromArgb(150, 28, 19, 52)))
        using (var edge = new Pen(Color.FromArgb(90, 167, 139, 250)))
        {
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }

        var color = state switch { State.Online => Online, State.Offline => Offline, _ => Theme.Muted };
        var dot = new RectangleF(Pad, (Height - Dot) / 2f, Dot, Dot);
        if (state == State.Online)
        {
            // Soft halo so "online" reads at a glance.
            var halo = RectangleF.Inflate(dot, Dot * 0.45f, Dot * 0.45f);
            using var glow = new SolidBrush(Color.FromArgb(70, color));
            g.FillEllipse(glow, halo);
        }
        using (var brush = new SolidBrush(color))
        {
            g.FillEllipse(brush, dot);
        }
        TextRenderer.DrawText(g, Label, Font, new Point(Pad + Dot + Gap, (Height - text.Height) / 2),
            state == State.Checking ? Theme.TextSecondary : Theme.Text, TextFormatFlags.NoPadding);
    }
}
