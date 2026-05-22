using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{
    private const float TransitionMergeDistance = 18.0f;

    public static bool TryGetTransitionToward(uint currentTerritoryId, uint targetTerritoryId, out Vector3 position)
    {
        position = default;

        if (currentTerritoryId == 0 ||
            targetTerritoryId == 0 ||
            currentTerritoryId == targetTerritoryId)
        {
            return false;
        }

        var transitions = GetOrLoadTransitions().Transitions;

        if (transitions.Count == 0)
            return false;

        var route = FindTerritoryRoute(transitions, currentTerritoryId, targetTerritoryId);

        if (route.Count < 2)
            return false;

        uint nextTerritoryId = route[1];
        var transition = transitions
            .Where(candidate =>
                candidate.SourceTerritoryId == currentTerritoryId &&
                candidate.TargetTerritoryId == nextTerritoryId)
            .OrderByDescending(candidate => candidate.Observations)
            .ThenByDescending(candidate => candidate.LastObservedUtc)
            .FirstOrDefault();

        if (transition == null || !IsValidNodePosition(transition.SourcePosition))
            return false;

        position = transition.SourcePosition;
        return true;
    }

    public static List<TerritoryTransitionMapMarker> GetTransitionMapMarkers(uint territoryId)
    {
        List<TerritoryTransitionMapMarker> markers = new();

        foreach (var transition in GetOrLoadTransitions().Transitions)
        {
            if (transition.SourceTerritoryId == territoryId &&
                IsValidNodePosition(transition.SourcePosition))
            {
                markers.Add(new TerritoryTransitionMapMarker
                {
                    SourceTerritoryId = transition.SourceTerritoryId,
                    TargetTerritoryId = transition.TargetTerritoryId,
                    Position = transition.SourcePosition,
                    IsSource = true,
                    Observations = transition.Observations
                });
            }

            if (transition.TargetTerritoryId == territoryId &&
                IsValidNodePosition(transition.TargetPosition))
            {
                markers.Add(new TerritoryTransitionMapMarker
                {
                    SourceTerritoryId = transition.SourceTerritoryId,
                    TargetTerritoryId = transition.TargetTerritoryId,
                    Position = transition.TargetPosition,
                    IsSource = false,
                    Observations = transition.Observations
                });
            }
        }

        return markers;
    }

    public static int GetTransitionMarkerCount(uint territoryId)
    {
        return GetTransitionMapMarkers(territoryId).Count;
    }

    public static IReadOnlyList<TerritoryTransition> GetTransitionSnapshot()
    {
        return GetOrLoadTransitions()
            .Transitions
            .OrderBy(transition => transition.SourceTerritoryId)
            .ThenBy(transition => transition.TargetTerritoryId)
            .ThenByDescending(transition => transition.Observations)
            .ToList();
    }

    private static void LearnTerritoryTransition(
        uint sourceTerritoryId,
        Vector3 sourcePosition,
        uint targetTerritoryId,
        Vector3 targetPosition)
    {
        if (sourceTerritoryId == 0 ||
            targetTerritoryId == 0 ||
            sourceTerritoryId == targetTerritoryId ||
            !IsValidNodePosition(sourcePosition) ||
            !IsValidNodePosition(targetPosition))
        {
            return;
        }

        var store = GetOrLoadTransitions();
        float mergeDistanceSq = TransitionMergeDistance * TransitionMergeDistance;
        var existing = store.Transitions.FirstOrDefault(transition =>
            transition.SourceTerritoryId == sourceTerritoryId &&
            transition.TargetTerritoryId == targetTerritoryId &&
            Vector3.DistanceSquared(transition.SourcePosition, sourcePosition) <= mergeDistanceSq &&
            Vector3.DistanceSquared(transition.TargetPosition, targetPosition) <= mergeDistanceSq);

        if (existing == null)
        {
            store.Transitions.Add(new TerritoryTransition
            {
                SourceTerritoryId = sourceTerritoryId,
                TargetTerritoryId = targetTerritoryId,
                SourcePosition = sourcePosition,
                TargetPosition = targetPosition
            });
        }
        else
        {
            int observations = Math.Max(1, existing.Observations);
            float nextWeight = 1.0f / (observations + 1);

            existing.SourcePosition = Vector3.Lerp(existing.SourcePosition, sourcePosition, nextWeight);
            existing.TargetPosition = Vector3.Lerp(existing.TargetPosition, targetPosition, nextWeight);
            existing.Observations = observations + 1;
            existing.LastObservedUtc = DateTime.UtcNow;
        }

        TransitionsDirty = true;
    }

    private static List<uint> FindTerritoryRoute(
        IReadOnlyList<TerritoryTransition> transitions,
        uint sourceTerritoryId,
        uint targetTerritoryId)
    {
        Queue<uint> pending = new();
        Dictionary<uint, uint> previous = new();
        HashSet<uint> visited = new() { sourceTerritoryId };

        pending.Enqueue(sourceTerritoryId);

        while (pending.Count > 0)
        {
            uint territoryId = pending.Dequeue();

            foreach (uint nextTerritoryId in transitions
                         .Where(transition => transition.SourceTerritoryId == territoryId)
                         .Select(transition => transition.TargetTerritoryId)
                         .Distinct())
            {
                if (!visited.Add(nextTerritoryId))
                    continue;

                previous[nextTerritoryId] = territoryId;

                if (nextTerritoryId == targetTerritoryId)
                    return RebuildTerritoryRoute(previous, sourceTerritoryId, targetTerritoryId);

                pending.Enqueue(nextTerritoryId);
            }
        }

        return new List<uint>();
    }

    private static List<uint> RebuildTerritoryRoute(
        IReadOnlyDictionary<uint, uint> previous,
        uint sourceTerritoryId,
        uint targetTerritoryId)
    {
        List<uint> route = new() { targetTerritoryId };
        uint territoryId = targetTerritoryId;

        while (territoryId != sourceTerritoryId)
        {
            if (!previous.TryGetValue(territoryId, out territoryId))
                return new List<uint>();

            route.Add(territoryId);
        }

        route.Reverse();
        return route;
    }

    private static TerritoryTransitionStore GetOrLoadTransitions()
    {
        if (Transitions != null)
            return Transitions;

        string path = GetTransitionPath();

        if (!File.Exists(path))
        {
            Transitions = new TerritoryTransitionStore();
            return Transitions;
        }

        try
        {
            string json = File.ReadAllText(path);
            Transitions = JsonSerializer.Deserialize<TerritoryTransitionStore>(json, JsonOptions) ??
                          new TerritoryTransitionStore();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to load AetherTrail territory transitions.");
            Transitions = new TerritoryTransitionStore();
        }

        return Transitions;
    }

    private static void SaveTransitions()
    {
        if (!TransitionsDirty || Transitions == null)
            return;

        try
        {
            string path = GetTransitionPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(Transitions, JsonOptions);

            File.WriteAllText(path, json);
            TransitionsDirty = false;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to save AetherTrail territory transitions.");
        }
    }

    private static string GetTransitionPath()
    {
        return Path.Combine(
            Plugin.PluginInterface.ConfigDirectory.FullName,
            "Transitions.json");
    }
}
