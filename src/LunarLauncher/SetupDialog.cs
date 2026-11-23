using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LunarLauncher;

/// <summary>
/// Small setup popup: asks where to install ArcaneMP, defaulting to the current install folder. A player who
/// already has ArcaneMP can link that install instead (LinkedRoot); the launcher then checks its version.
/// </summary>
internal sealed class SetupDialog : FramelessForm
{
    private const string SubfolderName = "Lunar Roleplay";
    private const int W = 540, H = 300;
    private readonly string version;
    private readonly long required;
    private readonly Panel field = new();
    private readonly TextBox folder = new();
    private readonly Label info = new();
    private readonly Label folderLabel = new();
    private readonly Label space = new();
    private readonly Label problem = new();
    private readonly LunarButton browse = new() { Text = "Sfoglia…", Secondary = true };
    private readonly LunarButton cancel = new() { Text = "Annulla", Secondary = true };
    private readonly LunarButton install = new() { Text = "Installa" };
    private readonly WindowButton close = new(WindowButton.Kind.Close);
    private readonly LinkLabel link = new();

    public string SelectedFolder => Path.GetFullPath(folder.Text.Trim());
    /// <summary>Set when the player linked an existing ArcaneMP install instead of choosing a new folder.</summary>
    public string? LinkedRoot { get; private set; }

    public SetupDialog(string version, long requiredBytes, string defaultFolder)
    {
        this.version = version;
        required = requiredBytes;
        Text = "Installa ArcaneMP";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Night;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;

        Label(info, "Verrà scaricato dal server ufficiale MafiaHub e verificato prima dell'installazione.", Theme.TextSecondary, 9.5f);
        Label(folderLabel, "Cartella di installazione", Theme.Text, 9.5f, bold: true);
        Label(space, "", Theme.TextSecondary, 9f);
        Label(problem, "", Theme.Danger, 9f);

        field.BackColor = Theme.Surface;
        field.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, field.ClientRectangle, folder.Focused ? Theme.PrimaryHover : Theme.Border, ButtonBorderStyle.Solid);
        folder.BorderStyle = BorderStyle.None;
        folder.BackColor = Theme.Surface;
        folder.ForeColor = Theme.Text;
        folder.Font = new Font("Segoe UI", 10f);
        folder.Text = defaultFolder;
        folder.TextChanged += (_, _) => CheckInput();
        folder.GotFocus += (_, _) => field.Invalidate();
        folder.LostFocus += (_, _) => field.Invalidate();
        field.Controls.Add(folder);

        link.Text = "Hai già ArcaneMP? Collega la tua installazione";
        link.AutoSize = true;
        link.BackColor = Color.Transparent;
        link.Font = new Font("Segoe UI Semibold", 9f);
        link.LinkColor = Theme.Gold;
        link.ActiveLinkColor = Theme.Text;
        link.LinkBehavior = LinkBehavior.HoverUnderline;
        link.LinkClicked += (_, _) => LinkExisting();

