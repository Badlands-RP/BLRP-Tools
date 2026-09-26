## Start here

BLRP NavMesh creates pedestrian navigation for custom maps. Open it from the NAVMESH tile in BLRP Tools. You can select files, change settings, generate a preview and build a resource entirely in this window.

For your first attempt:

1. In Map & area, add one mapping resource folder.
2. Check the YMAP placement you want to work on. For an interior, start with its MLO placement rather than selecting every map in the resource.
3. Set Game build to the build used by your FiveM server. The initial value is 3095; change it when your server changes.
4. For GTA props, open Game archives and click LOAD BASE GAME. Then click READ MAP BOUNDS & ENTITY SETS in Map & area. Review the area and the active furnishings.
5. Choose a Results folder outside your mapping resources.
6. Click GENERATE PREVIEW. Inspect the surfaces and read the Issues list.
7. Save the project so you can return to the same inputs and settings.

A preview can be generated with incomplete inputs. It is an inspection result, not a ready-to-stream map. BUILD RESOURCE adds stricter checks and requires the original surrounding navigation.

Cool Beans now uses hns_josecafe_mirror_park together with hns_josecafe_base. The earlier cafe and Vespucci Market previews are diagnostic results only. No pilot has passed FiveM movement testing.

## Choose maps and the replacement area

Mapping resources
Add the resource containing the interior, its YTYP definitions and collision. Add other resource folders when they own props used by that map. Adding a folder lists its YMAP files; check only the placements you intend to include.

Maps to include
The map must describe the object in its placed world location. For Cool Beans, add both hns_josecafe_mirror_park and hns_josecafe_base, then select hns_josecafe_mrpark_milo_.ymap from the Mirror Park resource. For the Market pilot, start with gabz_vbm_store01_milo_.ymap. Other shop placements and different furnishings need their own checks.

READ MAP BOUNDS & ENTITY SETS
This fits interiors to their readable YBN collision in the placed world orientation and lists available entity sets. Other objects use their archetype bounds. Enabled game archives supply definitions as well as the selected resource folders. It replaces the current area values, so review your crop after using it. It does not prove that every floor, obstacle or pavement approach has collision coverage.

The Mirror Park cafe's metadata bounds are much larger than its interior. The tool now uses its readable collision to avoid the old 181-metre replacement box. Add both cafe resources and load base game archives to resolve its fence post and wall vent. Approaches still need supplied collision and review. Protected furniture and door models remain separate collision blockers; a successful area fit is not a complete bake.

Replacement box
Min and Max are world X, Y and Z coordinates in metres. The box defines where existing navigation will be replaced. Include the intended floors, entrance and approaches, and provide collision for all of them. Keep unrelated areas outside the box. If bounds cannot be read, add the resource owning the missing archetype or enter a measured area manually.

Active entity sets
Check the furnishings actually enabled on the server. Unchecked sets are excluded; checking every set can combine mutually exclusive shop layouts. After reading map details, an empty selection explicitly means no optional sets.

Conflict scan folders
Add the server resource directory here to find other resources streaming the same navigation tile names. The tool only scans the folders you supply, plus your selected mapping resources.

## Set pedestrian size and collision

Navigation tab
The initial settings are a starting point, not universal GTA values. Check them against your doors, stairs and overhead obstructions.

• Pedestrian height: required headroom above the floor.
• Pedestrian radius: clearance around walls and obstacles. A larger radius can close narrow doorways.
• Maximum step: the highest climbable step.
• Maximum slope: steepest allowed surface, in degrees.
• Cell width / cell height: horizontal and vertical sampling resolution. Smaller values capture more detail and use more memory.
• Raster cell budget: limits the size of one bake. If you hit it, reduce the replacement area before increasing the budget.

Interior navigation applies to the whole generated patch. Keep deliberately different navigation types in separate authored regions. Leave advanced polygon flags at their defaults unless you understand the native flags you need.

Resource dependencies should name the mapping resources that must be present. Separate names with commas.

