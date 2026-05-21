using AetherTrail.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Threading.Tasks;
using System.IO;
using System.Numerics;

namespace AetherTrail;

public sealed class Plugin : IDalamudPlugin
{
    public static Plugin Instance { get; private set; } = null!;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private const string CommandName = "/trailflag";
    private const string NodeCommandName = "/atrailnode";
    private const string StatsCommandName = "/atrailstats";
    private const string RecordCommandName = "/atrailrecord";
    private const string ReloadCommandName = "/atrailreload";
    private const string GraphCommandName = "/atrailgraph";
    private const string ExportCommandName = "/atrailexport";
    private const string ImportCommandName = "/atrailimport";
    private const string PruneCommandName = "/atrailprune";
    private const string CleanFlightCommandName = "/atrailcleanflight";
    private const string SplitCrossingsCommandName = "/atrailsplitcrossings";
    private const string CleanRedundantLinksCommandName = "/atrailcleanlinks";
    private const string SyncExportCommandName = "/atrailsyncexport";
    private const string SyncImportCommandName = "/atrailsyncimport";
    private const string SyncPreviewCommandName = "/atrailsyncpreview";
    private const string MapCommandName = "/atrailmap";
    private const string ToolsCommandName = "/atrailtools";

    private bool trailEnabled;
    private bool recordingEnabled;
    private bool disposed;

    private readonly TrailRenderer trailRenderer = new();
    private readonly GraphDebugRenderer graphDebugRenderer = new();
    private readonly OverlayRenderer overlayRenderer = new();
    private readonly PartySyncService partySyncService = new();
    private readonly PartyPresenceWorldRenderer partyPresenceWorldRenderer = new();

    private DateTime lastPathUpdate = DateTime.MinValue;
    private const double PathUpdateIntervalSeconds = 0.20;
    private Vector3? lastTargetPosition;
    private DateTime lastForcedPathRefresh = DateTime.MinValue;

    private const float TargetChangedDistance = 3.0f;
    private const float RouteDeviationDistance = 10.0f;
    private const double ForcedPathRefreshSeconds = 1.5;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("AetherTrail");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private MapWindow MapWindow { get; init; }
    private readonly ToolsWindow toolsWindow;

    public TrailRenderer TrailRenderer => this.trailRenderer;

    public Plugin()
    {
        Instance = this;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Migrate();

        if (Configuration.SyncServerUrl == "http://localhost:5207")
        {
            Configuration.SyncServerUrl = "https://aethertrailsyncserver.loplop6754loplop.workers.dev";
            Configuration.Save();
        }

        recordingEnabled = Configuration.RecordingEnabledByDefault;

        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);
        MapWindow = new MapWindow(this);
        this.toolsWindow = new ToolsWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(MapWindow);
        this.WindowSystem.AddWindow(this.toolsWindow);

        RegisterCommands();
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("AetherTrail loaded.");
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;

        DisposeStep("unregister UI callbacks", () =>
        {
            PluginInterface.UiBuilder.Draw -= DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        });

        DisposeStep("stop party sync", this.partySyncService.Dispose);
        DisposeStep("clear queued graph mutations", GraphMutationQueue.Clear);
        DisposeStep("flush dirty graphs", NavigationManager.FlushDirtyGraphsImmediately);
        DisposeStep("clear party presence", PartyPresenceService.Clear);
        DisposeStep("remove windows", WindowSystem.RemoveAllWindows);

        DisposeStep("dispose config window", ConfigWindow.Dispose);
        DisposeStep("dispose main window", MainWindow.Dispose);
        DisposeStep("dispose map window", MapWindow.Dispose);

        DisposeStep("unregister commands", UnregisterCommands);

