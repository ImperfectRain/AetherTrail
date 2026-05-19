using System.Numerics;

namespace AetherTrail;

public enum NavigationTargetType
{
    None,
    MapFlag,
    Quest
}

public sealed class NavigationTarget
{
    public NavigationTargetType Type { get; init; }
    public Vector3 Position { get; init; }
    public string Label { get; init; } = string.Empty;
}
