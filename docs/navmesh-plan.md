Navmesh generation investigation and implementation plan — 27 September 2026

Current pilot update: Cool Beans is now `hns_josecafe_mirror_park`, with assets supplied by `hns_josecafe_base`. The desktop guide and example use that selection and its Mirror Park placement. The old `hane_coolbeans_ves` inspection and bake figures below are retained as historical evidence, not results for the replacement cafe. A new baseline and FiveM acceptance are required for the new location.

Recommendation: build a small, repeatable collision-to-YNV baker using CodeWalker.Core for GTA data and Recast for walkability. Keep BadWalker as the map inspector and eventual front end. Start with Cool Beans on build 3095, then validate repeated placements and entity sets at Gabz Vespucci Market. The original investigation below is retained; the subsequent implementation and its current limits are documented in [apps/NavMesh/README.md](../apps/NavMesh/README.md). The baker, baseline composition, offline checks and FiveM route helper are implemented. Following the user's preference, the default interface is now a WinForms desktop app integrated into the BLRP Tools Hub, with file pickers, project settings, map preview and issue reporting. The two pilot outputs remain diagnostic-only; runtime acceptance is outstanding.

**What is established**

The existing BadWalker generator is unfinished, rather than evidence that only 3ds Max can produce usable YNV files. The local source is `external/BadWalker` at `c900f24a19c31bcb53aec7a7d30f00b1ef5aa702`; the previous experiment is `origin/navmesh-experiment` at `881bc1435ad1b41ace93ae4079b8c46f34dbdd0c` (11 January 2026). Its only extra commit changes the generator UI, raycast scheduling and world-query list copies. It does not change the YNV builder or serializer.

| Finding | Evidence and consequence |
| --- | --- |
| Missing navigation links | `GenerateNavMeshPanel.cs:774` still has `TODO: add poly edges!`, including on the experiment branch. `YnvFile.cs:301` writes unconnected sentinel edges when these are absent. Two touching polygons generated this way reload with **zero links**. This is a confirmed output defect, not a runtime diagnosis of every previous attempt. |
| Incomplete custom collision inputs | The panel calls `Space.RayIntersect`, which queries game-cache stores. Project collision is separately overlaid by `ProjectForm.GetVisibleYbns` for the viewer. The generator does not call that project overlay path. Seeing an object in the viewer therefore does not establish that its collision entered the bake. |
| Incomplete loading is accepted | The panel preloads standalone YBNs but does not gate sampling on `SpaceRayIntersectResult.TestComplete`. The branch can skip timed-out YBNs and continue. Drawables and MLO dependencies are also needed. Missing inputs must be reported, not interpreted as empty space. |
| Concurrent queries remain unsafe | The experiment adds parallel raycasts and `.ToArray()` calls, but both spatial stores clear and fill a shared `VisibleItems` list inside `GetItems`. Copying its result does not protect the query itself from concurrent mutation. |
| Walkability is only approximated | Rays move down **3 metres** after each hit, potentially skipping nearby floors. The algorithm does not establish ped radius, headroom or wall clearance. `ConnectVertices()` retains a hard-coded **0.5m** density while the branch offers several sampling densities. |
| Large polygons can be corrupted | The existing builder makes one split pass per axis. A test quad from X=10 to X=460 produces **2 tiles instead of 4**; save/reload moves vertices by approximately **150m** because normalization clamps out-of-tile coordinates. Single X and combined X/Y seam fixtures do pass. |

Relevant local sources: [generator](../external/BadWalker/CodeWalker/Project/Panels/GenerateNavMeshPanel.cs), [world collision queries and nav grid](../external/BadWalker/CodeWalker.Core/World/Space.cs), [project collision overlays](../external/BadWalker/CodeWalker/Project/ProjectForm.cs), [YNV builder](../external/BadWalker/CodeWalker.Core/GameFiles/FileTypes/Builders/YnvBuilder.cs), [YNV serialization](../external/BadWalker/CodeWalker.Core/GameFiles/FileTypes/YnvFile.cs).

**Checks actually run**

The retained [PowerShell diagnostic](../tests/Test-NavmeshSerialization.ps1) loads the existing DLLs in the required order, generates synthetic polygons in memory, saves native `RSC7` resources, reloads them and checks geometry and reciprocal references. Run `pwsh -File tests/Test-NavmeshSerialization.ps1` from this repository.

| Fixture | Reloaded tiles / polygons / directed links | Result |
| --- | --- | --- |
| Two touching quads, no links supplied | 1 / 2 / 0 | Reproduces the generator's missing connectivity |
| Two touching quads, reciprocal links supplied | 1 / 2 / 2 | Pass |
| One quad crossing an X cell boundary | 2 / 2 / 2 | Pass |
| One quad crossing X and Y cell boundaries | 4 / 4 / 8 | Pass |
| One quad crossing three X boundaries | 2 tiles, expected 4 | Known limitation reproduced; not a passing geometry check |

