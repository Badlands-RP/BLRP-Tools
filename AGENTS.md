# BLRP Tools instructions

## MLO and map asset work

- Reuse the existing CodeWalker dependencies in `shared/lib` and the BadWalker source in `external/BadWalker` before adding tooling. For PowerShell inspection, load `SharpDX.dll`, `SharpDX.Mathematics.dll`, then `CodeWalker.Core.dll`. Inspect the installed API/source rather than guessing serializer behaviour. Discover local GTA and Blender installations; do not assume another machine uses the same paths.
- Establish the target FiveM game build and preserve a baseline of the original assets, entity transforms, collision coverage and resource dependencies before conversion. Assets available in the local GTA installation may not exist on the target client.
- Keep editable sources alongside deployable native assets using the resource's conventions. Compile with CodeWalker and reload the resulting binaries for verification; an XML file renamed to `.ybn`, `.ytyp` or `.ymf` is not a native asset. Check that the installed binaries are the ones tested.

### Placement, rooms and visibility

- Use a consistent MLO-local coordinate frame for entities, bounds and portals. Ordinary `CEntityDef.rotation` and `CMloInstanceDef.rotation` use different orientation conventions; validate instantiated world positions and rotations with CodeWalker rather than blindly copying quaternion conversions.
- Validate entity attachments, room indices, room bounds, portal counts, planar portal corners, winding and room-from/room-to direction. Match portals to actual openings and distinguish playable rooms from limbo. Do not copy another interior's coordinates, flags or room layout as universal defaults.
- Audit actual door leaves separately from doorway portals: identify operable doors, static dressing and empty openings. Portals do not create doors, collision or missing wall faces.
- Inspect shell surfaces from inside and outside, including oblique views with doors open. Check backface winding, normals, non-degenerate UVs and tangents on added faces. Missing jambs, lintel returns and wall strips need geometry, not larger portals.
- Preserve unrelated world collision and occluders when overriding vanilla assets. Check applicable normal/heist variants; remove only the occluders intersecting the new interior, not an entire region.

### Collision: geometry, registration and masks

- Verify all three independently: the shape exists, its physics is registered/streamed, and its masks include the intended actors. A weapon/camera ray hit alone does not prove player or vehicle collision works. Inspect both composite flag sets against the installed presets and the collider's intended role.
- For embedded drawable collision, verify the archetype's physics dictionary and compatible archetype flags. Do not confuse YTYP archetype flags with YMAP entity flags. Also verify MLO physics references, manifest interior bounds registration and collision-material room IDs.
- Cover exterior aprons, slopes, steps, walls, closed decorative doors, interior floors and both sides of partitions. Test that solid sections block movement while intended doorways remain clear; sample floor heights against the rendered mesh rather than only checking for any hit below it.
- Compare geometry after native save/reload. CodeWalker rebuilds BVHs and quantizes vertices; inconsistent `CenterGeom` and bounds can silently clamp vertices. If recentering is needed, preserve world positions by offsetting relative vertices by `oldCenter - newCenter`, then rebuild/save/reload and measure the error. Do not hide geometry changes by loosening tolerances. Allow only justified quantization error and account for polygon reordering.

### Dependencies and verification

- Resolve each prop's owning YTYP from the loaded game archives/archetypes, not a guessed name prefix. Build native YMF dependencies from actual owner filenames and verify them after reload. `XmlPso.GetPso(...).Save()` compiles manifest XML to native PSO; consult BadWalker's manifest implementation for its schema.
- Use a resource-specific manifest filename and check for duplicate streamed asset names across resources. Verify `DLC_ITYP_REQUEST` points to an existing YTYP. A manifest cannot make a newer-DLC prop available on an older client; do not raise the server build or silently replace materially different props.
- Treat offline rendering as a visual aid, not runtime proof. Use real meshes/textures, backface culling and views without exterior geometry masking holes in the interior shell. Retain requested previews outside disposable worktrees and pack textures into retained Blender files before cleaning exports.
- Report offline checks and FiveM checks separately. Runtime acceptance includes walking/driving over exterior ground, pushing against partitions and exterior walls, crossing open doorways, checking dependent props, and viewing portals from both directions with doors open/closed. Until tested in-game, describe fixes as awaiting runtime confirmation and keep suspected causes distinct from confirmed findings.
