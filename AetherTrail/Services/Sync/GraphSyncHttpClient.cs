using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

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
        string baseUrl = GetValidatedBaseUrl();
        string room = GetEscapedRoomCode();

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
        string baseUrl = GetValidatedBaseUrl();
        string room = GetEscapedRoomCode();

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

    public static async Task<List<PartySyncPresence>> SyncPresenceAsync(PartySyncPresence presence)
    {
        string baseUrl = GetValidatedBaseUrl();
        string room = GetEscapedRoomCode();

        string json = JsonSerializer.Serialize(presence);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        var response = await Client.PostAsync(
            $"{baseUrl}/rooms/{room}/presence/{presence.TerritoryId}/sync",
            content
        );

        if (!response.IsSuccessStatusCode)
            return new List<PartySyncPresence>();

        string responseJson = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<List<PartySyncPresence>>(responseJson)
            ?? new List<PartySyncPresence>();
    }

    private static string GetValidatedBaseUrl()
    {
        string configuredUrl = Plugin.Instance.Configuration.SyncServerUrl.Trim();

        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("AetherTrail sync server URL is invalid.");

        bool isLocalhost =
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);

        if (!isLocalhost && uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("AetherTrail sync requires HTTPS unless using localhost.");

        if (!isLocalhost && IPAddress.TryParse(uri.Host, out _))
            throw new InvalidOperationException("AetherTrail sync server must use a DNS hostname.");

        return configuredUrl.TrimEnd('/');
    }

    private static string GetEscapedRoomCode()
    {
        return Uri.EscapeDataString(Plugin.Instance.Configuration.SyncRoomCode.Trim());
    }
}
