## Start here

BLRP NavMesh creates pedestrian navigation for custom maps. Open it from the NAVMESH tile in BLRP Tools. You can select files, change settings, generate a preview and build a resource entirely in this window.

For your first attempt:

1. In Map & preview, click ADD MAP RESOURCE and choose the folder containing fxmanifest.lua. A resource with one YMAP placement selects it automatically; otherwise, check the placement you want.
2. Check Server build. It starts at 3095 and remembers your last choice. GTA V Legacy is detected automatically; only use GTA SETTINGS if it was not found. A default results folder is already chosen.
3. Click GENERATE PREVIEW. The tool finds nearby interior dependencies, fits the area and checks the collision. You do not need to enter coordinates or choose archives for an initial preview.
4. Inspect the surfaces. Select a category under Needs attention for an explanation and next step. Open Export when you are ready to prepare a server resource. SAVE PROJECT retains your setup.

Advanced settings reveals area/layout, game sources, pedestrian settings and extra collision. For interiors with optional furnishings, use Area & layout to select the entity sets actually enabled on your server.

A partial preview helps inspect readable geometry. BUILD RESOURCE stays unavailable until a current preview passes the checks and includes original surrounding navigation.

For Cool Beans, add hns_josecafe_mirror_park. Its hns_josecafe_base dependency is detected if it is installed beside it. Protected furniture and door collision still prevent a complete build from these installed files alone. No pilot has passed FiveM movement testing.

## Choose maps and the replacement area

Map resources
Add the resource containing the map. Names are shown without long paths; hover over a name to see its full folder. Adding a resource lists its YMAP files; check only the placements you intend to include.

When fitting the area, the tool looks in sibling resource folders for the selected interior's actual YTYP owner. It adds an unambiguous interior dependency without selecting that dependency's other maps. It does not search your entire server or choose between competing owners. Add other resources manually when they supply props or live elsewhere.

Placements
The map must describe the object in its placed world location. For Cool Beans, select hns_josecafe_mrpark_milo_.ymap from the Mirror Park resource. For the Market pilot, start with gabz_vbm_store01_milo_.ymap. Other shop placements and different furnishings need their own checks.

Automatic area fitting
New projects fit the area before each preview. Interiors use readable YBN collision in the placed world orientation; other objects use archetype bounds. Enabled game archives and selected resources supply definitions. Existing projects retain their saved area unless you enable automatic fitting.

Under Advanced settings → Area & layout, READ MAP BOUNDS & ENTITY SETS also performs this step on demand. It replaces the current coordinates and lists optional layouts. Editing a coordinate turns automatic fitting off so your crop is preserved. Fitting does not prove that every floor, obstacle or pavement approach has collision coverage.

The Mirror Park cafe's metadata bounds are much larger than its interior. Its readable collision gives an area around 30 × 18 metres, instead of the old 181-metre box. Automatically selected base game archives resolve its fence post and wall vent. Approaches and protected furniture/door models still need collision review.

Replacement box — Advanced settings → Area & layout
Min and Max are world X, Y and Z coordinates in metres. The box defines where existing navigation will be replaced. Include the intended floors, entrance and approaches, and provide collision for all of them. Keep unrelated areas outside the box. If bounds cannot be read, add the resource owning the missing archetype or enter a measured area manually.

Active entity sets — Advanced settings → Area & layout
Check the furnishings actually enabled on the server. Unchecked sets are excluded; checking every set can combine mutually exclusive shop layouts. After reading map details, an empty selection explicitly means no optional sets.

Conflict scan folders — Export
Add the server resource directory here to find other resources streaming the same navigation tile names. The tool only scans the folders you supply, plus your selected mapping resources.

## Set pedestrian size and collision

Advanced settings → Pedestrian settings
The initial settings are a starting point, not universal GTA values. Check them against your doors, stairs and overhead obstructions.

• Pedestrian height: required headroom above the floor.
• Pedestrian radius: clearance around walls and obstacles. A larger radius can close narrow doorways.
• Maximum step: the highest climbable step.
• Maximum slope: steepest allowed surface, in degrees.
• Cell width / cell height: horizontal and vertical sampling resolution. Smaller values capture more detail and use more memory.
• Raster cell budget: limits the size of one bake. If you hit it, reduce the replacement area before increasing the budget.

Interior navigation applies to the whole generated patch. Keep deliberately different navigation types in separate authored regions. Leave advanced polygon flags at their defaults unless you understand the native flags you need.

Resource dependencies should name the mapping resources that must be present. Separate names with commas.

Advanced settings → Extra collision
Add readable native YBN collision for surfaces not supplied by the selected maps, such as pavement approaches or creator-provided bounds. Enter the world position, decoded quaternion orientation and scale. A local interior collision file needs the correct placement; adding it at zero will not align it with the map.

