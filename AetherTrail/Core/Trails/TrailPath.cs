using System.Collections.Generic;
using System.Numerics;

namespace AetherTrail;

public sealed class TrailPath
{
    public List<TrailPoint> Points { get; init; } = new();
}

public sealed class TrailPoint
{
    public Vector3 Position { get; init; }
    public bool IsGraphPoint { get; init; }
}
