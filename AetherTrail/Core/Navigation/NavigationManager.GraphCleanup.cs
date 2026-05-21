using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace AetherTrail;

public static partial class NavigationManager
{

    public static int RemoveRedundantLinks(uint territoryId)
    {
        var graph = GetOrLoadGraph(territoryId);

        int removed = 0;

        foreach (var node in graph.Nodes.ToList())
        {
            foreach (string linkId in node.Links.ToList())
            {
                var linked = graph.GetNode(linkId);

                if (linked == null)
                    continue;

                // Only process each undirected link once.
                if (string.CompareOrdinal(node.Id, linked.Id) > 0)
                    continue;

                if (node.TraversalMode != linked.TraversalMode)
                    continue;

                if (node.TraversalMode == NavTraversalMode.Flight)
                    continue;

                float directDistance = Vector3.Distance(node.Position, linked.Position);

                // Short links are usually fine.
                if (directDistance < 8.0f)
                    continue;

                if (HasAlternativeRoute(graph, node, linked, directDistance * 1.40f))
                {
                    RemoveLink(node, linked);
                    removed++;
                }
            }
        }

        if (removed > 0)
        {
            graph.MarkDirty();
            MarkGraphDirty(territoryId);
            SaveGraph(territoryId, graph);
        }

        return removed;
    }

    private static bool HasAlternativeRoute(
    NavGraph graph,
    NavNode start,
    NavNode goal,
    float maxRouteDistance)
    {
        PriorityQueue<NavNode, float> open = new();
        Dictionary<string, float> distanceById = new();

        open.Enqueue(start, 0f);
        distanceById[start.Id] = 0f;

        while (open.Count > 0)
        {
            var current = open.Dequeue();

            if (current.Id == goal.Id)
                return true;

            float currentDistance = distanceById[current.Id];

            if (currentDistance > maxRouteDistance)
                continue;

            foreach (string linkId in current.Links)
            {
                if ((current.Id == start.Id && linkId == goal.Id) ||
                    (current.Id == goal.Id && linkId == start.Id))
                {
                    continue;
                }

                var neighbor = graph.GetNode(linkId);

                if (neighbor == null)
                    continue;

                if (neighbor.TraversalMode != start.TraversalMode)
                    continue;

                float edgeDistance = Vector3.Distance(current.Position, neighbor.Position);
                float nextDistance = currentDistance + edgeDistance;

                if (nextDistance > maxRouteDistance)
                    continue;

                if (distanceById.TryGetValue(neighbor.Id, out float knownDistance) &&
                    knownDistance <= nextDistance)
                {
                    continue;
                }

                distanceById[neighbor.Id] = nextDistance;
                open.Enqueue(neighbor, nextDistance);
            }
        }

        return false;
    }

    public static int SplitCrossingLinks(uint territoryId)
    {
        var graph = GetOrLoadGraph(territoryId);

        int splitCount = 0;
        bool changed;

        const int maxPasses = 500;

        int passes = 0;

        do
        {
            changed = false;
            passes++;

            foreach (var a in graph.Nodes.ToList())
            {
                if (a.TraversalMode == NavTraversalMode.Flight)
                    continue;

                foreach (string linkId in a.Links.ToList())
                {
                    var b = graph.GetNode(linkId);

                    if (b == null)
                        continue;

                    if (b.TraversalMode == NavTraversalMode.Flight)
                        continue;

                    // Only process each undirected edge once.
                    if (string.CompareOrdinal(a.Id, b.Id) > 0)
                        continue;

                    if (TrySplitCrossingLinksForNewLink(graph, a, b, out _))
                    {
                        splitCount++;
                        changed = true;
                        break;
                    }
                }

                if (changed)
                    break;
            }
        }
        while (changed && passes < maxPasses);

        if (splitCount > 0)
        {
            graph.MarkDirty();
            MarkGraphDirty(territoryId);
            SaveGraph(territoryId, graph);
        }

        return splitCount;
    }

    public static GraphPruneResult PruneGraph(uint territoryId)
    {
        var graph = GetOrLoadGraph(territoryId);

        graph.Nodes.RemoveAll(node => !IsValidNodePosition(node.Position));
        graph.MarkDirty();

        int originalNodeCount = graph.Nodes.Count;
        int removedBrokenLinks = 0;
        int removedSelfLinks = 0;
        int removedDuplicateLinks = 0;

        HashSet<string> validIds = graph.Nodes
            .Select(n => n.Id)
            .ToHashSet();

        foreach (var node in graph.Nodes)
        {
            int before = node.Links.Count;

            node.Links.RemoveAll(linkId => !validIds.Contains(linkId));
            removedBrokenLinks += before - node.Links.Count;

            before = node.Links.Count;

            node.Links.RemoveAll(linkId => linkId == node.Id);
            removedSelfLinks += before - node.Links.Count;

            before = node.Links.Count;

            node.Links.RemoveAll(linkId =>
            {
                var linkedNode = graph.GetNode(linkId);

                if (linkedNode == null)
                    return true;

                return !IsTraversableLink(node.Position, linkedNode.Position);
            });

            removedBrokenLinks += before - node.Links.Count;

            before = node.Links.Count;

            var distinctLinks = node.Links.Distinct().ToList();
            node.Links.Clear();
            node.Links.AddRange(distinctLinks);

            removedDuplicateLinks += before - node.Links.Count;


        }

        foreach (var node in graph.Nodes)
        {
            NavConfidence.NormalizeNodeConfidence(node);
        }

        graph.Nodes.RemoveAll(node => node.Links.Count == 0);
        graph.MarkDirty();

        int removedIsolatedNodes = originalNodeCount - graph.Nodes.Count;

        int mergedDuplicateNodes = MergeDuplicateNodes(graph);

        MarkGraphDirty(territoryId);

        return new GraphPruneResult
        {
            RemovedIsolatedNodes = removedIsolatedNodes,
            RemovedBrokenLinks = removedBrokenLinks,
            RemovedSelfLinks = removedSelfLinks,
            RemovedDuplicateLinks = removedDuplicateLinks,
            MergedDuplicateNodes = mergedDuplicateNodes,
            RemainingNodes = graph.Nodes.Count,
            RemainingLinks = graph.Nodes.Sum(n => n.Links.Count)
        };
    }

