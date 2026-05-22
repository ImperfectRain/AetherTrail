using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace AetherTrail.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin, string goatImagePath)
        : base("AetherTrail")
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
        uint territoryId = Plugin.ClientState.TerritoryType;
        int nodes = NavigationManager.GetNodeCount(territoryId);
        int links = NavigationManager.GetLinkCount(territoryId);

        ImGui.Text("AetherTrail");
        ImGui.Separator();

        ImGui.Text($"Territory: {territoryId}");
        ImGui.Text($"Nodes: {nodes}");
        ImGui.Text($"Links: {links}");

        ImGui.Spacing();

        if (ImGui.Button("Open Settings"))
        {
            plugin.ToggleConfigUi();
        }

        ImGui.Separator();

        if (ImGui.Button("Export Current Territory Graph"))
        {
            plugin.ExportCurrentTerritoryGraph();
        }

        if (ImGui.Button("Import Current Territory Graph"))
        {
            plugin.ImportCurrentTerritoryGraph();
        }

        if (ImGui.Button("Tools"))
        {
            this.plugin.ToggleToolsWindow();
        }

        ImGui.Separator();
        ImGui.Text("Network Sync");

        var config = plugin.Configuration;

        bool graphSyncEnabled = config.PartySyncEnabled;
        if (ImGui.Checkbox("Enable Graph Sync", ref graphSyncEnabled))
        {
            if (graphSyncEnabled)
            {
                this.plugin.RequestNetworkFeatureEnable(() =>
                {
                    config.PartySyncEnabled = true;
                    config.Save();
                });
            }
            else
            {
                config.PartySyncEnabled = false;
                config.Save();
            }
        }

        bool presenceSyncEnabled = config.PartyPresenceSyncEnabled;
        if (ImGui.Checkbox("Share Party Position", ref presenceSyncEnabled))
        {
            if (presenceSyncEnabled)
            {
                this.plugin.RequestNetworkFeatureEnable(() =>
                {
                    config.PartyPresenceSyncEnabled = true;
                    config.PartyPresenceMarkersEnabled = true;
                    config.Save();
                });
            }
            else
            {
                config.PartyPresenceSyncEnabled = false;
                config.Save();
                PartyPresenceService.Clear();
            }
        }

        bool autoSyncEnabled = config.AutoSyncEnabled;
        if (ImGui.Checkbox("Auto Sync", ref autoSyncEnabled))
        {
            if (autoSyncEnabled)
            {
                this.plugin.RequestNetworkFeatureEnable(() =>
                {
                    config.AutoSyncEnabled = true;
                    config.Save();
                });
            }
            else
            {
                config.AutoSyncEnabled = false;
                config.Save();
            }
        }

        string syncServerUrl = this.plugin.Configuration.SyncServerUrl;

        ImGui.Text("Sync Server URL");

        if (ImGui.InputText("##SyncServerUrl", ref syncServerUrl, 256))
        {
            if (ImGui.Button("Use Default Cloudflare Server"))
            {
                this.plugin.Configuration.SyncServerUrl = "https://aethertrailsyncserver.loplop6754loplop.workers.dev";
                this.plugin.Configuration.Save();
            }
            this.plugin.Configuration.SyncServerUrl = syncServerUrl.Trim();
            this.plugin.Configuration.Save();
        }

        string roomCode = config.SyncRoomCode;
        ImGui.InputText("Room Code", ref roomCode, 32);

        if (roomCode != config.SyncRoomCode)
        {
            config.SyncRoomCode = roomCode.Trim().ToUpperInvariant();
            config.Save();
        }

        if (ImGui.Button("Create Room"))
        {
            config.SyncRoomCode = PartySyncService.GenerateRoomCode();
            this.plugin.RequestNetworkFeatureEnable(() =>
            {
                config.PartySyncEnabled = true;
                config.AutoSyncEnabled = true;
                config.Save();

                Plugin.ChatGui.Print($"AetherTrail graph sync room created: {config.SyncRoomCode}");
            });
        }

        if (ImGui.Button("Join Room"))
        {
            this.plugin.RequestNetworkFeatureEnable(() =>
            {
                config.PartySyncEnabled = true;
                config.AutoSyncEnabled = true;
                config.Save();

                Plugin.ChatGui.Print($"AetherTrail joined graph sync room: {config.SyncRoomCode}");
            });
        }

        if (ImGui.Button("Leave Room"))
        {
            config.PartySyncEnabled = false;
            config.PartyPresenceSyncEnabled = false;
            config.Save();
            PartyPresenceService.Clear();

            Plugin.ChatGui.Print("AetherTrail left sync room.");
        }

        ImGui.Text($"Current Room: {(string.IsNullOrWhiteSpace(config.SyncRoomCode) ? "None" : config.SyncRoomCode)}");

        if (ImGui.Button("Clean Current Territory Graph"))
        {
            plugin.PruneCurrentTerritoryGraph();
        }
    }
}
