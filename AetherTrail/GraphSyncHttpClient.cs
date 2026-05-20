using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AetherTrail;

public static class GraphSyncHttpClient
{
    private static readonly HttpClient Client = new();

    public static async Task<bool> UploadAsync(GraphSyncPacket packet)
    {
        string baseUrl = Plugin.Instance.Configuration.SyncServerUrl.TrimEnd('/');
        string room = Plugin.Instance.Configuration.SyncRoomCode;

        string json = JsonSerializer.Serialize(packet);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        var response = await Client.PostAsync(
            $"{baseUrl}/rooms/{room}/graphs/{packet.TerritoryId}",
            content
        );

        return response.IsSuccessStatusCode;
    }

    public static async Task<GraphSyncPacket?> DownloadAsync(uint territoryId)
    {
        string baseUrl = Plugin.Instance.Configuration.SyncServerUrl.TrimEnd('/');
        string room = Plugin.Instance.Configuration.SyncRoomCode;

        var response = await Client.GetAsync(
            $"{baseUrl}/rooms/{room}/graphs/{territoryId}"
        );

        if (!response.IsSuccessStatusCode)
            return null;

        string json = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<GraphSyncPacket>(json);
    }
}