    public static GraphPruneResult CleanFlightNodes(uint territoryId)
    {
        var graph = GetOrLoadGraph(territoryId);

        int originalNodeCount = graph.Nodes.Count;

        graph.Nodes.RemoveAll(node => node.TraversalMode == NavTraversalMode.Flight);
        graph.MarkDirty();

        var validIds = graph.Nodes
            .Select(node => node.Id)
            .ToHashSet();

        foreach (var node in graph.Nodes)
        {
            node.Links.RemoveAll(linkId => !validIds.Contains(linkId));

            foreach (var key in node.LinkConfidence.Keys.ToList())
            {
                if (!validIds.Contains(key))
                    node.LinkConfidence.Remove(key);
            }
        }

        int removedFlightNodes = originalNodeCount - graph.Nodes.Count;

        MarkGraphDirty(territoryId);

        var result = PruneGraph(territoryId);

        return new GraphPruneResult
        {
            RemovedFlightNodes = removedFlightNodes,
            RemovedIsolatedNodes = result.RemovedIsolatedNodes,
            RemovedBrokenLinks = result.RemovedBrokenLinks,
            RemovedSelfLinks = result.RemovedSelfLinks,
            RemovedDuplicateLinks = result.RemovedDuplicateLinks,
            MergedDuplicateNodes = result.MergedDuplicateNodes,
            RemainingNodes = result.RemainingNodes,
            RemainingLinks = result.RemainingLinks
        };
    }

    public static int ResetCurrentTerritoryConfidence(uint territoryId)
    {
        var graph = GetOrLoadGraph(territoryId);

        int updatedLinks = NavConfidenceService.ResetGraphConfidence(graph);

        MarkGraphDirty(territoryId);
        SaveGraph(territoryId, graph);

        return updatedLinks;
    }

    private static int MergeDuplicateNodes(NavGraph graph)
    {
        const float mergeDistance = 2.0f;
        int mergedCount = 0;

        bool mergedSomething;

        do
        {
            mergedSomething = false;

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                var a = graph.Nodes[i];

                for (int j = i + 1; j < graph.Nodes.Count; j++)
                {
                    var b = graph.Nodes[j];

                    if (Vector3.Distance(a.Position, b.Position) > mergeDistance)
                        continue;

                    MergeNodeInto(graph, source: b, target: a);
                    graph.Nodes.RemoveAt(j);
                    graph.MarkDirty();

                    mergedCount++;
                    mergedSomething = true;
                    break;
                }

                if (mergedSomething)
                    break;
            }
        }
        while (mergedSomething);

        foreach (var node in graph.Nodes)
        {
            node.Links.RemoveAll(linkId => linkId == node.Id);

            var distinctLinks = node.Links.Distinct().ToList();
            node.Links.Clear();
            node.Links.AddRange(distinctLinks);
        }

        return mergedCount;
    }

    private static void MergeNodeInto(NavGraph graph, NavNode source, NavNode target)
    {
        foreach (string linkId in source.Links)
        {
            if (linkId == target.Id)
                continue;

            var linkedNode = graph.GetNode(linkId);

            if (linkedNode == null)
                continue;

            int mergedConfidence = GetLinkConfidence(source, linkedNode);

            linkedNode.Links.RemoveAll(id => id == source.Id);

            if (linkedNode.Id != target.Id && !linkedNode.Links.Contains(target.Id))
                linkedNode.Links.Add(target.Id);

            if (!target.Links.Contains(linkedNode.Id))
                target.Links.Add(linkedNode.Id);

            linkedNode.LinkConfidence.Remove(source.Id);
            NavConfidence.MergeLinkConfidence(target, linkedNode, mergedConfidence);
        }

        foreach (var node in graph.Nodes)
        {
            for (int i = 0; i < node.Links.Count; i++)
            {
                if (node.Links[i] == source.Id)
                    node.Links[i] = target.Id;
            }

            if (node.LinkConfidence.TryGetValue(source.Id, out int confidence))
            {
                node.LinkConfidence.Remove(source.Id);

                if (node.Id != target.Id)
                    NavConfidence.MergeLinkConfidence(node, target, confidence);
            }
        }

        foreach (var key in source.LinkConfidence.Keys.ToList())
            source.LinkConfidence.Remove(key);
    }
}
