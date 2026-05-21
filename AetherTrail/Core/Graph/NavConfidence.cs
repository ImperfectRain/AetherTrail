using System;
using System.Linq;

namespace AetherTrail;

public static class NavConfidence
{
    public const int DeleteThreshold = 10;

    public const int Critical = 20;
    public const int Weak = 35;
    public const int Imported = 50;
    public const int NewLocal = 60;
    public const int Trusted = 90;
    public const int Locked = 100;

    public const int GainPerTraversal = 2;
    public const int ImportedGainPerTraversal = 1;
    public const int DecayAmount = 1;

    public static int Clamp(int value)
    {
        return Math.Clamp(value, 0, Locked);
    }

    public static int NewLocalConfidence()
    {
        return NewLocal;
    }

    public static int ImportedConfidence()
    {
        return Imported;
    }

    public static int IncreaseTraversal(int current)
    {
        current = Clamp(current);

        if (current >= Locked)
            return Locked;

        int gain = current < NewLocal
            ? ImportedGainPerTraversal
            : GainPerTraversal;

        return Math.Min(Locked, current + gain);
    }

    public static int Decay(int current)
    {
        current = Clamp(current);

        if (current >= Locked)
            return Locked;

        return Math.Max(0, current - DecayAmount);
    }

    public static bool ShouldDelete(int confidence)
    {
        return confidence <= DeleteThreshold;
    }

    public static bool IsLocked(int confidence)
    {
        return confidence >= Locked;
    }

    public static void IncrementTraversal(NavNode node, string linkedNodeId)
    {
        int current = node.LinkConfidence.TryGetValue(linkedNodeId, out int value)
            ? value
            : NewLocalConfidence();

        node.LinkConfidence[linkedNodeId] = IncreaseTraversal(current);
    }

    public static void MergeLinkConfidence(NavNode a, NavNode b, int incomingConfidence)
    {
        int merged = Clamp(incomingConfidence);

        if (a.LinkConfidence.TryGetValue(b.Id, out int aValue))
            merged = Math.Max(merged, Clamp(aValue));

        if (b.LinkConfidence.TryGetValue(a.Id, out int bValue))
            merged = Math.Max(merged, Clamp(bValue));

        a.LinkConfidence[b.Id] = merged;
        b.LinkConfidence[a.Id] = merged;
    }

    public static void NormalizeNodeConfidence(NavNode node)
    {
        foreach (var key in node.LinkConfidence.Keys.ToList())
        {
            if (!node.Links.Contains(key))
            {
                node.LinkConfidence.Remove(key);
                continue;
            }

            node.LinkConfidence[key] = Clamp(node.LinkConfidence[key]);
        }

        foreach (string linkId in node.Links)
        {
            if (!node.LinkConfidence.ContainsKey(linkId))
                node.LinkConfidence[linkId] = ImportedConfidence();
        }
    }

    public static int GetAverageConfidence(NavNode node)
    {
        if (node.LinkConfidence.Count == 0)
            return ImportedConfidence();

        return Clamp((int)MathF.Round((float)node.LinkConfidence.Values.Average()));
    }

    public static int GetAverageConfidence(NavNodeSnapshot node)
    {
        if (node.LinkConfidence.Count == 0)
            return ImportedConfidence();

        return Clamp((int)MathF.Round((float)node.LinkConfidence.Values.Average()));
    }

    public static int GetMedianConfidence(NavNode node)
    {
        if (node.LinkConfidence.Count == 0)
            return ImportedConfidence();

        var values = node.LinkConfidence.Values
            .Select(Clamp)
            .OrderBy(value => value)
            .ToList();

        int middle = values.Count / 2;

        if (values.Count % 2 == 1)
            return values[middle];

        return (values[middle - 1] + values[middle]) / 2;
    }

    public static int GetMedianConfidence(NavNodeSnapshot node)
    {
        if (node.LinkConfidence.Count == 0)
            return ImportedConfidence();

        var values = node.LinkConfidence.Values
            .Select(Clamp)
            .OrderBy(value => value)
            .ToList();

        int middle = values.Count / 2;

        if (values.Count % 2 == 1)
            return values[middle];

        return (values[middle - 1] + values[middle]) / 2;
    }

    public static int DecayNode(NavNode node)
    {
        int removedLinks = 0;

        foreach (var key in node.LinkConfidence.Keys.ToList())
        {
            int current = node.LinkConfidence[key];

            if (IsLocked(current))
                continue;

            int decayed = Decay(current);

            if (ShouldDelete(decayed))
            {
                node.LinkConfidence.Remove(key);
                node.Links.Remove(key);
                removedLinks++;
                continue;
            }

            node.LinkConfidence[key] = decayed;
        }

        return removedLinks;
    }
}
