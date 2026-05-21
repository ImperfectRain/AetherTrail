using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace AetherTrail;

public static unsafe class MapTransformService
{
    public static bool TryGetCurrent(out MapTransformSnapshot transform)
    {
        transform = default;

        var agentMap = AgentMap.Instance();

        if (agentMap == null)
            return false;

        float sizeFactor = agentMap->CurrentMapSizeFactor;
        uint mapId = agentMap->CurrentMapId;
        uint territoryId = agentMap->CurrentTerritoryId;
        float offsetX = agentMap->CurrentOffsetX;
        float offsetY = agentMap->CurrentOffsetY;
        string texturePath = GetFirstNonEmptyPath(
            agentMap->CurrentMapPath.ToString(),
            agentMap->CurrentMapBgPath.ToString());

        if (sizeFactor <= 0)
        {
            sizeFactor = agentMap->SelectedMapSizeFactor;
            mapId = agentMap->SelectedMapId;
            territoryId = agentMap->SelectedTerritoryId;
            offsetX = agentMap->SelectedOffsetX;
            offsetY = agentMap->SelectedOffsetY;
            texturePath = GetFirstNonEmptyPath(
                agentMap->SelectedMapPath.ToString(),
                agentMap->SelectedMapBgPath.ToString());
        }

        if (sizeFactor <= 0)
            return false;

        transform = new MapTransformSnapshot(
            territoryId,
            mapId,
            sizeFactor,
            offsetX,
            offsetY,
            texturePath);

        return true;
    }

    private static string GetFirstNonEmptyPath(params string[] paths)
    {
        foreach (string path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
                return path;
        }

        return string.Empty;
    }
}
