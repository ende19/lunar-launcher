using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LunarLauncher;

/// <summary>Finds ArcaneMPLauncher.exe and remembers the player's choice.</summary>
internal static class GameLocator
{
    public const string ExeName = "ArcaneMPLauncher.exe";
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LunarLauncher", "settings.json");

    public static string? Find()
    {
        var candidates = new List<string?> { LoadSaved() };
        // The arcanemp:// handler points at the current launcher; hogwartsmp:// may point at an older exe in the same folder.
        foreach (var scheme in new[] { "arcanemp", "hogwartsmp" })
        {
            var exe = HandlerExe(scheme);
            if (exe != null) candidates.Add(Path.Combine(Path.GetDirectoryName(exe) ?? "", ExeName));
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "HogwartsMP", "client", ExeName));
        return candidates.FirstOrDefault(IsValid);
    }

    public static bool IsValid(string? path) =>
        !string.IsNullOrEmpty(path) && File.Exists(path) &&
        string.Equals(Path.GetFileName(path), ExeName, StringComparison.OrdinalIgnoreCase);

    public static void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Dictionary<string, string> { ["gamePath"] = path }));
    }

    private static string? LoadSaved()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return null;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsFile));
            return saved != null && saved.TryGetValue("gamePath", out var path) ? path : null;
        }
        catch (Exception) { return null; }
    }

    private static string? HandlerExe(string scheme)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{scheme}\shell\open\command");
            var command = key?.GetValue(null) as string;
            if (command == null) return null;
            var match = Regex.Match(command, "^\\s*\"([^\"]+)\"|^\\s*(\\S+)");
            return match.Success ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
        }
        catch (Exception) { return null; }
    }
}
