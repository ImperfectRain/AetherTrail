using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherTrail.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin) : base("AetherTrail Configuration")
    {
        this.plugin = plugin;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        UiOcclusionService.AddRect(
        ImGui.GetWindowPos(),
        ImGui.GetWindowPos() + ImGui.GetWindowSize()
        );
        var config = plugin.Configuration;

        bool recordingDefault = config.RecordingEnabledByDefault;
        if (ImGui.Checkbox("Recording enabled by default", ref recordingDefault))
        {
            config.RecordingEnabledByDefault = recordingDefault;
            config.Save();
        }

        bool overlayEnabled = config.OverlayEnabled;
        if (ImGui.Checkbox("Show data overlay", ref overlayEnabled))
        {
            config.OverlayEnabled = overlayEnabled;
            config.Save();
        }

        ImGui.Separator();

        float nodeSpacing = config.NodeSpacing;
        if (ImGui.SliderFloat("Node spacing", ref nodeSpacing, 3.0f, 15.0f))
        {
            config.NodeSpacing = nodeSpacing;
            config.Save();
        }

        float cornerNodeSpacing = config.CornerNodeSpacing;
        if (ImGui.SliderFloat("Corner node spacing", ref cornerNodeSpacing, 1.0f, 10.0f))
        {
            config.CornerNodeSpacing = cornerNodeSpacing;
            config.Save();
        }

        float directionChangeThreshold = config.DirectionChangeThreshold;
        if (ImGui.SliderFloat("Direction change sensitivity", ref directionChangeThreshold, 0.05f, 0.8f))
        {
            config.DirectionChangeThreshold = directionChangeThreshold;
            config.Save();
        }

        float teleportResetDistance = config.TeleportResetDistance;
        if (ImGui.SliderFloat("Teleport reset distance", ref teleportResetDistance, 20.0f, 100.0f))
        {
            config.TeleportResetDistance = teleportResetDistance;
            config.Save();
        }

        float sessionAttachDistance = config.SessionAttachDistance;
        if (ImGui.SliderFloat("Session attach distance", ref sessionAttachDistance, 5.0f, 50.0f))
        {
            config.SessionAttachDistance = sessionAttachDistance;
            config.Save();
        }

        ImGui.Separator();

        ImGui.Separator();
        ImGui.Text("Trail Appearance");

        float trailMarkerSize = Plugin.Instance.Configuration.TrailMarkerSize;

        if (ImGui.SliderFloat("Trail Dot Size", ref trailMarkerSize, 3.0f, 16.0f))
        {
            Plugin.Instance.Configuration.TrailMarkerSize = trailMarkerSize;
            Plugin.Instance.Configuration.Save();
        }

        float trailMarkerSpacing = Plugin.Instance.Configuration.TrailMarkerSpacing;

        if (ImGui.SliderFloat("Distance Between Trail Dots", ref trailMarkerSpacing, 2.0f, 30.0f))
        {
            Plugin.Instance.Configuration.TrailMarkerSpacing = trailMarkerSpacing;
            Plugin.Instance.Configuration.Save();
        }

        Vector4 trailGraphPointColor = Plugin.Instance.Configuration.TrailGraphPointColor;

        if (ImGui.ColorEdit4("Trail Color", ref trailGraphPointColor))
        {
            Plugin.Instance.Configuration.TrailGraphPointColor = trailGraphPointColor;
            Plugin.Instance.Configuration.Save();
        }

        Vector4 trailInterpolatedPointColor = Plugin.Instance.Configuration.TrailInterpolatedPointColor;

        if (ImGui.ColorEdit4("Trail Smoothing Color", ref trailInterpolatedPointColor))
        {
            Plugin.Instance.Configuration.TrailInterpolatedPointColor = trailInterpolatedPointColor;
            Plugin.Instance.Configuration.Save();
        }

        if (ImGui.Button("Reset Trail Appearance"))
        {
            Plugin.Instance.Configuration.TrailMarkerSize = 9.0f;
            Plugin.Instance.Configuration.TrailMarkerSpacing = 3.5f;
            Plugin.Instance.Configuration.TrailGraphPointColor = new Vector4(0.3f, 0.9f, 1.0f, 1.0f);
            Plugin.Instance.Configuration.TrailInterpolatedPointColor = new Vector4(0.15f, 0.35f, 1.0f, 1.0f);
            Plugin.Instance.Configuration.Save();
        }

        float graphDebugDrawDistance = config.GraphDebugDrawDistance;
        if (ImGui.SliderFloat("Graph debug draw distance", ref graphDebugDrawDistance, 20.0f, 200.0f))
        {
            config.GraphDebugDrawDistance = graphDebugDrawDistance;
            config.Save();
        }

        ImGui.Separator();
        ImGui.Text("Party Presence");

        Vector4 partyPresenceColor = config.PartyPresenceColor;
        if (ImGui.ColorEdit4("Synced marker color", ref partyPresenceColor))
        {
            config.PartyPresenceColor = partyPresenceColor;
            config.Save();
        }

        bool partyPresenceMarkersEnabled = config.PartyPresenceMarkersEnabled;
        if (ImGui.Checkbox("Show synced party markers", ref partyPresenceMarkersEnabled))
        {
            config.PartyPresenceMarkersEnabled = partyPresenceMarkersEnabled;
            config.Save();
        }
    }
}
