using System.Numerics;

namespace AetherTrail;

public sealed class TerritoryTransitionMapMarker
{
    public uint SourceTerritoryId { get; init; }
    public uint TargetTerritoryId { get; init; }
    public Vector3 Position { get; init; }
    public bool IsSource { get; init; }
    public int Observations { get; init; }
}
