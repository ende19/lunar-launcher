namespace LunarLauncher;

/// <summary>An expected failure whose message is Italian and safe to show to the player.</summary>
internal sealed class LauncherException(string message, Exception? inner = null) : Exception(message, inner);
