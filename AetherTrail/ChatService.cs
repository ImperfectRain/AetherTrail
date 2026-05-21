using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherTrail.Chat;

public sealed class ChatService
{
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

    public void Update()
    {
        var config = Plugin.Instance.Configuration;

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

        string senderName = player?.Name.TextValue ?? "Unknown";
        string senderId = config.SyncClientId;

        if (string.IsNullOrWhiteSpace(senderId))
        {
            senderId = Guid.NewGuid().ToString("N");
            config.SyncClientId = senderId;
            config.Save();
        }

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

                if (knownIds.Contains(message.Id))
                    continue;

                message.Room = room;
                message.Text = NormalizeMessage(message.Text);

                this.messages.Add(message);
                knownIds.Add(message.Id);
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

    private static string NormalizeMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        text = text.Replace("\r", " ").Replace("\n", " ").Trim();

        int maxLength = Math.Clamp(
            Plugin.Instance.Configuration.ChatMessageMaxCharacters,
            50,
            2000
        );

        if (text.Length > maxLength)
            text = text[..maxLength];

        return text;
    }
}
