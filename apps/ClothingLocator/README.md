# BLRP Clothing Utility

Clean local rebuild of the BadlandsRP clothing collection locator.

See [CHANGELOG.md](CHANGELOG.md) for release history since version 1.4.2.

The app locates custom collection files and can now resolve base-game/DLC
components directly from the installed GTA V archives through CodeWalker Core.
For Rockstar items it can extract the matching `.ydd` model and every `.ytd`
texture into a normal folder.

## Run

Open `app\BLRP.ClothingUtility.exe`. The normal development build targets the
installed .NET 8 Windows Desktop runtime and defaults to `D:\BadlandsRP_EUP`.

Enter the item being searched under `CLOTHING #`. `AUTO START` shows the first
add-on drawable number and is deliberately read-only. The GTA location and
active FiveM game build are read automatically from `CitizenFX.ini`.
Search results show the number of indexed YTD textures available for each model.

Rockstar extraction only reads GTA V. Copies are written to the folder selected
in the app (default: `D:\BLRP-Clothing-Exports`).

`IMPORT MODEL...` accepts one `.ydd` or `.ydr` model plus its `.ytd` textures.
It assigns the next safe filename, writes the files into the correct gender and
component directory, and appends the drawable to the existing addon YMT. A
target selector lists each safe addon pack, its next relative slot, remaining
capacity, and the resulting in-game clothing number. The
tool warns as a component approaches the 128-drawable YMT limit and refuses to
create a new global addon pack automatically. Existing YMT files are backed up
under `.clothing-locator-backups` before modification. Component import is
supported; prop import is not yet supported.

Both normal OpenIV-style `RSC7` assets and raw decompressed resources produced
by older versions of the locator are accepted.

Race/skin models are supported. A source model ending in `_r` is imported as
`_r`, uses the skin drawable flag, and reads texture suffixes such as `_whi`,
`_bla`, `_chi`, `_lat`, `_ara`, `_kor`, and `_pak` into the YMT race IDs.
Texture letters are assigned in YMT order while the race suffix is preserved
(for example, `_a_whi` followed by `_b_bla`).

`IMPORT TEXTURE...` adds one or more YTDs to an existing custom component.
Select a custom result/YDD, or enter its in-game clothing number, then choose
the source YTDs. The tool assigns each next texture letter, renames the internal
textures, updates the drawable's YMT texture data, and backs up the YMT first.

Choose a business under `DRAWABLE BLACKLIST FOR MODEL / DUPLICATE IMPORTS` to
restrict the complete new drawable. Texture imports open a separate assignment
dialog: each selected YTD defaults to public and can optionally be assigned to
a different business, with an `APPLY TO ALL` shortcut. If the whole drawable is
already restricted, per-texture choices are disabled because the drawable rule
takes precedence. `REFRESH BUSINESSES` downloads the current custom-business
names from the Panel's public endpoint and caches them for offline use.

Select any indexed custom component and use `DUPLICATE INTO CATEGORY` to copy it
to another component category (for example, `BERD` masks to `HAND` bags). The
tool assigns the destination filename/index automatically, retargets the model
and texture dictionaries to the destination component, and clones the source
drawable/component flags into the destination YMT entry. Hair-card
models moved outside `BERD`/`HAIR` are automatically changed from the
slot-sensitive `ped_hair_spiked` shader to GTA's cross-component
`ped_hair_cutout_alpha` equivalent. Geometry marked by the original shader's
`orderNumber` as suppressed is omitted so hidden helper hair cards do not
appear across the face. Models with
alternate drawables or cloth-simulation ownership are rejected because they
need additional companion assets and cannot be copied safely by metadata alone.

Use `REPLACE ITEM...` on an indexed custom model to replace the complete item
with a new `.ydd`/`.ydr` and all of its new `.ytd` textures while retaining the
existing clothing ID and blacklist. The replacement assets and YMT texture
metadata are retargeted to the selected slot. The previous YDD, YTDs, and YMT
are backed up before the files are swapped.

Select an indexed `FEET` model and use `YMT SETTINGS...` to choose its shoe sound
and enable or adjust its heel height from 0 to 3. Saving updates `pedXml_audioID`
and `pedXml_expressionMods.f4`, then backs up the
component YMT, and repairs or creates the creature-metadata YMT named by the
addon's SHOP_PED_APPAREL metadata. Both files are required for GTA to apply the
offset. Prop hair scaling/cutting is not exposed until prop import is supported.

Select an indexed custom model and use `ADD TO OUTFIT`. The outfit keeps one
item per GTA clothing/prop slot, so adding another item in the same slot replaces
the old one. Use `PREVIEW OUTFIT` to send the complete outfit to the bundled,
separate grzyClothTool viewer. Later outfits are handed to the existing viewer
window over a local named pipe; if that viewer is unavailable, the locator starts
a new one. On first use, grzyClothTool may ask for the GTA V installation folder
before its CodeWalker preview can initialize. The helper is stored under
`tools\grzyClothTool-outfit`; its GPL license, upstream source reference, and BLRP patch
description are included beside it.

## Security

- The locator only contacts the public BadlandsRP Panel business-list endpoint. It never asks for administrator rights or Defender exclusions.
- `PREVIEW OUTFIT` launches the separate grzyClothTool process. Upstream grzyClothTool includes update/telemetry network code; see its bundled source and license notice.
- No package-manager dependencies. The unused MoonSharp and Newtonsoft dependencies from the original project were removed.
- CodeWalker Core and its two SharpDX assemblies are included locally to read Rockstar RPF archives.
- The normal `app` output is framework-dependent. `Build-Package.ps1` produces a self-contained Windows ZIP.
- SmartScreen/Defender reputation cannot be guaranteed for an unsigned private executable. For external distribution, sign the Release executable and DLL with the BadlandsRP Authenticode certificate.

## Build

```powershell
dotnet publish .\BLRP.ClothingLocator.csproj -c Release -r win-x64 --self-contained false -o .\app
```

Build the all-inclusive Windows ZIP from the current Badlands-RP grzyClothTool
checkout (no symlink required):

```powershell
.\Build-Package.ps1
```

If the checkout moves:

```powershell
.\Build-Package.ps1 -GrzySource D:\path\to\grzyClothTool
```

The script also copies CodeWalker's SharpDX runtime modules. If that checkout
moves, pass `-CodeWalkerDependencies D:\path\to\CodeWalker\bin\Codewalker`.

Run the built-in logic check:

```powershell
.\app\BLRP.ClothingUtility.exe --self-test
$LASTEXITCODE
```

Run the GTA archive/extraction check:

```powershell
.\app\BLRP.ClothingUtility.exe --base-self-test D:\BLRP-Clothing-Test
$LASTEXITCODE
```

Run the importer against an isolated fixture copy (the source EUP files are
read-only during this check):

```powershell
.\app\BLRP.ClothingUtility.exe --import-self-test D:\BadlandsRP_EUP D:\BLRP-Clothing-Import-Test
$LASTEXITCODE
```
