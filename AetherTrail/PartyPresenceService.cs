using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherTrail;

public static class PartyPresenceService
{
    private static readonly object SyncRoot = new();

    private static readonly Dictionary<string, PartySyncPresence> Presences = new();

    private static readonly TimeSpan Expiration = TimeSpan.FromSeconds(45);

    public static void UpdatePresences(IEnumerable<PartySyncPresence> presences)
    {
        lock (SyncRoot)
        {
            foreach (var presence in presences)
            {
                if (string.IsNullOrWhiteSpace(presence.SenderId))
                    continue;

                Presences[presence.SenderId] = presence;
            }

            RemoveExpiredLocked();
        }
    }

    public static List<PartySyncPresence> GetCurrentTerritorySnapshot(uint territoryId)
    {
        lock (SyncRoot)
        {
            RemoveExpiredLocked();

            return Presences.Values
                .Where(presence => presence.TerritoryId == territoryId)
                .ToList();
        }
    }

    public static void Clear()
    {
        lock (SyncRoot)
        {
            Presences.Clear();
        }
    }

    private static void RemoveExpiredLocked()
    {
        DateTime cutoff = DateTime.UtcNow - Expiration;

        foreach (string senderId in Presences
            .Where(pair => pair.Value.UpdatedAtUtc < cutoff)
            .Select(pair => pair.Key)
            .ToList())
        {
            Presences.Remove(senderId);
        }
    }


}
