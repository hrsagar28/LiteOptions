# LiteOptions

A small, offline replacement for Logi Options+ for the **Logitech Pebble Mouse 2 (M350s)** and **K380** keyboard on Windows 11, built to run natively on ARM64 (Surface, Snapdragon) as well as x64.

- **Engine** (`src/`, .NET Framework 4.8, built into Windows): tray icon, Bluetooth HID++ link to the devices (DPI, battery, top-row Fn mode), button and key remapping, per-app profiles, gestures. Runs in the background with a few MB of memory.
- **Settings window** (`settings/`, WinUI 3, Windows App SDK, self-contained): starts only when you open it and exits completely when closed. It talks to the engine over a private per-user named pipe.
- If the WinUI window can't start, the engine falls back to its built-in classic window.

No account, no internet access, no telemetry.

## Download

See [Releases](../../releases): **LiteOptions-ARM64.exe** for Surface Laptop and other Snapdragon PCs, **LiteOptions-x64.exe** for Intel/AMD. Double-click to install or update; settings are kept.

## Building

GitHub Actions (`.github/workflows/build.yml`) builds everything, runs the engine tests against simulated devices, installs on real x64 and ARM64 Windows, runs the window's self-test against the engine, takes screenshots and uninstalls again.

Locally on Windows with the .NET 10 SDK:

```
dotnet build tests/LiteOptions.Tests.csproj -c Release -o out/tests && out\tests\LiteOptionsTests.exe
dotnet publish settings/LiteOptions.Settings.csproj -c Release -r win-arm64 -p:Platform=ARM64 -o out/settings-ARM64
# zip out/settings-ARM64 to out/settings-ARM64.zip, then:
dotnet build engine/LiteOptions.csproj -c Release -p:SettingsZip=%CD%\out\settings-ARM64.zip -o out/engine
```

`LiteOptions.Settings.exe --demo` opens the window with pretend devices.
