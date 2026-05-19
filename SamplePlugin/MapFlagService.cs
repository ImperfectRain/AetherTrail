using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AetherTrail;

public static unsafe class MapFlagService
{
    public static bool TryGetFlagPosition(out Vector3 position)
    {
        position = default;

        var agentMap = AgentMap.Instance();

        if (agentMap == null)
            return false;

        if (agentMap->FlagMarkerCount == 0)
            return false;

        var marker = agentMap->FlagMapMarkers[0];

        position = new Vector3(
            marker.XFloat,
            0f,
            marker.YFloat
        );

        return true;
    }
}
