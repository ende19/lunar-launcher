namespace LunarLauncher;

internal static class GameLink
{
    /// <summary>
    /// arcanemp://host:port?password=…&amp;ticket=… — the ArcaneMP client reads password, nickname and ticket
    /// from its deep link. The ticket is the Discord access token; the server verifies it with Discord.
    /// </summary>
    public static string Build(LauncherConfig config, string ticket)
    {
        var query = new List<string>();
        if (config.ServerPassword.Length > 0) query.Add("password=" + Uri.EscapeDataString(config.ServerPassword));
        query.Add("ticket=" + Uri.EscapeDataString(ticket));
        return $"arcanemp://{config.ServerHost}:{config.ServerPort}?{string.Join("&", query)}";
    }
}
