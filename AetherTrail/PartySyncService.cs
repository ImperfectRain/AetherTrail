using System;
using System.Linq;
using System.Threading.Tasks;

namespace AetherTrail;

public sealed class PartySyncService
{
    private DateTime lastSync = DateTime.MinValue;
    private bool syncing;

    private static string GetOrCreateSenderId()
    {
        var config = Plugin.Instance.Configuration;

        if (!string.IsNullOrWhiteSpace(config.SyncClientId))
            return config.SyncClientId;

        config.SyncClientId = Guid.NewGuid().ToString("N");
        config.Save();

        return config.SyncClientId;
    }

    private static async Task SyncPresence(PartySyncPresence? localPresence, uint territoryId)
    {
        if (localPresence != null)
            await GraphSyncHttpClient.UploadPresenceAsync(localPresence);

        var presences = await GraphSyncHttpClient.DownloadPresenceAsync(territoryId);

        string selfId = Plugin.Instance.Configuration.SyncClientId;

        PartyPresenceService.UpdatePresences(
            presences.Where(presence => presence.SenderId != selfId)
        );
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
    public async void Update()
    {
        var config = Plugin.Instance.Configuration;

        if (!config.PartySyncEnabled)
            return;

        if (!config.AutoSyncEnabled)
            return;

        if (string.IsNullOrWhiteSpace(config.SyncRoomCode))
            return;

        if (this.syncing)
            return;

        if ((DateTime.UtcNow - this.lastSync).TotalSeconds < config.SyncIntervalSeconds)
            return;

        this.lastSync = DateTime.UtcNow;

        Plugin.ChatGui.Print(
            $"AetherTrail sync tick: Room={config.SyncRoomCode}, Server={config.SyncServerUrl}"
        );

        await SyncCurrentTerritory();
    }

    public async Task SyncCurrentTerritory()
    {
        this.syncing = true;

        try
        {
            var config = Plugin.Instance.Configuration;
            uint territoryId = Plugin.ClientState.TerritoryType;
            PartySyncPresence? localPresence = CreateLocalPresenceSnapshot(territoryId);
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

            try
            {
                await SyncPresence(localPresence, territoryId);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "AetherTrail presence sync failed.");
                Plugin.ChatGui.Print($"[AetherTrail Sync] Presence sync failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "AetherTrail party sync failed.");
            Plugin.ChatGui.Print("[AetherTrail Sync] Exception during sync. Check dalamud.log.");
        }
        finally
        {
            this.syncing = false;
        }
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
