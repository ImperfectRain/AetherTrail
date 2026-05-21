using System.Collections.Generic;
using System.Numerics;

namespace AetherTrail;

public sealed class NavPathResult
{
    public bool Success { get; init; }
    public List<Vector3> Points { get; init; } = new();
    public float Cost { get; init; }
    public string FailureReason { get; init; } = string.Empty;
}
