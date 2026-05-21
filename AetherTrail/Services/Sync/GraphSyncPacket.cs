using System;
using System.Collections.Generic;

namespace AetherTrail;

public sealed class GraphSyncPacket
{
    public int Version { get; init; } = 1;
    public uint TerritoryId { get; init; }
    public string SenderId { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public NavGraph Graph { get; init; } = new();
}
