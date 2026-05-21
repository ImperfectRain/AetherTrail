using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AetherTrail.Chat;

public static class ChatHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string GetChatDirectory()
    {
        string path = Path.Combine(
            Plugin.PluginInterface.ConfigDirectory.FullName,
            "Chats"
        );

        Directory.CreateDirectory(path);

        return path;
    }

    public static string GetChatPath(string room)
    {
        string safeRoom = SanitizeFileName(room.Trim().ToUpperInvariant());

        return Path.Combine(
            GetChatDirectory(),
            $"{safeRoom}-chat.jsonl"
        );
    }

    public static List<ChatMessage> Load(string room)
    {
        string path = GetChatPath(room);

        if (!File.Exists(path))
            return new List<ChatMessage>();

        List<ChatMessage> messages = new();

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var message = JsonSerializer.Deserialize<ChatMessage>(line);

                if (message != null)
                    messages.Add(message);
            }
            catch
            {
                // Ignore malformed history lines.
            }
        }

        return messages
            .OrderBy(message => message.CreatedAtUtc)
            .ToList();
    }

    public static void Save(string room, IEnumerable<ChatMessage> messages)
    {
        int maxCharacters = Math.Clamp(
            Plugin.Instance.Configuration.ChatLocalMaxCharacters,
            2000,
            200000
        );

        List<ChatMessage> trimmed = TrimToCharacterLimit(
            messages
                .OrderBy(message => message.CreatedAtUtc)
                .ToList(),
            maxCharacters
        );

        string path = GetChatPath(room);

        File.WriteAllLines(
            path,
            trimmed.Select(message => JsonSerializer.Serialize(message, JsonOptions))
        );
    }

    private static List<ChatMessage> TrimToCharacterLimit(
        List<ChatMessage> messages,
        int maxCharacters)
    {
        int total = messages.Sum(message =>
            (message.SenderName?.Length ?? 0) +
            (message.Text?.Length ?? 0) +
            64
        );

        while (messages.Count > 0 && total > maxCharacters)
        {
            ChatMessage oldest = messages[0];

            total -=
                (oldest.SenderName?.Length ?? 0) +
                (oldest.Text?.Length ?? 0) +
                64;

            messages.RemoveAt(0);
        }

        return messages;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value;
    }
}
