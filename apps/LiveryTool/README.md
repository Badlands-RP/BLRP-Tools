# Badlands Livery Tool

WinForms helper for BadlandsRP livery work.

## Run

```powershell
dotnet run --project D:\BadlandsRP_LiveryTool\Badlands.LiveryTool.csproj
```

Or launch the built exe:

```powershell
D:\BadlandsRP_LiveryTool\bin\Debug\net8.0-windows\Badlands.LiveryTool.exe
```

## Current Workflow

1. Select the BadlandsRP repo root and modkit master list path.
2. Optionally select the local GTA V install folder.
3. Click **Scan Liveries**.
4. Double-click an existing livery prefix to fill the next YFT number, Lua slot, and label suggestion.
5. Optionally use **Find Metadata** to locate base-game `vehicles`, `carvariations`, and referenced `carcols` kit blocks for a vehicle model. Choose a single source/DLC pack, then copy or insert `vehicles`, `carvar`, and `carcols` blocks separately.
6. Select a PNG/image file.
7. Optionally select an existing livery `.yft` as a template.
8. Enter the vehicle data folder that contains `carcols.meta`.
9. Enter or confirm the vehicle model/hash, display name, livery lock permission, and blacklist options.
10. Optionally enable **Update modkit master list** and enter the exact modkit line to append.
11. Click **Apply Livery**.

The tool currently:

- Converts the selected image to DXT5/BC3 DDS with mipmaps.
- Creates a new streamed `.yft` from a selected template `.yft` by replacing the exported DDS texture and rebuilding through CodeWalker.
- Adds the livery visible mod to the selected `carcols.meta`.
- Adds or updates `AddTextEntry(...)` in `resources/lscustoms/client/blrp_custom_liveries.lua`.
- Adds or updates the vehicle entry in the `custom_liveries` table.
- Searches a local GTA V install read-only for base vehicle metadata blocks from `vehicles.meta`, `carvariations.meta/.ymt`, and referenced `carcols.meta/.ymt` kits.
- Groups metadata results by source DLC pack/base game and can copy or insert `vehicles`, `carvariations`, and `carcols` blocks independently into selected target `.meta` files.
- Can explicitly lock a livery to a business/gang/permission suffix, or leave it unrestricted.
- Can mark an old slot as `blacklisted`.
- Can append a missing line to `resources/addons/! modkit master list.txt`.
- Can create timestamped `.bak` files before editing metadata/Lua files when the checkbox is enabled.
- Saves your repo path, modkit master list path, and backup preference between app launches.

## Sign Batch Builder

Select **Open Sign Batch Builder** beside the image conversion controls to turn a folder of sign PNG or DDS files into numbered, ready-to-use YFT/DDS pairs. The builder previews every source-to-output mapping, converts textures to DXT5 with mipmaps when needed, and compiles the bundled sign template directly through CodeWalker. Existing output files are never overwritten.

The built-in blank is used by default. A custom `.yft.xml` and replacement token can be selected for advanced batches.

## Notes

The YFT generation path is template-based. Use an existing livery `.yft` from the same vehicle when possible so the drawable shape and shader setup already match the vehicle.
