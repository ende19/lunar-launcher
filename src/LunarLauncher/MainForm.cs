using System.Diagnostics;

namespace LunarLauncher;

internal sealed class MainForm : Form
{
    private enum ClientState { Checking, NotInstalled, Outdated, Ready }

    private readonly LauncherConfig config;
    private readonly Label status = new();
    private readonly Button primary = new();
    private readonly Panel progressPanel = new();
    private readonly ProgressStrip progressBar = new();
    private readonly Label progressDetail = new();
    private readonly Label gamePath = new();
    private readonly LinkLabel repair = new();
    private readonly Label discordUser = new();
    private readonly LinkLabel signOut = new();
    private readonly Label accountId = new();
    private readonly LinkLabel copyAccount = new();
    private readonly ToolTip tips = new();
    private readonly CancellationTokenSource closing = new();
    private readonly HttpClient http = CreateHttp();

    private DiscordSession? session;
    private DiscordMember? member;
    private ClientManifest? approved;
    private ClientState state = ClientState.Checking;
    private bool busy;
    private bool installing;
    private string? notice; // Shown once an action finishes, instead of the default status line.

    public MainForm(LauncherConfig config)
    {
        this.config = config;
        Text = "Lunar Roleplay";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(440, 460);
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
            Text = "Server roleplay italiano di Hogwarts Legacy.", AutoSize = true,
            ForeColor = Theme.TextSecondary, Margin = new Padding(2, 2, 0, 24),
        };

