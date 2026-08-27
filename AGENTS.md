# Repository instructions

## Build and reload

Build the Dalamud plugin for the configured x64 dev-plugin location with:

```powershell
$env:DALAMUD_HOME = Join-Path $env:APPDATA "XIVLauncher\addon\Hooks\dev"
dotnet build .\MidnightPlugin\MidnightPlugin.csproj --configuration Release --property:Platform=x64 --nologo
```

Dalamud loads this DLL:

```text
MidnightPlugin/bin/x64/Release/MidnightTimeline.dll
```

Do not treat `MidnightPlugin/bin/Release/MidnightTimeline.dll` as the reload target. After changing plugin UI or capture code, verify that the x64 DLL timestamp changed before asking the user to reload.

Run the test suite with:

```powershell
dotnet test .\MidnightPlugin.slnx --configuration Release --nologo
```
