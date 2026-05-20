using System;
using System.Threading.Tasks;

namespace AetherTrail;

public sealed class PartySyncService
{
    private DateTime lastSync = DateTime.MinValue;
    private bool syncing;

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
            uint territoryId = Plugin.ClientState.TerritoryType;

            var packet = NavigationManager.CreateSyncPacket(territoryId);

            bool uploaded = await GraphSyncHttpClient.UploadAsync(packet);

            Plugin.ChatGui.Print(uploaded
                ? "AetherTrail sync upload successful."
                : "AetherTrail sync upload failed.");

            if (!uploaded)
            {
                Plugin.ChatGui.Print("AetherTrail sync upload failed.");
                return;
            }

            var downloaded = await GraphSyncHttpClient.DownloadAsync(territoryId);

            if (downloaded == null)
            {
                Plugin.ChatGui.Print("AetherTrail sync download failed or no graph exists.");
                return;
            }

            if (downloaded.TerritoryId != territoryId)
                return;

            NavigationManager.ImportSyncPacket(downloaded);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "AetherTrail party sync failed.");
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
