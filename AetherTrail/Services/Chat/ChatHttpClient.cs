using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AetherTrail.Chat;

public static class ChatHttpClient
{
    private const int MaxResponseCharacters = 200000;

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<List<ChatMessage>> SendMessageAsync(ChatMessage message)
    {
        string baseUrl = GetSafeBaseUrl();
        string room = Plugin.Instance.Configuration.SyncRoomCode.Trim().ToUpperInvariant();

        string json = JsonSerializer.Serialize(message, JsonOptions);

        using var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json"
        );

        var response = await Client.PostAsync(
            $"{baseUrl}/rooms/{room}/chat/sync",
            content
        );

        if (!response.IsSuccessStatusCode)
        {
            Plugin.Log.Warning($"AetherTrail chat send failed: {(int)response.StatusCode}");
            return new List<ChatMessage>();
        }

        string responseJson = await ReadBoundedResponseAsync(response.Content);

        return JsonSerializer.Deserialize<List<ChatMessage>>(responseJson, JsonOptions)
            ?? new List<ChatMessage>();
    }

    public static async Task<List<ChatMessage>> DownloadMessagesAsync(DateTime? sinceUtc)
    {
        string baseUrl = GetSafeBaseUrl();
        string room = Plugin.Instance.Configuration.SyncRoomCode.Trim().ToUpperInvariant();

        string url = $"{baseUrl}/rooms/{room}/chat";

        if (sinceUtc.HasValue)
        {
            string encoded = Uri.EscapeDataString(sinceUtc.Value.ToUniversalTime().ToString("O"));
            url += $"?since={encoded}";
        }

        var response = await Client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            Plugin.Log.Warning($"AetherTrail chat download failed: {(int)response.StatusCode}");
            return new List<ChatMessage>();
        }

        string responseJson = await ReadBoundedResponseAsync(response.Content);

        return JsonSerializer.Deserialize<List<ChatMessage>>(responseJson, JsonOptions)
            ?? new List<ChatMessage>();
    }

    private static string GetSafeBaseUrl()
    {
        string baseUrl = Plugin.Instance.Configuration.SyncServerUrl.TrimEnd('/');

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("AetherTrail chat server URL is invalid.");

        bool isLocalHttp =
            string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase));

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && !isLocalHttp)
            throw new InvalidOperationException("AetherTrail chat requires HTTPS unless using localhost.");

        return baseUrl;
    }

    private static async Task<string> ReadBoundedResponseAsync(HttpContent content)
    {
        string response = await content.ReadAsStringAsync();

        if (response.Length > MaxResponseCharacters)
            throw new InvalidOperationException("AetherTrail chat response exceeded the local safety limit.");

        return response;
    }
}
