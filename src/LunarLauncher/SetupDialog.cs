namespace LunarLauncher;

/// <summary>Small setup popup: asks where to install ArcaneMP, defaulting to the current install folder.</summary>
internal sealed class SetupDialog : Form
{
    private const string SubfolderName = "Lunar Roleplay";
    private readonly long required;
    private readonly TextBox folder = new();
    private readonly Label space = new();
    private readonly Label problem = new();
    private readonly Button install = new();

    public string SelectedFolder => Path.GetFullPath(folder.Text.Trim());

    public SetupDialog(string version, long requiredBytes, string defaultFolder)
    {
        required = requiredBytes;
        Text = "Installa ArcaneMP";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 250);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 20), ColumnCount = 1, BackColor = Theme.Bg };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label { Text = "Installa ArcaneMP " + version, AutoSize = true, Font = new Font("Segoe UI Semibold", 14f), Margin = new Padding(0) };
        var info = new Label
        {
            Text = "Verrà scaricato dal server ufficiale MafiaHub e verificato prima dell'installazione.",
            AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Theme.TextSecondary, Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 4, 0, 16),
        };
        var folderLabel = new Label { Text = "Cartella di installazione", AutoSize = true, Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 0, 0, 4) };

        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 6) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folder.Dock = DockStyle.Fill;
        folder.BorderStyle = BorderStyle.FixedSingle;
        folder.BackColor = Theme.Surface;
        folder.ForeColor = Theme.Text;
        folder.Margin = new Padding(0, 1, 8, 0);
        folder.Text = defaultFolder;
        folder.TextChanged += (_, _) => CheckInput();
        var browse = SecondaryButton("Sfoglia…");
        browse.Margin = new Padding(0);
        browse.Click += (_, _) => Browse();
        row.Controls.Add(folder, 0, 0);
        row.Controls.Add(browse, 1, 0);

        space.AutoSize = true;
        space.ForeColor = Theme.TextSecondary;
        space.Font = new Font("Segoe UI", 9f);
        space.Margin = new Padding(0, 0, 0, 2);
        problem.AutoSize = true;
        problem.MaximumSize = new Size(430, 0);
        problem.ForeColor = Theme.Danger;
        problem.Font = new Font("Segoe UI", 9f);
        problem.Margin = new Padding(0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        install.Text = "Installa";
        install.Size = new Size(120, 36);
        install.FlatStyle = FlatStyle.Flat;
        install.FlatAppearance.BorderSize = 0;
        install.FlatAppearance.MouseOverBackColor = Theme.PrimaryHover;
        install.FlatAppearance.MouseDownBackColor = Theme.PrimaryDark;
        install.ForeColor = Color.White;
        install.Font = new Font("Segoe UI Semibold", 10f);
        install.Cursor = Cursors.Hand;
        install.Margin = new Padding(8, 0, 0, 0);
        install.Click += (_, _) => { if (CheckInput()) DialogResult = DialogResult.OK; };
        var cancel = SecondaryButton("Annulla");
        cancel.Size = new Size(100, 36);
        cancel.Margin = new Padding(0);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(install);
        buttons.Controls.Add(cancel);
        AcceptButton = install;
        CancelButton = cancel;

        layout.Controls.Add(title);
        layout.Controls.Add(info);
        layout.Controls.Add(folderLabel);
        layout.Controls.Add(row);
        layout.Controls.Add(space);
        layout.Controls.Add(problem);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        CheckInput();
    }

    private static Button SecondaryButton(string text)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface, ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.5f), Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.MouseOverBackColor = Theme.Border;
        return button;
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
        install.BackColor = error == null ? Theme.Primary : Theme.Border;
        return error == null;
    }
}
