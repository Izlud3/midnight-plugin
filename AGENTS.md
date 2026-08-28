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

## Publishing the next version

When the user asks to "push the next version", "publish an update", or equivalent, treat it as a complete custom-repository release, not only a Git push. Unless the user specifies a version, increment the patch component (for example, `0.1.0.0` to `0.1.1.0`) and use a matching three-component Git tag such as `v0.1.1`.

1. Update `<Version>` in `MidnightPlugin/MidnightPlugin.csproj`.
2. Update `AssemblyVersion` in `pluginmaster.json` to the exact same four-component version and set `LastUpdate` to the current Unix timestamp in seconds.
3. Run the Release x64 build and the full test suite shown above.
4. Verify that `MidnightPlugin/bin/x64/Release/MidnightTimeline.dll` has a fresh timestamp and that the generated `MidnightPlugin/bin/x64/Release/MidnightTimeline.json` contains the new `AssemblyVersion`.
5. Use the packager output at `MidnightPlugin/bin/x64/Release/MidnightTimeline/latest.zip`. Copy it to the exact release asset name `MidnightTimeline.zip`; do not publish it as `latest.zip`.
6. Commit and push the version changes, create and push the matching tag, and publish a GitHub release containing `MidnightTimeline.zip`. Never replace an existing release or tag without explicit user approval.
7. Confirm that the release asset is available at `https://github.com/Izlud3/midnight-plugin/releases/latest/download/MidnightTimeline.zip` and that the raw `pluginmaster.json` on `main` exposes the new version before reporting completion.

Dalamud detects updates by comparing the installed version with `AssemblyVersion` in `pluginmaster.json`; a new commit by itself does not trigger an update. The versions in the project, generated manifest, and repository manifest must agree. The `DownloadLinkInstall` and `DownloadLinkUpdate` values in `pluginmaster.json` depend on the release asset retaining the exact filename `MidnightTimeline.zip`.
