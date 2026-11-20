using System.Diagnostics;

namespace LunarLauncher;

internal sealed class MainForm : Form
{
    private readonly LauncherConfig config;
    private readonly Label status = new();
    private readonly Button primary = new();
    private readonly Label gamePath = new();
    private readonly LinkLabel changeGame = new();
    private readonly Label accountId = new();
    private readonly LinkLabel copyAccount = new();
    private readonly CancellationTokenSource closing = new();

    private string? token;
    private DiscordMember? member;
    private string? gameExe;
    private bool busy;

    public MainForm(LauncherConfig config)
    {
        this.config = config;
        Text = "Lunar Roleplay";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 420);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 10f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(32, 28, 32, 24),
            ColumnCount = 1,
            BackColor = Theme.Bg,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Lunar Roleplay", AutoSize = true, ForeColor = Theme.Text,
            Font = new Font("Segoe UI Semibold", 22f), Margin = new Padding(0),
        };
        var subtitle = new Label
        {
            Text = "Accedi con Discord per entrare nel server.", AutoSize = true,
            ForeColor = Theme.TextSecondary, Margin = new Padding(2, 2, 0, 24),
        };

        var card = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, 16), Padding = new Padding(16, 0, 16, 0) };
        card.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Theme.Border, ButtonBorderStyle.Solid);
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.ForeColor = Theme.TextSecondary;
        status.BackColor = Theme.Surface;
        card.Controls.Add(status);

        primary.Dock = DockStyle.Top;
        primary.Height = 46;
        primary.FlatStyle = FlatStyle.Flat;
        primary.FlatAppearance.BorderSize = 0;
        primary.FlatAppearance.MouseOverBackColor = Theme.PrimaryHover;
        primary.FlatAppearance.MouseDownBackColor = Theme.PrimaryDark;
        primary.BackColor = Theme.Primary;
        primary.ForeColor = Color.White;
        primary.Font = new Font("Segoe UI Semibold", 11f);
        primary.Cursor = Cursors.Hand;
        primary.Margin = new Padding(0, 0, 0, 24);
        primary.Click += async (_, _) => await OnPrimaryAsync();

        var gameRow = Row(gamePath, changeGame, "Cambia");
        changeGame.LinkClicked += (_, _) => PickGame();
        var accountRow = Row(accountId, copyAccount, "Copia");
        copyAccount.LinkClicked += (_, _) => CopyAccount();

        layout.Controls.Add(title);
        layout.Controls.Add(subtitle);
        layout.Controls.Add(card);
        layout.Controls.Add(primary);
        layout.Controls.Add(gameRow);
        layout.Controls.Add(accountRow);
        Controls.Add(layout);

        // Refresh the account ID when the player comes back from the game, which may have just created it.
        Activated += (_, _) => RefreshGame();
        FormClosing += (_, _) => closing.Cancel();
        RefreshGame();
        Render();
    }

    private static Control Row(Label label, LinkLabel link, string linkText)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 6) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        label.AutoSize = true;
        label.AutoEllipsis = true;
        label.ForeColor = Theme.TextSecondary;
        label.Font = new Font("Segoe UI", 9f);
        label.Margin = new Padding(0, 2, 8, 0);
        label.MaximumSize = new Size(300, 0);
        link.Text = linkText;
        link.AutoSize = true;
        link.Font = new Font("Segoe UI", 9f);
        link.LinkColor = Theme.PrimaryHover;
        link.ActiveLinkColor = Theme.Text;
        link.LinkBehavior = LinkBehavior.HoverUnderline;
        link.Margin = new Padding(0, 2, 0, 0);
        row.Controls.Add(label, 0, 0);
        row.Controls.Add(link, 1, 0);
        return row;
    }

    private void RefreshGame()
    {
        if (!GameLocator.IsValid(gameExe)) gameExe = GameLocator.Find();
        gamePath.Text = gameExe == null ? "Gioco non trovato" : "Gioco: " + Path.GetDirectoryName(gameExe);
        var id = AccountLog.Read(gameExe);
        accountId.Text = id == null ? "ID account: avvia il gioco una volta per generarlo" : "ID account: " + id;
        accountId.Tag = id;
        copyAccount.Visible = id != null;
    }

    private void Render(string? message = null, bool error = false)
    {
        primary.Text = member == null ? "Accedi con Discord" : "Gioca";
        primary.Enabled = !busy;
        primary.BackColor = busy ? Theme.Border : Theme.Primary;
        status.ForeColor = error ? Theme.Danger : member != null ? Theme.Text : Theme.TextSecondary;
        status.Text = message ?? (member == null ? "Non hai ancora effettuato l'accesso." : $"Connesso come {member.DisplayName}");
    }

    private async Task OnPrimaryAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            if (member == null || token == null) await SignInAsync();
            else Play();
        }
        catch (LauncherException error)
        {
            busy = false;
            Render(error.Message, error: true);
            return;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            busy = false;
            Render("Errore imprevisto: " + error.Message, error: true);
            return;
        }
        busy = false;
        Render();
    }

    private async Task SignInAsync()
    {
        Render("Completa l'accesso nel browser…");
        var signed = await DiscordAuth.SignInAsync(config, closing.Token);
        Render("Verifica del ruolo…");
        var found = await DiscordAuth.GetMemberAsync(config, signed, closing.Token);
        Activate();
        // Any listed role admits: the whitelist role for players, plus e.g. the staff role.
        if (!found.Roles.Any(config.RoleIds.Contains))
        {
            throw new LauncherException($"{found.DisplayName}, non hai ancora il ruolo whitelist sul Discord di Lunar Roleplay.");
        }
        token = signed;
        member = found;
    }

    private void Play()
    {
        if (!GameLocator.IsValid(gameExe) && !PickGame()) throw new LauncherException("Seleziona ArcaneMPLauncher.exe per avviare il gioco.");
        var link = GameLink.Build(config, token!);
        try
        {
            // Started directly rather than through the arcanemp:// handler, which may be stale on some PCs.
            Process.Start(new ProcessStartInfo(gameExe!)
            {
                Arguments = "\"" + link + "\"",
                WorkingDirectory = Path.GetDirectoryName(gameExe!)!,
                UseShellExecute = false,
            });
        }
        catch (Exception error)
        {
            throw new LauncherException("Impossibile avviare il gioco: " + error.Message, error);
        }
        Render("Avvio del gioco…");
        WindowState = FormWindowState.Minimized;
    }

    private bool PickGame()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Seleziona ArcaneMPLauncher.exe",
            Filter = "ArcaneMP launcher|" + GameLocator.ExeName,
            FileName = GameLocator.ExeName,
            InitialDirectory = gameExe != null ? Path.GetDirectoryName(gameExe) : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || !GameLocator.IsValid(dialog.FileName)) return false;
        gameExe = dialog.FileName;
        try { GameLocator.Save(gameExe); } catch (Exception) { /* Remembering the path is a convenience only. */ }
        RefreshGame();
        return true;
    }

    private void CopyAccount()
    {
        if (accountId.Tag is not string id) return;
        Clipboard.SetText(id);
        Render("ID account copiato negli appunti.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) closing.Dispose();
        base.Dispose(disposing);
    }
}
