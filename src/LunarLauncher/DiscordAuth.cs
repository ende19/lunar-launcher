using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LunarLauncher;

internal sealed record DiscordMember(string UserId, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>
/// Discord sign-in without a client secret. "pkce": authorization code + PKCE (requires "Public Client"
/// in the Discord app). "implicit": token returned in the URL fragment. Either way the result is an
/// access token scoped to identify + guilds.members.read; the PKCE flow also returns a refresh token, which
/// lets the launcher renew the sign-in without opening the browser again.
/// </summary>
internal static class DiscordAuth
{
    private const string Api = "https://discord.com/api/v10";
    private const string Scopes = "identify guilds.members.read";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(3);

    public static async Task<DiscordSession> SignInAsync(LauncherConfig config, CancellationToken cancel)
    {
        var state = RandomToken(24);
        var verifier = RandomToken(48);
        var pkce = config.AuthFlow == "pkce";
        var url = "https://discord.com/oauth2/authorize?" + string.Join("&",
            "client_id=" + config.ClientId,
            "response_type=" + (pkce ? "code" : "token"),
            "scope=" + Uri.EscapeDataString(Scopes),
            "redirect_uri=" + Uri.EscapeDataString(config.RedirectUri),
            "state=" + state,
            "prompt=none",
            pkce ? "code_challenge=" + Challenge(verifier) + "&code_challenge_method=S256" : "");

        using var server = new LoopbackServer(config.RedirectPort);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(SignInTimeout);
        Process.Start(new ProcessStartInfo(url.TrimEnd('&')) { UseShellExecute = true });

        try
        {
            while (true)
            {
                using var request = await server.NextAsync(timeout.Token);
                if (request.Path == "/callback" && !pkce && request.Get("error") == null)
                {
                    // Implicit flow: the token is in the fragment, which browsers never send. Hand it back as a query.
                    await request.RespondAsync(200, Page("Accesso in corso…",
                        "<script>location.replace('/token?' + location.hash.slice(1));</script>"), timeout.Token);
                    continue;
                }
                if (request.Path is not ("/callback" or "/token"))
                {
                    await request.RespondAsync(404, "", timeout.Token);
                    continue;
                }

                var error = request.Get("error");
                if (error != null)
                {
                    await request.RespondAsync(400, Page("Accesso annullato", "Puoi chiudere questa scheda e riprovare dal launcher."), timeout.Token);
                    throw new LauncherException(error == "access_denied"
                        ? "Accesso a Discord annullato."
                        : $"Discord ha rifiutato l'accesso ({error}).");
                }
                if (request.Get("state") != state)
                {
                    await request.RespondAsync(400, Page("Richiesta non valida", "Riprova dal launcher."), timeout.Token);
                    throw new LauncherException("Risposta di Discord non valida. Riprova.");
                }

                if (pkce)
                {
                    var code = request.Get("code") ?? throw new LauncherException("Discord non ha restituito un codice.");
                    await request.RespondAsync(200, Done(), timeout.Token);
                    return await TokenAsync(new Dictionary<string, string>
                    {
                        ["client_id"] = config.ClientId,
                        ["grant_type"] = "authorization_code",
                        ["code"] = code,
                        ["redirect_uri"] = config.RedirectUri,
                        ["code_verifier"] = verifier,
                    }, timeout.Token) ?? throw new LauncherException("Discord ha rifiutato il codice di accesso. Riprova.");
                }
                var token = request.Get("access_token") ?? throw new LauncherException("Discord non ha restituito un token.");
                await request.RespondAsync(200, Done(), timeout.Token);
                return new DiscordSession(token, null, Expiry(request.Get("expires_in")));
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new LauncherException("Tempo scaduto per l'accesso a Discord. Riprova.");
        }
    }

    public static async Task<DiscordMember> GetMemberAsync(LauncherConfig config, string token, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Api}/users/@me/guilds/{config.GuildId}/member");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancel);
        if ((int)response.StatusCode == 404) throw new LauncherException("Non fai parte del server Discord di Lunar Roleplay.");
        if ((int)response.StatusCode == 401) throw new DiscordExpiredException("Accesso Discord scaduto. Accedi di nuovo.");
        if (!response.IsSuccessStatusCode) throw new LauncherException($"Discord non ha risposto correttamente ({(int)response.StatusCode}).");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var root = json.RootElement;
        var user = root.GetProperty("user");
        var name = Text(root, "nick") ?? Text(user, "global_name") ?? Text(user, "username") ?? "?";
        var roles = root.GetProperty("roles").EnumerateArray().Select(role => role.GetString() ?? "").ToList();
        return new DiscordMember(user.GetProperty("id").GetString() ?? "", name, roles);
    }

    /// <summary>
    /// Renews a remembered sign-in without the browser. Null when Discord no longer accepts the refresh token
    /// (access removed by the player, or expired): the player must sign in again.
    /// </summary>
    public static Task<DiscordSession?> RefreshAsync(LauncherConfig config, string refreshToken, CancellationToken cancel) =>
        TokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        }, cancel);

    /// <summary>Best effort: tells Discord to drop the sign-in. The local copy is deleted either way.</summary>
    public static async Task RevokeAsync(LauncherConfig config, DiscordSession session)
    {
        foreach (var (token, hint) in new[] { (session.RefreshToken, "refresh_token"), (session.AccessToken, "access_token") })
        {
            if (token == null) continue;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{Api}/oauth2/token/revoke")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = config.ClientId,
                        ["token"] = token,
                        ["token_type_hint"] = hint,
                    }),
                };
                using var response = await Http.SendAsync(request);
            }
            catch (Exception) { /* Offline or refused: the token still expires on its own. */ }
        }
    }

    /// <summary>Calls the token endpoint. Null when Discord answers invalid_grant; other failures throw.</summary>
    private static async Task<DiscordSession?> TokenAsync(Dictionary<string, string> form, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Api}/oauth2/token") { Content = new FormUrlEncodedContent(form) };
        using var response = await SendAsync(request, cancel);
        var body = await response.Content.ReadAsStringAsync(cancel);
        var error = TryRead(body, "error");
        if (error == "invalid_grant") return null;
        if (!response.IsSuccessStatusCode)
        {
            // invalid_client here usually means the Discord app is not a Public Client: switch authFlow to "implicit".
            throw new LauncherException($"Accesso a Discord non riuscito ({error ?? ((int)response.StatusCode).ToString()}).");
        }
        var token = TryRead(body, "access_token") ?? throw new LauncherException("Discord non ha restituito un token.");
        string? expiresIn = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("expires_in", out var value) && value.TryGetInt64(out var seconds)) expiresIn = seconds.ToString();
        }
        catch (JsonException) { }
        return new DiscordSession(token, TryRead(body, "refresh_token"), Expiry(expiresIn));
    }

    // Discord access tokens last 7 days; assume 1 hour if the answer does not say.
    private static DateTimeOffset Expiry(string? expiresIn) =>
        DateTimeOffset.UtcNow.AddSeconds(long.TryParse(expiresIn, out var seconds) && seconds > 0 ? seconds : 3600);

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        try { return await Http.SendAsync(request, cancel); }
        catch (HttpRequestException error) { throw new LauncherException("Impossibile contattare Discord. Controlla la connessione.", error); }
        catch (TaskCanceledException error) when (!cancel.IsCancellationRequested)
        {
            throw new LauncherException("Discord non risponde. Riprova tra poco.", error);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text : null;

    private static string? TryRead(string json, string name)
    {
        try { using var doc = JsonDocument.Parse(json); return Text(doc.RootElement, name); }
        catch (JsonException) { return null; }
    }

    private static string RandomToken(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));
    private static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Done() => Page("Accesso completato", "Puoi chiudere questa scheda e tornare al launcher.");

    private static string Page(string title, string body) => $$"""
        <!doctype html><html lang="it"><head><meta charset="utf-8"><title>Lunar Roleplay</title>
        <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#09090F;color:#F5F3FF;
        font:16px 'Segoe UI',sans-serif}main{background:#1D1929;border:1px solid #332B45;border-radius:14px;
        padding:32px 40px;text-align:center}h1{margin:0 0 8px;font-size:22px;color:#A78BFA}p{margin:0;color:#A1A1B5}</style>
        </head><body><main><h1>{{title}}</h1><p>{{body}}</p></main></body></html>
        """;
}
