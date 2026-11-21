using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LunarLauncher;

/// <summary>A Discord sign-in: the access token used as join ticket, and the refresh token that renews it.</summary>
internal sealed record DiscordSession(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    /// <summary>The access token goes to the game server at join; keep a margin so it is still valid there.</summary>
    public bool IsFresh => ExpiresAt - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(30);
}

/// <summary>
/// Remembers the Discord sign-in on this PC, encrypted with Windows DPAPI for the current Windows user
/// (%LOCALAPPDATA%\LunarRoleplay\discord-session.bin). Copied to another PC or user, the file cannot be read.
/// </summary>
internal static class SessionStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunarRoleplay", "discord-session.bin");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LunarRoleplay.DiscordSession.v1");

    private sealed record Stored(string ClientId, string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

    public static DiscordSession? Load(string clientId)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<Stored>(json);
            // A sign-in made for another Discord application is useless here.
            return stored != null && stored.ClientId == clientId ? new DiscordSession(stored.AccessToken, stored.RefreshToken, stored.ExpiresAt) : null;
        }
        catch (Exception) { return null; }
    }

    public static void Save(string clientId, DiscordSession session)
    {
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(new Stored(clientId, session.AccessToken, session.RefreshToken, session.ExpiresAt));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllBytes(FilePath, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception) { /* Not remembered: the player signs in again next time. */ }
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch (Exception) { /* Removed on the next sign-out or overwritten on the next sign-in. */ }
    }
}
