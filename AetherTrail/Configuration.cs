using System;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace AetherTrail;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public string SyncClientId { get; set; } = "";
    public bool NetworkConsentAccepted { get; set; } = false;
    public bool PartyPresenceSyncEnabled { get; set; } = false;
    public bool PartyPresenceMarkersEnabled { get; set; } = false;
    public float PartyPresenceMaxDrawDistance { get; set; } = 300.0f;
    public Vector4 PartyPresenceColor { get; set; } = new(1.0f, 0.2f, 0.85f, 1.0f);

    public string SyncServerUrl { get; set; } = "https://aethertrailsyncserver.loplop6754loplop.workers.dev";
    public string SyncRoomCode { get; set; } = "";
    public bool PartySyncEnabled { get; set; } = false;
    public bool AutoSyncEnabled { get; set; } = false;
    public int SyncIntervalSeconds { get; set; } = 15;
    public int GraphSyncIntervalSeconds { get; set; } = 90;
    public int PresenceMovingSyncIntervalSeconds { get; set; } = 5;
    public int PresenceIdleSyncIntervalSeconds { get; set; } = 15;
    public float PresenceMovementThreshold { get; set; } = 1.5f;
    public float PresenceRotationThresholdRadians { get; set; } = 0.5f;
    public int Version { get; set; } = 1;

    public bool RecordingEnabledByDefault { get; set; } = true;
    public bool OverlayEnabled { get; set; } = true;
    public bool HideTrailBehindUi { get; set; } = true;

    public float NodeSpacing { get; set; } = 7.0f;
    public float CornerNodeSpacing { get; set; } = 3.0f;
    public float DirectionChangeThreshold { get; set; } = 0.35f;
    public float TeleportResetDistance { get; set; } = 40.0f;
    public float SessionAttachDistance { get; set; } = 25.0f;

    public float TrailMarkerSpacing { get; set; } = 3.5f;

    public Vector4 TrailGraphPointColor { get; set; } = new(0.3f, 0.9f, 1.0f, 1.0f);
    public Vector4 TrailInterpolatedPointColor { get; set; } = new(0.15f, 0.35f, 1.0f, 1.0f);

    public float TrailMarkerSize { get; set; } = 9.0f;
    public float GraphDebugDrawDistance { get; set; } = 80.0f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    public void Migrate()
    {
        const string oldRailwayUrl = "https://aethertrailsyncserver-production.up.railway.app";
        const string newCloudflareUrl = "https://aethertrailsyncserver.loplop6754loplop.workers.dev";

        if (string.Equals(
                this.SyncServerUrl.TrimEnd('/'),
                oldRailwayUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            this.SyncServerUrl = newCloudflareUrl;
            this.Save();
        }
    }
}
