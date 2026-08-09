# BLRP Clothing Locator

Clean local rebuild of the BadlandsRP clothing collection locator.

The app locates custom collection files and can now resolve base-game/DLC
components directly from the installed GTA V archives through CodeWalker Core.
For Rockstar items it can extract the matching `.ydd` model and every `.ytd`
texture into a normal folder.

## Run

Open `app\BLRP.ClothingLocator.exe`. The application targets the installed .NET 8 Windows Desktop runtime and defaults to `D:\BadlandsRP_EUP`.

Enter the item being searched under `CLOTHING #`. `AUTO START` shows the first
add-on drawable number and is deliberately read-only. The GTA location and
active FiveM game build are read automatically from `CitizenFX.ini`.

Rockstar extraction only reads GTA V. Copies are written to the folder selected
in the app (default: `D:\BLRP-Clothing-Exports`).

## Security

- No network access, administrator request, registry access, shell execution, dynamic scripting, or Defender exclusions.
- No package-manager dependencies. The unused MoonSharp and Newtonsoft dependencies from the original project were removed.
- CodeWalker Core and its two SharpDX assemblies are included locally to read Rockstar RPF archives.
- Release output is framework-dependent and deliberately not packed into a single-file executable.
- SmartScreen/Defender reputation cannot be guaranteed for an unsigned private executable. For external distribution, sign the Release executable and DLL with the BadlandsRP Authenticode certificate.

## Build

```powershell
dotnet publish .\BLRP.ClothingLocator.csproj -c Release -r win-x64 --self-contained false -o .\app
```

Run the built-in logic check:

```powershell
.\app\BLRP.ClothingLocator.exe --self-test
$LASTEXITCODE
```

Run the GTA archive/extraction check:

```powershell
.\app\BLRP.ClothingLocator.exe --base-self-test D:\BLRP-Clothing-Test
$LASTEXITCODE
```
