using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AetherTrail.Chat;

public static class ChatHttpClient
{
    private static readonly HttpClient Client = new();

    public static async Task<List<ChatMessage>> SendMessageAsync(ChatMessage message)
    {
        string baseUrl = Plugin.Instance.Configuration.SyncServerUrl.TrimEnd('/');
        string room = Plugin.Instance.Configuration.SyncRoomCode.Trim().ToUpperInvariant();

        string json = JsonSerializer.Serialize(message);

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
            return new List<ChatMessage>();

        string responseJson = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<List<ChatMessage>>(responseJson)
            ?? new List<ChatMessage>();
    }

    public static async Task<List<ChatMessage>> DownloadMessagesAsync(DateTime? sinceUtc)
    {
        string baseUrl = Plugin.Instance.Configuration.SyncServerUrl.TrimEnd('/');
        string room = Plugin.Instance.Configuration.SyncRoomCode.Trim().ToUpperInvariant();

        string url = $"{baseUrl}/rooms/{room}/chat";

        if (sinceUtc.HasValue)
        {
            string encoded = Uri.EscapeDataString(sinceUtc.Value.ToUniversalTime().ToString("O"));
            url += $"?since={encoded}";
        }

        var response = await Client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
            return new List<ChatMessage>();

        string responseJson = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<List<ChatMessage>>(responseJson)
            ?? new List<ChatMessage>();
    }
}