The flat-fixture geometry tolerance is derived from half a 16-bit quantization step per 150m XY axis, plus float rounding allowance. The flat Z extent also round-trips successfully in this installed library. These tests prove specific writer behavior; they do not prove game interpretation of edge flags, streaming or ped navigation.

Tested `shared/lib/CodeWalker.Core.dll` SHA-256: `5FB95D7BE67A961FD5586431923157236A36892E2CD5BB696B46BDD8B85B6532`. Record the shipped DLL hash again when implementing changes; a source patch alone does not change this bundled binary.

**Pilot inputs and constraints**

The user confirmed build **3095**; `D:\BadlandsRP\server.cfg` agrees. The discovered GTA Legacy installation is `C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V`, with executable version **1.0.3889.0**. Blender is installed alongside it at `...\Blender\blender.exe`, version **5.2**. The newer local game installation must not silently become the source of truth for 3095 base navmesh/collision/dependencies.

| Pilot | Local inspection |
| --- | --- |
| `D:\BadlandsRP\resources\[custom_maps]\[escrowed]\hane_coolbeans_ves` | Four YMAPs, two YTYPs, no YNV. Its readable `hane_coolbeans_col.ybn` loads with seven collision children and 1,288 polygons. The MLO has six room records, ten portals and 355 base entities. All 77 YDRs have FXAP headers; two YDDs are readable native resources. |
| `D:\BadlandsRP\resources\[custom_maps]\[escrowed]\[cfx-gabz]\cfx-gabz-vbmarket` | 66 YMAPs, five YTYPs, no YNV; 24 store placement YMAPs. Readable `gabz_vbm_store.ybn` loads with one child and 426 polygons. The store MLO has three room records, four portals, five base entities and 33 entity sets. 219 YDRs have FXAP headers, 60 are native readable resources; two YFTs and one YDD are also native. The resource requires `cfx-gabz-mapdata`. |

These counts do not establish complete floor, obstacle or doorway coverage. Both readable collision files are in local coordinates. CodeWalker decodes Cool Beans' placement as position `(-1189.611, -1160.893, 10.81227)`, orientation `(0, 0, 0.17364818, 0.9848077)`, unit scale. Market store 01 is `(-1273.3335, -1410.4198, 4.836831)`, orientation `(0, 0, 0.300706, 0.953717)`, unit scale. Treat these as decoded placement references, not a prescription for raw quaternion conversion.

Collision baseline hashes:

- Cool Beans: `8404D008677467E7C142CEFC3723B783D9A86F636211E59F2DC6E8E060D8245B`.
- Market store: `C5BDA809928E0C69FBC3284AB1B446BE36E375ABFC061100514536C3ECEC4F12`.

Use available readable collision first. If protected models contain necessary embedded collision absent from the readable shell, request creator-provided collision/source or author explicit navigation obstacles from measured geometry. The baker must list unresolved objects and affected regions. A successful shell bake must not be presented as complete coverage of protected props.

**Choice of tool**

