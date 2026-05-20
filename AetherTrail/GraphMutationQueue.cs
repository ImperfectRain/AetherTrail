using System;
using System.Collections.Generic;

namespace AetherTrail;

public static class GraphMutationQueue
{
    private static readonly Queue<Action> Pending = new();
    private static readonly object Lock = new();

    public static void Enqueue(Action action)
    {
        lock (Lock)
            Pending.Enqueue(action);
    }

    public static void Process()
    {
        while (true)
        {
            Action action;

            lock (Lock)
            {
                if (Pending.Count == 0)
                    return;

                action = Pending.Dequeue();
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "AetherTrail graph mutation failed.");
            }
        }
    }

    public static void Clear()
    {
        lock (Lock)
            Pending.Clear();
    }
}
