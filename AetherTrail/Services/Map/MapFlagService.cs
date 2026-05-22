using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AetherTrail;

public static unsafe class MapFlagService
{
    public static bool TryGetFlagPosition(out Vector3 position)
    {
        return TryGetFlagTarget(out position, out _);
    }

    public static bool TryGetFlagTarget(out Vector3 position, out uint territoryId)
    {
        position = default;
        territoryId = 0;

        var agentMap = AgentMap.Instance();

        if (agentMap == null)
            return false;

        if (agentMap->FlagMarkerCount == 0)
            return false;

        var marker = agentMap->FlagMapMarkers[0];

        territoryId = marker.TerritoryId;
        position = new Vector3(
            marker.XFloat,
            0f,
            marker.YFloat
        );

        return territoryId != 0;
    }
}
