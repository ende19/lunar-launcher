namespace LunarLauncher;

/// <summary>Lunar Roleplay design tokens (docs/ui-tokens.css in the game server repository).</summary>
internal static class Theme
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#09090F");
    public static readonly Color BgSecondary = ColorTranslator.FromHtml("#12101B");
    public static readonly Color Surface = ColorTranslator.FromHtml("#1D1929");
    public static readonly Color Border = ColorTranslator.FromHtml("#332B45");
    public static readonly Color Primary = ColorTranslator.FromHtml("#8B5CF6");
    public static readonly Color PrimaryHover = ColorTranslator.FromHtml("#A78BFA");
    public static readonly Color PrimaryDark = ColorTranslator.FromHtml("#7C3AED");
    public static readonly Color Text = ColorTranslator.FromHtml("#F5F3FF");
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#A1A1B5");
    public static readonly Color Danger = ColorTranslator.FromHtml("#F87171");
    public static readonly Color Success = ColorTranslator.FromHtml("#4ADE80");
    // Discord Blurple (discord.com/branding), for the sign-in button only; the hover shade is the button's pressed state.
    public static readonly Color Discord = ColorTranslator.FromHtml("#5865F2");
    public static readonly Color DiscordHover = ColorTranslator.FromHtml("#4752C4");
}
