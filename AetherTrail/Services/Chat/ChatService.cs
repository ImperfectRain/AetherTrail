using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherTrail.Chat;

public sealed class ChatService
{
    private const int MaxSenderIdLength = 80;
    private const int MaxSenderNameLength = 64;
    private const int MaxMessageIdLength = 80;
    private static readonly TimeSpan MaxFutureSkew = TimeSpan.FromMinutes(5);

    private readonly List<ChatMessage> messages = new();
    private readonly object lockObject = new();

    private DateTime lastPollUtc = DateTime.MinValue;
    private DateTime? newestMessageUtc;
    private string loadedRoom = "";

    private int pollInProgress;

    public event Action? MessagesChanged;

    public IReadOnlyList<ChatMessage> GetSnapshot()
    {
        lock (this.lockObject)
        {
            return this.messages.ToList();
        }
    }

    public bool IsMuted(string senderId)
    {
        if (string.IsNullOrWhiteSpace(senderId))
            return false;

        return Plugin.Instance.Configuration.MutedChatSenderIds
            .Any(id => string.Equals(id, senderId, StringComparison.OrdinalIgnoreCase));
    }

    public void MuteSender(string senderId)
    {
        senderId = NormalizeIdentifier(senderId, MaxSenderIdLength);

        if (string.IsNullOrWhiteSpace(senderId))
            return;

        var config = Plugin.Instance.Configuration;

        if (!config.MutedChatSenderIds.Any(id => string.Equals(id, senderId, StringComparison.OrdinalIgnoreCase)))
        {
            config.MutedChatSenderIds.Add(senderId);
            config.Save();
        }

        lock (this.lockObject)
        {
            this.messages.RemoveAll(message =>
                string.Equals(message.SenderId, senderId, StringComparison.OrdinalIgnoreCase));
        }

        SaveLoadedRoom();
        this.MessagesChanged?.Invoke();
    }

    public void UnmuteSender(string senderId)
    {
        var config = Plugin.Instance.Configuration;
        int removed = config.MutedChatSenderIds.RemoveAll(id =>
            string.Equals(id, senderId, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
            config.Save();
    }

    public void ClearCurrentRoomHistory()
    {
        string room;

        lock (this.lockObject)
        {
            room = this.loadedRoom;
            this.messages.Clear();
            this.newestMessageUtc = DateTime.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(room))
            ChatHistoryStore.Clear(room);

        this.MessagesChanged?.Invoke();
    }

    public void Update()
    {
        var config = Plugin.Instance.Configuration;

        if (config.ChatSystemDisabled)
            return;

        if (!config.ChatEnabled)
            return;

        if (!config.PartySyncEnabled)
            return;

        if (string.IsNullOrWhiteSpace(config.SyncRoomCode))
            return;

        string room = config.SyncRoomCode.Trim().ToUpperInvariant();

        EnsureRoomLoaded(room);

        int interval = Math.Clamp(config.ChatPollIntervalSeconds, 2, 30);

        if ((DateTime.UtcNow - this.lastPollUtc).TotalSeconds < interval)
            return;

        this.lastPollUtc = DateTime.UtcNow;

        _ = PollAsync(room);
    }

    public async Task SendAsync(string text)
    {
        if (Plugin.Instance.Configuration.ChatSystemDisabled)
            return;

        text = NormalizeMessage(text);

        if (string.IsNullOrWhiteSpace(text))
            return;

        var config = Plugin.Instance.Configuration;

        if (string.IsNullOrWhiteSpace(config.SyncRoomCode))
        {
            Plugin.ChatGui.Print("[AetherTrail Chat] No sync room selected.");
            return;
        }

        string room = config.SyncRoomCode.Trim().ToUpperInvariant();

        EnsureRoomLoaded(room);

        var player = Plugin.ObjectTable.LocalPlayer;

        string senderName = NormalizeSenderName(player?.Name.TextValue ?? "Unknown");
        string senderId = config.SyncClientId;

        if (string.IsNullOrWhiteSpace(senderId))
        {
            senderId = Guid.NewGuid().ToString("N");
            config.SyncClientId = senderId;
            config.Save();
        }

        senderId = NormalizeIdentifier(senderId, MaxSenderIdLength);

        ChatMessage message = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Room = room,
            SenderId = senderId,
            SenderName = senderName,
            Text = text,
            CreatedAtUtc = DateTime.UtcNow
        };

        try
        {
            var serverMessages = await ChatHttpClient.SendMessageAsync(message);
            MergeMessages(room, serverMessages);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "AetherTrail chat send failed.");
            Plugin.ChatGui.Print($"[AetherTrail Chat] Send failed: {ex.Message}");
        }
    }

