# BLRP Asset Studio

Windows utility for previewing and building BadlandsRP weapon skins, cups, and
transparent inventory images.

- **Staff Preview** is read-only. It ships with the BLRP bat template so staff
  can choose a ticket PNG or DDS and inspect it in 3D.
- **Developer Import** loads any YDR/YTD pair for preview and inventory icon
  capture. For configured weapons it also numbers and installs a skin, updates
  the weapon metadata, and expands the `WAPSkin` bone chain when required.
- **Cup Creator** loads any embedded-texture cup YDR, applies a 2:1 PNG or DDS
  wrap, and creates both the renamed YDR and a posed 256x256 transparent WebP.
- **Inventory Photo** loads any GTA YDR with an optional adjacent YTD and
  optional PNG/DDS diffuse override, then captures the posed model as a WebP.

## Preview and inventory images

Left-drag to rotate, right-drag horizontally to tilt a model diagonally, and
use the wheel for a fixed, predictable zoom. `SAVE 256 WEBP`
captures the current pose from any loaded model with a transparent background
and a soft drop shadow. When a BadlandsRP repository is selected, the save
dialog starts in `resources\blrp_inventory\images`.

Weapon imports use the inventory filename convention `comp_sk_<weapon>_<number>.webp`
(for example, `comp_sk_bat_bl_34.webp`).

PNG replacements must have the same aspect ratio as the model's diffuse
texture and use power-of-two dimensions. The studio normalizes them to the
template resolution and encodes DXT5 with mipmaps. DDS replacements are copied
directly, preserving their existing compression and mipmaps.

## Cup Creator

1. Select a cup YDR with embedded textures. The bundled template is selected by
   default; Browse starts in the BadlandsRP cup stream directory.
2. Select a 2:1 PNG or `coffee_main.dds` wrap and load the preview.
   Expand **Optional Top + LOD** to replace `coffee_top` and/or `coffee_lod`;
   blank fields preserve the textures already embedded in the template YDR.
3. Rotate and zoom to the inventory pose you want.
4. Enter the cup asset ID and choose **Create Cup YDR + WebP**.

The YDR is staged under
`resources\[custom_props]\props_Addon\stream\_furniture-only\housing_cups` and
the icon under `resources\blrp_inventory\images`. Adding the item definition to
`cups.lua` remains manual.

## Weapon Import

The default profile matches the BLRP bat setup from commits
`b9b5def02412ffe8e7ab3bf600d631a2d9e5b7e4` and
`ba9bc249fe867948579196554333a4ea520df1f6`. Scan preselects the highest-numbered
matching YDR/YTD pair and chooses the next safe skin number.

GTA weapon attachment groups hold 12 variants. At skin 13, 25, 37, and so on,
the studio creates or uses the matching `WAPSkinA`, `WAPSkinB`, `WAPSkinC`, ...
group. If the base YDR lacks that bone, it clones the preceding `WAPSkin` bone,
rebuilds and verifies the skeleton, then updates the metadata. Initial skeleton
authoring still requires a base model with `Gun_Root` and `WAPSkinA`.

Before repository writes, edited metadata and any modified base YDR are backed
up under `.weapon-skin-tool-backups`.

## Build

```powershell
dotnet publish .\BLRP.AssetStudio.csproj -c Release -r win-x64 --self-contained false -o .\app
```

Run the built-in checks:

```powershell
dotnet run --project .\BLRP.AssetStudio.csproj -- --self-test
```