        Log.Information("AetherTrail unloaded.");
    }

    private static void DisposeStep(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"AetherTrail unload step failed: {name}");
        }
    }

    private void RegisterCommands()
    {
        AddCommand(CommandName, OnCommand, "Toggle AetherTrail navigation markers toward your current map flag.");
        AddCommand(NodeCommandName, OnNodeCommand, "Print your current position as an AetherTrail nav node.");
        AddCommand(StatsCommandName, OnStatsCommand, "Print AetherTrail graph stats for the current territory.");
        AddCommand(RecordCommandName, OnRecordCommand, "Toggle AetherTrail graph recording.");
        AddCommand(ReloadCommandName, OnReloadCommand, "Reload AetherTrail graph files from disk.");
        AddCommand(GraphCommandName, OnGraphCommand, "Toggle AetherTrail graph debug rendering.");
        AddCommand(ExportCommandName, OnExportCommand, "Export the current territory's AetherTrail graph.");
        AddCommand(ImportCommandName, OnImportCommand, "Import the current territory's AetherTrail graph.");
        AddCommand(PruneCommandName, OnPruneCommand, "Clean up the current territory's AetherTrail graph.");
        AddCommand(SplitCrossingsCommandName, OnSplitCrossingsCommand, "Split existing crossing ground links in the current AetherTrail graph.");
        AddCommand(CleanRedundantLinksCommandName, OnCleanRedundantLinksCommand, "Remove redundant overlapping AetherTrail graph links in the current territory.");
        AddCommand(CleanFlightCommandName, OnCleanFlightCommand, "Remove flight-mode nodes from the current AetherTrail graph.");
        AddCommand("/atrailresetconfidence", OnResetConfidenceCommand, "Reset all AetherTrail link confidence in the current territory to default.");
        AddCommand(SyncExportCommandName, OnSyncExportCommand, "Export current territory graph as an AetherTrail sync packet.");
        AddCommand(SyncImportCommandName, OnSyncImportCommand, "Import current territory graph from an AetherTrail sync packet.");
        AddCommand(SyncPreviewCommandName, OnSyncPreviewCommand, "Preview an AetherTrail sync packet before importing.");
        AddCommand(MapCommandName, OnMapCommand, "Open the AetherTrail graph map.");
        AddCommand(ToolsCommandName, OnToolsCommand, "Open the AetherTrail tools window.");
    }

    private static void AddCommand(string command, IReadOnlyCommandInfo.HandlerDelegate handler, string helpMessage)
    {
        CommandManager.AddHandler(command, new CommandInfo(handler)
        {
            HelpMessage = helpMessage
        });
    }

    private static void UnregisterCommands()
    {
        foreach (string command in new[]
        {
            CommandName,
            NodeCommandName,
            StatsCommandName,
            RecordCommandName,
            ReloadCommandName,
            GraphCommandName,
            ExportCommandName,
            ImportCommandName,
            PruneCommandName,
            SyncExportCommandName,
            SyncImportCommandName,
            SyncPreviewCommandName,
            CleanFlightCommandName,
            MapCommandName,
            SplitCrossingsCommandName,
            CleanRedundantLinksCommandName,
            ToolsCommandName,
            "/atrailresetconfidence"
        })
        {
            CommandManager.RemoveHandler(command);
        }
    }

    private void UpdateRecording()
    {
        if (!this.recordingEnabled)
            return;

        var player = ObjectTable.LocalPlayer;

        if (player == null)
            return;

        uint territoryId = ClientState.TerritoryType;

        NavigationManager.RecordPlayerPosition(territoryId, player.Position);
    }

    private void UpdateTrailPath()
    {
        if (!this.trailEnabled)
            return;

        if ((DateTime.UtcNow - this.lastPathUpdate).TotalSeconds < PathUpdateIntervalSeconds)
            return;

        this.lastPathUpdate = DateTime.UtcNow;

        var player = ObjectTable.LocalPlayer;

        if (player == null)
            return;

        if (!TargetResolver.TryGetTarget(out var target))
        {
            this.trailRenderer.ClearPath();
            this.lastTargetPosition = null;
            return;
        }

        Vector3 playerPosition = player.Position;
        Vector3 targetPosition = target.Position;

        bool targetChanged =
            !this.lastTargetPosition.HasValue ||
            Vector3.Distance(this.lastTargetPosition.Value, targetPosition) > TargetChangedDistance;

        bool playerOffRoute =
            !this.trailRenderer.IsPlayerNearCurrentPath(playerPosition, RouteDeviationDistance);

        bool forcedRefresh =
            (DateTime.UtcNow - this.lastForcedPathRefresh).TotalSeconds > ForcedPathRefreshSeconds;

        if (!targetChanged && !playerOffRoute && !forcedRefresh)
            return;

        uint territoryId = ClientState.TerritoryType;

        this.trailRenderer.SetPath(
            NavigationManager.GetPath(territoryId, playerPosition, targetPosition)
        );

        this.lastTargetPosition = targetPosition;
        this.lastForcedPathRefresh = DateTime.UtcNow;
    }

    private void DrawUI()
    {
        if (this.disposed)
            return;

        NativeUiOcclusionService.BeginFrame();
        UiOcclusionService.BeginFrame();
        GraphMutationQueue.Process();

        WindowSystem.Draw();

        UpdateRecording();
        UpdateTrailPath();

        trailRenderer.Draw();
        graphDebugRenderer.Draw();
        partyPresenceWorldRenderer.Draw();

        overlayRenderer.Draw(
            this.trailEnabled,
            this.recordingEnabled,
            this.graphDebugRenderer.Enabled
        );

        partySyncService.Update();
        NavigationManager.FlushDirtyGraphs();
    }

    private void OnNodeCommand(string command, string args)
    {
        var player = ObjectTable.LocalPlayer;

        if (player == null)
        {
            ChatGui.Print("AetherTrail: player not found.");
            return;
        }

        var pos = player.Position;

        ChatGui.Print($"new NavNode {{ Id = \"node_id\", Position = new Vector3({pos.X:F2}f, {pos.Y:F2}f, {pos.Z:F2}f), Links = new List<string>() }},");
    }

    private void OnStatsCommand(string command, string args)
    {
        uint territoryId = ClientState.TerritoryType;
        int nodeCount = NavigationManager.GetNodeCount(territoryId);
        int linkCount = NavigationManager.GetLinkCount(territoryId);

        ChatGui.Print($"AetherTrail: territory {territoryId} has {nodeCount} nodes and {linkCount} links.");
    }

    private void OnRecordCommand(string command, string args)
    {
        this.recordingEnabled = !this.recordingEnabled;

        ChatGui.Print(this.recordingEnabled
            ? "AetherTrail recording enabled."
            : "AetherTrail recording disabled.");
    }

    private void OnReloadCommand(string command, string args)
    {
        NavigationManager.ReloadGraphs();
        ChatGui.Print("AetherTrail graphs reloaded from disk.");
    }

    private void OnGraphCommand(string command, string args)
    {
        graphDebugRenderer.Enabled = !graphDebugRenderer.Enabled;

        ChatGui.Print(graphDebugRenderer.Enabled
            ? "AetherTrail graph debug enabled."
            : "AetherTrail graph debug disabled.");
    }

    private void OnExportCommand(string command, string args)
    {
        ExportCurrentTerritoryGraph();
    }

    private void OnImportCommand(string command, string args)
    {
        ImportCurrentTerritoryGraph();
    }

    private void OnPruneCommand(string command, string args)
    {
        PruneCurrentTerritoryGraph();
    }

    private void OnSplitCrossingsCommand(string command, string args)
    {
        uint territoryId = ClientState.TerritoryType;

        ChatGui.Print($"[AetherTrail] Splitting crossing links for territory {territoryId}...");

        int splitCount = NavigationManager.SplitCrossingLinks(territoryId);

        ChatGui.Print(
            splitCount == 0
                ? "[AetherTrail] No crossing ground links found."
                : $"[AetherTrail] Split {splitCount} crossing ground link(s)."
        );
    }

    private void OnCleanRedundantLinksCommand(string command, string args)
    {
        uint territoryId = Plugin.ClientState.TerritoryType;

        Plugin.ChatGui.Print($"[AetherTrail] Cleaning redundant links for territory {territoryId}...");

        int removed = NavigationManager.RemoveRedundantLinks(territoryId);

        Plugin.ChatGui.Print(
            removed == 0
                ? "[AetherTrail] No redundant links found."
                : $"[AetherTrail] Removed {removed} redundant link(s)."
        );
    }

    private void OnCleanFlightCommand(string command, string args)
    {
        uint territoryId = ClientState.TerritoryType;

        var result = NavigationManager.CleanFlightNodes(territoryId);

        ChatGui.Print(
            $"AetherTrail flight cleanup: removed {result.RemovedFlightNodes} flight nodes. " +
            $"Remaining: {result.RemainingNodes} nodes, {result.RemainingLinks} links."
        );
    }

    private void OnResetConfidenceCommand(string command, string args)
    {
        uint territoryId = Plugin.ClientState.TerritoryType;

        int updatedLinks = NavigationManager.ResetCurrentTerritoryConfidence(territoryId);

        Plugin.ChatGui.Print(
            $"AetherTrail reset confidence for territory {territoryId}. Updated {updatedLinks} links."
        );
    }

    private void OnSyncExportCommand(string command, string args)
    {
        uint territoryId = ClientState.TerritoryType;

        if (NavigationManager.ExportSyncPacket(territoryId, out string path))
            ChatGui.Print($"AetherTrail sync packet exported: {path}");
        else
            ChatGui.Print("AetherTrail failed to export sync packet.");
    }

    private void OnSyncImportCommand(string command, string args)
    {
        uint territoryId = ClientState.TerritoryType;

        if (NavigationManager.ImportSyncPacket(territoryId, out string path, out int importedNodes))
            ChatGui.Print($"AetherTrail sync packet imported: {importedNodes} new nodes from {path}");
        else
            ChatGui.Print($"AetherTrail sync import failed. Put packet here: {path}");
    }

    private void OnSyncPreviewCommand(string command, string args)
    {
        uint territoryId = ClientState.TerritoryType;

        if (!NavigationManager.PreviewSyncPacket(territoryId, out string path, out var preview))
        {
            ChatGui.Print($"AetherTrail sync preview failed: {preview.Message} Path: {path}");
            return;
        }

        ChatGui.Print(
            $"AetherTrail sync preview: Territory={preview.TerritoryId}, " +
            $"Sender={preview.SenderId}, PacketNodes={preview.PacketNodes}, " +
            $"New={preview.NewNodes}, Duplicates={preview.DuplicateNodes}, " +
            $"RejectedInvalid={preview.RejectedInvalidNodes}, " +
            $"RejectedOutOfBounds={preview.RejectedOutOfBoundsNodes}"
        );
    }

    private void OnMapCommand(string command, string args)
    {
        MapWindow.Toggle();
    }

    private void OnToolsCommand(string command, string args)
    {
        this.toolsWindow.Toggle();
    }

    public void ExportCurrentTerritoryGraph()
    {
        uint territoryId = ClientState.TerritoryType;

        if (NavigationManager.ExportGraph(territoryId, out string exportPath))
        {
            ChatGui.Print($"AetherTrail exported territory {territoryId} graph to: {exportPath}");
        }
        else
        {
            ChatGui.Print($"AetherTrail failed to export territory {territoryId} graph.");
        }
    }

    public void ImportCurrentTerritoryGraph()
    {
        uint territoryId = ClientState.TerritoryType;

        if (NavigationManager.ImportGraph(territoryId, out string importPath, out int importedNodes))
        {
            ChatGui.Print($"AetherTrail imported {importedNodes} new nodes from: {importPath}");
        }
        else
        {
            ChatGui.Print($"AetherTrail import failed. Put a graph file here first: {importPath}");
        }
    }

    private void OnCommand(string command, string args)
    {
        this.trailEnabled = !this.trailEnabled;
        this.trailRenderer.Enabled = this.trailEnabled;

        if (this.trailEnabled)
        {
            this.lastPathUpdate = DateTime.MinValue;
            this.lastForcedPathRefresh = DateTime.MinValue;
            this.lastTargetPosition = null;
            ChatGui.Print("AetherTrail enabled.");
        }
        else
        {
            this.trailRenderer.ClearPath();
            ChatGui.Print("AetherTrail disabled.");
        }
    }

    public void PruneCurrentTerritoryGraph()
    {
        uint territoryId = ClientState.TerritoryType;

        var result = NavigationManager.PruneGraph(territoryId);

        ChatGui.Print(
            $"AetherTrail pruned territory {territoryId}: " +
            $"{result.RemovedIsolatedNodes} isolated nodes, " +
            $"{result.RemovedBrokenLinks} broken links, " +
            $"{result.RemovedSelfLinks} self-links, " +
            $"{result.RemovedDuplicateLinks} duplicate links removed, " +
            $"{result.MergedDuplicateNodes} duplicate nodes merged. " +
            $"Remaining: {result.RemainingNodes} nodes, {result.RemainingLinks} links."
        );
    }

    public void QueueManualPartySync()
    {
        if (!this.Configuration.PartySyncEnabled)
        {
            ChatGui.Print("[AetherTrail] Party sync is disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(this.Configuration.SyncRoomCode))
        {
            ChatGui.Print("[AetherTrail] No party sync room code set.");
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await this.partySyncService.SyncCurrentTerritory();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Manual AetherTrail party sync failed.");
                ChatGui.Print($"[AetherTrail] Manual sync failed: {ex.Message}");
            }
        });
    }

    public void ToggleToolsWindow()
    {
        this.toolsWindow.Toggle();
    }
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}

