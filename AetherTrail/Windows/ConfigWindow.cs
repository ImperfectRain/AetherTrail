using System;
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

        float trailMarkerSize = config.TrailMarkerSize;
        if (ImGui.SliderFloat("Trail marker size", ref trailMarkerSize, 4.0f, 16.0f))
        {
            config.TrailMarkerSize = trailMarkerSize;
            config.Save();
        }

        float graphDebugDrawDistance = config.GraphDebugDrawDistance;
        if (ImGui.SliderFloat("Graph debug draw distance", ref graphDebugDrawDistance, 20.0f, 200.0f))
        {
            config.GraphDebugDrawDistance = graphDebugDrawDistance;
            config.Save();
        }
    }
}
