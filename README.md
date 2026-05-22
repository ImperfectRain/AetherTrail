# AetherTrail

AI Usage Disclosure

AI has been used for documentation analysis, structure/code review, function rewrites, and function replacements when I personally could not figure out a clean solution. The most notable areas were the map rendering stage and early node/pathfinding systems. I understand how the code functions, I am actively cleaning it up, and I can field questions about the implementation.

Notice

This plugin has optional networked sync features. Graph sync and party position sync connect to a configured sync server, use a shared room code, and stay off unless the user enables them after accepting the network disclaimer. If you are uncomfortable with external sync features, keep them disabled or do not install the plugin until the server side has been reviewed by someone more knowledgeable than I am.

AetherTrail started as a way to draw world-space navigation toward quest and map-flag data without relying on another navigation plugin. It has grown into a personal navigation graph system. As you move through the overworld, the plugin records nodes, links them into paths, and slowly builds a local graph of where your character has actually traveled.

The short version is that as you walk, you create a personal navmesh. Over time, that graph becomes more useful for routing, map review, imports, exports, and party collaboration.

## What AetherTrail Does

AetherTrail records movement through outdoor zones and turns that movement into a node graph. When you ask it to route toward a target, it tries to use the graph first, then falls back to a direct trail when it does not have enough local data yet.

The system is intentionally incremental. A brand new user will not have a complete map on day one unless they import data or sync with someone else. The graph improves as the user travels, imports graph data, or collaborates with friends.

## Current Player-Facing Features

### World-Space Navigation Trail

`/trailflag` toggles the main navigation trail. When enabled, AetherTrail looks for a target, usually your current map flag or tracked quest target, and draws a trail in the world.

The trail prefers known graph routes when available. If the plugin does not have enough nodes for the current area yet, it can still draw a simpler direct path so the feature remains useful while the graph is young.

### Automatic Node Recording

AetherTrail can record your movement into local graph files. Nodes are placed as you travel, and links are created between nearby movement points. The idea is similar to desire paths, where the routes you actually walk become the routes the plugin trusts more.

Recording can be enabled by default in settings. The graph is stored locally in the XIVLauncher plugin configuration folder.

### Confidence-Based Routing

Links have confidence values. Local movement and repeated traversal can make a route more trusted, while imported or less-used routes start more cautiously.

This is meant to help the pathfinder prefer reliable, commonly traveled paths instead of treating every imported or newly discovered link as equally trustworthy.

### Ground and Flight Awareness

AetherTrail tracks traversal mode so walking routes and flying routes do not get treated as the same thing. This matters because a route that works in the air can be awful or impossible on foot.

There are cleanup tools for removing flight nodes from the current territory if a graph gets polluted while testing.

### Map Window

The map window shows the current territory graph over the loaded in-game map texture. From there, you can view and adjust things like

- current territory and map info
- node and link display
- ground/flying/unknown visibility toggles
- confidence, density, and traversal display modes
- active route display
- player position display
- zoom, pan, reset view, center player, and fit graph controls

### Import and Export

The main window has buttons to export and import the current territory graph.

Export writes the current territory graph to the plugin config export folder. Import reads a graph file from the import folder and merges it into your current graph instead of replacing your progress.

Importing is different from manually replacing files. AetherTrail attempts to merge useful incoming nodes, avoid duplicate nearby nodes, preserve existing local data, and clean up the result. This is intended to make graph sharing less destructive.

### Network Sync

Graph sync is optional. A user can create or join a room code, then sync graph data with other users using the same room.

When enabled, graph sync can upload and download graph data for the current territory and merge downloaded data into the local graph. Party position sync is a separate opt-in feature. When enabled, it shares anonymous presence data so synced players can appear in the world overlay and on the AetherTrail map using their chosen marker colors.

The feature is meant for friends, mentors, or small groups that want to build and improve navigation data together. It is not required for normal local use.

### Tools Window

The tools window is for maintenance and testing. It shows current graph status and gives access to cleanup and sync actions.

Current tool actions include syncing the current territory, pruning the current graph, splitting crossing links, cleaning redundant links, cleaning flight nodes, resetting link confidence, saving dirty graphs immediately, and exporting or reloading graph data.

These tools can modify the current territory graph, so they are mainly intended for testing, cleanup, and recovery.

### Settings

The configuration window exposes the main tuning options.

- recording enabled by default
- data overlay visibility
- node spacing
- corner node spacing
- direction-change sensitivity
- teleport reset distance
- session attach distance
- trail dot size
- trail dot spacing
- trail colors
- graph debug draw distance

Most users should not need to touch every setting. They are exposed because the plugin is still being tuned and different movement styles can produce different graph quality.

## Commands

- `/trailflag` toggles the world-space trail.
- `/atrailnode` prints the current player position as a nav node.
- `/atrailstats` prints graph stats for the current territory.
- `/atrailrecord` toggles graph recording.
- `/atrailreload` reloads graph files from disk.
- `/atrailgraph` toggles world-space graph rendering.
- `/atrailexport` exports the current territory graph.
- `/atrailimport` imports the current territory graph.
- `/atrailprune` cleans the current territory graph.
- `/atrailcleanflight` removes flight-mode nodes from the current territory graph.
- `/atrailsplitcrossings` splits crossing ground links.
- `/atrailcleanlinks` removes redundant overlapping links.
- `/atrailresetconfidence` resets link confidence in the current territory.
- `/atrailsyncexport` exports a sync packet.
- `/atrailsyncimport` imports a sync packet.
- `/atrailsyncpreview` previews a sync packet before importing.
- `/atrailmap` opens the map window.
- `/atrailtools` opens the tools window.

## Network and Privacy Notes

Local graph recording does not require the sync server.

Graph sync and party position sync are the networked parts of the plugin. Graph sync sends graph data to the configured sync server for the room code you enter. Party position sync is separate and sends anonymous presence data only while it is enabled. The room code is not strong security; it is a shared passkey for small-group coordination.

## Current State

Currently implemented

- local movement recording
- local node graph storage
- graph cleanup tools
- map-flag and quest-target pathing
- world-space trail rendering
- world-space graph rendering
- top-down map graph view
- import/export graph sharing
- confidence-aware pathfinding
- ground/flying traversal separation
- optional party graph sync
- optional party presence display

Still experimental

- graph quality varies heavily by how much a zone has been traveled
- network sync needs more privacy/security review
- route quality can still be rough in awkward terrain
- visual presentation is functional but not final
- cleanup tools are powerful and still somewhat tester-oriented

## TO-DO / Wishlist

- Improve route visuals so the trail feels more like a natural aether current and less like a debug line.
- Continue improving confidence scoring so trusted routes, imported routes, risky routes, and locked routes are easier to reason about.
- Add stronger review documentation for all network behavior and server-side data handling.
- Decide whether network sync belongs in the final review build or should remain an optional/experimental feature.
- Provide optional starter graph files for users who want navigation help without participating in sync.
- Improve map overlay polish, including clearer legends and better visual hierarchy.
- Keep splitting large implementation files into smaller, easier-to-review systems.
- Add tests for graph merge, pruning, confidence, and pathfinding behavior.
- Revisit tools/debug UI so public builds expose only what normal users actually need.
