# AetherTrail Architecture Notes

This project is organized around feature responsibility rather than file size.

## Root

- `Plugin.cs` wires Dalamud services, commands, windows, per-frame updates, and high-level plugin lifecycle.
- `Configuration.cs` stores user settings and configuration migrations.
- `AetherTrail.json` is the Dalamud plugin manifest.

## Core

- `Core/Graph` contains navigation graph data structures, confidence scoring, mutation queues, and graph cleanup result models.
- `Core/Navigation` contains route construction, target resolution, pathfinding entry points, and flag path helpers.
- `Core/Trails` contains trail path models shared by navigation and rendering.

## Rendering

Rendering code draws world-space overlays, trails, debug graph views, party markers, and status overlays. It should consume snapshots or simple models from `Core`/`Services` rather than owning graph state.

## Services

- `Services/Chat` owns party chat message transport and persistence.
- `Services/Map` owns map coordinate helpers and map flag integration.
- `Services/Party` owns party sync orchestration and party presence state.
- `Services/Quest` owns quest target lookup and quest-related game data access.
- `Services/Sync` owns HTTP sync transport and sync packet models.
- `Services/Ui` owns UI occlusion helpers used by renderers and windows.

## Windows

Dalamud window classes live in `Windows`. Window code should stay focused on presentation and user actions; reusable behavior belongs in `Core`, `Rendering`, or `Services`.

## Navigation Manager Split

`NavigationManager` is a partial static facade split by responsibility:

- `NavigationManager.GraphStore.cs` loads, saves, caches, reloads, and flushes graph files.
- `NavigationManager.GraphCleanup.cs` prunes graphs, removes redundant links, splits crossings, removes flight nodes, and resets confidence.
- `NavigationManager.GraphLinks.cs` owns link creation, link removal, traversal checks, intersection splitting, and link confidence helpers.
- `NavigationManager.Pathing.cs` builds trail paths from graph routes or direct fallback paths.
- `NavigationManager.Recording.cs` records player movement into graph nodes and links.
- `NavigationManager.ImportExport.cs` imports, exports, validates, bounds-checks, and merges graph files.
- `NavigationManager.SyncPackets.cs` creates, previews, imports, and exports sync packets.

The next cleanup target should be replacing the static facade with injectable services once tests exist for graph merge, pruning, recording, and path construction.
