using Microsoft.CommandPalette.Extensions.Toolkit;

namespace LittleAgentsExtension;

internal static class Icons
{
    public static IconInfo AgentDefault { get; } = new("\uE945");
    public static IconInfo New { get; } = new("\uE710");
    public static IconInfo Settings { get; } = new("\uE713");
    public static IconInfo Provider { get; } = new("\uE968");
    public static IconInfo Run { get; } = new("\uE7C5");
    public static IconInfo Delete { get; } = new("\uE74D");
}
