namespace AetherTrail;

public sealed class GraphSyncPreview
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public uint TerritoryId { get; init; }
    public string SenderId { get; init; } = string.Empty;

    public int PacketNodes { get; init; }
    public int NewNodes { get; init; }
    public int DuplicateNodes { get; init; }
    public int RejectedInvalidNodes { get; init; }
    public int RejectedOutOfBoundsNodes { get; init; }
}
