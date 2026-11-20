using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LunarLauncher;

/// <summary>Distribution settings. Contains no secrets: everything here is visible to anyone holding the exe.</summary>
internal sealed record LauncherConfig(
    string ClientId,
    string GuildId,
    IReadOnlyList<string> RoleIds,
    string ServerHost,
    int ServerPort,
    string ServerPassword,
    int RedirectPort,
    string AuthFlow)
{
    private const string FileName = "launcher.json";
    private static readonly Regex Snowflake = new("^[0-9]{17,20}$");

    public string RedirectUri => $"http://127.0.0.1:{RedirectPort}/callback";

    public static LauncherConfig Load()
    {
        var external = Path.Combine(AppContext.BaseDirectory, FileName);
        string json;
        if (File.Exists(external))
        {
            json = File.ReadAllText(external);
        }
        else
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(FileName)
                ?? throw new LauncherException("Configurazione del launcher mancante (launcher.json).");
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }

        LauncherConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<LauncherConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException error)
        {
            throw new LauncherException("launcher.json non è un JSON valido.", error);
        }
        return (config ?? throw new LauncherException("launcher.json è vuoto.")).Validate();
    }

    private LauncherConfig Validate()
    {
        string? problem =
            !Snowflake.IsMatch(ClientId ?? "") ? "clientId" :
            !Snowflake.IsMatch(GuildId ?? "") ? "guildId" :
            RoleIds is not { Count: > 0 } || RoleIds.Any(role => !Snowflake.IsMatch(role ?? "")) ? "roleIds" :
            string.IsNullOrWhiteSpace(ServerHost) || ServerHost.Any(char.IsWhiteSpace) ? "serverHost" :
            ServerPort is < 1 or > 65535 ? "serverPort" :
            RedirectPort is < 1024 or > 65535 ? "redirectPort" :
            AuthFlow is not ("pkce" or "implicit") ? "authFlow (\"pkce\" o \"implicit\")" :
            null;
        if (problem != null) throw new LauncherException($"launcher.json: valore non valido per {problem}.");
        return this with { ServerPassword = ServerPassword ?? "" };
    }
}