The historical [ONV exporter/OFIO workflow](https://www.gta5-mods.com/tools/navmesh-to-openformats-exporter) does use 3ds Max. However, [Luman's Sollumz fork](https://github.com/LumanGH/Sollumz_Navmesh_Edit) supports Blender YNV XML editing/export; inspection of its `ynv/operators.py` and `ynv/ynvexport.py` at `b8a84baa612518d77db9502fdeb2b8d20829dd5a` finds editing, flag tools and adjacency export, not an automatic collision baker. The inspected official Sollumz tree at `f7fe61620bd32039e709bb137a3896b3b7c44e0b` contains a YNV importer but no corresponding exporter/generator in that directory. These are possible authoring aids, not a verified end-to-end solution for our resources.

[CodeWalker TSS](https://github.com/TinySpriteScripts/CodeWalker_tss#navmesh-editor) also documents manual navmesh editing, validation and link repair. Its advertised feature set does not establish automatic collision generation. None of these forks was installed or runtime-tested during this investigation.

[Recast](https://github.com/recastnavigation/recastnavigation) already provides triangle rasterization, clearance filtering and polygon generation. Use that established algorithm rather than expanding the current ray grid. Prefer the C# port [DotRecast](https://github.com/ikpil/DotRecast) after a small geometry trial. Only its Core and Recast components are needed for generation; GTA retains runtime pathfinding.

There is a concrete integration constraint: BadWalker's UI targets **net48**, while current [DotRecast.Recast targets](https://github.com/ikpil/DotRecast/blob/main/src/DotRecast.Recast/DotRecast.Recast.csproj) start at **netstandard2.1/net8.0**. BLRP Tools already uses net8.0. The smallest route is a **net8 command-line baker** using existing CodeWalker.Core plus pinned compatible DotRecast packages. BadWalker can launch it and open its outputs later. Avoid migrating all of BadWalker or embedding an incompatible package. If the port fails the geometry trial, evaluate a thin native Recast wrapper before inventing a generator.

**Implementation sequence and acceptance gates**

1. **Capture a complete, reproducible scene.** Start with Cool Beans' collision and its immediate pavement approaches. Record target build, enabled resources, active entity sets, input hashes, transforms, bake bounds and parameters. Preserve the affected original YNV tiles and their neighbors before conversion. Obtain baseline assets corresponding to build 3095; the installed 3889 files alone are insufficient proof. Resolve resource overrides, YTYP ownership and collision references explicitly. Extract standalone YBN, supported embedded YDR/YDD/YFT bounds, MLO children and active entity-set collision. Apply child transforms, geometry centers, entity scale and CodeWalker's correct MLO/ordinary entity orientation conventions. Unsupported primitives or unresolved collision must fail a complete bake or mark a deliberately partial diagnostic. Gate: collision preview and floor/wall/doorway samples agree with the intended scene; inputs are fixed independently of the camera.

2. **Prove automatic walkability on that collision.** Feed complete triangles, including walls and ceilings, to Recast. Make cell size, cell height, ped radius, ped height, climb and slope explicit. Establish their values with narrow doors, stairs and overhead obstructions, rather than claiming universal GTA defaults. Convert GTA Z-up to Recast Y-up with an explicit reversible basis, preserve winding and verify normals. Bake padded regions at every 150m GTA tile boundary so erosion does not create false borders. Preserve height detail on ramps; do not discard Recast detail triangles without measuring surface error. Gate: expected areas connect, walls and excessive steps block paths, and floors above/below one another do not merge.

3. **Build valid GTA topology and preserve the neighborhood.** Convert polygon neighbors into reciprocal YNV edges, with both edge slots referring to the neighbor as the current builder expects; boundary edges use the documented sentinel. Derive flags from inspected vanilla equivalents and measured behavior, not collision-material flag copying. Clip at **every** tile boundary, rebuild links after clipping, and verify all vertices fit their assigned tile before calling the existing writer. Use its `SpaceNavGrid` conventions: 150m cells, origin (-6000,-6000), `AreaID = x + 100*y`, filename coordinates `3*x,3*y`. Reject outside-grid inputs and index/adjacency-table overflow. Test concave input handling, exact-boundary vertices, sloped seams and multiple-cell spans before trusting general output.

   Compose each affected YNV from preserved vanilla navigation outside the edit region plus the new local navigation. A small resource's output must not erase the rest of an existing tile. Preserve/remap existing edge references, points and portals; keep polygon indices stable where possible and repair incoming references in affected neighbor tiles when indices change. Match border segmentation in 3D, not only XY proximity. Scan other resources for the same streamed tile names and produce one composed result for each affected tile. Gate: native save/reload preserves geometry within justified quantization error, has no dangling references and leaves surrounding routes intact.

4. **Validate in FiveM before expanding the UI.** Use a separate test resource with recorded output hashes and the candidate map dependencies. Reuse the map's existing physics registration; do not invent manifest entries as a cure for missing topology. Check any actual registration changes through native compilation and reload. On build 3095, test an NPC with [`TASK_FOLLOW_NAV_MESH_TO_COORD`](https://github.com/citizenfx/natives/blob/master/TASK/TaskFollowNavMeshToCoord.md) and record [`GET_NAVMESH_ROUTE_RESULT`](https://github.com/citizenfx/natives/blob/master/TASK/GetNavmeshRouteResult.md), timeout, route trace and arrival distance. Require actual movement and arrival as well as the native result. Compare original, generated and disabled-navmesh cases so an existing route is not mistaken for success. Test pavement → entrance → interior → back outside, reverse direction, wall detours, stairs, open and closed doors, tile seams, streaming out/in and resource restart. Check physical player collision separately. Gate: reliable routes and no surrounding regressions with the exact streamed binaries. Dynamic doors require an explicit runtime policy; MLO visibility portals are not navmesh links.

5. **Generalize on Vespucci Market, then expose the workflow.** Validate a single shop first, then its other rotated placements and the actually enabled entity sets. Account for furniture and changing doors without blindly unioning mutually exclusive sets. Add a thin BadWalker or Hub action that selects a region/resource, runs the same baker and loads results for inspection. Retain a bake description, diagnostics, editable YNV XML and native outputs using the resource's conventions. Make build selection and input hashes part of the bake description so a future server-build change triggers fresh dependency/baseline checks and runtime validation.

The first useful deliverable is a reproducible Cool Beans bake with a passing NPC route from the pavement through the interior and back. Pedestrian navigation is the initial scope. Road traffic YND work, ambient population/scenarios, special jump/ladder links and navigation on moving vehicles require separate acceptance cases; `YnvBuilder.Build(true)` is not a switch for generating road traffic routes.

Original investigation status: source review, candidate asset inspection and the described serializer checks were complete. Subsequent implementation now generates Recast/native outputs and passes the checks in the CLI README. Full collision coverage, build-3095 native edge semantics and FiveM acceptance remain outstanding. No game files, map resources, server build or BadWalker branches were changed.
