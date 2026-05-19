using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace AetherTrail;

public sealed class NavGraph
{
    public List<NavNode> Nodes { get; set; } = new();

    public NavNode? GetNearestNode(Vector3 position, float maxDistance = 80f)
    {
        NavNode? best = null;
        float bestDistanceSq = maxDistance * maxDistance;

        foreach (var node in Nodes)
        {
            float distanceSq = Vector3.DistanceSquared(position, node.Position);

            if (distanceSq < bestDistanceSq)
            {
                bestDistanceSq = distanceSq;
                best = node;
            }
        }

        return best;
    }

    public NavNode? GetNode(string id)
    {
        return Nodes.FirstOrDefault(n => n.Id == id);
    }

    public void AddNode(NavNode node)
    {
        Nodes.Add(node);
    }

    public bool HasNearbyNode(Vector3 position, float distance)
    {
        float distanceSq = distance * distance;
        return Nodes.Any(n => Vector3.DistanceSquared(n.Position, position) <= distanceSq);
    }
}
