# Audio Manager Documentation

## Tech Stack

| Layer | Technology |
| --- | --- |
| Language | C# |
| Runtime | .NET 8 |
| UI | WPF, MVVM-style view models, custom dark theme |
| Audio API | WASAPI via NAudio |
| MIDI | NAudio.Midi |
| DI | Microsoft.Extensions.DependencyInjection |
| Configuration | JSON via System.Text.Json |
| Platform | Windows x64 |

## Architecture

Audio Manager has three main views:

- `Mixer`: channel strips for microphone, master, and app groups.
- `Apps List`: active and remembered app sessions with channel assignment.
- `Settings`: tray behavior, startup, notifications, MIDI bindings, keyboard bindings, and about information.

`CoreAudioManager` owns the audio state and uses WASAPI/CoreAudio APIs through NAudio:

- `Microphone` and `Master` control endpoint volume and mute through `AudioEndpointVolume`.
- `VirtualOutput` channels control assigned process sessions through `SimpleAudioVolume`.
- Active audio sessions are enumerated across render devices and grouped by process name.
- Peak meters are refreshed in the background.

The app does not process raw PCM audio. It does not currently implement DSP, EQ, virtual device routing, or APO-based effects.

## Configuration

Settings are stored at:

```text
%APPDATA%\AudioManager\settings.json
```

Current schema version: `6`.

Important configuration sections:

- `Channels`: channel names, roles, volume, mute state, assigned processes.
- `MidiBindings`: MIDI CC/note bindings for volume and mute.
- `KeyboardBindings`: low-level keyboard hook bindings.
- `ProcessExecutablePaths`: cached executable paths for app icons.
- `RunOnStartup`, `StartInTray`, `MinimizeToTray`, `CloseToTray`: tray/startup behavior.
- `NotificationsEnabled`: global on-screen popup toggle.

## Build

```powershell
dotnet build src\AudioManager\AudioManager.csproj
```

## Run

```powershell
dotnet run --project src\AudioManager\AudioManager.csproj
```

## Publish

The installer build publishes a self-contained Windows x64 build:

```powershell
dotnet publish src\AudioManager\AudioManager.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=false `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o artifacts\publish\win-x64
```

Single-file publishing is intentionally disabled for the installer. Keeping files expanded avoids WPF/font extraction edge cases and makes Program Files installs easier to inspect and service.

## Installer

The repository uses Inno Setup for the Windows installer.

Local prerequisites:

- .NET SDK
- Inno Setup 6, with `iscc.exe` available in `PATH`

Build locally:

```powershell
.\scripts\build-release.ps1 -Version 1.0.0
```

Outputs:

```text
artifacts\publish\win-x64\
artifacts\installer\AudioManager-Setup-1.0.0.exe
artifacts\AudioManager-1.0.0-win-x64.zip
```

The installer writes to:

```text
C:\Program Files\Audio Manager
```

It creates a Start Menu shortcut and optionally creates a desktop shortcut.

## GitHub Release

The `release.yml` workflow builds the app, compiles the installer, creates a zip package, and publishes both files to a GitHub Release when a tag matching `v*` is pushed.

Create a release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The workflow requires repository permission:

```yaml
permissions:
  contents: write
```

No signing certificate is configured yet. Unsigned installers may trigger SmartScreen until the project has signing and reputation.

## Known Limitations

- App assignment is process-name based.
- Some applications expose multiple audio sessions.
- Per-app output-device routing is not implemented.
- EQ/DSP is intentionally not implemented because low-latency system-wide processing would require a different architecture, such as APO or virtual-device routing.
- Installer binaries are unsigned unless a signing step is added later.
