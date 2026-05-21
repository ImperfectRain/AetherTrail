using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace AetherTrail;

public sealed class PartySyncService : IDisposable
{
    private DateTime lastGraphSync = DateTime.MinValue;
    private DateTime lastPresenceSync = DateTime.MinValue;

    private Vector3? lastPresencePosition;
    private float? lastPresenceRotationRadians;
    private uint? lastPresenceTerritoryId;

    private int graphSyncInProgress;
    private int presenceSyncInProgress;
    private volatile bool disposed;

    private static string GetOrCreateSenderId()
    {
        var config = Plugin.Instance.Configuration;

        if (!string.IsNullOrWhiteSpace(config.SyncClientId))
            return config.SyncClientId;

        config.SyncClientId = Guid.NewGuid().ToString("N");
        config.Save();

        return config.SyncClientId;
    }

    public void Dispose()
    {
        this.disposed = true;
    }

    public void Update()
    {
        if (this.disposed)
            return;

        var config = Plugin.Instance.Configuration;

        if (!config.PartySyncEnabled)
            return;

        if (!config.AutoSyncEnabled)
            return;

        if (string.IsNullOrWhiteSpace(config.SyncRoomCode))
            return;

        uint territoryId = Plugin.ClientState.TerritoryType;
        DateTime now = DateTime.UtcNow;

        PartySyncPresence? localPresence = CreateLocalPresenceSnapshot(territoryId);

        if (Volatile.Read(ref this.presenceSyncInProgress) == 0 &&
            ShouldSyncPresence(localPresence, territoryId, now, config))
        {
            this.lastPresenceSync = now;

            if (localPresence != null)
                RememberPresenceSnapshot(localPresence, territoryId);

            _ = SyncPresenceCurrentTerritory(localPresence, territoryId);
        }

        if (ShouldSyncGraph(now, config))
        {
            this.lastGraphSync = now;

            _ = SyncCurrentTerritory();
        }
    }