Extra collision tab
Add readable native YBN collision for surfaces not supplied by the selected maps, such as pavement approaches or creator-provided bounds. Enter the world position, decoded quaternion orientation and scale. A local interior collision file needs the correct placement; adding it at zero will not align it with the map.

Excluded archetypes and Collision review
Exclude an object only when you have established that it has no relevant collision or that another input covers it. Record that reason in Collision review. Excluding everything in the Issues list can produce routes through furniture and walls.

## Select game archives

Use Game archives when placed props or original navigation must come from GTA's game files.

For a quick preview, click LOAD BASE GAME with an empty archive list. It locates a registered GTA V Legacy installation or asks you to select its folder, adds common.rpf and x64 archives in order, and enables game sources. The selection remains unverified for the target build. It does not add a newer installed update or assume the correct DLC set. Existing archive selections are kept; use ADD to extend them.

1. Enable Include assets from game archives.
2. Browse to the GTA Legacy installation.
3. Set Archive build to the build represented by your source files. It must match the project's Game build.
4. Add the relevant base, DLC and update archives, or use IMPORT SOURCE SET to load an existing selection.
5. Review the Original archive path column. A base archive might be x64f.rpf; an update archive might be update/update.rpf. Cached copies need their original archive name/path, not the cache's hash-suffixed filename.
6. Use UP and DOWN to set the override order. Later archives override earlier assets with the same name.
7. Write where the files came from in Source notes. CALCULATE HASHES records their identities.

Only enable Archive selection verified for this game build after establishing the source set's build compatibility. Hashing a newer installation does not make it compatible with 3095. The tool does not automatically reconstruct the server's DLC mount order.

Unverified sources can be used for inspection. A complete resource build requires a verified, reproducible source set. A future server-build change needs fresh source/baseline checks and another in-game test.

## Generate and inspect a preview

Click GENERATE PREVIEW after choosing your maps, world area and settings. The app remains responsive while it loads collision, generates walkable surfaces, connects polygons and checks native save/reload.

Preview controls
• Scroll over the preview to zoom.
• Drag with the left mouse button to pan.
• Double-click or use FIT VIEW to fit the geometry.
• Enable Floor at Z to inspect a height slice around that Z value.
• Enable Collision to view the supplied collision geometry instead of generated navigation.

The preview is a top-down inspection aid. Use the height filter for stacked floors, and inspect native files in BadWalker or collision geometry in Blender when a 3D view is needed.

Issues lists unresolved inputs and checks that need attention. Activity shows progress and verification details. CANCEL stops after the current processing step; completed diagnostics are retained.

PARTIAL PREVIEW — resource build blocked means the visible geometry is incomplete or the source set is unverified. Resolve the listed issues before building. Protected assets are identified explicitly, rather than being treated as empty space.

Each run gets a new folder under Results folder, preserving earlier attempts. OPEN RESULTS opens the latest folder. OPEN PREVIOUS RESULT loads a report and its preview; it does not replace the project settings on the left.

Preview output contains inspection YNVs, editable XML, collision and navigation OBJ files, settings snapshots and a report. Without original surrounding navigation, the YNVs contain only your patch. Do not stream those partial tiles: they would replace the rest of the original tiles.

## Build a resource

Before building a resource, preserve the original navigation for the replacement area and its neighboring tiles.

1. Configure the appropriate game archives and verify the build represented by those sources.
2. In Map & area, click CAPTURE ORIGINAL NAVIGATION and choose a new, empty folder.
3. Check that Original navigation points to that folder and Include original surrounding navigation is enabled. Capturing retains the source set's verification status; it does not certify an unverified source set.
4. Generate another preview to exercise composition with the surrounding navigation.
5. Resolve missing collision, conflicting tile names and disconnected entrance seams.
6. Click BUILD RESOURCE.

