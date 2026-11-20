using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LunarLauncher;

/// <summary>Finds an ArcaneMP client the player installed before, to reuse its game path setting.</summary>
internal static class GameLocator
{
    public const string ExeName = "ArcaneMPLauncher.exe";

    /// <summary>The folder of an existing ArcaneMP client outside the launcher's own install, if any.</summary>
    public static string? FindExistingClient()
    {
        var candidates = new List<string?>();
        foreach (var scheme in new[] { "arcanemp", "hogwartsmp" })
        {
            var exe = HandlerExe(scheme);
            if (exe != null) candidates.Add(Path.GetDirectoryName(exe));
        }
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "HogwartsMP", "client"));
        return candidates.FirstOrDefault(dir =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, ExeName)) &&
            !Path.GetFullPath(dir).StartsWith(ClientInstaller.Root, StringComparison.OrdinalIgnoreCase));
    }

    private static string? HandlerExe(string scheme)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{scheme}\shell\open\command");
            var command = key?.GetValue(null) as string;
            if (command == null) return null;
            var match = Regex.Match(command, @"^\s*""([^""]+)""|^\s*(\S+)");
            return match.Success ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
        }
        catch (Exception) { return null; }
    }
}
