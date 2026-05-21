using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace AetherTrail;

public sealed class PartySyncService
{
    private DateTime lastGraphSync = DateTime.MinValue;
    private DateTime lastPresenceSync = DateTime.MinValue;

    private Vector3? lastPresencePosition;
    private float? lastPresenceRotationRadians;
    private uint? lastPresenceTerritoryId;

    private int graphSyncInProgress;
    private int presenceSyncInProgress;

    private static string GetOrCreateSenderId()
    {
        var config = Plugin.Instance.Configuration;

        if (!string.IsNullOrWhiteSpace(config.SyncClientId))
            return config.SyncClientId;

        config.SyncClientId = Guid.NewGuid().ToString("N");
        config.Save();

        return config.SyncClientId;
    }

    public async void Update()
    {
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

            Plugin.ChatGui.Print(
                $"AetherTrail graph sync tick: Room={config.SyncRoomCode}, Server={config.SyncServerUrl}"
            );

            await SyncCurrentTerritory();
        }
    }

    public async Task SyncCurrentTerritory()
    {
        if (Interlocked.Exchange(ref this.graphSyncInProgress, 1) == 1)
            return;

        try
        {
            var config = Plugin.Instance.Configuration;
            uint territoryId = Plugin.ClientState.TerritoryType;
            string roomCode = config.SyncRoomCode;

            var localGraphBefore = NavigationManager.GetGraph(territoryId);

            Plugin.ChatGui.Print($"[AetherTrail Sync] Room: {roomCode}");
            Plugin.ChatGui.Print($"[AetherTrail Sync] Upload territory: {territoryId}");
            Plugin.ChatGui.Print($"[AetherTrail Sync] Local nodes before upload: {localGraphBefore.Nodes.Count}");

            var packet = NavigationManager.CreateSyncPacket(territoryId);

            Plugin.ChatGui.Print($"[AetherTrail Sync] Packet nodes uploading: {packet.Graph.Nodes.Count}");

            bool uploaded = await GraphSyncHttpClient.UploadAsync(packet);

            Plugin.ChatGui.Print(uploaded
                ? "[AetherTrail Sync] Upload successful."
                : "[AetherTrail Sync] Upload failed.");

            if (!uploaded)
                return;

            Plugin.ChatGui.Print($"[AetherTrail Sync] Download territory: {territoryId}");

            var downloaded = await GraphSyncHttpClient.DownloadAsync(territoryId);

            if (downloaded == null)
            {
                Plugin.ChatGui.Print("[AetherTrail Sync] Download failed or no graph exists.");
                return;
            }

            Plugin.ChatGui.Print($"[AetherTrail Sync] Downloaded packet territory: {downloaded.TerritoryId}");
            Plugin.ChatGui.Print($"[AetherTrail Sync] Downloaded packet nodes: {downloaded.Graph.Nodes.Count}");

            if (downloaded.TerritoryId != territoryId)
            {
                Plugin.ChatGui.Print("[AetherTrail Sync] Territory mismatch. Import cancelled.");
                return;
            }

            GraphMutationQueue.Enqueue(() =>
            {
                int importedNodes = NavigationManager.ImportSyncPacket(downloaded);

                var localGraphAfter = NavigationManager.GetGraph(territoryId);

                Plugin.ChatGui.Print($"[AetherTrail Sync] Imported new nodes: {importedNodes}");
                Plugin.ChatGui.Print($"[AetherTrail Sync] Local nodes after import: {localGraphAfter.Nodes.Count}");
            });
        }
        catch (Exception ex)
        {
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
            DisplayName = player.Name.TextValue,
            TerritoryId = territoryId,
            Position = SyncVector3.FromVector3(player.Position),
            RotationRadians = player.Rotation,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private async Task SyncPresenceCurrentTerritory(
        PartySyncPresence? localPresence,
        uint territoryId)
    {
        if (localPresence == null)
            return;

        if (Interlocked.Exchange(ref this.presenceSyncInProgress, 1) == 1)
            return;

        try
        {
            Plugin.Log.Information(
                $"[AetherTrail Presence Debug] SyncPresence room={Plugin.Instance.Configuration.SyncRoomCode} territory={territoryId}"
            );
            var presences = await GraphSyncHttpClient.SyncPresenceAsync(localPresence);

            string selfId = Plugin.Instance.Configuration.SyncClientId;

            PartyPresenceService.UpdatePresences(
                presences.Where(presence => presence.SenderId != selfId)
            );
        }
        catch (Exception ex)
        {
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
