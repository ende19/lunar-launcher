using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LunarLauncher;

/// <summary>
/// The ArcaneMP build that Lunar Roleplay staff approved, read from client-version.json in the public
/// launcher repository. The zip is always downloaded from MafiaHub's official URL; the checksum makes sure
/// it is exactly the approved build. The same file may also carry the game server address, so staff can move
/// the server without a new launcher release; without it, the address built into the launcher is used.
/// </summary>
internal sealed record ClientManifest(
    string Version, string ModId, string BuildId, string ClientFolder, string Sha256, long Size,
    string? ServerHost = null, int? ServerPort = null)
{
    public const string Url = "https://raw.githubusercontent.com/ende19/lunar-launcher/main/client-version.json";

    private static readonly Regex Guid = new("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
    private static readonly Regex Hex64 = new("^[0-9a-f]{64}$");
    private static readonly Regex SafeVersion = new("^[0-9A-Za-z.+-]{1,32}$");
    private static readonly Regex SafeFolder = new("^[0-9A-Za-z._-]+(/[0-9A-Za-z._-]+)*$");
    private static readonly Regex SafeHost = new("^[0-9A-Za-z.-]{1,253}$");

    /// <summary>The launcher config with the server address from this file, when it has one.</summary>
    public LauncherConfig Apply(LauncherConfig config) =>
        ServerHost == null ? config : config with { ServerHost = ServerHost, ServerPort = ServerPort ?? config.ServerPort };

    public Uri DownloadUri => new($"https://api.mafiahub.dev/mods/{ModId}/builds/{BuildId}/download");

    public static async Task<ClientManifest> FetchAsync(HttpClient http, CancellationToken cancel)
    {
        ClientManifest? manifest;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            // The query string defeats intermediate caches so a new approval reaches players quickly.
            var uri = Url + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            manifest = await http.GetFromJsonAsync<ClientManifest>(uri, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, timeout.Token);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            throw new LauncherException("Impossibile leggere la versione approvata di ArcaneMP.", error);
        }
        return (manifest ?? throw new LauncherException("Versione approvata di ArcaneMP vuota.")).Validate();
    }

    private ClientManifest Validate()
    {
        var valid =
            SafeVersion.IsMatch(Version ?? "") && Guid.IsMatch(ModId ?? "") && Guid.IsMatch(BuildId ?? "") &&
            SafeFolder.IsMatch(ClientFolder ?? "") && !ClientFolder!.Split('/').Contains("..") &&
            Hex64.IsMatch(Sha256 ?? "") && Size is > 0 and < 4L * 1024 * 1024 * 1024 &&
            (ServerHost == null || SafeHost.IsMatch(ServerHost)) && ServerPort is null or (>= 1 and <= 65535);
        if (!valid) throw new LauncherException("La versione approvata di ArcaneMP non è valida.");
        return this;
    }
}
