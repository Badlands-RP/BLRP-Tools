# BLRP NavMesh

Desktop workflow for turning collision into connected native GTA V YNV tiles. Open **NAVMESH** in the BLRP Tools Hub, or double-click `BLRP.NavMesh.exe`. Windows, .NET 8, GTA Legacy. It reuses CodeWalker.Core and Recast; no 3ds Max installation is needed for generation.

The baker is implemented and passes offline checks. **Pilot outputs are diagnostic only, not approved streaming resources.** Cool Beans now uses `hns_josecafe_mirror_park` and `hns_josecafe_base`; the earlier cafe results below refer to the old Vespucci resource. Protected collision, build provenance and special vanilla navigation still need resolution before FiveM acceptance. New edge flags are structurally verified but their runtime behavior has not been established.

## Desktop workflow

Click **HELP / F1** at the top right, or press **F1**, for the built-in walkthrough. It includes setup, previews, original navigation, resource builds, FiveM checks and common errors. The guide is embedded in the application and works offline, including when launched through the Hub. It can stay open while a bake runs.

1. In **Map & preview**, click **Add map resource** and choose its folder. Check the YMAP placement to include; a single placement is selected automatically.
2. Check **Server build** (initially 3095). GTA V Legacy is detected automatically from remembered NavMesh/Livery settings or registered installations. Only use **GTA settings → Change GTA folder** if needed. The game path, server build and results folder are remembered.
3. Click **Generate preview**. The tool discovers local DLC for the selected build, finds unambiguous custom prop/interior owners in the resource tree and fits the area to readable collision. The right panel shows the top-down preview, floor-height filter and collision toggle. Scroll to zoom, drag to pan and double-click to fit. **Cancel** stops after the current processing step.
4. Select a category under **Needs attention** for an explanation and next step; complete object details remain underneath. **Technical log** retains processing details. **Advanced settings** reveals **Area & layout**, **Game sources**, **Pedestrian settings** and **Extra collision**. Select the entity sets actually enabled on your server before reviewing a furnished interior.
5. Open **Export** for the remaining build requirements. After verifying game sources, use **Prepare original navigation** to retain surrounding tiles in a new results subfolder. Generate another preview to check composition.
6. For an in-game trial, enable **Export → Allow warnings — build a test resource**, then click **Build test resource**. Original navigation is captured automatically if needed. Leave bypass off for a complete build requiring resolved inputs and a validated matching baseline. **Open results** opens retained files; **Open previous result** reloads a report for inspection without enabling export.

**Save project** and **Open project** retain selections and settings. They manage the JSON files and archive-source companion for you; normal use requires no commands or JSON editing. Existing bake configurations remain compatible. Every GUI bake creates a uniquely named folder and adjacent input snapshots inside the chosen results folder, preserving earlier runs.

New GUI projects fit their area before each preview. Existing projects retain their saved coordinates unless automatic fitting is enabled; editing a coordinate turns fitting off. Explicit archive selections are preserved. Automatic game setup finds a hash-matched target update in local GTA/FiveM files, then uses its DLC list and setup order to select installed packs. It never substitutes a newer update or certifies the entire source set. Manual selection and verification remain under **Advanced settings → Game sources**.

Resource paths are literal, including square brackets. Outputs cannot be inside source resources or baseline directories. The tool never installs files, edits map resources or changes the server build.

For Cool Beans, add `hns_josecafe_mirror_park` and select `hns_josecafe_mrpark_milo_.ymap`. Preview preparation finds `hns_josecafe_base` when installed beside it, using actual MLO archetype ownership. Add the base resource manually if it lives elsewhere. `examples/coolbeans.json` targets both resources. Automatic area fitting produces an area around 30 × 18m instead of its oversized 181 × 181m metadata box. Base GTA archives resolve the placed fence post (`prop_fncply_01post`, owner `v_fences_2.ytyp`) and wall vent (`prop_wall_vent_06`, owner `v_rooftop.ytyp`). Inspect entrances and supply approach collision before expanding the area. Protected furniture/door collision still blocks a complete bake. Example paths assume this repository's local sibling resource checkout; use the desktop pickers to select your own paths.

**Generate preview** produces `inspection/*.ynv`, editable `source/*.ynv.xml`, `collision.obj`, `generated-navmesh.obj`, the resolved `bake.json` and `report.json`. The built-in preview is an inspection aid; open native files in BadWalker for deeper editing and inspect collision in Blender. Diagnostic YNVs without a baseline contain only the generated patch: streaming them would replace the rest of those tiles.

