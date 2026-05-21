using System;
using System.Numerics;
using AetherTrail.Chat;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherTrail.Windows;

public sealed class ChatWindow : Window
{
    private readonly Plugin plugin;
    private string input = "";
    private bool shouldScrollToBottom;

    public ChatWindow(Plugin plugin)
        : base("AetherTrail Chat###AetherTrailChat")
    {
        this.plugin = plugin;

        this.Size = new Vector2(460, 360);
        this.SizeCondition = ImGuiCond.FirstUseEver;

        this.plugin.ChatService.MessagesChanged += OnMessagesChanged;
    }

    public void Dispose()
    {
        this.plugin.ChatService.MessagesChanged -= OnMessagesChanged;
    }

    public override void Draw()
    {
        var config = this.plugin.Configuration;

        if (!config.ChatEnabled)
        {
            ImGui.TextWrapped("AetherTrail chat is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.SyncRoomCode))
        {
            ImGui.TextWrapped("Join or create a party sync room before using chat.");
            return;
        }

        ImGui.Text($"Room: {config.SyncRoomCode.Trim().ToUpperInvariant()}");
        ImGui.Separator();

        DrawMessages();
        DrawInput();
    }

    private void DrawMessages()
    {
        float inputHeight = ImGui.GetFrameHeightWithSpacing() + 8f;
        Vector2 childSize = new(0, -inputHeight);

        ImGui.BeginChild("##AetherTrailChatMessages", childSize, true);

        var messages = this.plugin.ChatService.GetSnapshot();

        foreach (var message in messages)
        {
            string time = message.CreatedAtUtc.ToLocalTime().ToString("HH:mm");
            string sender = string.IsNullOrWhiteSpace(message.SenderName)
                ? "Unknown"
                : message.SenderName;

            ImGui.TextDisabled($"[{time}]");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.35f, 0.9f, 1.0f, 1.0f), sender);
            ImGui.SameLine();
            ImGui.TextWrapped(message.Text);
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

            _ = this.plugin.ChatService.SendAsync(toSend);
        }
    }

    private void OnMessagesChanged()
    {
        this.shouldScrollToBottom = true;
    }
}
