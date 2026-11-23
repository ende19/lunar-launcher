using System.Diagnostics;
using System.Security.Cryptography;

namespace LunarLauncher;

/// <summary>
/// A launcher release announced in client-version.json ("launcher"). The exe must come from this repository's
/// GitHub releases and match the SHA-256 and size; MinVersion, when set, is the oldest launcher allowed to play.
/// </summary>
internal sealed record LauncherRelease(string Version, string Url, string Sha256, long Size, string? MinVersion = null)
{
    public const string ReleasePrefix = "https://github.com/ende19/lunar-launcher/releases/download/";
    public const string ReleasesPage = "https://github.com/ende19/lunar-launcher/releases/latest";

    public bool IsValid() =>
        LauncherUpdater.Parse(Version) != null && (MinVersion == null || LauncherUpdater.Parse(MinVersion) != null) &&
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        Url.StartsWith(ReleasePrefix, StringComparison.Ordinal) && !Url.Contains("..") &&
        Sha256 is { Length: 64 } && Sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') &&
        Size is > 0 and < 512L * 1024 * 1024;
}

/// <summary>
/// Updates the launcher in place: downloads the announced exe next to the running one, checks it, renames the
/// running exe to *.old.exe (Windows allows renaming a running exe, not overwriting it), moves the new one into
/// its place, starts it and lets this one close. The new launcher deletes the .old file on start.
/// </summary>
internal static class LauncherUpdater
{
    public const string UpdatedArgument = "--updated";
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    public static Version Current => Parse(Application.ProductVersion) ?? new Version(0, 0, 0);

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
    private static string OldPath => Path.Combine(Path.GetDirectoryName(ExePath)!, Path.GetFileNameWithoutExtension(ExePath) + ".old.exe");
    private static string TempPath => Path.Combine(Path.GetDirectoryName(ExePath)!, Path.GetFileNameWithoutExtension(ExePath) + ".update.tmp");

    /// <summary>"0.4.5" or "0.4.5+commit" as a Version; null when it is not one.</summary>
    public static Version? Parse(string? text)
    {
        var plain = (text ?? "").Split('+')[0].Trim();
        return plain.Length <= 32 && Version.TryParse(plain, out var version) ? version : null;
    }

    public static bool IsNewer(LauncherRelease release) => Parse(release.Version) is { } version && version > Current;

    public static bool IsBelowMinimum(LauncherRelease release) => Parse(release.MinVersion) is { } min && Current < min;

    /// <summary>Removes what an earlier update left behind; the old process may still be closing, so it retries.</summary>
    public static void CleanUp()
    {
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(OldPath)) File.Delete(OldPath);
                    if (File.Exists(TempPath)) File.Delete(TempPath);
                    return;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    /// <summary>Downloads and installs <paramref name="release"/>, then starts it. Returns once the new launcher is running.</summary>
    public static async Task UpdateAsync(HttpClient http, LauncherRelease release, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        if (!CanWrite()) throw new LauncherUpdateBlockedException();
        try
        {
            await DownloadAsync(http, release, progress, cancel);
            Swap();
        }
        finally
        {
            try { if (File.Exists(TempPath)) File.Delete(TempPath); } catch (Exception) { /* Removed on the next start. */ }
        }
        try
        {
            Process.Start(new ProcessStartInfo(ExePath) { Arguments = UpdatedArgument, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(ExePath)! });
        }
        catch (Exception error)
        {
            throw new LauncherException("Launcher aggiornato, ma non è stato possibile riavviarlo: aprilo di nuovo.", error);
        }
    }

    public static void OpenReleasesPage()
    {
        try { Process.Start(new ProcessStartInfo(LauncherRelease.ReleasesPage) { UseShellExecute = true }); }
        catch (Exception) { /* No browser: the message already names the page. */ }
    }

    private static bool CanWrite()
    {
        var probe = Path.Combine(Path.GetDirectoryName(ExePath)!, ".lunar-write-test");
        try { File.WriteAllText(probe, ""); File.Delete(probe); return true; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static async Task DownloadAsync(HttpClient http, LauncherRelease release, IProgress<InstallProgress> progress, CancellationToken cancel)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stall.CancelAfter(StallTimeout);
        try
        {
            using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            if (!response.IsSuccessStatusCode) throw new LauncherException($"Download del launcher non riuscito (HTTP {(int)response.StatusCode}).");
            await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            await using (var target = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
                {
                    stall.CancelAfter(StallTimeout);
                    total += read;
                    if (total > release.Size) throw new LauncherException("Il download del launcher è più grande del previsto.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                    progress.Report(new InstallProgress($"Aggiornamento del launcher a {release.Version}…", (double)total / release.Size,
                        $"{total * 100 / release.Size}% · {total / (1024 * 1024)} / {release.Size / (1024 * 1024)} MB"));
                }
            }
            if (total != release.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != release.Sha256)
            {
                throw new LauncherException("Il launcher scaricato non corrisponde alla versione pubblicata. Riprova più tardi.");
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new LauncherException("Download del launcher interrotto: connessione assente o troppo lenta.");
        }
        catch (HttpRequestException error)
        {
            throw new LauncherException("Download del launcher non riuscito: " + error.Message, error);
        }
    }

    private static void Swap()
    {
        try
        {
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(ExePath, OldPath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LauncherException("Impossibile sostituire il launcher: chiudi le altre copie aperte e riprova.", error);
        }
        try { File.Move(TempPath, ExePath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { File.Move(OldPath, ExePath); } catch (Exception) { /* Left as .old.exe; the release page has a fresh copy. */ }
            throw new LauncherException("Impossibile installare il nuovo launcher: riprova.", error);
        }
    }
}

/// <summary>The launcher's folder is not writable, so it cannot replace itself; the release page is the way.</summary>
internal sealed class LauncherUpdateBlockedException() : LauncherException(
    "Il launcher non può aggiornarsi da solo in questa cartella: scarica la nuova versione da GitHub (pagina aperta nel browser).");