**Build resource** requires complete supported collision and a validated, build-matched baseline. After verification, it creates `resource/stream/*.ynv` and `resource/fxmanifest.lua`; failed staging remains under `resource.pending`, never `resource`. Status is still `awaiting-fivem-validation`. The manifest declares resource dependencies, the minimum game build and a hash of the native output set. The report records individual file hashes. A newer client satisfying the manifest's minimum-build dependency still needs its own baseline and runtime validation.

When diagnostic issues remain, the window says **Preview with warnings**. The Export tab offers the explicit test-build bypass. FXAP-protected inputs are reported; their collision is omitted in a test build, without decrypting them or claiming collision is absent.

`allowWarnings: true` enables this test mode across both desktop and headless builds. It bypasses incomplete-input, unverified-source and disconnected-component warnings. Output remains labelled `test-resource-with-warnings`, contains `resource/BUILD-WARNINGS.txt`, and carries `navmesh_test_resource 'yes'` in its manifest. Settings, original warning details and source hashes remain in `report.json`. Turning bypass off restores strict checks. Wrong baseline builds, changed file hashes, missing neighboring tiles, invalid native references and conflicting streamed YNV filenames are never bypassed.

## Inputs and bounds

`resourceRoots` indexes custom YTYP/YDR/YDD/YFT/YBN assets. Differing duplicate names and conflicting owning archetypes produce warnings; diagnostic previews keep the first selected copy, while complete builds reject them. `ymaps` selects placements explicitly. CodeWalker supplies decoded world/MLO child transforms. `entitySets` is keyed by `filename.ymap:entityIndex`; every MLO with sets needs an explicit list, including `[]` for none. Select the sets actually enabled by the server, not all mutually exclusive interiors.

The desktop's `autoDetectGame` and `autoFitArea` preferences control setup, not the headless pipeline. Every GUI run writes resolved roots, coordinates and a source-set companion before baking. Dependency discovery reads actual YTYP owners across the containing `resources` tree, falling back to sibling folders elsewhere. It follows MLO children and enabled entity sets, adds unique missing owners, and avoids automatically selecting unrelated overrides of available GTA objects. It does not determine which resources the server activates. User preferences are stored in `%APPDATA%/BadlandsRP/NavMesh/settings.json`; Livery Tool settings are read without modification.

`min`/`max` are the **world XYZ replacement box**, not just a preview crop. Include every floor and approach that should change, and supply its collision. The box removes old navigation inside it; new collision must cover the full replacement region. Only explicitly selected YMAPs, explicit collision inputs and their resolved props enter the scene. The baker does not automatically load surrounding world YMAP/YBN collision, dynamic objects, weather/time variants, or the server's resource activation state.

`collision` optionally supplies native collision files with decoded world `position`, `orientation` quaternion and `scale`. This is useful for standalone world YBNs or creator-provided triangulated collision. It does not accept raw `CEntityDef.rotation` as a decoded quaternion. Supported extraction includes triangle geometry, composite child transforms, boxes, spheres, capsules, cylinders, and YDR/YDD embedded bounds. Curved collision is triangulated to a 2mm world-space chord-error budget under the placed transform. YFTs use pristine LOD1 physics only after verifying each child's decoded transform against its fragment matrix and absolute bind bone. Other fragment layouts and scaled MLO child placement are reported and stop complete bakes.

Both collision mask sets are recorded. Collision-disabled and non-HD entities are excluded, as are composite children without PED actor masks. Mismatched PED masks are unresolved. Explicit `ignoreArchetypes` requires `collisionReview` describing measured replacement coverage or why the objects have no relevant collision; an exclusion is an author assertion, not a proof of coverage. Protected files are never treated as empty space.

Agent settings expose raster cell size/height, height, radius, climb, slope, maximum polygon edge length and simplification error. Defaults are starting values for pedestrian trials. One padded raster preserves continuity across GTA's 150m tile seams; the raster cell budget bounds memory. Large regions must be split into deliberately composed bakes. Recast detail triangles retain ramp heights. Quantization displacement, removed vertices and collapsed sub-grid polygons are reported separately from save/reload error.

`polygonFlags` supplies five explicit bytes; `interior` sets the interior bit for the whole generated patch. Use separate authored regions when different semantic flags are needed. Footpath/road/water/special-link flags are not inferred from collision materials. Vehicle YND routes, moving geometry and jump/ladder links are outside this baker's scope.

## Game archives and baseline

