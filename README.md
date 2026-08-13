# BLRP Tools

One Windows launcher and updater for BadlandsRP's internal desktop utilities.

## Included tools

- **BLRP Asset Studio** — weapon skins, cups, 3D previews, and inventory images.
- **BLRP Clothing Utility** — clothing search, preview, import, metadata, and blacklist workflows.
- **BLRP Livery Tool** — vehicle livery conversion, scanning, metadata, and installation.
- **BLRP Mapping Deconflicter** — scans mapping resources and reports duplicate assets.

The Hub launches each utility in its own hosted process, so a crash or long-running job
in one tool does not take down the others. The package carries one .NET runtime and
one shared CodeWalker/SharpDX dependency set. Updates are distributed as one GitHub
release ZIP; the Hub checks `Badlands-RP/BLRP-Tools`, replaces the complete
installation, and restarts itself.

## Repository layout

```text
apps/
  ToolsHub/
  AssetStudio/
  ClothingLocator/
  LiveryTool/
  MappingDeconflicter/
branding/
shared/
```

## Build

```powershell
dotnet build .\BLRP.Tools.sln -c Release
```

Create the complete self-contained Windows bundle:

```powershell
.\Build-Package.ps1
```

Publish the generated ZIP as a GitHub release named `v<version>`. The Hub finds
the `BLRP-Tools-v<version>-win-x64.zip` release asset automatically.
