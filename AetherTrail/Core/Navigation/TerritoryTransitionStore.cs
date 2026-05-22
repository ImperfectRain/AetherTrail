using System.Collections.Generic;

namespace AetherTrail;

public sealed class TerritoryTransitionStore
{
    public List<TerritoryTransition> Transitions { get; set; } = new();
}
