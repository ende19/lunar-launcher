using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace LunarLauncher;

internal sealed class MainForm : FramelessForm
{
    private enum ClientState { Checking, NotInstalled, Outdated, Ready, LauncherOutdated }

    // Layout, in logical pixels at 100 % scaling.
    private const int W = 960, H = 560, TitleBar = 56;
    private static readonly Rectangle LogoBox = new(300, 48, 360, 292);
    private static readonly Rectangle CardBox = new(32, 382, 896, 120);

    private static readonly Image Logo = Theme.LoadImage("lunar-logo.png");
    private static readonly Image Mark = Theme.LoadImage("lunar-icon.png");

    private readonly LauncherConfig config;
    private readonly Label status = new();
    private readonly LunarButton primary = new();
    private readonly ProgressStrip progressBar = new();
    private readonly Label progressDetail = new();
    private readonly Label gamePath = new();
    private readonly LinkLabel repair = new();
    private readonly DiscordButton signIn = new();
    private readonly Label discordName = new();
    private readonly LinkLabel signOut = new();
    private readonly Label accountId = new();
    private readonly LinkLabel copyAccount = new();
    private readonly WindowButton minimize = new(WindowButton.Kind.Minimize);
    private readonly WindowButton close = new(WindowButton.Kind.Close);
    private readonly ToolTip tips = new();
    private readonly CancellationTokenSource closing = new();
    private readonly HttpClient http = CreateHttp();
    private readonly SocialButton[] socials;
    private readonly ServerStatus serverStatus = new();
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 30_000 };
    private bool probing;
    private Bitmap? backdrop;

    private DiscordSession? session;
    private DiscordMember? member;
    private bool allowed; // Signed in and holding an allowed role: unlocks the main button.
    private ClientManifest? approved;
    private ClientState state = ClientState.Checking;
    private bool busy;
    private bool installing;
    private string? notice; // Shown once an action finishes, instead of the default status line.

    public MainForm(LauncherConfig config, bool updated = false)
    {
        this.config = config;
        // Started by a self-update: say so once the first check finishes.
        if (updated)
        {
            notice = $"Launcher aggiornato alla versione {LauncherUpdater.Current.ToString(3)}.";
            // The old launcher closed after starting this one, so Windows may open it behind other windows.
            Shown += (_, _) => { TopMost = true; TopMost = false; Activate(); };
        }
        Text = "Lunar Roleplay";
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Night;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        Style(status, 11.5f, Theme.Text, bold: true);
        status.AutoEllipsis = true;
        Style(progressDetail, 9f, Theme.TextSecondary);
        Style(gamePath, 9f, Theme.TextSecondary);
        Style(accountId, 9f, Theme.TextSecondary);
        Style(discordName, 10f, Theme.Text, bold: true);
        StyleLink(repair, "Ripara");
        StyleLink(copyAccount, "Copia");
        StyleLink(signOut, "Esci");
        progressBar.Visible = false;
        progressDetail.Visible = false;
        signIn.AutoSize = false;

        primary.Click += async (_, _) => await RunAsync(state switch
        {
            ClientState.NotInstalled => InstallAsync,
            ClientState.Outdated => UpdateAsync,
            ClientState.LauncherOutdated => UpdateLauncherAsync,
            _ => PlayAsync,
        });
        signIn.Click += async (_, _) => await RunAsync(SignInAsync);
        signOut.LinkClicked += (_, _) => SignOut();
        repair.LinkClicked += async (_, _) => await RunAsync(RepairAsync);
        copyAccount.LinkClicked += (_, _) => CopyAccount();
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        close.Click += (_, _) => Close();

        socials = SocialLinks.All.Select(link => new SocialButton(link, tips)).ToArray();
        Controls.AddRange(socials);
        Controls.AddRange([status, progressBar, progressDetail, gamePath, repair, accountId, copyAccount,
            primary, signIn, discordName, signOut, minimize, close, serverStatus]);
        Draggable(serverStatus);
        Draggable(this);
        Draggable(status);
        Draggable(progressDetail);

        // Refresh the account ID when the player comes back from the game, which may have just created it.
        Activated += (_, _) => RefreshGame();
        Shown += async (_, _) =>
        {
            statusTimer.Start();
            await RunAsync(async () =>
            {
                await RestoreSessionAsync();
                await CheckAsync();
            });
        };
        // The address may come from client-version.json, so probe once more after it is read.
        statusTimer.Tick += async (_, _) => await ProbeServerAsync();
        Shown += async (_, _) => await ProbeServerAsync();
        FormClosing += OnClosing;
        ApplyScale();
        RefreshGame();
        Render();
    }

    private static void Style(Label label, float size, Color color, bool bold = false)
    {
        label.AutoSize = true;
        label.BackColor = Color.Transparent;
        label.ForeColor = color;
        label.Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size);
    }

    private static void StyleLink(LinkLabel link, string text)
    {
        link.Text = text;
        link.AutoSize = true;
        link.BackColor = Color.Transparent;
        link.Font = new Font("Segoe UI Semibold", 9f);
        link.LinkColor = Theme.Gold;
        link.ActiveLinkColor = Theme.Text;
        link.DisabledLinkColor = Theme.Muted;
        link.LinkBehavior = LinkBehavior.HoverUnderline;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale();
    }

    /// <summary>Sizes the window and places every control for the current monitor scaling.</summary>
    private void ApplyScale()
    {
        ClientSize = new Size(S(W), S(H));
        backdrop?.Dispose();
        backdrop = null;
        close.Bounds = new Rectangle(S(W - 46), 0, S(46), S(36));
        minimize.Bounds = new Rectangle(S(W - 92), 0, S(46), S(36));
        status.MaximumSize = new Size(S(560), 0);
        status.Location = new Point(S(CardBox.X + 26), S(CardBox.Y + 22));
        progressBar.Bounds = new Rectangle(S(CardBox.X + 26), S(CardBox.Y + 62), S(560), S(8));
        progressDetail.Location = new Point(S(CardBox.X + 24), S(CardBox.Y + 78));
        gamePath.Location = new Point(S(CardBox.X + 24), S(CardBox.Y + 58));
        accountId.Location = new Point(S(CardBox.X + 24), S(CardBox.Y + 82));
        primary.Bounds = new Rectangle(S(CardBox.Right - 26 - 250), S(CardBox.Y + 30), S(250), S(64));
        // Server status in the title bar, after the name.
        using (var g = CreateGraphics())
        using (var title = new Font(Theme.Display, 12f))
        {
            var titleRight = S(52) + Theme.MeasureSpaced(g, "LUNAR ROLEPLAY", title, S(3)).Width;
            serverStatus.Size = serverStatus.GetPreferredSize(Size.Empty);
            serverStatus.Location = new Point((int)titleRight + S(16), (S(TitleBar) - serverStatus.Height) / 2);
        }
        // Social links bottom right, under the card, in the order of SocialLinks.All from right to left.
        // 20 px below the card and 16 px above the window edge.
        var x = CardBox.Right - 2;
        foreach (var social in socials)
        {
            x -= 24;
            social.Bounds = new Rectangle(S(x), S(CardBox.Bottom + 20), S(24), S(24));
            x -= 12;
        }
        PlaceInline();
        Invalidate();
    }

    /// <summary>Places the size-dependent pieces: links after their labels, and the Discord area top right.</summary>
    private void PlaceInline()
    {
        repair.Location = new Point(gamePath.Right + S(6), gamePath.Top);
        copyAccount.Location = new Point(accountId.Right + S(6), accountId.Top);
        var right = minimize.Left - S(14);
        signIn.Size = signIn.GetPreferredSize(Size.Empty);
        signIn.Location = new Point(right - signIn.Width, (S(TitleBar) - signIn.Height) / 2);
        signOut.Location = new Point(right - signOut.Width, (S(TitleBar) - signOut.Height) / 2);
        discordName.Location = new Point(signOut.Left - S(10) - discordName.Width, (S(TitleBar) - discordName.Height) / 2);
        Invalidate(new Rectangle(0, 0, ClientSize.Width, S(TitleBar)));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (backdrop == null || backdrop.Size != ClientSize)
        {
            backdrop?.Dispose();
            backdrop = PaintBackdrop();
        }
        e.Graphics.DrawImageUnscaled(backdrop, 0, 0);
        // The signed-in chip is drawn live because the name changes.
        if (discordName.Visible)
        {
            var chip = RectangleF.FromLTRB(discordName.Left - S(14), S(12), signOut.Right + S(14), S(TitleBar - 12));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.Rounded(chip, chip.Height / 2);
            using var fill = new SolidBrush(Color.FromArgb(150, 28, 19, 52));
            using var edge = new Pen(Color.FromArgb(110, 167, 139, 250));
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(edge, path);
        }
    }

    /// <summary>Night sky, violet glow behind the logo, stars, the logo, the title and the bottom card.</summary>
    private Bitmap PaintBackdrop()
    {
        var bitmap = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var full = new Rectangle(Point.Empty, bitmap.Size);

        using (var sky = new LinearGradientBrush(full, Theme.NightTop, Theme.Night, LinearGradientMode.Vertical))
        {
            g.FillRectangle(sky, full);
        }
        Glow(g, new RectangleF(S(140), S(-120), S(680), S(620)), Color.FromArgb(120, 124, 58, 237));
        Glow(g, new RectangleF(S(-200), S(300), S(520), S(420)), Color.FromArgb(50, 124, 58, 237));
        Glow(g, new RectangleF(S(700), S(-160), S(460), S(380)), Color.FromArgb(40, 212, 154, 69));

        // Stars: fixed seed so the sky looks the same on every start.
        var random = new Random(1987);
        for (var i = 0; i < 170; i++)
        {
            var x = random.Next(W) * UiScale;
            var y = random.Next(H) * UiScale;
            var size = (random.NextDouble() < 0.12 ? 2.2f : 1.2f) * UiScale;
            var alpha = random.Next(50, 200);
            var color = random.NextDouble() < 0.25 ? Color.FromArgb(alpha, Theme.Gold) : Color.FromArgb(alpha, 230, 225, 255);
            using var star = new SolidBrush(color);
            g.FillEllipse(star, x, y, size, size);
        }

        g.DrawImage(Logo, Fit(Logo.Size, Scaled(LogoBox)));

        using (var tagline = new Font(Theme.Display, 12.5f))
        {
            const string text = "Server roleplay italiano di Hogwarts Legacy";
            var size = Theme.MeasureSpaced(g, text, tagline, S(1));
            Theme.DrawSpaced(g, text, tagline, Theme.TextSecondary, new PointF((bitmap.Width - size.Width) / 2, S(346)), S(1));
        }

        // Title bar: the Lunar mark and the name in spaced capitals.
        g.DrawImage(Mark, new Rectangle(S(18), S(14), S(28), S(28)));
        using (var title = new Font(Theme.Display, 12f))
        {
            Theme.DrawSpaced(g, "LUNAR ROLEPLAY", title, Theme.Gold, new PointF(S(52), S(19)), S(3));
        }

        // Bottom card, frosted over the sky.
        var card = Scaled(CardBox);
        using (var path = Theme.Rounded(card, S(18)))
        using (var fill = new SolidBrush(Theme.Card))
        using (var edge = new Pen(Color.FromArgb(70, 167, 139, 250)))
        {
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }
        using (var shine = new LinearGradientBrush(new RectangleF(card.X, card.Y, card.Width, 2), Color.Transparent, Color.FromArgb(150, Theme.Gold), 0f))
        {
            var blend = new ColorBlend
            {
                Colors = [Color.Transparent, Color.FromArgb(150, Theme.Gold), Color.Transparent],
                Positions = [0f, 0.5f, 1f],
            };
            shine.InterpolationColors = blend;
            g.FillRectangle(shine, card.X + S(40), card.Y, card.Width - S(80), Math.Max(1, S(1)));
        }

        using (var small = new Font("Segoe UI", 8.5f))
        using (var muted = new SolidBrush(Theme.Muted))
        {
            var version = "Launcher v" + Application.ProductVersion.Split('+')[0];
            g.DrawString(version, small, muted, S(CardBox.X + 4), S(H - 33));
        }
        return bitmap;
    }

    private static void Glow(Graphics g, RectangleF area, Color color)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(area);
        using var brush = new PathGradientBrush(path) { CenterColor = color, SurroundColors = [Color.Transparent] };
        g.FillEllipse(brush, area);
    }

    private Rectangle Scaled(Rectangle r) => new(S(r.X), S(r.Y), S(r.Width), S(r.Height));

    private static Rectangle Fit(Size image, Rectangle box)
    {
        var ratio = Math.Min((double)box.Width / image.Width, (double)box.Height / image.Height);
        var w = (int)(image.Width * ratio);
        var h = (int)(image.Height * ratio);
        return new Rectangle(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
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
        PlaceInline();
    }

    private void Render(string? message = null, bool error = false)
    {
        primary.Text = state switch
        {
            ClientState.Checking => "Controllo…",
            ClientState.NotInstalled => "Installa",
            ClientState.Outdated or ClientState.LauncherOutdated => "Aggiorna",
            _ => "Avvia",
        };
        // Everything starts with the Discord sign-in: the main button stays locked until it succeeds.
        // Updating the launcher needs no sign-in.
        primary.Enabled = !busy && (allowed || state == ClientState.LauncherOutdated);
        signIn.Visible = session == null;
        signIn.Enabled = !busy;
        discordName.Visible = session != null;
        discordName.Text = member?.DisplayName ?? "Discord";
        signOut.Visible = session != null;
        signOut.Enabled = !busy;
        repair.Enabled = !busy && allowed;
        // While installing, the progress bar takes the place of the two info lines.
        progressBar.Visible = progressDetail.Visible = installing;
        gamePath.Visible = accountId.Visible = !installing;
        copyAccount.Visible = !installing && accountId.Tag != null;
        repair.Visible = !installing && ClientInstaller.ReadInstalled() != null;
        PlaceInline();
        status.ForeColor = error ? Theme.Danger : allowed ? Theme.Text : Theme.TextSecondary;
        status.Text = message ??
            (state == ClientState.LauncherOutdated ? $"È disponibile il launcher {approved?.Launcher?.Version}."
            : session == null ? "Accedi con Discord per iniziare."
            : !allowed ? (member == null ? "Accesso a Discord da verificare: controlla la connessione."
                : $"{member.DisplayName}, non hai ancora il ruolo whitelist sul Discord di Lunar Roleplay.")
            : state switch
            {
                ClientState.Checking => "Controllo della versione di ArcaneMP…",
                ClientState.NotInstalled => "Installa ArcaneMP per iniziare.",
                ClientState.Outdated => $"È disponibile ArcaneMP {approved?.Version}: aggiorna per entrare nel server.",
                _ => "Pronto. Premi Avvia per giocare.",
            });
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
            _ = ProbeServerAsync();
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
        // A newer launcher comes first: the main button becomes Aggiorna until the player updates it.
        if (manifest?.Launcher is { } release && LauncherUpdater.IsNewer(release)) state = ClientState.LauncherOutdated;
        RefreshGame();
    }

    /// <summary>
    /// Aggiorna with a newer launcher announced: downloads it, replaces this exe and restarts. A failed update is
    /// reported (for a folder it cannot write to, the release page opens) and the button stays on Aggiorna.
    /// </summary>
    private async Task UpdateLauncherAsync()
    {
        var manifest = await ApprovedAsync();
        if (manifest.Launcher is not { } release || !LauncherUpdater.IsNewer(release))
        {
            await UpdateStateAsync();
            return;
        }
        installing = true;
        progressBar.Value = 0;
        progressDetail.Text = "";
        Render($"Aggiornamento del launcher a {release.Version}…");
        try
        {
            var progress = new Progress<InstallProgress>(ShowProgress);
            await Task.Run(() => LauncherUpdater.UpdateAsync(http, release, progress, closing.Token));
        }
        catch (LauncherException error)
        {
            installing = false;
            Render();
            if (error is LauncherUpdateBlockedException) LauncherUpdater.OpenReleasesPage();
            throw;
        }
        // The new launcher is running: close this one.
        installing = false;
        Close();
        throw new OperationCanceledException();
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
        string? linked;
        using (var setup = new SetupDialog(manifest.Version, ClientInstaller.RequiredBytes(manifest), ClientInstaller.Root))
        {
            if (setup.ShowDialog(this) != DialogResult.OK)
            {
                await UpdateStateAsync();
                return;
            }
            linked = setup.LinkedRoot;
            folder = linked == null ? setup.SelectedFolder : "";
        }
        if (linked != null) await LinkIntoAsync(manifest, linked);
        else await InstallIntoAsync(manifest, folder);
    }

    /// <summary>Links the player's own ArcaneMP: kept when it is the approved build, else that build is installed into it.</summary>
    private async Task LinkIntoAsync(ClientManifest manifest, string root)
    {
        installing = true;
        progressBar.Value = 0;
        progressDetail.Text = "";
        Render($"Controllo di ArcaneMP in {root}…");
        bool kept;
        try
        {
            var progress = new Progress<InstallProgress>(ShowProgress);
            kept = await Task.Run(() => ClientInstaller.LinkAsync(http, manifest, root, progress, closing.Token));
        }
        finally
        {
            installing = false;
            Render();
            await UpdateStateAsync();
        }
        notice = kept
            ? $"ArcaneMP {manifest.Version} collegato. Premi Avvia per giocare."
            : $"La tua installazione è stata portata ad ArcaneMP {manifest.Version}, la versione approvata. Premi Avvia per giocare.";
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
        Render();
        try
        {
            var progress = new Progress<InstallProgress>(ShowProgress);
            await Task.Run(() => ClientInstaller.InstallAsync(http, manifest, folder, progress, closing.Token));
        }
        finally
        {
            installing = false;
            Render();
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
        // A launcher release that appeared while the window was open: the player updates it first.
        if (manifest?.Launcher is { } release && LauncherUpdater.IsNewer(release))
        {
            state = ClientState.LauncherOutdated;
            throw new LauncherException($"È disponibile il launcher {release.Version}.");
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

        // The server address comes from client-version.json when it has one, so a new IP needs no new launcher.
        var link = GameLink.Build(approved?.Apply(config) ?? config, session!.AccessToken);
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
        notice = "In esecuzione";
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
            await VerifyAsync(session!);
        }
        catch (DiscordExpiredException)
        {
            ForgetSession();
        }
        catch (LauncherException)
        {
            // Offline or no role yet: keep the sign-in; the status line shows the reason.
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

    /// <summary>Avvia: renews the remembered sign-in and checks the role again, without opening the browser.</summary>
    private async Task EnsureSignedInAsync()
    {
        Render("Verifica dell'accesso a Discord…");
        try
        {
            if (await RenewAsync())
            {
                await VerifyAsync(session!);
                return;
            }
        }
        catch (DiscordExpiredException)
        {
            ForgetSession();
        }
        throw new LauncherException("Accesso a Discord scaduto: accedi di nuovo in alto a destra.");
    }

    /// <summary>"Accedi con Discord": opens the browser, then checks the role.</summary>
    private async Task SignInAsync()
    {
        Render("Completa l'accesso a Discord nel browser…");
        session = await DiscordAuth.SignInAsync(config, closing.Token);
        // Remembered even without the role yet, so a player who gets it later does not sign in again.
        SessionStore.Save(config.ClientId, session);
        Activate();
        await VerifyAsync(session);
        await UpdateStateAsync();
    }

    private async Task VerifyAsync(DiscordSession current)
    {
        Render("Verifica del ruolo…");
        var found = await DiscordAuth.GetMemberAsync(config, current.AccessToken, closing.Token);
        member = found;
        // Any listed role admits: the whitelist role for players, plus e.g. the staff role.
        allowed = found.Roles.Any(config.RoleIds.Contains);
        if (!allowed) throw new LauncherException($"{found.DisplayName}, non hai ancora il ruolo whitelist sul Discord di Lunar Roleplay.");
    }

    private void ForgetSession()
    {
        session = null;
        member = null;
        allowed = false;
        SessionStore.Clear();
    }

    /// <summary>Esci: forgets the sign-in on this PC and asks Discord to revoke it.</summary>
    private void SignOut()
    {
        if (busy || session == null) return;
        var old = session;
        ForgetSession();
        Render("Disconnesso da Discord.");
        _ = DiscordAuth.RevokeAsync(config, old);
    }

    /// <summary>Green or red dot: can the game server port be reached right now?</summary>
    private async Task ProbeServerAsync()
    {
        if (probing || closing.IsCancellationRequested || WindowState == FormWindowState.Minimized) return;
        probing = true;
        try
        {
            var target = approved?.Apply(config) ?? config;
            var online = await ServerStatus.ProbeAsync(target.ServerHost, target.ServerPort, closing.Token);
            serverStatus.Current = online ? ServerStatus.State.Online : ServerStatus.State.Offline;
            tips.SetToolTip(serverStatus, online
                ? "Il server Lunar Roleplay è raggiungibile."
                : "Il server Lunar Roleplay non risponde: potrebbe essere spento o in riavvio.");
        }
        catch (OperationCanceledException)
        {
            // Closing.
        }
        finally
        {
            probing = false;
        }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (installing && MessageBox.Show(this, "Interrompere l'installazione di ArcaneMP?", "Lunar Roleplay",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        statusTimer.Stop();
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
            statusTimer.Dispose();
            closing.Dispose();
            http.Dispose();
            tips.Dispose();
            backdrop?.Dispose();
        }
        base.Dispose(disposing);
    }
}
