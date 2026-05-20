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

    public static void PrintQuestSheetDebug(uint questId)
    {
        if (questId == 0)
        {
            Plugin.ChatGui.Print("AetherTrail Quest Debug: no tracked quest.");
            return;
        }

        if (!TryGetQuestRow(questId, out var quest))
        {
            Plugin.ChatGui.Print($"AetherTrail Quest Debug: Quest row not found for quest id {questId}.");
            return;
        }

        PrintQuestProperties(quest);
    }

    private static void PrintQuestProperties(Quest quest)
    {
        byte sequence = GetTrackedQuestSequence();

        Plugin.ChatGui.Print($"Quest RowId: {quest.RowId}");
        Plugin.ChatGui.Print($"Current Sequence: {sequence}");
        Plugin.ChatGui.Print($"IssuerLocation: {quest.IssuerLocation.RowId}");
        Plugin.ChatGui.Print($"IssuerStart: {quest.IssuerStart.RowId}");
        Plugin.ChatGui.Print($"TargetEnd: {quest.TargetEnd.RowId}");

        int todoIndex = 0;

        foreach (var todo in quest.TodoParams)
        {
            Plugin.ChatGui.Print(
                $"Todo {todoIndex}: ToDoCompleteSeq={todo.ToDoCompleteSeq}, ToDoQty={todo.ToDoQty}, CountableNum={todo.CountableNum}"
            );

            int locationIndex = 0;

            foreach (var locationRef in todo.ToDoLocation)
            {
                if (locationRef.RowId != 0)
                {
                    var level = locationRef.Value;
                    var player = Plugin.ObjectTable.LocalPlayer;

                    float distance = player == null
                        ? -1f
                        : Vector3.Distance(player.Position, new Vector3(level.X, level.Y, level.Z));

                    Plugin.ChatGui.Print(
                        $"Todo {todoIndex} Location {locationIndex}: LevelRow={locationRef.RowId}, X={level.X:F2}, Y={level.Y:F2}, Z={level.Z:F2}, Territory={level.Territory.RowId}, Distance={distance:F1}"
                    );
                }

                locationIndex++;
            }

            todoIndex++;
        }
    }

    public static void PrintTrackedQuestWorkDebug()
    {
        foreach (var questWork in GetAllTrackedQuestWorks())
        {
            Plugin.ChatGui.Print(
                $"QuestWork: QuestId={questWork.QuestId}, Sequence={questWork.Sequence}, Flags={questWork.Flags}, IsHidden={questWork.IsHidden}, IsPriority={questWork.IsPriority}"
            );

            int variableIndex = 0;

            foreach (var variable in questWork.Variables)
            {
                Plugin.ChatGui.Print($"Variable[{variableIndex}] = {variable}");
                variableIndex++;
            }
        }
    }

    public static void PrintNearbyObjectsDebug(float maxDistance = 25f)
    {
        var player = Plugin.ObjectTable.LocalPlayer;

        if (player == null)
        {
            Plugin.ChatGui.Print("AetherTrail Quest Debug: player not found.");
            return;
        }

        Plugin.ChatGui.Print($"AetherTrail Quest Debug: nearby objects within {maxDistance} yalms:");

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null)
                continue;

            float distance = Vector3.Distance(player.Position, obj.Position);

            if (distance > maxDistance)
                continue;

            Plugin.ChatGui.Print(
                $"Obj: Name={obj.Name}, Kind={obj.ObjectKind}, BaseId={obj.BaseId}, EntityId={obj.EntityId}, Dist={distance:F1}, Pos=({obj.Position.X:F1}, {obj.Position.Y:F1}, {obj.Position.Z:F1})"
            );
        }
    }
}
