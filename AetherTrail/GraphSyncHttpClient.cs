using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AetherTrail;

public static class GraphSyncHttpClient
{
    private static readonly HttpClient Client = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
        WriteIndented = false
    };

    public static async Task<bool> UploadAsync(GraphSyncPacket packet)
    {
        string baseUrl = Plugin.Instance.Configuration.SyncServerUrl.TrimEnd('/');
        string room = Plugin.Instance.Configuration.SyncRoomCode.Trim();

        string json = JsonSerializer.Serialize(packet, JsonOptions);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        var response = await Client.PostAsync(
            $"{baseUrl}/rooms/{room}/graphs/{packet.TerritoryId}",
            content
        );

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            Plugin.Log.Warning($"AetherTrail sync upload failed: {(int)response.StatusCode} {error}");
        }

        return response.IsSuccessStatusCode;
    }

    public static async Task<GraphSyncPacket?> DownloadAsync(uint territoryId)
    {
        string baseUrl = Plugin.Instance.Configuration.SyncServerUrl.TrimEnd('/');
        string room = Plugin.Instance.Configuration.SyncRoomCode.Trim();

        var response = await Client.GetAsync(
            $"{baseUrl}/rooms/{room}/graphs/{territoryId}"
        );

        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            Plugin.Log.Warning($"AetherTrail sync download failed: {(int)response.StatusCode} {error}");
            return null;
        }

        string json = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<GraphSyncPacket>(json, JsonOptions);
    }
}