    private async Task PollAsync(string room)
    {
        if (Interlocked.Exchange(ref this.pollInProgress, 1) == 1)
            return;

        try
        {
            var downloaded = await ChatHttpClient.DownloadMessagesAsync(this.newestMessageUtc);
            MergeMessages(room, downloaded);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "AetherTrail chat poll failed.");
        }
        finally
        {
            Interlocked.Exchange(ref this.pollInProgress, 0);
        }
    }

    private void EnsureRoomLoaded(string room)
    {
        lock (this.lockObject)
        {
            if (string.Equals(this.loadedRoom, room, StringComparison.OrdinalIgnoreCase))
                return;

            this.messages.Clear();
            this.messages.AddRange(ChatHistoryStore.Load(room));

            this.loadedRoom = room;

            this.newestMessageUtc = this.messages.Count == 0
                ? null
                : this.messages.Max(message => message.CreatedAtUtc);
        }

        this.MessagesChanged?.Invoke();
    }

    private void MergeMessages(string room, IEnumerable<ChatMessage> incoming)
    {
        bool changed = false;

        lock (this.lockObject)
        {
            HashSet<string> knownIds = this.messages
                .Select(message => message.Id)
                .ToHashSet();

            foreach (var message in incoming)
            {
                if (message == null)
                    continue;

                if (string.IsNullOrWhiteSpace(message.Id))
                    continue;

                if (!TryNormalizeIncomingMessage(room, message, out var normalized))
                    continue;

                if (knownIds.Contains(normalized.Id))
                    continue;

                this.messages.Add(normalized);
                knownIds.Add(normalized.Id);
                changed = true;
            }

            if (!changed)
                return;

            this.messages.Sort((a, b) => a.CreatedAtUtc.CompareTo(b.CreatedAtUtc));

            this.newestMessageUtc = this.messages.Count == 0
                ? null
                : this.messages[^1].CreatedAtUtc;

            ChatHistoryStore.Save(room, this.messages);
        }

        this.MessagesChanged?.Invoke();
    }

    private bool TryNormalizeIncomingMessage(
        string room,
        ChatMessage message,
        out ChatMessage normalized)
    {
        normalized = new ChatMessage();

        string id = NormalizeIdentifier(message.Id, MaxMessageIdLength);
        string senderId = NormalizeIdentifier(message.SenderId, MaxSenderIdLength);
        string text = NormalizeMessage(message.Text);

        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(senderId) ||
            string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (IsMuted(senderId))
            return false;

        string incomingRoom = (message.Room ?? "").Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(incomingRoom) &&
            !string.Equals(incomingRoom, room, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        DateTime createdAtUtc = message.CreatedAtUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(message.CreatedAtUtc, DateTimeKind.Utc)
            : message.CreatedAtUtc.ToUniversalTime();

        DateTime now = DateTime.UtcNow;

        if (createdAtUtc == default || createdAtUtc > now + MaxFutureSkew)
            createdAtUtc = now;

        normalized = new ChatMessage
        {
            Id = id,
            Room = room,
            SenderId = senderId,
            SenderName = NormalizeSenderName(message.SenderName),
            Text = text,
            CreatedAtUtc = createdAtUtc
        };

        return true;
    }

    private static string NormalizeMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = NormalizeSingleLine(text);

        int maxLength = Math.Clamp(
            Plugin.Instance.Configuration.ChatMessageMaxCharacters,
            50,
            2000
        );

        if (text.Length > maxLength)
            text = text[..maxLength];

        return text;
    }

    private static string NormalizeSenderName(string senderName)
    {
        senderName = NormalizeSingleLine(senderName);

        if (string.IsNullOrWhiteSpace(senderName))
            return "Unknown";

        if (senderName.Length > MaxSenderNameLength)
            senderName = senderName[..MaxSenderNameLength];

        return senderName;
    }

    private static string NormalizeIdentifier(string value, int maxLength)
    {
        value = NormalizeSingleLine(value);

        if (value.Length > maxLength)
            value = value[..maxLength];

        return value;
    }

    private static string NormalizeSingleLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        char[] chars = value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Where(ch => !char.IsControl(ch))
            .ToArray();

        return new string(chars).Trim();
    }

    private void SaveLoadedRoom()
    {
        lock (this.lockObject)
        {
            if (!string.IsNullOrWhiteSpace(this.loadedRoom))
                ChatHistoryStore.Save(this.loadedRoom, this.messages);
        }
    }
}
