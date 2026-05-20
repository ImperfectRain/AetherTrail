AI Usage Disclosure: used for documentation analysis, structure/code review, function rewrites, function replacements when I personally was unable to figure out a solution (Notably during map rendering stage and initial node systems.) I understand how the code functions and am working on manually cleaning the project up, I can field any questions.

NOTICE: this plugin currently connects to a railway server for party sync. It is purely opt-in, and you connect to other users via a shared passkey, however those with security concerns should not install this plugin until the server structure has been reviewed by someone more knowledgable than I.

This project started as a way to display worldspace navigation towards quest data, and has now evolved into something I feel is quite feature rich. The inital issue I ran into was how I was supposed to draw navigation into the world without relying on any dependancies. The main way to get that data was by involving a pre-existing plugin that cannot be used if this was to be an official repo submission.

Nodes are dynamically plotted as you travel the overworld, and linked together in chains. This means if you start from scratch and don't use then file sharing server or manual imports/exports, you will have no navigation stored to XIVlauncher's pluginconfig folder, but as you travel the world and make a more consistent high-quality node network, the pathfinding gets more and more reliable as your character learn's the world layout, essentially the concept of desire paths in real life but translated into a game mechanic. TL;DR, as you walk YOU create a personal navmesh.

Importing/Exporting
Placing this section high on the readme as it's one of the more convoluted features both on the backend and user-experience. There are two (technically 3) ways of doing this. Manual file merging/loading, or the yet to be finalized server syncing. To start if you open the AetherTrails window, you will see an import and export button, self explanatory. These buttons will output your node graph for the current zone into the GraphExport folder in pluginconfig, and import will load files in the GraphImport folder. This allows you to share your progress to other players to give them an edge when it comes to navigation, or be on the receiving end either for filling in an empty map, or improving the quality (hopefully) of your current and incomplete node graph. There's also the method of manually replacing the files in your nodegraphs folder with other ones, but then you lose access to your progress that you walked oh so hard for. But wait, what's the difference between swapping files and importing them, doesn't it accomplish the same thing? WRONG, importing a file will take nodes that you currently don't have, place them in your existing node graph, and then clean it up. Redundant nodes? Gone. Nodes close but not quite on your nodes? Vanished. Basically it interpolates the two files and gives you a consistent output of both inputs. This means if you and a friend have taken close but not identical paths, you don't need to worry about your graph being filled with uneeded data. If I am remembering the default params right, there can only be one node every 5 yalms in most use cases, so it really will take the visual of clean paths and a quaint little wireframe of paths travelled. There's also party sync. Little shaky on it right now, but you can press a button to generate a code, give that code to a friend, and then if you two are in the same area you both upload, download, and merge your graphs automatically. This feature is intended to be an enjoyable form of collaboration between groups of friends, or an easy way for mentors to provide data to new players.

Features
Currently implemented
-import/export
-server framework
-quest pathfinding to the best of my ability
-more debug messages than is healthy to leave in a public build
-a map window that pulls the loaded map texture and overlays a top-down view of your area's nodes
-flight paths (this is a good one, basically if you fly a lot and then want to walk, the pathfinding trail won't tell your WoL to start walking into the sky)
-visible full node network in worldspace, see literally everywhere you or one of your import providers have walked
-low performance impact (initial testing pushed my 60fps below my usual, not frames are stable and match my regular plugin-disapled FPS)
-Confidence system so that routes take you through the most travelled paths, and steer you away from areas you may not want to (or can't) go.

TO-DO/Wishlist
-improved mapping and possibly the ability to overlay features on the vanilla map window
-more feature rich confidence system with stat locks and more variables
-better pathing trail, currently is a big mess but hey, the blue line will mostly get you from A-B
-installed optional node masterfile, I would like to provide files for all important areas for users who may not want to participate in the player interaction systems and who also value the questing/flag help more than the personalized desire pathing
-much better visuals, the blue snake looks awful, eventually would like something that looks like a naturalistic aether current