        var card = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(16, 0, 16, 0) };
        card.Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle, Theme.Border, ButtonBorderStyle.Solid);
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.ForeColor = Theme.TextSecondary;
        status.BackColor = Theme.Surface;
        card.Controls.Add(status);

        // Shown only while downloading or installing.
        progressPanel.Dock = DockStyle.Top;
        progressPanel.Height = 34;
        progressPanel.Margin = new Padding(0, 0, 0, 12);
        progressPanel.Visible = false;
        progressBar.Dock = DockStyle.Top;
        progressDetail.Dock = DockStyle.Bottom;
        progressDetail.Height = 20;
        progressDetail.ForeColor = Theme.TextSecondary;
        progressDetail.Font = new Font("Segoe UI", 9f);
        progressDetail.TextAlign = ContentAlignment.BottomLeft;
        progressPanel.Controls.Add(progressDetail);
        progressPanel.Controls.Add(progressBar);

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
        primary.Click += async (_, _) => await RunAsync(state switch
        {
            ClientState.NotInstalled => InstallAsync,
            ClientState.Outdated => UpdateAsync,
            _ => PlayAsync,
        });

        var gameRow = Row(gamePath, repair, "Ripara");
        repair.LinkClicked += async (_, _) => await RunAsync(RepairAsync);
        var discordRow = Row(discordUser, signOut, "Esci");
        signOut.LinkClicked += (_, _) => SignOut();
        var accountRow = Row(accountId, copyAccount, "Copia");
        copyAccount.LinkClicked += (_, _) => CopyAccount();

        layout.Controls.Add(title);
        layout.Controls.Add(subtitle);
        layout.Controls.Add(card);
        layout.Controls.Add(progressPanel);
        layout.Controls.Add(primary);
        layout.Controls.Add(gameRow);
        layout.Controls.Add(discordRow);
        layout.Controls.Add(accountRow);
        Controls.Add(layout);

        // Refresh the account ID when the player comes back from the game, which may have just created it.
        Activated += (_, _) => RefreshGame();
        Shown += async (_, _) => await RunAsync(async () =>
        {
            await RestoreSessionAsync();
            await CheckAsync();
        });
        FormClosing += OnClosing;
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
        gamePath.Text = installed == null ? "ArcaneMP non installato" : $"ArcaneMP {installed.Version} installato";
        tips.SetToolTip(gamePath, installed == null ? "" : ClientInstaller.Root);
        repair.Visible = installed != null && !installing;
        var existing = GameLocator.FindExistingClient();
        var id = AccountLog.Read(ClientInstaller.ExePath) ?? (existing == null ? null : AccountLog.Read(Path.Combine(existing, GameLocator.ExeName)));
        accountId.Text = id == null ? "ID account: avvia il gioco una volta per generarlo" : "ID account: " + id;
        accountId.Tag = id;
        copyAccount.Visible = id != null;
    }

    private void Render(string? message = null, bool error = false)
    {
        primary.Text = state switch
        {
            ClientState.Checking => "Controllo…",
            ClientState.NotInstalled => "Installa",
            ClientState.Outdated => "Aggiorna",
            _ => "Avvia",
        };
        primary.Enabled = !busy;
        discordUser.Text = member == null ? "Discord: non connesso" : "Discord: " + member.DisplayName;
        signOut.Visible = session != null && !busy;
        primary.BackColor = busy ? Theme.Border : Theme.Primary;
        status.ForeColor = error ? Theme.Danger : member != null ? Theme.Text : Theme.TextSecondary;
        status.Text = message ?? state switch
        {
            ClientState.Checking => "Controllo della versione di ArcaneMP…",
            ClientState.NotInstalled => "Installa ArcaneMP per iniziare.",
            ClientState.Outdated => $"È disponibile ArcaneMP {approved?.Version}: aggiorna per entrare nel server.",
            _ => member == null ? "Pronto. Premi Avvia e accedi con Discord." : $"Connesso come {member.DisplayName}",
        };
    }

    private void ShowProgress(InstallProgress progress)
    {
        progressBar.Value = progress.Fraction;
        progressDetail.Text = progress.Detail;
        Render(progress.Text);
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
        catch (OperationCanceledException)
        {
            return; // The window is closing.
        }
        catch (Exception error)
        {
            busy = false;
            Render("Errore imprevisto: " + error.Message, error: true);
            return;
        }
        busy = false;
        Render(notice);
        notice = null;
    }

    /// <summary>On start: reads the approved build and decides between Installa, Aggiorna and Avvia.</summary>
    private async Task CheckAsync()
    {
        try
        {
            approved = await ClientManifest.FetchAsync(http, closing.Token);
        }
        catch (LauncherException)
        {
            // Offline: an installed client can still be started; a missing one needs the version file.
            state = ClientInstaller.ReadInstalled() == null ? ClientState.NotInstalled : ClientState.Ready;
            throw;
        }
        await UpdateStateAsync();
    }

    private async Task UpdateStateAsync()
    {
        var manifest = approved;
        state = ClientInstaller.ReadInstalled() == null ? ClientState.NotInstalled
            : manifest != null && !await Task.Run(() => ClientInstaller.IsCurrent(manifest)) ? ClientState.Outdated
            : ClientState.Ready;
        RefreshGame();
    }

    private async Task<ClientManifest> ApprovedAsync()
    {
        Render("Controllo della versione di ArcaneMP…");
        return approved = await ClientManifest.FetchAsync(http, closing.Token);
    }

    private async Task InstallAsync()
    {
        var manifest = await ApprovedAsync();
        string folder;
        using (var setup = new SetupDialog(manifest.Version, ClientInstaller.RequiredBytes(manifest), ClientInstaller.Root))
        {
            if (setup.ShowDialog(this) != DialogResult.OK)
            {
                await UpdateStateAsync();
                return;
            }
            folder = setup.SelectedFolder;
        }
        await InstallIntoAsync(manifest, folder);
    }

    private async Task UpdateAsync() => await InstallIntoAsync(await ApprovedAsync(), null);

    private async Task RepairAsync()
    {
        if (ClientInstaller.ReadInstalled() == null)
        {
            await InstallAsync();
            return;
        }
        await InstallIntoAsync(await ApprovedAsync(), null);
    }

    private async Task InstallIntoAsync(ClientManifest manifest, string? folder)
    {
        installing = true;
        progressBar.Value = 0;
        progressDetail.Text = "";
        progressPanel.Visible = true;
        repair.Visible = false;
        try
        {
            var progress = new Progress<InstallProgress>(ShowProgress);
            await Task.Run(() => ClientInstaller.InstallAsync(http, manifest, folder, progress, closing.Token));
        }
        finally
        {
            installing = false;
            progressPanel.Visible = false;
            await UpdateStateAsync();
        }
        notice = $"ArcaneMP {manifest.Version} installato. Premi Avvia per giocare.";
    }

    private async Task PlayAsync()
    {
        // Avvia also applies an approved update, so players never join on an old build.
        ClientManifest? manifest = null;
        try
        {
            manifest = await ApprovedAsync();
        }
        catch (LauncherException) when (ClientInstaller.ReadInstalled() is { } installed && ClientInstaller.IsIntact(installed))
        {
            // Version file unreachable: keep playing on the build already installed.
        }
        if (manifest != null && !await Task.Run(() => ClientInstaller.IsCurrent(manifest)))
        {
            await InstallIntoAsync(manifest, null);
            notice = null;
        }
        if (!File.Exists(ClientInstaller.ExePath))
        {
            state = ClientState.NotInstalled;
            throw new LauncherException("ArcaneMP non è installato: premi Installa.");
        }
        await EnsureSignedInAsync();

        var link = GameLink.Build(config, session!.AccessToken);
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
        notice = "Avvio del gioco…";
        WindowState = FormWindowState.Minimized;
    }

    /// <summary>On start: picks up the sign-in remembered on this PC, without opening the browser.</summary>
    private async Task RestoreSessionAsync()
    {
        var saved = SessionStore.Load(config.ClientId);
        if (saved == null) return;
        session = saved;
        try
        {
            if (!await RenewAsync()) return;
            member = await VerifyAsync(session!);
        }
        catch (DiscordExpiredException)
        {
            ForgetSession();
        }
        catch (LauncherException)
        {
            // Offline or no role yet: keep the sign-in; Avvia checks again and shows the reason.
        }
    }

    /// <summary>
    /// Makes sure the session's access token stays valid long enough for the join, renewing it with the
    /// refresh token if needed. False (session forgotten) when Discord no longer accepts it.
    /// </summary>
    private async Task<bool> RenewAsync()
    {
        if (session == null) return false;
        if (session.IsFresh) return true;
        var renewed = session.RefreshToken == null ? null : await DiscordAuth.RefreshAsync(config, session.RefreshToken, closing.Token);
        if (renewed == null)
        {
            ForgetSession();
            return false;
        }
        session = renewed;
        SessionStore.Save(config.ClientId, renewed);
        return true;
    }

    /// <summary>Avvia: reuses the remembered sign-in and checks the role again; opens the browser only when needed.</summary>
    private async Task EnsureSignedInAsync()
    {
        if (session != null)
        {
            Render("Verifica dell'accesso a Discord…");
            try
            {
                if (await RenewAsync())
                {
                    member = await VerifyAsync(session!);
                    return;
                }
            }
            catch (DiscordExpiredException)
            {
                ForgetSession();
            }
        }
        Render("Completa l'accesso a Discord nel browser…");
        session = await DiscordAuth.SignInAsync(config, closing.Token);
        // Remembered even without the role yet, so a player who gets it later does not sign in again.
        SessionStore.Save(config.ClientId, session);
        Activate();
        member = await VerifyAsync(session);
    }

    private async Task<DiscordMember> VerifyAsync(DiscordSession current)
    {
        Render("Verifica del ruolo…");
        var found = await DiscordAuth.GetMemberAsync(config, current.AccessToken, closing.Token);
        // Any listed role admits: the whitelist role for players, plus e.g. the staff role.
        if (!found.Roles.Any(config.RoleIds.Contains))
        {
            member = null;
            throw new LauncherException($"{found.DisplayName}, non hai ancora il ruolo whitelist sul Discord di Lunar Roleplay.");
        }
        return found;
    }

    private void ForgetSession()
    {
        session = null;
        member = null;
        SessionStore.Clear();
    }

    /// <summary>Esci: forgets the sign-in on this PC and asks Discord to revoke it.</summary>
    private void SignOut()
    {
        if (busy || session == null) return;
        var old = session;
        ForgetSession();
        Render("Disconnesso da Discord. Al prossimo Avvia ti verrà chiesto di accedere.");
        _ = DiscordAuth.RevokeAsync(config, old);
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (installing && MessageBox.Show(this, "Interrompere l'installazione di ArcaneMP?", "Lunar Roleplay",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        closing.Cancel();
    }

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
            tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
