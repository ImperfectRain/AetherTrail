using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{

    private static bool TrySplitCrossingLinksForNewLink(
    NavGraph graph,
    NavNode a,
    NavNode b,
    out NavNode? intersectionNode)
    {
        intersectionNode = null;

        if (a.Id == b.Id)
            return false;

        if (a.TraversalMode != b.TraversalMode)
            return false;

        if (a.TraversalMode == NavTraversalMode.Flight ||
            b.TraversalMode == NavTraversalMode.Flight)
        {
            return false;
        }

        Vector2 a2 = new(a.Position.X, a.Position.Z);
        Vector2 b2 = new(b.Position.X, b.Position.Z);

        foreach (var c in graph.Nodes.ToList())
        {
            if (c.TraversalMode != a.TraversalMode)
                continue;

            foreach (string linkedId in c.Links.ToList())
            {
                var d = graph.GetNode(linkedId);

                if (d == null)
                    continue;

                if (d.TraversalMode != a.TraversalMode)
                    continue;

                if (c.Id == a.Id || c.Id == b.Id || d.Id == a.Id || d.Id == b.Id)
                    continue;

                Vector2 c2 = new(c.Position.X, c.Position.Z);
                Vector2 d2 = new(d.Position.X, d.Position.Z);

                if (!TryGetSegmentIntersection(
                        a2,
                        b2,
                        c2,
                        d2,
                        out Vector2 intersection,
                        out float abT,
                        out float cdT))
                {
                    continue;
                }

                if (IsNearEndpoint(intersection, a2, b2, c2, d2, LinkIntersectionEndpointTolerance))
                    continue;

                float yOnAB = Lerp(a.Position.Y, b.Position.Y, abT);
                float yOnCD = Lerp(c.Position.Y, d.Position.Y, cdT);

                if (MathF.Abs(yOnAB - yOnCD) > LinkIntersectionMaxVerticalDelta)
                    continue;

                Vector3 intersectionPosition = new(
                    intersection.X,
                    (yOnAB + yOnCD) * 0.5f,
                    intersection.Y
                );

                intersectionNode = GetOrCreateIntersectionNode(
                    graph,
                    intersectionPosition,
                    a.TraversalMode
                );

                int abConfidence = GetLinkConfidence(a, b);
                int cdConfidence = GetLinkConfidence(c, d);

                RemoveLink(a, b);
                RemoveLink(c, d);

                LinkNodesWithoutIntersectionSplit(a, intersectionNode);
                SetLinkConfidence(a, intersectionNode, abConfidence);

                LinkNodesWithoutIntersectionSplit(intersectionNode, b);
                SetLinkConfidence(intersectionNode, b, abConfidence);

                LinkNodesWithoutIntersectionSplit(c, intersectionNode);
                SetLinkConfidence(c, intersectionNode, cdConfidence);

                LinkNodesWithoutIntersectionSplit(intersectionNode, d);
                SetLinkConfidence(intersectionNode, d, cdConfidence);

                graph.MarkDirty();

                return true;
            }
        }

        return false;
    }

    private static string CreateNodeId(uint territoryId)
    {
        return $"{territoryId}_{Guid.NewGuid():N}";
    }

    private static NavNode GetOrCreateIntersectionNode(
        NavGraph graph,
        Vector3 position,
        NavTraversalMode traversalMode)
    {
        foreach (var node in graph.Nodes)
        {
            if (node.TraversalMode != traversalMode)
                continue;

            if (Vector3.Distance(node.Position, position) <= LinkIntersectionNodeMergeDistance)
                return node;
        }

        var newNode = new NavNode
        {
            Id = CreateNodeId(Plugin.ClientState.TerritoryType),
            Position = position,
            TraversalMode = traversalMode,
            Links = new List<string>(),
            LinkConfidence = new Dictionary<string, int>()
        };

        graph.AddNode(newNode);

        return newNode;
    }

    private static bool TryGetSegmentIntersection(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        out Vector2 intersection,
        out float abT,
        out float cdT)
    {
        intersection = Vector2.Zero;
        abT = 0f;
        cdT = 0f;

        Vector2 r = b - a;
        Vector2 s = d - c;

        float denominator = Cross(r, s);

        if (MathF.Abs(denominator) < 0.0001f)
            return false;

        Vector2 cMinusA = c - a;

        abT = Cross(cMinusA, s) / denominator;
        cdT = Cross(cMinusA, r) / denominator;

        if (abT <= 0f || abT >= 1f || cdT <= 0f || cdT >= 1f)
            return false;

        intersection = a + abT * r;
        return true;
    }

    private static bool IsNearEndpoint(
        Vector2 intersection,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        Vector2 d,
        float tolerance)
    {
        return Vector2.Distance(intersection, a) <= tolerance ||
               Vector2.Distance(intersection, b) <= tolerance ||
               Vector2.Distance(intersection, c) <= tolerance ||
               Vector2.Distance(intersection, d) <= tolerance;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.X * b.Y - a.Y * b.X;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    private static int GetLinkConfidence(NavNode a, NavNode b)
    {
        if (a.LinkConfidence.TryGetValue(b.Id, out int aValue))
            return NavConfidence.Clamp(aValue);

        if (b.LinkConfidence.TryGetValue(a.Id, out int bValue))
            return NavConfidence.Clamp(bValue);

        return NavConfidence.ImportedConfidence();
    }

    private static void SetLinkConfidence(NavNode a, NavNode b, int confidence)
    {
        NavConfidence.MergeLinkConfidence(a, b, confidence);
    }

    private static void RemoveLink(NavNode a, NavNode b)
    {
        a.Links.Remove(b.Id);
        b.Links.Remove(a.Id);

        a.LinkConfidence.Remove(b.Id);
        b.LinkConfidence.Remove(a.Id);
    }

    private static void LinkNodesWithoutIntersectionSplit(NavNode a, NavNode b)
    {
        if (a.Id == b.Id)
            return;

        if (a.TraversalMode != b.TraversalMode)
            return;

        if (!a.Links.Contains(b.Id))
            a.Links.Add(b.Id);

        if (!b.Links.Contains(a.Id))
            b.Links.Add(a.Id);

        if (!a.LinkConfidence.ContainsKey(b.Id))
            a.LinkConfidence[b.Id] = NavConfidence.ImportedConfidence();

        if (!b.LinkConfidence.ContainsKey(a.Id))
            b.LinkConfidence[a.Id] = NavConfidence.ImportedConfidence();
    }

    private static void LinkNodes(
        NavGraph graph,
        NavNode a,
        NavNode b,
        bool increaseConfidence = true)
    {
        if (a.Id == b.Id)
            return;

        if (a.TraversalMode != b.TraversalMode)
            return;

        if (!IsTraversableLink(a.Position, b.Position))
            return;

        if (a.Links.Contains(b.Id) && b.Links.Contains(a.Id))
        {
            if (increaseConfidence)
                IncrementLinkConfidence(a, b);

            return;
        }

        if (TrySplitCrossingLinksForNewLink(graph, a, b, out _))
            return;

        LinkNodesWithoutIntersectionSplit(a, b);

        if (increaseConfidence)
            IncrementLinkConfidence(a, b);
    }

    private static void IncrementLinkConfidence(NavNode a, NavNode b)
    {
        if (!NavLinkTraversalTracker.CanIncrease(a.Id, b.Id))
            return;

        NavConfidence.IncrementTraversal(a, b.Id);
        NavConfidence.IncrementTraversal(b, a.Id);
    }

    private static bool IsTraversableLink(Vector3 a, Vector3 b)
    {
        const float maxLinkDistance = 24.0f;
        const float maxVerticalDelta = 7.0f;
        const float maxSlopeRatio = 1.10f;

        Vector3 delta = b - a;

        float totalDistance = delta.Length();

        if (totalDistance > maxLinkDistance)
            return false;

        float verticalDelta = MathF.Abs(delta.Y);

        if (verticalDelta > maxVerticalDelta)
            return false;

        delta.Y = 0f;
        float horizontalDistance = delta.Length();

        if (horizontalDistance < 0.1f)
            return verticalDelta <= 1.5f;

        float slopeRatio = verticalDelta / horizontalDistance;

        if (slopeRatio > maxSlopeRatio)
            return false;

        return true;
    }
}
