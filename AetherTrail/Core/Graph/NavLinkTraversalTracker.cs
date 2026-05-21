using System;
using System.Collections.Generic;

namespace AetherTrail;

public static class NavLinkTraversalTracker
{
    private static readonly Dictionary<string, DateTime> LastTraversal = new();

    private const double CooldownSeconds = 12.0;

    public static bool CanIncrease(string a, string b)
    {
        string key = BuildKey(a, b);

        DateTime now = DateTime.UtcNow;

        if (LastTraversal.TryGetValue(key, out var last))
        {
            if ((now - last).TotalSeconds < CooldownSeconds)
                return false;
        }

        LastTraversal[key] = now;
        return true;
    }

    private static string BuildKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) < 0
            ? $"{a}|{b}"
            : $"{b}|{a}";
    }

    public static void Clear()
    {
        LastTraversal.Clear();
    }
}
