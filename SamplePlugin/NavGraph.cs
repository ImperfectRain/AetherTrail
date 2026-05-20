using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherTrail;

public sealed class NavGraph
{
    private const float CellSize = 32f;

    private readonly Dictionary<string, NavNode> nodeById = new();
    private readonly Dictionary<(int X, int Z), List<NavNode>> nodesByCell = new();

    private bool cacheDirty = true;

    public List<NavNode> Nodes { get; set; } = new();

    public NavNode? GetNearestNode(Vector3 position, float maxDistance = 80f)
    {
        EnsureCache();

        NavNode? best = null;
        float bestDistanceSq = maxDistance * maxDistance;

        int cellRadius = Math.Max(1, (int)MathF.Ceiling(maxDistance / CellSize));
        var centerCell = GetCell(position);

        for (int x = centerCell.X - cellRadius; x <= centerCell.X + cellRadius; x++)
        {
            for (int z = centerCell.Z - cellRadius; z <= centerCell.Z + cellRadius; z++)
            {
                if (!nodesByCell.TryGetValue((x, z), out var nodes))
                    continue;

                foreach (var node in nodes)
                {
                    float distanceSq = Vector3.DistanceSquared(position, node.Position);

                    if (distanceSq < bestDistanceSq)
                    {
                        bestDistanceSq = distanceSq;
                        best = node;
                    }
                }
            }
        }

        return best;
    }

    public NavNode? GetNode(string id)
    {
        EnsureCache();

        return nodeById.TryGetValue(id, out var node)
            ? node
            : null;
    }

    public void AddNode(NavNode node)
    {
        Nodes.Add(node);
        cacheDirty = true;
    }

    public bool RemoveNode(NavNode node)
    {
        bool removed = Nodes.Remove(node);

        if (removed)
            cacheDirty = true;

        return removed;
    }

    public void MarkDirty()
    {
        cacheDirty = true;
    }

    public bool HasNearbyNode(Vector3 position, float distance)
    {
        return GetNearestNode(position, distance) != null;
    }

    private void EnsureCache()
    {
        if (!cacheDirty)
            return;

        nodeById.Clear();
        nodesByCell.Clear();

        foreach (var node in Nodes)
        {
            nodeById[node.Id] = node;

            var cell = GetCell(node.Position);

            if (!nodesByCell.TryGetValue(cell, out var cellNodes))
            {
                cellNodes = new List<NavNode>();
                nodesByCell[cell] = cellNodes;
            }

            cellNodes.Add(node);
        }

        cacheDirty = false;
    }

    private static (int X, int Z) GetCell(Vector3 position)
    {
        return (
            (int)MathF.Floor(position.X / CellSize),
            (int)MathF.Floor(position.Z / CellSize)
        );
    }
}
