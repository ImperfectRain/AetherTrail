using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{
    private static readonly HashSet<uint> DirtyGraphs = new();

    private static DateTime LastFlushTime = DateTime.UtcNow;

    private const double SaveFlushIntervalSeconds = 10.0;

    private const float LinkIntersectionEndpointTolerance = 1.5f;
    private const float LinkIntersectionMaxVerticalDelta = 2.5f;
    private const float LinkIntersectionNodeMergeDistance = 1.25f;

    private static readonly Dictionary<uint, NavGraph> Graphs = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        IncludeFields = true
    };

    private static float NodeSpacing => Plugin.Instance.Configuration.NodeSpacing;
    private static float CornerNodeSpacing => Plugin.Instance.Configuration.CornerNodeSpacing;
    private static float DirectionChangeThreshold => Plugin.Instance.Configuration.DirectionChangeThreshold;
    private static float TeleportResetDistance => Plugin.Instance.Configuration.TeleportResetDistance;
    private static float SessionAttachDistance => Plugin.Instance.Configuration.SessionAttachDistance;


    private static Vector3? LastRecordedPosition;
    private static Vector3? LastMovementDirection;
    private static string? LastRecordedNodeId;
    private static uint LastTerritoryId;

    private static TerritoryTransitionStore? Transitions;
    private static bool TransitionsDirty;

    private static TransitionSample? LastTransitionSample;
    private static TransitionSample? LastGroundMovementSample;
    private static DateTime LastGroundMovementObservedAt = DateTime.MinValue;
    private static DateTime LastCastObservedAt = DateTime.MinValue;

    private sealed class TransitionSample
    {
        public uint TerritoryId { get; init; }
        public Vector3 Position { get; init; }
        public DateTime ObservedAt { get; init; }
        public bool IsFlying { get; init; }
    }

}
