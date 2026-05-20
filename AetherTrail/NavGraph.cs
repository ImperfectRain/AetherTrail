using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherTrail;

public sealed class NavGraph
{
    private const float CellSize = 32f;

    private readonly object syncRoot = new();

    private readonly Dictionary<string, NavNode> nodeById = new();
    private readonly Dictionary<(int X, int Z), List<NavNode>> nodesByCell = new();

    private bool cacheDirty = true;

    public List<NavNode> Nodes { get; set; } = new();

    public NavNode? GetNearestNode(Vector3 position, float maxDistance = 80f)
    {
        lock (this.syncRoot)
        {
            EnsureCacheLocked();

            NavNode? best = null;
            float bestDistanceSq = maxDistance * maxDistance;

            int cellRadius = Math.Max(1, (int)MathF.Ceiling(maxDistance / CellSize));
            var centerCell = GetCell(position);

            for (int x = centerCell.X - cellRadius; x <= centerCell.X + cellRadius; x++)
            {
                for (int z = centerCell.Z - cellRadius; z <= centerCell.Z + cellRadius; z++)
                {
                    if (!this.nodesByCell.TryGetValue((x, z), out var nodes))
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
    }

    public NavNode? GetNode(string id)
    {
        lock (this.syncRoot)
        {
            EnsureCacheLocked();

            return this.nodeById.TryGetValue(id, out var node)
                ? node
                : null;
        }
    }

    public void AddNode(NavNode node)
    {
        lock (this.syncRoot)
        {
            this.Nodes.Add(node);
            this.cacheDirty = true;
        }
    }

    public bool RemoveNode(NavNode node)
    {
        lock (this.syncRoot)
        {
            bool removed = this.Nodes.Remove(node);

            if (removed)
                this.cacheDirty = true;

            return removed;
        }
    }

    public List<NavNodeSnapshot> GetSnapshot()
    {
        lock (this.syncRoot)
        {
            return this.Nodes
                .Select(node => new NavNodeSnapshot
                {
                    Id = node.Id,
                    Position = node.Position,
                    TraversalMode = node.TraversalMode,
                    Links = node.Links.ToList(),
                    LinkConfidence = node.LinkConfidence.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value
                    )
                })
                .ToList();
        }
    }

    public void MarkDirty()
    {
        lock (this.syncRoot)
        {
            this.cacheDirty = true;
        }
    }

    public bool HasNearbyNode(Vector3 position, float distance)
    {
        return GetNearestNode(position, distance) != null;
    }

    private void EnsureCacheLocked()
    {
        if (!this.cacheDirty)
            return;

        this.nodeById.Clear();
        this.nodesByCell.Clear();

        foreach (var node in this.Nodes)
        {
            if (!this.nodeById.ContainsKey(node.Id))
                this.nodeById[node.Id] = node;

            var cell = GetCell(node.Position);

            if (!this.nodesByCell.TryGetValue(cell, out var cellNodes))
            {
                cellNodes = new List<NavNode>();
                this.nodesByCell[cell] = cellNodes;
            }

            cellNodes.Add(node);
        }

        this.cacheDirty = false;
    }

    private static (int X, int Z) GetCell(Vector3 position)
    {
        return (
            (int)MathF.Floor(position.X / CellSize),
            (int)MathF.Floor(position.Z / CellSize)
        );
    }
}