    public async Task SyncCurrentTerritory()
    {
        if (this.disposed)
            return;

        if (Interlocked.Exchange(ref this.graphSyncInProgress, 1) == 1)
            return;

        try
        {
            var config = Plugin.Instance.Configuration;
            uint territoryId = Plugin.ClientState.TerritoryType;

            var localGraphBefore = NavigationManager.GetGraph(territoryId);
            var packet = NavigationManager.CreateSyncPacket(territoryId);

            bool uploaded = await GraphSyncHttpClient.UploadAsync(packet);

            if (this.disposed)
                return;

            if (!uploaded)
            {
                Plugin.ChatGui.Print("[AetherTrail Sync] Upload failed.");
                return;
            }

            var downloaded = await GraphSyncHttpClient.DownloadAsync(territoryId);

            if (this.disposed)
                return;

            if (downloaded == null)
            {
                Plugin.ChatGui.Print("[AetherTrail Sync] Download failed or no graph exists.");
                return;
            }

            if (downloaded.TerritoryId != territoryId)
            {
                Plugin.ChatGui.Print("[AetherTrail Sync] Territory mismatch. Import cancelled.");
                return;
            }

            if (this.disposed)
                return;

            GraphMutationQueue.Enqueue(() =>
            {
                if (this.disposed)
                    return;

                int importedNodes = NavigationManager.ImportSyncPacket(downloaded);

                var localGraphAfter = NavigationManager.GetGraph(territoryId);

                Plugin.Log.Information(
                    $"AetherTrail graph sync complete for territory {territoryId}: " +
                    $"{localGraphBefore.Nodes.Count} -> {localGraphAfter.Nodes.Count} nodes, " +
                    $"{importedNodes} imported."
                );
            });
        }
        catch (Exception ex)
        {
            if (this.disposed)
                return;

            Plugin.Log.Error(ex, "AetherTrail graph sync failed.");
            Plugin.ChatGui.Print($"[AetherTrail Sync] Graph sync failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref this.graphSyncInProgress, 0);
        }
    }

    public async Task SyncPresenceNow()
    {
        if (this.disposed)
            return;

        uint territoryId = Plugin.ClientState.TerritoryType;
        PartySyncPresence? localPresence = CreateLocalPresenceSnapshot(territoryId);

        if (localPresence != null)
            RememberPresenceSnapshot(localPresence, territoryId);

        this.lastPresenceSync = DateTime.UtcNow;

        await SyncPresenceCurrentTerritory(localPresence, territoryId);
    }

    private static PartySyncPresence? CreateLocalPresenceSnapshot(uint territoryId)
    {
        var player = Plugin.ObjectTable.LocalPlayer;

        if (player == null)
            return null;

        string senderId = GetOrCreateSenderId();

        return new PartySyncPresence
        {
            SenderId = senderId,
            DisplayName = CreatePresenceDisplayName(senderId),
            TerritoryId = territoryId,
            Position = SyncVector3.FromVector3(player.Position),
            RotationRadians = player.Rotation,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static string CreatePresenceDisplayName(string senderId)
    {
        if (senderId.Length < 6)
            return "Synced Player";

        return $"Synced Player {senderId[..6]}";
    }

    private async Task SyncPresenceCurrentTerritory(
        PartySyncPresence? localPresence,
        uint territoryId)
    {
        if (this.disposed)
            return;

        if (localPresence == null)
            return;

        if (Interlocked.Exchange(ref this.presenceSyncInProgress, 1) == 1)
            return;

        try
        {
            var presences = await GraphSyncHttpClient.SyncPresenceAsync(localPresence);

            if (this.disposed)
                return;

            string selfId = Plugin.Instance.Configuration.SyncClientId;

            PartyPresenceService.UpdatePresences(
                presences.Where(presence => presence.SenderId != selfId)
            );
        }
        catch (Exception ex)
        {
            if (this.disposed)
                return;

            Plugin.Log.Error(ex, "AetherTrail presence sync failed.");
            Plugin.ChatGui.Print($"[AetherTrail Sync] Presence sync failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref this.presenceSyncInProgress, 0);
        }
    }

    private bool ShouldSyncGraph(DateTime now, Configuration config)
    {
        int intervalSeconds = Math.Clamp(config.GraphSyncIntervalSeconds, 30, 600);

        return (now - this.lastGraphSync).TotalSeconds >= intervalSeconds;
    }

    private bool ShouldSyncPresence(
        PartySyncPresence? localPresence,
        uint territoryId,
        DateTime now,
        Configuration config)
    {
        if (localPresence == null)
            return false;

        if (this.lastPresenceTerritoryId != territoryId)
            return true;

        Vector3 currentPosition = localPresence.Position.ToVector3();
        bool moved = !this.lastPresencePosition.HasValue ||
                     Vector3.Distance(
                         this.lastPresencePosition.Value,
                         currentPosition
                     ) >= Math.Clamp(config.PresenceMovementThreshold, 0.25f, 10.0f);

        bool rotated = !this.lastPresenceRotationRadians.HasValue ||
                       MathF.Abs(NormalizeRadiansDelta(
                           localPresence.RotationRadians - this.lastPresenceRotationRadians.Value
                       )) >= Math.Clamp(config.PresenceRotationThresholdRadians, 0.05f, MathF.PI);

        bool activelyChanged = moved || rotated;

        int intervalSeconds = activelyChanged
            ? Math.Clamp(config.PresenceMovingSyncIntervalSeconds, 2, 60)
            : Math.Clamp(config.PresenceIdleSyncIntervalSeconds, 5, 40);

        return (now - this.lastPresenceSync).TotalSeconds >= intervalSeconds;
    }

    private void RememberPresenceSnapshot(
        PartySyncPresence localPresence,
        uint territoryId)
    {
        this.lastPresencePosition = localPresence.Position.ToVector3();
        this.lastPresenceRotationRadians = localPresence.RotationRadians;
        this.lastPresenceTerritoryId = territoryId;
    }

    private static float NormalizeRadiansDelta(float radians)
    {
        while (radians > MathF.PI)
            radians -= MathF.Tau;

        while (radians < -MathF.PI)
            radians += MathF.Tau;

        return radians;
    }

    public static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        Random random = new();
        char[] code = new char[6];

        for (int i = 0; i < code.Length; i++)
            code[i] = chars[random.Next(chars.Length)];

        return new string(code);
    }
}
