using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace LunarLauncher;

internal sealed record InstalledClient(string Version, string BuildId, string Sha256, Dictionary<string, string> Files);

/// <summary>Install progress: Fraction is 0–1 for the bar, Detail is the line under it (size, speed, time left).</summary>
internal sealed record InstallProgress(string Text, double Fraction, string Detail);

/// <summary>
/// Installs the approved ArcaneMP client into &lt;install folder&gt;\client. The folder is chosen by the player
/// (default %LOCALAPPDATA%\LunarRoleplay\ArcaneMP) and remembered in %LOCALAPPDATA%\LunarRoleplay\settings.json.
/// Only the zip's client folder is extracted; the server folders in the same zip are skipped.
/// </summary>
internal static class ClientInstaller
{
    public static readonly string DefaultRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunarRoleplay", "ArcaneMP");
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunarRoleplay", "settings.json");
    private const string StateName = "installed.json";

    public static string Root { get; private set; } = LoadRoot();
    public static string ClientDir => Path.Combine(Root, "client");
    public static string ExePath => Path.Combine(ClientDir, GameLocator.ExeName);

    private static string StateFile => Path.Combine(Root, StateName);
    private static string StagingDir => Path.Combine(Root, "staging");
    private static string DownloadFile => Path.Combine(Root, "download.zip.part");
    // Hashed after install so a client changed by ArcaneMP's own updater is detected and put back.
    private static readonly string[] KeyFiles = ["ArcaneMPClient.dll", "ArcaneMPLauncher.exe"];
    // Kept from the previous install: the game path chosen in ArcaneMP, and the logs that hold the account ID.
    private const string GameConfig = "ArcaneMP_launcher.json";
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Approximate disk space an install needs: the zip plus the extracted client and a margin.</summary>
    public static long RequiredBytes(ClientManifest manifest) => manifest.Size * 4;

