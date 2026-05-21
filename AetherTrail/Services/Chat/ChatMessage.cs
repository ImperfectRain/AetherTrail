using System;

namespace AetherTrail.Chat;

public sealed class ChatMessage
{
    public string Id { get; set; } = "";
    public string Room { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}
