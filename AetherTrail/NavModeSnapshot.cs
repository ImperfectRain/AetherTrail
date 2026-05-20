using System.Collections.Generic;
using System.Numerics;

namespace AetherTrail;

public sealed class NavNodeSnapshot
{
    public string Id { get; init; } = "";
    public Vector3 Position { get; init; }
    public NavTraversalMode TraversalMode { get; init; }
    public List<string> Links { get; init; } = new();
    public Dictionary<string, int> LinkConfidence { get; init; } = new();
}
