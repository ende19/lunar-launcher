using System.Text;
using System.Text.RegularExpressions;

namespace LunarLauncher;

/// <summary>
/// Reads the player's ArcaneMP account ID from the client log ("Account &lt;id&gt; (key at …)").
/// Only the public ID is read; the account.key file itself is never opened.
/// </summary>
internal static class AccountLog
{
    private const int TailBytes = 4 * 1024 * 1024;
    private static readonly Regex Line = new(@"\] Account ([A-Za-z0-9]+) \(key at ");

    public static string? Read(string? gameExe)
    {
        if (gameExe == null) return null;
        var log = Path.Combine(Path.GetDirectoryName(gameExe) ?? "", "logs", "ArcaneMP.log");
        try
        {
            // The game keeps the log open while running.
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - TailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var matches = Line.Matches(reader.ReadToEnd());
            return matches.Count > 0 ? matches[^1].Groups[1].Value : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
