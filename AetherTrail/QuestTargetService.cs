using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;

namespace AetherTrail;

public static unsafe class QuestTargetService
{


    public static bool TryGetQuestTarget(out Vector3 position)
    {
        return QuestService.TryGetTrackedQuestLevelPosition(out position);
    }

    public static void PrintQuestMarkerDebug()
    {
        var agentMap = AgentMap.Instance();

        if (agentMap == null)
        {
            Plugin.ChatGui.Print("AetherTrail Quest Debug: AgentMap is null.");
            return;
        }

        Plugin.ChatGui.Print($"AetherTrail Quest Debug: CurrentMapId={agentMap->CurrentMapId}, SelectedMapId={agentMap->SelectedMapId}, CurrentTerritoryId={agentMap->CurrentTerritoryId}, SelectedTerritoryId={agentMap->SelectedTerritoryId}");

        PrintContainer("MiniMapQuestLinkContainer", agentMap->MiniMapQuestLinkContainer);
        PrintContainer("MapQuestLinkContainer", agentMap->MapQuestLinkContainer);
    }

    private static void PrintContainer(string name, QuestLinkContainer container)
    {
        Plugin.ChatGui.Print($"AetherTrail Quest Debug: {name} MarkerCount={container.MarkerCount}");

        int index = 0;

        foreach (var marker in container.Markers)
        {
            Plugin.ChatGui.Print(
                $"Marker {index}: Valid={marker.Valid}, QuestId={marker.QuestId}, LevelId={marker.LevelId}, SourceMapId={marker.SourceMapId}, TargetMapId={marker.TargetMapId}, IconId={marker.IconId}"
            );

            if (marker.LevelId != 0)
            {
                if (TryGetPositionFromLevelId(marker.LevelId, out var pos))
                {
                    Plugin.ChatGui.Print($"Marker {index}: Level position = X={pos.X:F2}, Y={pos.Y:F2}, Z={pos.Z:F2}");
                }
                else
                {
                    Plugin.ChatGui.Print($"Marker {index}: Level lookup failed.");
                }
            }

            index++;
        }
    }

    private static bool TryGetQuestTargetFromContainer(QuestLinkContainer container, out Vector3 position)
    {
        position = default;

        if (container.MarkerCount == 0)
            return false;

        foreach (var marker in container.Markers)
        {
            if (marker.Valid == 0)
                continue;

            if (marker.LevelId == 0)
                continue;

            if (TryGetPositionFromLevelId(marker.LevelId, out position))
                return true;
        }

        return false;
    }

    private static bool TryGetPositionFromLevelId(uint levelId, out Vector3 position)
    {
        position = default;

        var levelSheet = Plugin.DataManager.GetExcelSheet<Level>();

        if (levelSheet == null)
            return false;

        if (!levelSheet.TryGetRow(levelId, out var level))
            return false;

        position = new Vector3(
            level.X,
            level.Y,
            level.Z
        );

        return true;
    }
}
