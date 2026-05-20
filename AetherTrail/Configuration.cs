using System;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AetherTrail;

[Serializable]
public class Configuration : IPluginConfiguration
{

    public string SyncServerUrl { get; set; } = "https://aethertrailsyncserver-production.up.railway.app";
    public string SyncRoomCode { get; set; } = "";
    public bool PartySyncEnabled { get; set; } = false;
    public bool AutoSyncEnabled { get; set; } = true;
    public int SyncIntervalSeconds { get; set; } = 15;
    public int Version { get; set; } = 1;

    public bool RecordingEnabledByDefault { get; set; } = true;
    public bool OverlayEnabled { get; set; } = true;

    public float NodeSpacing { get; set; } = 7.0f;
    public float CornerNodeSpacing { get; set; } = 3.0f;
    public float DirectionChangeThreshold { get; set; } = 0.35f;
    public float TeleportResetDistance { get; set; } = 40.0f;
    public float SessionAttachDistance { get; set; } = 25.0f;

    public float TrailMarkerSize { get; set; } = 9.0f;
    public float GraphDebugDrawDistance { get; set; } = 80.0f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