Excluded archetypes and Collision review
Exclude an object only when you have established that it has no relevant collision or that another input covers it. Record that reason in Collision review under Pedestrian settings. Excluding every reported object can produce routes through furniture and walls.

## Select game archives

GTA V Legacy is selected automatically when opening a new project. The tool tries your remembered NavMesh folder, the Livery Tool's saved folder, then registered GTA installations. It remembers a manual choice for next time, along with your server build and results folder.

Automatic setup includes common.rpf and x64 archives in order. It leaves build compatibility unverified and does not add a newer installed update or assume the right DLC set. An existing project's explicit archive selection is preserved. A project saved with game sources disabled stays that way.

Use GTA SETTINGS or Advanced settings → Game sources for manual setup and verification:

1. If GTA was not detected, click CHANGE GTA FOLDER. This replaces the base archive selection and remembers the folder. IMPORT SOURCE SET can restore an authored selection.
2. Enable Include assets from game archives if it is off.
3. Set Archive build to the build represented by your source files. It must match Server build.
4. Add the relevant base, DLC and update archives, or use IMPORT SOURCE SET to load an existing selection.
5. Review the Original archive path column. A base archive might be x64f.rpf; an update archive might be update/update.rpf. Cached copies need their original archive name/path, not the cache's hash-suffixed filename.
6. Use UP and DOWN to set the override order. Later archives override earlier assets with the same name.
7. Write where the files came from in Source notes. CALCULATE HASHES records their identities.

Only enable Archive selection verified for this game build after establishing the source set's build compatibility. Hashing a newer installation does not make it compatible with 3095. The tool does not automatically reconstruct the server's DLC mount order.

Unverified sources can be used for inspection. A complete resource build requires a verified, reproducible source set. A future server-build change needs fresh source/baseline checks and another in-game test.

## Generate and inspect a preview

Click GENERATE PREVIEW after choosing your map. With automatic fitting enabled, the area is read first. The app remains responsive while it loads collision, generates walkable surfaces, connects polygons and checks native save/reload.

Preview controls
• Scroll over the preview to zoom.
• Drag with the left mouse button to pan.
• Double-click or use FIT VIEW to fit the geometry.
• Enable Floor height to inspect a height slice around that Z value. Area fitting starts the value near the map's lowest surface.
• Enable Show collision to view the supplied collision geometry instead of generated navigation.

The preview is a top-down inspection aid. Use the height filter for stacked floors, and inspect native files in BadWalker or collision geometry in Blender when a 3D view is needed.

Needs attention groups unresolved inputs by their cause. Click a category to see what to do next, then scroll its Details for the complete list of affected objects. Technical log shows processing and verification details. CANCEL stops after the current processing step; completed diagnostics are retained.

PARTIAL PREVIEW — resource build blocked means the visible geometry is incomplete or the source set is unverified. Resolve the listed issues before building. Protected assets are identified explicitly, rather than being treated as empty space.

Each run gets a new folder under Save results to, preserving earlier attempts. OPEN RESULTS opens the latest folder. OPEN PREVIOUS RESULT loads a report and its preview; it does not replace the project settings or enable export. Generate a fresh preview after changing inputs.

Preview output contains inspection YNVs, editable XML, collision and navigation OBJ files, settings snapshots and a report. Without original surrounding navigation, the YNVs contain only your patch. Do not stream those partial tiles: they would replace the rest of the original tiles.

## Build a resource

Before building a resource, preserve the original navigation for the replacement area and its neighboring tiles.

1. Configure the appropriate game archives and verify the build represented by those sources.
2. Open Export and click PREPARE ORIGINAL NAVIGATION. A new folder is created inside your results folder automatically.
3. Original navigation is set to that folder and Include original surrounding navigation is enabled. Preparing retains the source set's verification status; it does not certify an unverified source set.
4. Generate another preview to exercise composition with the surrounding navigation.
5. Resolve missing collision, conflicting tile names and disconnected entrance seams.
6. Click BUILD RESOURCE once the current preview has no outstanding issues and includes original navigation. The Export tab explains what is still needed; changing inputs requires another preview.

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
Check the selected YMAP, world placement and Z range. Enable Show collision. Make sure collision covers the requested area and that pedestrian height, radius, slope and step settings allow the intended surfaces. A visible model alone does not establish collision coverage.

Missing owning YTYP / missing drawable or collision
Include the resource or build-matched game archive that owns the object. Verify the actual dependency; a name prefix is not proof of ownership.

Cannot locate object definition while reading the map area
Use GTA SETTINGS if GTA was not detected, then preview again. If the definition is still missing, add the actual owning custom resource or a matching DLC archive under Advanced settings → Game sources. This warning concerns area selection; the bake separately checks complete collision.

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
