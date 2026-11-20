using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace LunarLauncher;

internal sealed record InstalledClient(string Version, string BuildId, string Sha256, Dictionary<string, string> Files);

/// <summary>
/// Installs the approved ArcaneMP client into %LOCALAPPDATA%\LunarRoleplay\ArcaneMP\client.
/// Only the zip's client folder is extracted; the server folders in the same zip are skipped.
/// </summary>
internal static class ClientInstaller
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunarRoleplay", "ArcaneMP");
    public static readonly string ClientDir = Path.Combine(Root, "client");
    public static readonly string ExePath = Path.Combine(ClientDir, GameLocator.ExeName);

    private static readonly string StateFile = Path.Combine(Root, "installed.json");
    private static readonly string StagingDir = Path.Combine(Root, "staging");
    private static readonly string DownloadFile = Path.Combine(Root, "download.zip.part");
    // Hashed after install so a client changed by ArcaneMP's own updater is detected and put back.
    private static readonly string[] KeyFiles = ["ArcaneMPClient.dll", "ArcaneMPLauncher.exe"];
    // Kept from the previous install: the game path chosen in ArcaneMP, and the logs that hold the account ID.
    private const string GameConfig = "ArcaneMP_launcher.json";
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

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

    public static async Task InstallAsync(HttpClient http, ClientManifest manifest, IProgress<string> progress, CancellationToken cancel)
    {
        if (IsGameRunning()) throw new LauncherException("Chiudi il gioco prima di aggiornare ArcaneMP.");
        Directory.CreateDirectory(Root);
        if (new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace < manifest.Size * 4)
        {
            throw new LauncherException($"Spazio su disco insufficiente: servono circa {Mb(manifest.Size * 4)} MB liberi.");
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
    }

    private static async Task DownloadAsync(HttpClient http, ClientManifest manifest, IProgress<string> progress, CancellationToken cancel)
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
            int read;
            while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
            {
                stall.CancelAfter(StallTimeout);
                total += read;
                if (total > manifest.Size) throw new LauncherException("Il download di ArcaneMP è più grande del previsto.");
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                if (lastReport.ElapsedMilliseconds >= 250)
                {
                    lastReport.Restart();
                    progress.Report($"Download di ArcaneMP {manifest.Version}… {total * 100 / manifest.Size}% ({Mb(total)}/{Mb(manifest.Size)} MB)");
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

    private static void Extract(ClientManifest manifest, IProgress<string> progress, CancellationToken cancel)
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
            progress.Report($"Installazione di ArcaneMP {manifest.Version}… {done * 100 / totalBytes}%");
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
