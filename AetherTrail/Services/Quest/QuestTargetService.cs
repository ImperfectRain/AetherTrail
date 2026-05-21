using System.Numerics;
using Lumina.Excel.Sheets;

namespace AetherTrail;

public static class QuestTargetService
{
    public static bool TryGetQuestTarget(out Vector3 position)
    {
        return QuestService.TryGetTrackedQuestLevelPosition(out position);
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
