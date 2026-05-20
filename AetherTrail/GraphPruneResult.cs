namespace AetherTrail;

public sealed class GraphPruneResult
{
    public int RemovedIsolatedNodes { get; init; }
    public int RemovedBrokenLinks { get; init; }
    public int RemovedSelfLinks { get; init; }
    public int RemovedDuplicateLinks { get; init; }
    public int MergedDuplicateNodes { get; init; }
    public int RemainingNodes { get; init; }
    public int RemainingLinks { get; init; }
    public int RemovedFlightNodes { get; init; }
}
