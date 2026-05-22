using FFXIVClientStructs.FFXIV.Application.Network.WorkDefinitions;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace AetherTrail;

public static unsafe class QuestService
{
    public static uint GetTrackedQuestId()
    {
        return TryGetPrimaryTrackedQuestWork(out var questWork)
            ? (uint)questWork.QuestId
            : 0;
    }

    public static byte GetTrackedQuestSequence()
    {
        return TryGetPrimaryTrackedQuestWork(out var questWork)
            ? questWork.Sequence
            : (byte)0;
    }

    public static bool TryGetTrackedQuestLevelPosition(out Vector3 position)
    {
        position = default;

        uint currentTerritory = (uint)Plugin.ClientState.TerritoryType;

        if (TryGetPrimaryTrackedQuestWork(out var primaryQuestWork) &&
            TryResolveQuestWorkPosition(primaryQuestWork, currentTerritory, out position))
        {
            return true;
        }

        foreach (var questWork in GetAllTrackedQuestWorks())
        {
            if (primaryQuestWorkEquals(questWork))
                continue;

            if (TryResolveQuestWorkPosition(questWork, currentTerritory, out position))
                return true;
        }

        return false;

        bool primaryQuestWorkEquals(QuestWork other)
        {
            return TryGetPrimaryTrackedQuestWork(out var primary) &&
                   primary.QuestId == other.QuestId &&
                   primary.Sequence == other.Sequence;
        }
    }

    public static bool TryGetTrackedQuestLevelTarget(out uint territoryId, out Vector3 position)
    {
        territoryId = 0;
        position = default;

        if (TryGetPrimaryTrackedQuestWork(out var primaryQuestWork) &&
            TryResolveQuestWorkTarget(primaryQuestWork, out territoryId, out position))
        {
            return true;
        }

        foreach (var questWork in GetAllTrackedQuestWorks())
        {
            if (primaryQuestWorkEquals(questWork))
                continue;

            if (TryResolveQuestWorkTarget(questWork, out territoryId, out position))
                return true;
        }

        return false;

        bool primaryQuestWorkEquals(QuestWork other)
        {
            return TryGetPrimaryTrackedQuestWork(out var primary) &&
                   primary.QuestId == other.QuestId &&
                   primary.Sequence == other.Sequence;
        }
    }

    private static bool TryResolveQuestWorkPosition(QuestWork questWork, uint currentTerritory, out Vector3 position)
    {
        position = default;

        if (!TryGetQuestRow(questWork.QuestId, out var quest))
            return false;

        if (TryGetQuestTodoPosition(quest, questWork.Sequence, currentTerritory, exactSequenceOnly: true, out position))
            return true;

        if (TryGetQuestTodoPosition(quest, questWork.Sequence, currentTerritory, exactSequenceOnly: false, out position))
            return true;

        if (TryFindLoadedObjectPosition(quest.TargetEnd.RowId, out position))
            return true;

        if (TryFindLoadedObjectPosition(quest.IssuerStart.RowId, out position))
            return true;

        return false;
    }

    private static bool TryResolveQuestWorkTarget(QuestWork questWork, out uint territoryId, out Vector3 position)
    {
        territoryId = 0;
        position = default;

        if (!TryGetQuestRow(questWork.QuestId, out var quest))
            return false;

        if (TryGetQuestTodoTarget(quest, questWork.Sequence, exactSequenceOnly: true, out territoryId, out position))
            return true;

        return TryGetQuestTodoTarget(quest, questWork.Sequence, exactSequenceOnly: false, out territoryId, out position);
    }

    private static bool TryGetQuestTodoPosition(
        Quest quest,
        byte sequence,
        uint currentTerritory,
        bool exactSequenceOnly,
        out Vector3 position)
    {
        position = default;

        foreach (var todo in quest.TodoParams)
        {
            if (exactSequenceOnly && todo.ToDoCompleteSeq != sequence)
                continue;

            foreach (var locationRef in todo.ToDoLocation)
            {
                if (locationRef.RowId == 0)
                    continue;

                var level = locationRef.Value;

                if (level.Territory.RowId != currentTerritory)
                    continue;

                position = new Vector3(level.X, level.Y, level.Z);
                return true;
            }
        }

        return false;
    }

    private static bool TryGetQuestTodoTarget(
        Quest quest,
        byte sequence,
        bool exactSequenceOnly,
        out uint territoryId,
        out Vector3 position)
    {
        territoryId = 0;
        position = default;

        foreach (var todo in quest.TodoParams)
        {
            if (exactSequenceOnly && todo.ToDoCompleteSeq != sequence)
                continue;

            foreach (var locationRef in todo.ToDoLocation)
            {
                if (locationRef.RowId == 0)
                    continue;

                var level = locationRef.Value;

                if (level.Territory.RowId == 0)
                    continue;

                territoryId = level.Territory.RowId;
                position = new Vector3(level.X, level.Y, level.Z);
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPrimaryTrackedQuestWork(out QuestWork questWork)
    {
        questWork = default;

        QuestWork? fallback = null;

        foreach (var candidate in GetAllTrackedQuestWorks())
        {
            fallback ??= candidate;

            if (candidate.IsPriority)
            {
                questWork = candidate;
                return true;
            }
        }

        if (fallback.HasValue)
        {
            questWork = fallback.Value;
            return true;
        }

        return false;
    }

    private static unsafe List<QuestWork> GetAllTrackedQuestWorks()
    {
        List<QuestWork> results = new();

        var questManager = QuestManager.Instance();

        if (questManager == null)
            return results;

        var normalQuests = questManager->NormalQuests;

        foreach (var trackedQuest in questManager->TrackedQuests)
        {
            if (trackedQuest.Index == 255)
                continue;

            if (trackedQuest.QuestType != 1)
                continue;

            int questIndex = trackedQuest.Index;

            if (questIndex < 0 || questIndex >= normalQuests.Length)
                continue;

            var questWork = normalQuests[questIndex];

            if (questWork.QuestId == 0)
                continue;

            results.Add(questWork);
        }

        return results;
    }

    private static bool TryGetQuestRow(uint questId, out Quest quest)
    {
        var questSheet = Plugin.DataManager.GetExcelSheet<Quest>();

        if (questSheet == null)
        {
            quest = default;
            return false;
        }

        uint[] candidateIds =
        {
            questId,
            questId + 65536,
            questId + 66000,
            questId + 70000,
            questId + 100000
        };

        foreach (uint candidateId in candidateIds)
        {
            if (questSheet.TryGetRow(candidateId, out quest))
                return true;
        }

        quest = default;
        return false;
    }

    private static bool TryFindLoadedObjectPosition(uint baseId, out Vector3 position)
    {
        position = default;

        if (baseId == 0)
            return false;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null)
                continue;

            if (obj.BaseId != baseId)
                continue;

            position = obj.Position;
            return true;
        }

        return false;
    }

}
