using System.Collections.Generic;
using System.Numerics;

namespace AetherTrail;

public sealed class NavNode
{
    public string Id { get; init; } = string.Empty;
    public Vector3 Position { get; init; }
    public List<string> Links { get; init; } = new();

    public Dictionary<string, int> LinkConfidence { get; init; } = new();

    public NavTraversalMode TraversalMode { get; init; } = NavTraversalMode.Ground;
}
