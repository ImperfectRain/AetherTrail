using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace AetherTrail;

public static unsafe class QuestTargetService
{
    public static bool TryGetQuestTarget(out Vector3 position)
    {
        if (TryGetQuestTarget(out position, out uint territoryId) &&
            territoryId == Plugin.ClientState.TerritoryType)
        {
            return true;
        }

        position = default;
        return false;
    }

    public static bool TryGetQuestTarget(out Vector3 position, out uint territoryId)
    {
        if (QuestService.TryGetTrackedQuestLevelPosition(out position))
        {
            territoryId = Plugin.ClientState.TerritoryType;
            return true;
        }

        if (QuestService.TryGetTrackedQuestLevelTarget(out territoryId, out position))
            return true;

        if (TryGetQuestLinkPosition(out position, out territoryId))
            return true;

        position = default;
        territoryId = 0;
        return false;
    }

    private static bool TryGetQuestLinkPosition(out Vector3 position, out uint territoryId)
    {
        position = default;
        territoryId = 0;

        uint questId = QuestService.GetTrackedQuestId();

        if (questId == 0)
            return false;

        var agentMap = AgentMap.Instance();

        if (agentMap == null)
            return false;

        if (TryGetQuestLinkPosition(agentMap->MiniMapQuestLinkContainer, questId, out position, out territoryId))
            return true;

        if (TryGetQuestLinkPosition(agentMap->MapQuestLinkContainer, questId, out position, out territoryId))
            return true;

        return TryGetLinkedMsqPosition(agentMap, out position, out territoryId);
    }

    private static bool TryGetQuestLinkPosition(
        QuestLinkContainer container,
        uint questId,
        out Vector3 position,
        out uint territoryId)
    {
        position = default;
        territoryId = 0;

        uint currentMapId = Plugin.ClientState.MapId;
        int markerCount = Math.Min(container.MarkerCount, (ushort)container.Markers.Length);

        for (int i = 0; i < markerCount; i++)
        {
            var marker = container.Markers[i];

            if (marker.Valid == 0)
                continue;

            if (marker.QuestId != questId)
                continue;

            if (marker.SourceMapId != 0 && marker.SourceMapId != currentMapId)
                continue;

            if (!TryGetPositionFromLevelId(marker.LevelId, out position, out territoryId))
                continue;

            return true;
        }

        return false;
    }

    private static bool TryGetLinkedMsqPosition(
        AgentMap* agentMap,
        out Vector3 position,
        out uint territoryId)
    {
        position = default;
        territoryId = 0;

        foreach (var marker in agentMap->MinimapMSQLinkedTooltipMarkers)
        {
            if (!TryGetPositionFromLevelId(marker.LevelId, out position, out territoryId))
                continue;

            return true;
        }

        return false;
    }

    private static bool TryGetPositionFromLevelId(
        uint levelId,
        out Vector3 position,
        out uint territoryId)
    {
        position = default;
        territoryId = 0;

        var levelSheet = Plugin.DataManager.GetExcelSheet<Level>();

        if (levelSheet == null)
            return false;

        if (!levelSheet.TryGetRow(levelId, out var level))
            return false;

        territoryId = level.Territory.RowId;

        if (territoryId == 0)
            return false;

        position = new Vector3(
            level.X,
            level.Y,
            level.Z
        );

        return true;
    }
}
