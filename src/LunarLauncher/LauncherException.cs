namespace LunarLauncher;

/// <summary>An expected failure whose message is Italian and safe to show to the player.</summary>
internal class LauncherException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Discord no longer accepts the access token: the player has to sign in again.</summary>
internal sealed class DiscordExpiredException(string message) : LauncherException(message);
