using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System.Numerics;


namespace AetherTrail.Windows;

public sealed class ToolsWindow : Window
{
    private readonly Plugin plugin;

    public ToolsWindow(Plugin plugin)
        : base("AetherTrail Tools###AetherTrailTools")
    {
        this.plugin = plugin;

        this.Size = new Vector2(430, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        uint territoryId = Plugin.ClientState.TerritoryType;

        ImGui.Text("AetherTrail Tools");
        ImGui.Separator();

        DrawStatusSection(territoryId);
        ImGui.Spacing();

        DrawSyncSection();
        ImGui.Spacing();

        DrawGraphCleanupSection(territoryId);
        ImGui.Spacing();

        DrawDebugSection(territoryId);
    }

    private void DrawStatusSection(uint territoryId)
    {
        ImGui.Text("Current Status");

        ImGui.Text($"Territory: {territoryId}");
        ImGui.Text($"Nodes: {NavigationManager.GetNodeCount(territoryId)}");
        ImGui.Text($"Links: {NavigationManager.GetLinkCount(territoryId)}");

        ImGui.Text($"Sync Room: {this.plugin.Configuration.SyncRoomCode}");
        ImGui.TextWrapped($"Sync Server: {this.plugin.Configuration.SyncServerUrl}");
    }

    private void DrawSyncSection()
    {
        if (ImGui.CollapsingHeader("Network Sync", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var config = this.plugin.Configuration;
            bool graphSyncEnabled = config.PartySyncEnabled;

            if (ImGui.Checkbox("Graph Sync Enabled", ref graphSyncEnabled))
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

            if (ImGui.Checkbox("Auto Sync Enabled", ref autoSyncEnabled))
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

            string roomCode = config.SyncRoomCode;

            if (ImGui.InputText("Room Code", ref roomCode, 32))
            {
                config.SyncRoomCode = roomCode.Trim().ToUpperInvariant();
                config.Save();
            }

            if (ImGui.Button("Generate New Room Code"))
            {
                config.SyncRoomCode = PartySyncService.GenerateRoomCode();
                config.Save();

                Plugin.ChatGui.Print($"AetherTrail sync room created: {config.SyncRoomCode}");
            }

            ImGui.SameLine();

            if (ImGui.Button("Leave Room"))
            {
                config.SyncRoomCode = "";
                config.PartySyncEnabled = false;
                config.PartyPresenceSyncEnabled = false;
                config.Save();

                PartyPresenceService.Clear();

                Plugin.ChatGui.Print("AetherTrail left sync room.");
            }

            if (ImGui.Button("Sync Current Territory Now"))
            {
                this.plugin.QueueManualPartySync();
            }
        }
    }

    private void DrawGraphCleanupSection(uint territoryId)
    {
        if (ImGui.CollapsingHeader("Graph Cleanup", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("These tools modify the current territory graph. Use them carefully while testing.");

            if (ImGui.Button("Prune Current Graph"))
            {
                var result = NavigationManager.PruneGraph(territoryId);

                Plugin.ChatGui.Print(
                    $"[AetherTrail] Pruned graph. " +
                    $"Removed isolated={result.RemovedIsolatedNodes}, " +
                    $"broken={result.RemovedBrokenLinks}, " +
                    $"self={result.RemovedSelfLinks}, " +
                    $"duplicate={result.RemovedDuplicateLinks}, " +
                    $"merged={result.MergedDuplicateNodes}. " +
                    $"Remaining nodes={result.RemainingNodes}, links={result.RemainingLinks}."
                );
            }

            if (ImGui.Button("Split Crossing Links"))
            {
                int splitCount = NavigationManager.SplitCrossingLinks(territoryId);

                Plugin.ChatGui.Print(
                    splitCount == 0
                        ? "[AetherTrail] No crossing ground links found."
                        : $"[AetherTrail] Split {splitCount} crossing ground link(s)."
                );
            }

            if (ImGui.Button("Clean Redundant Links"))
            {
                int removed = NavigationManager.RemoveRedundantLinks(territoryId);

                Plugin.ChatGui.Print(
                    removed == 0
                        ? "[AetherTrail] No redundant links found."
                        : $"[AetherTrail] Removed {removed} redundant link(s)."
                );
            }

            if (ImGui.Button("Clean Flight Nodes"))
            {
                var result = NavigationManager.CleanFlightNodes(territoryId);

                Plugin.ChatGui.Print(
                    $"[AetherTrail] Removed {result.RemovedFlightNodes} flight node(s). " +
                    $"Remaining nodes={result.RemainingNodes}, links={result.RemainingLinks}."
                );
            }

            if (ImGui.Button("Reset Confidence"))
            {
                int updated = NavigationManager.ResetCurrentTerritoryConfidence(territoryId);

                Plugin.ChatGui.Print($"[AetherTrail] Reset confidence for {updated} link(s).");
            }

            if (ImGui.Button("Save Graph Now"))
            {
                NavigationManager.FlushDirtyGraphsImmediately();
                Plugin.ChatGui.Print("[AetherTrail] Saved dirty graphs.");
            }
        }
    }

    private void DrawDebugSection(uint territoryId)
    {
        if (ImGui.CollapsingHeader("Debug"))
        {
            if (ImGui.Button("Print Territory Debug"))
            {
                var player = Plugin.ObjectTable.LocalPlayer;

                if (player == null)
                {
                    Plugin.ChatGui.Print("[AetherTrail] No local player found.");
                    return;
                }

                Vector3 pos = player.Position;

                Plugin.ChatGui.Print(
                    $"[AetherTrail Debug] Territory={territoryId}, " +
                    $"Position=({pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}), " +
                    $"Room={this.plugin.Configuration.SyncRoomCode}, " +
                    $"Server={this.plugin.Configuration.SyncServerUrl}"
                );
            }

            if (ImGui.Button("Export Current Graph"))
            {
                bool success = NavigationManager.ExportGraph(territoryId, out string path);

                Plugin.ChatGui.Print(
                    success
                        ? $"[AetherTrail] Exported graph to: {path}"
                        : "[AetherTrail] Failed to export graph."
                );
            }

            if (ImGui.Button("Reload Graphs"))
            {
                NavigationManager.ReloadGraphs();
                Plugin.ChatGui.Print("[AetherTrail] Reloaded graphs from disk.");
            }
        }
    }
}
