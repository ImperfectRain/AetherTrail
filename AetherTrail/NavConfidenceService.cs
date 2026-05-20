using System.Linq;

namespace AetherTrail;

public static class NavConfidenceService
{
    public static int ResetGraphConfidence(NavGraph graph)
    {
        int updatedLinks = 0;

        foreach (var node in graph.Nodes)
        {
            foreach (string linkId in node.Links.ToList())
            {
                node.LinkConfidence[linkId] = NavConfidence.ImportedConfidence();
                updatedLinks++;
            }

            NavConfidence.NormalizeNodeConfidence(node);
        }

        graph.MarkDirty();

        return updatedLinks;
    }

    public static int NormalizeGraphConfidence(NavGraph graph)
    {
        int touchedNodes = 0;

        foreach (var node in graph.Nodes)
        {
            NavConfidence.NormalizeNodeConfidence(node);
            touchedNodes++;
        }

        graph.MarkDirty();

        return touchedNodes;
    }

    public static int DecayGraphConfidence(NavGraph graph)
    {
        int removedLinks = 0;

        foreach (var node in graph.Nodes)
        {
            removedLinks += NavConfidence.DecayNode(node);
        }

        graph.Nodes.RemoveAll(node => node.Links.Count == 0);
        graph.MarkDirty();

        return removedLinks;
    }
}
