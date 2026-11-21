using System.Runtime.InteropServices;

namespace LunarLauncher;

/// <summary>
/// Window without the Windows frame: drop shadow, rounded corners on Windows 11, and dragging by any part of
/// the background that is not a control.
/// </summary>
internal class FramelessForm : Form
{
    private const int CsDropShadow = 0x00020000;
    private const int WmNcLButtonDown = 0xA1;
    private const int HtCaption = 0x2;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmRound = 2;

    public FramelessForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected float UiScale => DeviceDpi / 96f;

    protected int S(float logical) => (int)Math.Round(logical * UiScale);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CsDropShadow;
            cp.Style |= 0x00020000; // WS_MINIMIZEBOX: lets the taskbar button minimise and restore the window.
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var round = DwmRound;
            DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref round, sizeof(int));
            var border = ColorTranslator.ToWin32(Theme.Border);
            DwmSetWindowAttribute(Handle, DwmBorderColor, ref border, sizeof(int));
        }
        catch (Exception) { /* Before Windows 11: square corners, which is fine. */ }
    }

    /// <summary>Lets the player drag the window from <paramref name="control"/> (the form itself or a label on it).</summary>
    protected void Draggable(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, int wParam, int lParam);
}
