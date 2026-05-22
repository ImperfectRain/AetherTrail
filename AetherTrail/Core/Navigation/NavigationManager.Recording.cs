using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{

    private static NavNode? GetNearestNodeExcluding(NavGraph graph, Vector3 position, float maxDistance, string? excludedId)
    {
        NavNode? best = null;
        float bestDistanceSq = maxDistance * maxDistance;

        foreach (var node in graph.Nodes)
        {
            if (node.Id == excludedId)
                continue;

            float distanceSq = Vector3.DistanceSquared(node.Position, position);

            if (distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                best = node;
            }
        }

        return best;
    }

    public static void RecordPlayerPosition(uint territoryId, Vector3 position)
    {
        var graph = GetOrLoadGraph(territoryId);

        if (!IsValidNodePosition(position))
            return;

        ObserveTerritoryTransition(territoryId, position);

        if (territoryId != LastTerritoryId)
        {
            LastRecordedPosition = null;
            LastMovementDirection = null;
            LastRecordedNodeId = null;
            LastTerritoryId = territoryId;
        }

        if (LastRecordedPosition.HasValue)
        {
            float movedDistance = Vector3.Distance(LastRecordedPosition.Value, position);

            if (movedDistance > TeleportResetDistance)
            {
                LastRecordedPosition = null;
                LastMovementDirection = null;
                LastRecordedNodeId = null;
            }
        }

        if (LastRecordedPosition.HasValue)
        {
            Vector3 movement = position - LastRecordedPosition.Value;
            movement.Y = 0f;

            float movedDistance = movement.Length();

            if (movedDistance < CornerNodeSpacing)
                return;

            Vector3 currentDirection = Vector3.Normalize(movement);

            bool changedDirection = false;

            if (LastMovementDirection.HasValue)
            {
                float directionDot = Vector3.Dot(LastMovementDirection.Value, currentDirection);
                changedDirection = directionDot < 1.0f - DirectionChangeThreshold;
            }

            bool movedFarEnough = movedDistance >= NodeSpacing;

            if (!movedFarEnough && !changedDirection)
                return;

            LastMovementDirection = currentDirection;
        }

        var nearbyExistingNode = graph.GetNearestNode(position, NodeSpacing * 0.6f);

        if (nearbyExistingNode != null)
        {
            if (LastRecordedNodeId != null)
            {
                var previousNode = graph.GetNode(LastRecordedNodeId);

                if (previousNode != null && previousNode.Id != nearbyExistingNode.Id)
                {
                    LinkNodes(graph, previousNode, nearbyExistingNode);
                    MarkGraphDirty(territoryId);
                }
            }

            LastRecordedPosition = nearbyExistingNode.Position;
            LastRecordedNodeId = nearbyExistingNode.Id;
            return;
        }

        var traversalMode = IsPlayerFlying()
            ? NavTraversalMode.Flight
            : NavTraversalMode.Ground;

        var newNode = new NavNode
        {
            Id = CreateNodeId(territoryId),
            Position = position,
            Links = new List<string>(),
            LinkConfidence = new Dictionary<string, int>(),
            TraversalMode = traversalMode
        };

        graph.AddNode(newNode);

        if (LastRecordedNodeId != null)
        {
            var previousNode = graph.GetNode(LastRecordedNodeId);

            if (previousNode != null)
                LinkNodes(graph, previousNode, newNode);
        }

        if (LastRecordedNodeId == null)
        {
            var bridgeNode = GetNearestNodeExcluding(
                graph,
                position,
                SessionAttachDistance,
                newNode.Id
            );

            if (bridgeNode != null)
                LinkNodes(graph, bridgeNode, newNode);
        }

        LastRecordedPosition = position;
        LastRecordedNodeId = newNode.Id;

        MarkGraphDirty(territoryId);
    }

    private static bool IsPlayerFlying()
    {
        return Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InFlight];
    }

    private static void ObserveTerritoryTransition(uint territoryId, Vector3 position)
    {
        const float minMovementDistance = 0.05f;
        const double recentMovementSeconds = 6.0;
        const double recentCastSeconds = 20.0;

        DateTime now = DateTime.UtcNow;
        bool isFlying = IsPlayerFlying();

        if (Plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Casting])
            LastCastObservedAt = now;

        if (LastTransitionSample != null)
        {
            float sampleDistance = Vector3.Distance(LastTransitionSample.Position, position);
            bool sameTerritory = LastTransitionSample.TerritoryId == territoryId;

            bool ordinaryGroundMovement =
                sameTerritory &&
                !LastTransitionSample.IsFlying &&
                !isFlying &&
                sampleDistance >= minMovementDistance &&
                sampleDistance <= TeleportResetDistance;

            if (ordinaryGroundMovement)
            {
                LastGroundMovementObservedAt = now;

                LastGroundMovementSample = new TransitionSample
                {
                    TerritoryId = territoryId,
                    Position = position,
                    ObservedAt = now,
                    IsFlying = isFlying
                };
            }

            if (!sameTerritory)
            {
                bool hadRecentGroundMovement =
                    LastGroundMovementSample != null &&
                    LastGroundMovementSample.TerritoryId == LastTransitionSample.TerritoryId &&
                    (now - LastGroundMovementObservedAt).TotalSeconds <= recentMovementSeconds;

                bool hadRecentCast =
                    (now - LastCastObservedAt).TotalSeconds <= recentCastSeconds;

                TransitionSample sourceSample = hadRecentGroundMovement && LastGroundMovementSample != null
                    ? LastGroundMovementSample
                    : LastTransitionSample;

                float sourceToEntryDistance = Vector3.Distance(sourceSample.Position, position);

                // Keep this conservative. A large coordinate discontinuity with no recent walking
                // is probably teleport/return/duty/ferry-style travel, not a physical zone line.
                bool likelyPhysicalZoneLine =
                    hadRecentGroundMovement ||
                    sourceToEntryDistance <= TeleportResetDistance;

                bool canLearnTransition =
                    likelyPhysicalZoneLine &&
                    !hadRecentCast &&
                    !sourceSample.IsFlying &&
                    !isFlying;

                Plugin.Log.Information(
                    "[AetherTrail Transition Debug] Territory changed " +
                    $"{sourceSample.TerritoryId} -> {territoryId}. " +
                    $"canLearn={canLearnTransition}, " +
                    $"recentMove={hadRecentGroundMovement}, " +
                    $"recentCast={hadRecentCast}, " +
                    $"wasFlying={sourceSample.IsFlying}, " +
                    $"isFlying={isFlying}, " +
                    $"source=({sourceSample.Position.X:F2}, {sourceSample.Position.Y:F2}, {sourceSample.Position.Z:F2}), " +
                    $"target=({position.X:F2}, {position.Y:F2}, {position.Z:F2}), " +
                    $"sourceToEntryDistance={sourceToEntryDistance:F2}"
                );

                if (canLearnTransition)
                {
                    LearnTerritoryTransition(
                        sourceSample.TerritoryId,
                        sourceSample.Position,
                        territoryId,
                        position);
                }
            }
        }

        LastTransitionSample = new TransitionSample
        {
            TerritoryId = territoryId,
            Position = position,
            ObservedAt = now,
            IsFlying = isFlying
        };
    }
}
