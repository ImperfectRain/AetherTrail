using AetherTrail.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;

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
    private const string DebugCommandName = "/atraildebug";
    private const string NodeCommandName = "/atrailnode";
    private const string StatsCommandName = "/atrailstats";
    private const string RecordCommandName = "/atrailrecord";
    private const string ReloadCommandName = "/atrailreload";
    private const string GraphCommandName = "/atrailgraph";
    private const string ExportCommandName = "/atrailexport";
    private const string ImportCommandName = "/atrailimport";
    private const string PruneCommandName = "/atrailprune";
    private const string CleanFlightCommandName = "/atrailcleanflight";
    private const string QuestDebugCommandName = "/atrailquestdebug";
    private const string SyncExportCommandName = "/atrailsyncexport";
    private const string SyncImportCommandName = "/atrailsyncimport";
    private const string SyncPreviewCommandName = "/atrailsyncpreview";
    private const string MapCommandName = "/atrailmap";

    private bool trailEnabled;
    private bool recordingEnabled;

    private readonly TrailRenderer trailRenderer = new();
    private readonly GraphDebugRenderer graphDebugRenderer = new();
    private readonly OverlayRenderer overlayRenderer = new();
    private readonly PartySyncService partySyncService = new();

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

    public TrailRenderer TrailRenderer => this.trailRenderer;



    public Plugin()
    {
        Instance = this;

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        if (Configuration.SyncServerUrl == "http://localhost:5207")
        {
            Configuration.SyncServerUrl = "https://aethertrailsyncserver-production.up.railway.app";
            Configuration.Save();
        }

        recordingEnabled = Configuration.RecordingEnabledByDefault;

        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);
        MapWindow = new MapWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(MapWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle AetherTrail navigation markers toward your current map flag."
        });

        CommandManager.AddHandler(DebugCommandName, new CommandInfo(OnDebugCommand)
        {
            HelpMessage = "Print AetherTrail debug info."
        });
        CommandManager.AddHandler(NodeCommandName, new CommandInfo(OnNodeCommand)
        {
            HelpMessage = "Print your current position as an AetherTrail nav node."
        });
        CommandManager.AddHandler(StatsCommandName, new CommandInfo(OnStatsCommand)
        {
            HelpMessage = "Print AetherTrail graph stats for the current territory."
        });
        CommandManager.AddHandler(RecordCommandName, new CommandInfo(OnRecordCommand)
        {
            HelpMessage = "Toggle AetherTrail graph recording."
        });
        CommandManager.AddHandler(ReloadCommandName, new CommandInfo(OnReloadCommand)
        {
            HelpMessage = "Reload AetherTrail graph files from disk."
        });
        CommandManager.AddHandler(GraphCommandName, new CommandInfo(OnGraphCommand)
        {
            HelpMessage = "Toggle AetherTrail graph debug rendering."
        });
        CommandManager.AddHandler(ExportCommandName, new CommandInfo(OnExportCommand)
        {
            HelpMessage = "Export the current territory's AetherTrail graph."
        });

        CommandManager.AddHandler(ImportCommandName, new CommandInfo(OnImportCommand)
        {
            HelpMessage = "Import the current territory's AetherTrail graph."
        });
        CommandManager.AddHandler(PruneCommandName, new CommandInfo(OnPruneCommand)
        {
            HelpMessage = "Clean up the current territory's AetherTrail graph."
        });
        CommandManager.AddHandler(CleanFlightCommandName, new CommandInfo(OnCleanFlightCommand)
        {
            HelpMessage = "Remove flight-mode nodes from the current AetherTrail graph."
        });
        CommandManager.AddHandler(QuestDebugCommandName, new CommandInfo(OnQuestDebugCommand)
        {
            HelpMessage = "Print AetherTrail quest debug info."
        });
        CommandManager.AddHandler(SyncExportCommandName, new CommandInfo(OnSyncExportCommand)
        {
            HelpMessage = "Export current territory graph as an AetherTrail sync packet."
        });

        CommandManager.AddHandler(SyncImportCommandName, new CommandInfo(OnSyncImportCommand)
        {
            HelpMessage = "Import current territory graph from an AetherTrail sync packet."
        });
        CommandManager.AddHandler(SyncPreviewCommandName, new CommandInfo(OnSyncPreviewCommand)
        {
            HelpMessage = "Preview an AetherTrail sync packet before importing."
        });
        CommandManager.AddHandler(MapCommandName, new CommandInfo(OnMapCommand)
        {
            HelpMessage = "Open the AetherTrail graph map."
        });


        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        Log.Information("AetherTrail loaded.");
    }

    public void Dispose()
    {
        NavigationManager.FlushDirtyGraphsImmediately();

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        MapWindow.Dispose();


        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(DebugCommandName);
        CommandManager.RemoveHandler(NodeCommandName);
        CommandManager.RemoveHandler(StatsCommandName);
        CommandManager.RemoveHandler(RecordCommandName);
        CommandManager.RemoveHandler(ReloadCommandName);
        CommandManager.RemoveHandler(GraphCommandName);
        CommandManager.RemoveHandler(ExportCommandName);
        CommandManager.RemoveHandler(ImportCommandName);
        CommandManager.RemoveHandler(PruneCommandName);
        CommandManager.RemoveHandler(QuestDebugCommandName);
        CommandManager.RemoveHandler(SyncExportCommandName);
        CommandManager.RemoveHandler(SyncImportCommandName);
        CommandManager.RemoveHandler(SyncPreviewCommandName);
        CommandManager.RemoveHandler(CleanFlightCommandName);
        CommandManager.RemoveHandler(MapCommandName);

        Log.Information("AetherTrail unloaded.");
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
        WindowSystem.Draw();

        UpdateRecording();
        UpdateTrailPath();

        trailRenderer.Draw();
        graphDebugRenderer.Draw();

        overlayRenderer.Draw(
            this.trailEnabled,
            this.recordingEnabled,
            this.graphDebugRenderer.Enabled
        );

        partySyncService.Update();
        NavigationManager.FlushDirtyGraphs();
    }

    private void OnDebugCommand(string command, string args)
    {
        var type = GameGui.GetType();

        ChatGui.Print($"AetherTrail Debug: IGameGui runtime type = {type.FullName}");

        foreach (var property in type.GetProperties())
        {
            ChatGui.Print($"Property: {property.Name}");
        }

        foreach (var method in type.GetMethods())
        {
            if (method.Name.Contains("Map") || method.Name.Contains("Flag") || method.Name.Contains("Marker"))
                ChatGui.Print($"Method: {method.Name}");
        }
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

    private void OnCleanFlightCommand(string command, string args)
    {
        uint territoryId = ClientState.TerritoryType;

        var result = NavigationManager.CleanFlightNodes(territoryId);

        ChatGui.Print(
            $"AetherTrail flight cleanup: removed {result.RemovedFlightNodes} flight nodes. " +
            $"Remaining: {result.RemainingNodes} nodes, {result.RemainingLinks} links."
        );
    }

    private void OnQuestDebugCommand(string command, string args)
    {
        uint questId = QuestService.GetTrackedQuestId();
        byte sequence = QuestService.GetTrackedQuestSequence();

        ChatGui.Print($"AetherTrail Quest Debug: tracked quest id = {questId}");
        ChatGui.Print($"AetherTrail Quest Debug: sequence = {sequence}");

        QuestService.PrintTrackedQuestWorkDebug();
        QuestService.PrintQuestSheetDebug(questId);
        QuestService.PrintNearbyObjectsDebug(45f);
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

    private void PrintTypeMembers(Type type)
    {
        ChatGui.Print($"--- {type.Name} ---");

        foreach (var field in type.GetFields())
            ChatGui.Print($"Field: {field.Name} : {field.FieldType.Name}");

        foreach (var property in type.GetProperties())
            ChatGui.Print($"Property: {property.Name} : {property.PropertyType.Name}");

        foreach (var method in type.GetMethods())
        {
            if (method.Name.Contains("Quest", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Map", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Marker", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Open", StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains("Show", StringComparison.OrdinalIgnoreCase))
            {
                ChatGui.Print($"Method: {method.Name}");
            }
        }
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
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