A successful build creates a resource folder containing fxmanifest.lua and stream/*.ynv. It is labelled as awaiting FiveM testing. Failed or cancelled staging may remain under resource.pending; use only the completed resource folder.

The tool keeps navigation outside the replacement box and remaps affected connections. It stops if the edit intersects special navigation points/portals or needs unsupported link handling. Those cases require deliberate authoring; do not delete unrelated navigation simply to bypass the check.

Generated surfaces must join preserved navigation in 3D. Different floor heights or border segmentation may require an authored transition. Allow intentionally disconnected navigation is for intended islands, not a way to repair a missing entrance connection.

The app does not install output or alter your server. Review the resource dependencies and test the completed output on a development server before using it in production.

## Test movement in FiveM

A clean bake and a connected graph do not prove that GTA will follow the routes. Test on the exact client build and confirm that the streamed files match the output hashes in report.json.

The packaged fivem/blrp_navmesh_test folder contains an optional development-server helper. Add it as a separate resource alongside the completed candidate navigation resource. The helper creates one local NPC and logs results to the invoking client's F8 console.

1. Stand at the start and enter /navtest_start.
2. Stand at the destination and enter /navtest_goal. Keep the two points 2–200 metres apart.
3. Enter /navtest_run generated blrp_navmesh_candidate, replacing the resource name with your actual candidate resource.
4. Save the JSON result from F8. It includes the game build, candidate bake ID, sampled positions, route results and arrival distance.
5. Use /navtest_cancel to stop a test.

Compare original, generated and disabled-resource cases, using fresh sessions when necessary to avoid cached navigation. Require observed movement, arrival and a sensible route, not just a native result code.

Test pavement → entrance → interior → exit, then the reverse route. Also check wall detours, steps/ramps, open and closed doors, tile boundaries, resource restarts, streaming out/in and neighboring vanilla routes. Walk into walls and over floors yourself to check physical collision separately.

Door behavior needs a runtime policy. MLO room visibility portals do not create navigation links. This baker targets pedestrian navigation; vehicle road routes and special jump/ladder links need separate work.

## Common problems

Nothing is generated
Check the selected YMAP, world placement and Z range. Switch the preview to Collision. Make sure collision covers the requested area and that pedestrian height, radius, slope and step settings allow the intended surfaces. A visible model alone does not establish collision coverage.

Missing owning YTYP / missing drawable or collision
Include the resource or build-matched game archive that owns the object. Verify the actual dependency; a name prefix is not proof of ownership.

Cannot locate object definition while reading the map area
Open Game archives and use LOAD BASE GAME, then read the bounds again. If the definition is still missing, add the actual owning custom resource or matching DLC archive. This warning concerns area selection; the bake separately checks complete collision.

Unreadable or escrowed asset
Request readable collision from the map author, or supply measured replacement collision. The baker cannot inspect protected geometry and does not treat it as empty space.

Unsupported primitive or fragment physics
Use validated, triangulated collision for that object. The app reports unsupported extraction instead of guessing its shape or transforms.

Conflicting asset or archetype
Check whether you selected duplicate resource versions or two competing owners of the same object. Choose the actual active resources and resolve the conflict before baking.

Unverified archive set / archive hash mismatch
Establish that the archives belong to the chosen build. If an input changed, inspect why before recalculating its hash. Rebuild the baseline when its source or target build changes.

Missing baseline neighbor / special navigation point or portal
Capture the required neighboring tiles. If the replacement area intersects special data, review it in a navigation editor and author the transition deliberately.

Generated component does not connect to the baseline
Inspect the doorway and border in 3D. Check floor heights, step clearance and matching edge segmentation. Shrinking a radius or allowing isolated components does not repair a bad seam.

Existing streamed tile conflict
Combine overlapping edits into one owner for each tile. Starting another resource with the same YNV filename does not safely merge the navigation.

Preview works, NPC does not move
Check the exact streamed output, client build, route start/destination, door state and runtime collision. Record the route trace and compare with the original/disabled cases. Offline success is only one part of acceptance.
