using System;
using System.Numerics;

namespace AetherTrail;

public sealed class TerritoryTransition
{
    public uint SourceTerritoryId { get; set; }
    public uint TargetTerritoryId { get; set; }
    public Vector3 SourcePosition { get; set; }
    public Vector3 TargetPosition { get; set; }
    public int Observations { get; set; } = 1;
    public DateTime LastObservedUtc { get; set; } = DateTime.UtcNow;
}
