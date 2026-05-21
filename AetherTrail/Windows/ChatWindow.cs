using System;
using System.Numerics;
using AetherTrail.Chat;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherTrail.Windows;

public sealed class ChatWindow : Window
{
    private readonly Plugin plugin;
    private readonly ChatService chatService = new();
    private string input = "";
    private bool shouldScrollToBottom;

    public ChatWindow(Plugin plugin)
        : base("AetherTrail Chat###AetherTrailChat")
    {
        this.plugin = plugin;

        this.Size = new Vector2(460, 360);
        this.SizeCondition = ImGuiCond.FirstUseEver;

        this.chatService.MessagesChanged += OnMessagesChanged;
    }

    public void Dispose()
    {
        this.chatService.MessagesChanged -= OnMessagesChanged;
    }

    public override void Draw()
    {
        var config = this.plugin.Configuration;

        bool chatEnabled = config.ChatEnabled;
        if (ImGui.Checkbox("Enable party chat", ref chatEnabled))
        {
            config.ChatEnabled = chatEnabled;
            config.Save();
        }

        if (!config.ChatEnabled)
        {
            ImGui.TextWrapped("Chat sends messages through the configured sync server. Enable it only for rooms you trust.");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.SyncRoomCode))
        {
            ImGui.TextWrapped("Join or create a party sync room before using chat.");
            return;
        }

        ImGui.Text($"Room: {config.SyncRoomCode.Trim().ToUpperInvariant()}");
        ImGui.TextWrapped("Messages are room-shared. You can locally mute senders, but server-side moderation still depends on the sync server.");

        if (ImGui.Button("Clear Local History"))
            this.chatService.ClearCurrentRoomHistory();

        DrawMutedSenders();
        ImGui.Separator();

        DrawMessages();
        DrawInput();
    }

    private void DrawMessages()
    {
        float inputHeight = ImGui.GetFrameHeightWithSpacing() + 8f;
        Vector2 childSize = new(0, -inputHeight);

        ImGui.BeginChild("##AetherTrailChatMessages", childSize, true);

        var messages = this.chatService.GetSnapshot();

        foreach (var message in messages)
        {
            string time = message.CreatedAtUtc.ToLocalTime().ToString("HH:mm");
            string sender = string.IsNullOrWhiteSpace(message.SenderName)
                ? "Unknown"
                : message.SenderName;
            bool isSelf = string.Equals(
                message.SenderId,
                this.plugin.Configuration.SyncClientId,
                StringComparison.OrdinalIgnoreCase);

            ImGui.PushID(message.Id);
            ImGui.TextDisabled($"[{time}]");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.35f, 0.9f, 1.0f, 1.0f), sender);

            if (!isSelf && !string.IsNullOrWhiteSpace(message.SenderId))
            {
                ImGui.SameLine();

                if (ImGui.SmallButton("Mute"))
                    this.chatService.MuteSender(message.SenderId);
            }

            ImGui.SameLine();
            ImGui.TextWrapped(message.Text);
            ImGui.PopID();
        }

        if (this.shouldScrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            this.shouldScrollToBottom = false;
        }

        ImGui.EndChild();
    }

    private void DrawInput()
    {
        ImGui.SetNextItemWidth(-80f);

        bool submitted = ImGui.InputText(
            "##AetherTrailChatInput",
            ref this.input,
            1024,
            ImGuiInputTextFlags.EnterReturnsTrue
        );

        ImGui.SameLine();

        if (ImGui.Button("Send") || submitted)
        {
            string toSend = this.input;
            this.input = "";

            _ = this.chatService.SendAsync(toSend);
        }
    }

    private void DrawMutedSenders()
    {
        var muted = this.plugin.Configuration.MutedChatSenderIds;

        if (!ImGui.CollapsingHeader($"Muted Senders ({muted.Count})"))
            return;

        if (muted.Count == 0)
        {
            ImGui.TextDisabled("No muted senders.");
            return;
        }

        for (int i = muted.Count - 1; i >= 0; i--)
        {
            string senderId = muted[i];

            ImGui.PushID(senderId);
            ImGui.TextUnformatted(senderId);
            ImGui.SameLine();

            if (ImGui.SmallButton("Unmute"))
                this.chatService.UnmuteSender(senderId);

            ImGui.PopID();
        }
    }

    private void OnMessagesChanged()
    {
        this.shouldScrollToBottom = true;
    }
}
