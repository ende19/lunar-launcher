using System.Diagnostics;

namespace LunarLauncher;

internal sealed class MainForm : Form
{
    private readonly LauncherConfig config;
    private readonly Label status = new();
    private readonly Button primary = new();
    private readonly Label gamePath = new();
    private readonly LinkLabel repair = new();
    private readonly Label accountId = new();
    private readonly LinkLabel copyAccount = new();
    private readonly CancellationTokenSource closing = new();
    private readonly HttpClient http = CreateHttp();

    private string? token;
    private DiscordMember? member;
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
        primary.Click += async (_, _) => await RunAsync(() => member == null || token == null ? SignInAsync() : PlayAsync());

        var gameRow = Row(gamePath, repair, "Ripara");
        repair.LinkClicked += async (_, _) => await RunAsync(RepairAsync);
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

    private static HttpClient CreateHttp()
    {
        // Timeouts are per request: a 15 s limit for the version file, a stall limit for the download.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LunarLauncher/" + Application.ProductVersion.Split('+')[0]);
        return client;
    }

    private void RefreshGame()
    {
        var installed = ClientInstaller.ReadInstalled();
        gamePath.Text = installed == null ? "ArcaneMP non ancora installato" : "ArcaneMP " + installed.Version;
        repair.Visible = installed != null;
        var existing = GameLocator.FindExistingClient();
        var id = AccountLog.Read(ClientInstaller.ExePath) ?? (existing == null ? null : AccountLog.Read(Path.Combine(existing, GameLocator.ExeName)));
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

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        Render();
        try
        {
            await action();
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
        catch (OperationCanceledException)
        {
            return; // The window is closing.
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

    private async Task PlayAsync()
    {
        await EnsureClientAsync(force: false);
        var link = GameLink.Build(config, token!);
        try
        {
            // Started directly rather than through the arcanemp:// handler, which may be stale on some PCs.
            Process.Start(new ProcessStartInfo(ClientInstaller.ExePath)
            {
                Arguments = "\"" + link + "\"",
                WorkingDirectory = ClientInstaller.ClientDir,
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

    private async Task RepairAsync()
    {
        await EnsureClientAsync(force: true);
    }

    /// <summary>Installs the approved ArcaneMP build when it is missing, outdated or changed.</summary>
    private async Task EnsureClientAsync(bool force)
    {
        Render("Controllo della versione di ArcaneMP…");
        ClientManifest manifest;
        try
        {
            manifest = await ClientManifest.FetchAsync(http, closing.Token);
        }
        catch (LauncherException) when (!force)
        {
            // Version file unreachable: keep playing on the build already installed, if it is intact.
            if (await Task.Run(IsInstalledIntact)) return;
            throw;
        }
        if (!force && await Task.Run(() => ClientInstaller.IsCurrent(manifest))) return;
        var progress = new Progress<string>(text => Render(text));
        await Task.Run(() => ClientInstaller.InstallAsync(http, manifest, progress, closing.Token));
        RefreshGame();
    }

    private static bool IsInstalledIntact() => ClientInstaller.ReadInstalled() is { } installed && ClientInstaller.IsIntact(installed);

    private void CopyAccount()
    {
        if (accountId.Tag is not string id) return;
        Clipboard.SetText(id);
        Render("ID account copiato negli appunti.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing.Dispose();
            http.Dispose();
        }
        base.Dispose(disposing);
    }
}
