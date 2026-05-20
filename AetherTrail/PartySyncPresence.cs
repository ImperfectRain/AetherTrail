using System;

namespace AetherTrail;

public sealed class PartySyncPresence
{
    public string SenderId { get; set; } = "";
    public string DisplayName { get; set; } = "";

    public uint TerritoryId { get; set; }

    public SyncVector3 Position { get; set; }
    public float RotationRadians { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