Optional `gameSourceFile` points to a JSON manifest like `examples/game-sources.json`. It names the GTA directory for CodeWalker's keys and an **ordered list of archives**. Each entry has a physical `path`, original `logicalPath` (needed for cached archive decryption), and SHA-256. Later archives override earlier asset names; custom resources override game assets. Include the relevant base/DLC/update variants in the correct order for the chosen build.

GUI source sets with `autoDiscoverDlc` use update identities from [Cfx's game-cache catalog](https://github.com/citizenfx/fivem/blob/master/code/client/launcher/GameCache.cpp), checked against local file bytes. Discovery reads the target update's `dlclist.xml` and patched `setup2.xml`, includes subpacks and orders DLC by setup order. Missing update or DLC files remain reported; no game files are downloaded. FiveM can use a newer executable with [build-specific mounted overrides](https://github.com/citizenfx/fivem/blob/master/code/components/rage-device-five/src/UpdateRpfOverrideMount.cpp), so the automatic preview set does not certify runtime mount equivalence. An old `update2.rpf` is not required merely because the server requests an older build. Base/DLC identities and runtime mount overrides still require verification.

`validatedForBuild: true` asserts that this archive selection was independently established for `gameBuild`; every archive must then have a matching SHA-256. Hashes establish reproducibility, not build compatibility by themselves. Unverified or unpinned archive selections are usable only for diagnostics. Keep them false until verified. Inspect the installed archive/version data rather than simply relabelling newer files as 3095.

Use **Export → Prepare original navigation** to retain original tiles and their immediate neighbors in a new folder under your chosen results location.

This writes native resources from the original resource blocks without rebuilding their geometry, plus `baseline.json` and consumed-source hashes. The original archives remain unchanged. A baseline can also be prepared from existing native tiles with a manifest containing `gameBuild`, `source`, `validatedForBuild`, and `files` mapping each plain `.ynv` filename to its SHA-256. Include existing custom overrides in the audited baseline where applicable. Set `baselineDirectory` in the bake configuration. A diagnostic bake with a baseline exercises composition without making a streaming resource.

Composition clips old polygons outside the replacement box, retains flags and existing blocked boundaries, rebuilds affected links and updates incoming references in loaded neighbors. Concave originals are triangulated without moving their vertices before clipping. Preserved original polygons are split only where changed geometry needs a junction; existing zero-length vanilla edges remain unchanged. Unrelated points and portals are retained and verified. Strict builds stop on edits intersecting special points/portals. Test builds retain those points and portal-bearing polygons too, with explicit warnings that original surfaces may overlap the patch. Unsupported winding, invalid references, non-manifold new edges and capacity limits still stop with an error.

Seams match in 3D within 0.5mm after XY quantization. The baker does not automatically move old floor heights to fit a new raster: differing surface heights or border segmentation may require authoring a compatible transition. A complete bake rejects generated components with no connection to preserved navigation. `allowIsolatedComponents: true` is an explicit opt-in for intended disconnected islands, not a repair for missing entrance links. The report includes graph counts; graph connectivity still does not establish in-game traversal.

Set `conflictScanRoots` to the server's full resource directory to check candidate YNV names against other resources. The baker also checks `resourceRoots`. There is no automatic discovery of every enabled resource, so omitting a directory limits the conflict scan. Resolve conflicting outputs into one owner before streaming.

## Offline checks and pilot results

`self-test` covers multi-cell clipping (including the old 450m failure), reciprocal edges, T-junctions, native geometry/flag/reference round trips, walls, separate stacked floors, headroom, ramp coverage and sloped seams, transformed primitive geometry, fragment rest-pose checks, rejected concave input, exact-Z clipping, quantization accounting, conflict preview/export gating, and replacement of a patch within nine connected baseline tiles. Temporary native fixtures are removed in `finally`. Recast may print dangling-face cleanup messages during the ramp case; the check also verifies final coverage and seam connectivity.

Clinton Pawn was additionally checked on 29 September 2026 with server build 3095 and automatic discovery of `props_Addon`. The desktop preview reduced missing-object messages from 216 to 10 placements (six distinct unresolved definitions), with no unsupported collision messages. Resource conflicts and source/baseline verification remain unresolved. This is an offline preview result, not a complete resource or FiveM movement acceptance.

Inspected pilots on 27 September 2026:

| Input | Offline result | Remaining limits |
| --- | --- | --- |
| Current Cool Beans (`hns_josecafe_mirror_park` + `hns_josecafe_base`), local assets only | 3,710 collision triangles; 245 polygons in one native tile; approximately 0.000122m maximum additional save/reload displacement. Desktop and packaged Hub preview/help checks pass. | 461 unresolved items without game archives; collision and build-matched baseline still require review. Not tested in FiveM. |
| Earlier cafe (`hane_coolbeans_ves`), selected MLO plus selected base/update archives | 3,400 collision triangles; 273 polygons in two native tiles; approximately 0.000122m maximum additional save/reload displacement | Historical result, not the current Mirror Park cafe. 159 unresolved items in that source set, including escrowed props, missing archetypes, fragment/primitive support and archive provenance. Composition stops on special navigation points inside the edit box. |
| Vespucci Market store 01, no entity sets | 426 collision triangles; 42 polygons in one native tile | Diagnostic shell only, 31 unresolved items. Adding `cfx-gabz-mapdata` reveals conflicting archetype ownership that must be reconciled before a complete bake. Other placements and active furnishing sets remain untested. |

The local cached update archive's SHA-1 matches build 3095's update in Cfx's game-cache list, but the installed base archives and DLC selection have not been certified for that build. The captured local baseline is deliberately labelled unverified. Pilot working files are retained under the ignored `tools/navmesh-investigation` directory; proprietary assets are not vendored into this project.

## FiveM acceptance

Copy `fivem/blrp_navmesh_test` to an isolated development server as a separate resource. Start it and the candidate resource only after the complete bake and collision review pass. The test creates one local NPC and removes it on completion/cancellation/resource stop. It sends no server events and changes no player tasks.

1. Stand at the route start and run `/navtest_start`.
2. Stand at its destination and run `/navtest_goal` (2–200m from the start).
3. Run `/navtest_run generated blrp_navmesh_candidate`, using the actual candidate resource name. Save the JSON result from F8.
4. Repeat with labels `original` and `disabled` under the corresponding resource configurations. Use a fresh client/server session where necessary to avoid mistaking cached streamed navigation for a restart result.
5. Run `/navtest_cancel` to stop a test.

The JSON includes exact client build, candidate state, bake ID, sampled positions, raw route-result values, distance travelled and arrival. The test uses [`TASK_FOLLOW_NAV_MESH_TO_COORD`](https://github.com/citizenfx/natives/blob/master/TASK/TaskFollowNavMeshToCoord.md) with default flags. [`GET_NAVMESH_ROUTE_RESULT`](https://github.com/citizenfx/natives/blob/master/TASK/GetNavmeshRouteResult.md) is recorded as a raw value rather than treating undocumented result codes as proof of arrival. Require observed movement, arrival and a sensible trace. Compare the streamed binaries' hashes with `report.json`; a manifest's bake label alone does not verify the installed bytes.

Test pavement → doorway → interior → exit in both directions, wall detours, steps/ramps, open and closed doors, tile seams, resource restart, and streaming out/in. Check neighboring vanilla routes and physical player collision separately. Door state requires a runtime policy; MLO visibility portals do not supply navigation links. The Lua helper has not been run in FiveM here. Runtime acceptance remains outstanding; the desktop interface and Hub integration are implemented.

## Developer checks

The executable opens the desktop interface by default. Headless entry points remain for automated verification and existing integrations:

```powershell
dotnet restore apps/NavMesh/BLRP.NavMesh.csproj --locked-mode
dotnet run --project apps/NavMesh/BLRP.NavMesh.csproj -c Release -- self-test
dotnet run --project apps/NavMesh/BLRP.NavMesh.csproj -c Release -- --ui-self-test path/to/pilot.json path/to/preview.png
```

The desktop check opens the form off screen, round-trips a project, executes a real background preview bake, checks grouped issues/export gating and renders both normal and advanced tabs. It also starts an empty project and adds only the map resource to verify automatic game setup, dependency discovery and area fitting. Temporary native outputs, preferences and project fixtures are removed. The core checks exercise topology, serialization and baseline preservation. `Build-Package.ps1` includes the desktop tool, examples, usage guide, license and FiveM helper; it shares the Hub runtime and CodeWalker DLLs like the other native apps.

## Dependencies

DotRecast.Recast/Core 2026.3.1 are pinned in `packages.lock.json`; use locked restore. Their zlib license is in `DotRecast-LICENSE.txt`. GTA file handling reuses the repository's existing CodeWalker and SharpDX binaries. Build provenance records the actual loaded CodeWalker DLL hash, not just the source submodule revision.
