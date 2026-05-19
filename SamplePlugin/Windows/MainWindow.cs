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

        ImGui.Separator();
        ImGui.Text("Party Sync");

        var config = plugin.Configuration;

        bool partySyncEnabled = config.PartySyncEnabled;
        if (ImGui.Checkbox("Enable Party Sync", ref partySyncEnabled))
        {
            config.PartySyncEnabled = partySyncEnabled;
            config.Save();
        }

        bool autoSyncEnabled = config.AutoSyncEnabled;
        if (ImGui.Checkbox("Auto Sync", ref autoSyncEnabled))
        {
            config.AutoSyncEnabled = autoSyncEnabled;
            config.Save();
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
            config.PartySyncEnabled = true;
            config.AutoSyncEnabled = true;
            config.Save();

            Plugin.ChatGui.Print($"AetherTrail party sync room created: {config.SyncRoomCode}");
        }

        if (ImGui.Button("Join Room"))
        {
            config.PartySyncEnabled = true;
            config.AutoSyncEnabled = true;
            config.Save();

            Plugin.ChatGui.Print($"AetherTrail joined sync room: {config.SyncRoomCode}");
        }

        if (ImGui.Button("Leave Room"))
        {
            config.PartySyncEnabled = false;
            config.Save();

            Plugin.ChatGui.Print("AetherTrail left party sync room.");
        }

        ImGui.Text($"Current Room: {(string.IsNullOrWhiteSpace(config.SyncRoomCode) ? "None" : config.SyncRoomCode)}");

        if (ImGui.Button("Clean Current Territory Graph"))
        {
            plugin.PruneCurrentTerritoryGraph();
        }
    }
}