    /// <summary>
    /// Null when the folder can hold the install: it is missing, empty, or already a Lunar Roleplay install.
    /// Otherwise the reason in Italian. Installs replace their own client subfolder, so an unrelated folder
    /// with content is refused.
    /// </summary>
    public static string? CheckFolder(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            if (Path.GetPathRoot(full) == full) return "Scegli una cartella, non la radice del disco.";
            if (!Directory.Exists(full)) return null;
            if (File.Exists(Path.Combine(full, StateName))) return null;
            return Directory.EnumerateFileSystemEntries(full).Any() ? "La cartella non è vuota: scegline una vuota o creane una nuova." : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return "Percorso non valido.";
        }
    }

    public static InstalledClient? ReadInstalled()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<InstalledClient>(File.ReadAllText(StateFile)) : null; }
        catch (Exception) { return null; }
    }

    /// <summary>True when the installed client is the approved build and its key files are unchanged.</summary>
    public static bool IsCurrent(ClientManifest manifest)
    {
        var installed = ReadInstalled();
        return installed != null && installed.BuildId == manifest.BuildId && installed.Sha256 == manifest.Sha256 && IsIntact(installed);
    }

    public static bool IsIntact(InstalledClient installed)
    {
        try
        {
            return File.Exists(ExePath) && KeyFiles.All(name =>
                installed.Files.TryGetValue(name, out var expected) && HashFile(Path.Combine(ClientDir, name)) == expected);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Installs into <paramref name="folder"/>, or into the current install folder when null.</summary>
    public static async Task InstallAsync(HttpClient http, ClientManifest manifest, string? folder, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        if (IsGameRunning()) throw new LauncherException("Chiudi il gioco prima di installare ArcaneMP.");
        if (folder != null)
        {
            var problem = CheckFolder(folder);
            if (problem != null) throw new LauncherException(problem);
            Root = Path.GetFullPath(folder);
        }
        try { Directory.CreateDirectory(Root); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LauncherException("Impossibile creare la cartella di installazione: " + error.Message, error);
        }
        if (new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace < RequiredBytes(manifest))
        {
            throw new LauncherException($"Spazio su disco insufficiente: servono circa {Gb(RequiredBytes(manifest))} GB liberi.");
        }
        DeleteDirectory(StagingDir);
        try
        {
            await DownloadAsync(http, manifest, progress, cancel);
            Extract(manifest, progress, cancel);
            CarryOver();
            Swap();
        }
        finally
        {
            TryDelete(DownloadFile);
            DeleteDirectory(StagingDir);
        }
        var files = KeyFiles.ToDictionary(name => name, name => HashFile(Path.Combine(ClientDir, name)));
        File.WriteAllText(StateFile, JsonSerializer.Serialize(new InstalledClient(manifest.Version, manifest.BuildId, manifest.Sha256, files)));
        SaveRoot();
    }

    /// <summary>
    /// The install root of an ArcaneMP client the player already has: <paramref name="chosen"/> may be the client
    /// folder itself (the one with ArcaneMPLauncher.exe, named "client") or the folder that holds it. Throws with
    /// the reason in Italian when it is not one.
    /// </summary>
    public static string ResolveExisting(string chosen)
    {
        string full;
        try { full = Path.GetFullPath(chosen); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
        {
            throw new LauncherException("Percorso non valido.", error);
        }
        if (File.Exists(Path.Combine(full, "client", GameLocator.ExeName))) return full;
        if (File.Exists(Path.Combine(full, GameLocator.ExeName)))
        {
            var parent = Path.GetDirectoryName(full);
            if (Path.GetFileName(full).Equals("client", StringComparison.OrdinalIgnoreCase) && parent != null && Path.GetPathRoot(parent) != parent) return parent;
            throw new LauncherException("ArcaneMP va tenuto in una cartella \"client\" (come nello zip ufficiale): sposta la cartella o installa da zero.");
        }
        throw new LauncherException($"Non trovo {GameLocator.ExeName} in questa cartella: scegli la cartella di ArcaneMP.");
    }

    /// <summary>
    /// Links an existing ArcaneMP install (root from <see cref="ResolveExisting"/>) as the launcher's install.
    /// When its key files are the approved build it is adopted as it is and true is returned; otherwise the approved
    /// build is installed into it (an update or a downgrade), keeping the game path setting and logs, and false is
    /// returned. On failure the launcher keeps its previous install folder.
    /// </summary>
    public static async Task<bool> LinkAsync(HttpClient http, ClientManifest manifest, string root, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        if (IsGameRunning()) throw new LauncherException("Chiudi il gioco prima di collegare ArcaneMP.");
        var probe = Path.Combine(root, ".lunar-write-test");
        try { File.WriteAllText(probe, ""); File.Delete(probe); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LauncherException("Il launcher non può scrivere in questa cartella: scegli un'altra installazione o installa da zero.", error);
        }
        var previous = Root;
        Root = Path.GetFullPath(root);
        try
        {
            if (manifest.Files is { } approvedFiles && KeyFiles.All(name =>
                    approvedFiles.TryGetValue(name, out var expected) && File.Exists(Path.Combine(ClientDir, name)) &&
                    HashFile(Path.Combine(ClientDir, name)) == expected))
            {
                var files = KeyFiles.ToDictionary(name => name, name => approvedFiles[name]);
                File.WriteAllText(StateFile, JsonSerializer.Serialize(new InstalledClient(manifest.Version, manifest.BuildId, manifest.Sha256, files)));
                SaveRoot();
                return true;
            }
            await InstallAsync(http, manifest, null, progress, cancel);
            return false;
        }
        catch (Exception)
        {
            Root = previous;
            throw;
        }
    }

    private static string LoadRoot()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return DefaultRoot;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsFile));
            return saved != null && saved.TryGetValue("installDir", out var dir) && Path.IsPathFullyQualified(dir) ? dir : DefaultRoot;
        }
        catch (Exception) { return DefaultRoot; }
    }

    private static void SaveRoot()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Dictionary<string, string> { ["installDir"] = Root }));
        }
        catch (Exception) { /* Without it the next start looks in the default folder and offers Installa again. */ }
    }

    private static async Task DownloadAsync(HttpClient http, ClientManifest manifest, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stall.CancelAfter(StallTimeout);
        try
        {
            using var response = await http.GetAsync(manifest.DownloadUri, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new LauncherException($"Download di ArcaneMP non riuscito (HTTP {(int)response.StatusCode}).");
            }
            await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
            await using var target = new FileStream(DownloadFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 16];
            long total = 0;
            var lastReport = Stopwatch.StartNew();
            long lastTotal = 0;
            double speed = 0; // bytes per second, smoothed
            int read;
            while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
            {
                stall.CancelAfter(StallTimeout);
                total += read;
                if (total > manifest.Size) throw new LauncherException("Il download di ArcaneMP è più grande del previsto.");
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                if (lastReport.ElapsedMilliseconds >= 500)
                {
                    var sample = (total - lastTotal) / lastReport.Elapsed.TotalSeconds;
                    speed = speed == 0 ? sample : speed * 0.7 + sample * 0.3;
                    lastTotal = total;
                    lastReport.Restart();
                    var detail = $"{total * 100 / manifest.Size}% · {Mb(total)} / {Mb(manifest.Size)} MB · {speed / (1024 * 1024):0.0} MB/s";
                    if (speed > 0) detail += " · " + Remaining(TimeSpan.FromSeconds((manifest.Size - total) / speed));
                    progress.Report(new InstallProgress($"Download di ArcaneMP {manifest.Version}…", (double)total / manifest.Size, detail));
                }
            }
            if (total != manifest.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != manifest.Sha256)
            {
                throw new LauncherException("Il file scaricato non corrisponde alla versione approvata. Riprova più tardi.");
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new LauncherException("Download di ArcaneMP interrotto: connessione assente o troppo lenta.");
        }
        catch (HttpRequestException error)
        {
            throw new LauncherException("Download di ArcaneMP non riuscito: " + error.Message, error);
        }
    }

    private static void Extract(ClientManifest manifest, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        var prefix = manifest.ClientFolder.TrimEnd('/') + "/";
        var root = Path.GetFullPath(StagingDir) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(DownloadFile);
        var entries = zip.Entries
            .Where(entry => entry.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        if (!entries.Any(entry => entry.FullName.Replace('\\', '/') == prefix + GameLocator.ExeName))
        {
            throw new LauncherException("Lo zip di ArcaneMP non contiene il client atteso.");
        }
        var totalBytes = Math.Max(1, entries.Sum(entry => entry.Length));
        long done = 0;
        Directory.CreateDirectory(root);
        foreach (var entry in entries)
        {
            cancel.ThrowIfCancellationRequested();
            var relative = entry.FullName.Replace('\\', '/')[prefix.Length..];
            if (relative.Length == 0) continue;
            var target = Path.GetFullPath(Path.Combine(root, relative));
            // Zip-slip guard: every entry must land inside the staging folder.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new LauncherException("Lo zip di ArcaneMP contiene un percorso non valido.");
            }
            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            done += entry.Length;
            progress.Report(new InstallProgress(
                $"Installazione di ArcaneMP {manifest.Version}…", (double)done / totalBytes, $"{done * 100 / totalBytes}%"));
        }
    }

    /// <summary>Copies the game path setting and logs into the new install.</summary>
    private static void CarryOver()
    {
        var gameConfigSource = new[] { ClientDir, GameLocator.FindExistingClient() }
            .Where(dir => dir != null)
            .Select(dir => Path.Combine(dir!, GameConfig))
            .FirstOrDefault(File.Exists);
        if (gameConfigSource != null) File.Copy(gameConfigSource, Path.Combine(StagingDir, GameConfig), overwrite: true);

        var oldLogs = Path.Combine(ClientDir, "logs");
        var newLogs = Path.Combine(StagingDir, "logs");
        if (Directory.Exists(oldLogs) && !Directory.Exists(newLogs))
        {
            try { Directory.Move(oldLogs, newLogs); } catch (IOException) { /* Logs are a convenience only. */ }
        }
    }

    private static void Swap()
    {
        foreach (var leftover in Directory.GetDirectories(Root, "client.old-*")) DeleteDirectory(leftover);
        var old = Path.Combine(Root, "client.old-" + DateTime.UtcNow.Ticks);
        try
        {
            if (Directory.Exists(ClientDir)) Directory.Move(ClientDir, old);
            Directory.Move(StagingDir, ClientDir);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (!Directory.Exists(ClientDir) && Directory.Exists(old)) Directory.Move(old, ClientDir);
            throw new LauncherException("Impossibile sostituire i file di ArcaneMP: chiudi il gioco e riprova.", error);
        }
        DeleteDirectory(old);
    }

    private static bool IsGameRunning() =>
        new[] { "ArcaneMPLauncher", "HogwartsLegacy", "MafiaHubUpdater" }.Any(name =>
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes) process.Dispose();
            return processes.Length > 0;
        });

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static long Mb(long bytes) => bytes / (1024 * 1024);

    public static string Gb(long bytes) => (bytes / (1024.0 * 1024 * 1024)).ToString("0.0");

    private static string Remaining(TimeSpan left) =>
        left.TotalMinutes >= 1 ? $"circa {(int)Math.Ceiling(left.TotalMinutes)} min" : $"circa {Math.Max(1, (int)left.TotalSeconds)} s";

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception) { /* Left for the next install, which overwrites it. */ }
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception) { /* A leftover folder is removed by the next install. */ }
    }
}