        browse.Click += (_, _) => Browse();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        install.Click += (_, _) => { if (CheckInput()) DialogResult = DialogResult.OK; };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
            else if (e.KeyCode == Keys.Enter && install.Enabled && CheckInput()) DialogResult = DialogResult.OK;
        };

        Controls.AddRange([info, folderLabel, field, browse, space, problem, link, cancel, install, close]);
        Draggable(this);
        Draggable(info);
        // Caret at the end instead of the whole path selected.
        Shown += (_, _) => folder.Select(folder.TextLength, 0);
        ApplyScale();
        CheckInput();
    }

    private static void Label(Label label, string text, Color color, float size, bool bold = false)
    {
        label.Text = text;
        label.AutoSize = true;
        label.BackColor = Color.Transparent;
        label.ForeColor = color;
        label.Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale();
    }

    private void ApplyScale()
    {
        ClientSize = new Size(S(W), S(H));
        close.Bounds = new Rectangle(S(W - 46), 0, S(46), S(36));
        info.MaximumSize = new Size(S(W - 56), 0);
        info.Location = new Point(S(26), S(72));
        folderLabel.Location = new Point(S(26), S(118));
        field.Bounds = new Rectangle(S(28), S(144), S(W - 56 - 130), S(38));
        folder.Bounds = new Rectangle(S(10), (field.Height - folder.PreferredHeight) / 2, field.Width - S(20), folder.PreferredHeight);
        browse.Bounds = new Rectangle(field.Right + S(10), field.Top, S(W - 28) - field.Right - S(10), field.Height);
        space.Location = new Point(S(26), S(190));
        problem.MaximumSize = new Size(S(W - 56), 0);
        problem.Location = new Point(S(26), S(210));
        install.Bounds = new Rectangle(S(W - 28 - 150), S(H - 28 - 46), S(150), S(46));
        cancel.Bounds = new Rectangle(install.Left - S(12) - S(120), install.Top, S(120), S(46));
        link.MaximumSize = new Size(cancel.Left - S(26) - S(12), 0);
        link.Location = new Point(S(26), install.Top + (install.Height - link.PreferredSize.Height) / 2);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var sky = new LinearGradientBrush(ClientRectangle, Theme.NightTop, Theme.Night, LinearGradientMode.Vertical))
        {
            g.FillRectangle(sky, ClientRectangle);
        }
        using (var path = new GraphicsPath())
        {
            var glow = new RectangleF(S(-80), S(-160), S(420), S(320));
            path.AddEllipse(glow);
            using var brush = new PathGradientBrush(path) { CenterColor = Color.FromArgb(90, 124, 58, 237), SurroundColors = [Color.Transparent] };
            g.FillEllipse(brush, glow);
        }
        using (var title = new Font(Theme.Display, 16f))
        {
            Theme.DrawSpaced(g, "Installa ArcaneMP " + version, title, Theme.Gold, new PointF(S(26), S(28)), S(1));
        }
        using var edge = new Pen(Theme.Border);
        g.DrawRectangle(edge, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Scegli dove installare ArcaneMP",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = ExistingParent(folder.Text.Trim()),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var chosen = dialog.SelectedPath;
        // A folder that already holds other things (e.g. D:\Games) gets its own "Lunar Roleplay" subfolder.
        if (ClientInstaller.CheckFolder(chosen) != null) chosen = Path.Combine(chosen, SubfolderName);
        folder.Text = chosen;
    }

    private void LinkExisting()
    {
        var found = GameLocator.FindExistingClient();
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Scegli la cartella di ArcaneMP (quella con {GameLocator.ExeName})",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            InitialDirectory = found ?? "",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            LinkedRoot = ClientInstaller.ResolveExisting(dialog.SelectedPath);
            DialogResult = DialogResult.OK;
        }
        catch (LauncherException error)
        {
            problem.Text = error.Message;
            problem.Visible = true;
        }
    }

    private static string ExistingParent(string path)
    {
        try
        {
            var dir = Path.GetFullPath(path);
            while (!Directory.Exists(dir) && Path.GetDirectoryName(dir) is { } parent) dir = parent;
            return Directory.Exists(dir) ? dir : "";
        }
        catch (Exception) { return ""; }
    }

    private bool CheckInput()
    {
        var text = folder.Text.Trim();
        string? error = text.Length == 0 || !Path.IsPathFullyQualified(text) ? "Inserisci un percorso completo, ad esempio D:\\Giochi\\Lunar Roleplay." : ClientInstaller.CheckFolder(text);
        long? free = null;
        if (error == null)
        {
            try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(text))!).AvailableFreeSpace; }
            catch (Exception) { error = "Disco non disponibile."; }
        }
        if (error == null && free < required) error = "Spazio su disco insufficiente.";
        space.Text = $"Spazio richiesto: circa {ClientInstaller.Gb(required)} GB" + (free is { } f ? $" · disponibile: {ClientInstaller.Gb(f)} GB" : "");
        problem.Text = error ?? "";
        problem.Visible = error != null;
        install.Enabled = error == null;
        return error == null;
    }
}
