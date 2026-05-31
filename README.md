# AudioManager

Windows desktop software audio mixer controlled via **MIDI** (Loupedeck) and **keyboard hotkeys**. Groups active Windows audio sessions by application process into logical mixer channels.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Language | C# 12 |
| Runtime | .NET 8 |
| UI | WPF (MVVM), custom dark theme |
| Audio API | WASAPI via NAudio 2.2.1 |
| MIDI | NAudio.Midi |
| DI | Microsoft.Extensions.DependencyInjection 8.0.1 |
| Config | JSON via System.Text.Json (`%APPDATA%\AudioManager\settings.json`) |
| Platform | Windows (x64) |

## Views

The app has three tabs, switched via radio buttons in the title bar:

### Mixer

Six channel strips displayed in a `UniformGrid`:

| Channel | Role | Description |
|---------|------|-------------|
| **Mic** | Capture | Default Windows microphone |
| **MASTER** | Master | Default Windows output device |
| **CHAT** | VirtualOutput | Assignable application group |
| **GAME** | VirtualOutput | Assignable application group |
| **MEDIA** | VirtualOutput | Assignable application group |
| **MUSIC** | VirtualOutput | Assignable application group |

- **Mic** and **MASTER** are locked to Windows default devices and track endpoint changes automatically.
- **VirtualOutput** channels control `SimpleAudioVolume` per assigned process session.
- Each channel strip shows: name, vertical slider, VU meter (logarithmic, smoothed), mute toggle, and assigned app icons (up to 7 visible, overflow in a popup).
- `EQ` placeholder button visible on Mic and MASTER (not yet implemented).

### Apps List

Displays all known audio applications in a `WrapPanel` of cards. App visibility:

| State | Visible? | Icon |
|-------|----------|------|
| Running, assigned to channel | ✅ Always | From running process |
| Running, unassigned | ✅ While running | From running process |
| Closed, assigned to channel | ✅ Always | Cached from last run or config |
| Closed, unassigned (or UNASSIGNED selected) | ❌ Hidden | — |

- Each card shows: app icon, display name (two-line spacing), and a channel assignment ComboBox.
- The ComboBox lists VirtualOutput channels plus an **UNASSIGNED** option (clears the assignment, causing the app to disappear when closed).
- Assignment is persistent by process name (e.g. `firefox.exe`).
- The "Refresh Apps" button re-enumerates all active audio sessions.
- Process executable paths are cached in `settings.json` (`ProcessExecutablePaths`) so icons persist across restarts.

### Settings

- MIDI device selection (auto-connect, connect/disconnect toggle)
- MIDI bindings table per channel (volume CC, mute CC/Note) with Learn mode
- Keyboard bindings per channel (mute, volume up/down) with Learn mode
- Manual MIDI binding creator (channel, kind, command, CC/note number)
- Tray behavior: minimize to tray, close to tray, start in tray
- Clear All Assignments button

## Architecture

### Audio Engine (`CoreAudioManager`)

Runs a background loop polling WASAPI every **100ms** (UI visible) or **1000ms** (minimized):

1. **RefreshPeaks** — reads `AudioMeterInformation.MasterPeakValue` per endpoint/process
2. **RefreshActiveSessions** — enumerates all render devices, caches sessions per process, syncs default endpoint changes, applies pending volume/mute changes, raises `ActiveSessionsChanged` event

Channels are classified by `AudioChannelRole`:
- `Microphone` / `Master` → control endpoint device volume directly (`AudioEndpointVolume`)
- `VirtualOutput` → control individual `SimpleAudioVolume` per assigned process session

### MIDI (`MidiListenerService`)

- Loupedeck endless encoders send relative CC ticks — wrapped at 128 for direction detection
- Volume learning binds the first CC message; mute learning accepts CC, Note, or keyboard key
- Debouncing: CC mute ignores re-triggers within 250ms, keyboard within 300ms
- Startup mute input ignored for 800ms after MIDI connect

### Keyboard (`KeyboardHookService`)

- Low-level `WH_KEYBOARD_LL` hook (no focus required)
- Only processes keys with configured bindings
- F13–F24 mapped via `KeyInterop.KeyFromVirtualKey`

### Configuration (`MixerConfiguration`)

Persisted to `%APPDATA%\AudioManager\settings.json` (schema v6):

```json
{
  "SchemaVersion": 6,
  "Channels": [
    /* 6 channels with volume, mute, endpoint, AssignedProcesses */
  ],
  "MidiBindings": [ /* CC/Note → Channel mapping */ ],
  "KeyboardBindings": [ /* VirtualKey → Channel mapping */ ],
  "ProcessExecutablePaths": { /* processName → exePath cache */ },
  "SelectedMidiDeviceId": "...",
  "MidiAutoConnect": true,
  "MinimizeToTray": false,
  "CloseToTray": false,
  "AudioDiagnosticsEnabled": false
}
```

## Project Structure

```
src/
│   ├── AudioManager/
│   │   ├── App.xaml / App.xaml.cs        # DI setup, startup
│   │   ├── AudioManager.csproj           # net8.0-windows
│   │   ├── Contracts/                    # Service interfaces
│   │   │   ├── ICoreAudioManager.cs
│   │   │   ├── IMidiListenerService.cs
│   │   │   ├── IKeyboardHookService.cs
│   │   │   ├── IOsdService.cs
│   │   │   └── ISettingsService.cs
│   │   ├── Models/
│   │   │   ├── AudioModels.cs            # Channel state, config, event args
│   │   │   ├── MidiModels.cs             # Bindings, commands, message types
│   │   │   ├── KeyboardModels.cs         # Keyboard bindings
│   │   │   └── MixerConfiguration.cs     # Root config
│   │   ├── Services/
│   │   │   ├── Audio/
│   │   │   │   └── CoreAudioManager.cs   # WASAPI engine
│   │   │   ├── Input/
│   │   │   │   └── KeyboardHookService.cs
│   │   │   ├── Midi/
│   │   │   │   ├── MidiListenerService.cs
│   │   │   │   └── MidiService.cs
│   │   │   ├── Osd/
│   │   │   │   └── OsdService.cs
│   │   │   ├── Settings/
│   │   │   │   └── JsonSettingsService.cs
│   │   │   ├── AudioMeterScaler.cs
│   │   │   └── ProcessPresentationHelper.cs
│   │   ├── ViewModels/
│   │   │   ├── MainWindowViewModel.cs    # Orchestrator
│   │   │   ├── ChannelStripViewModel.cs
│   │   │   ├── ActiveAudioAppViewModel.cs
│   │   │   ├── MidiBindingRowViewModel.cs
│   │   │   ├── ChannelOptionViewModel.cs
│   │   │   ├── EndpointOptionViewModel.cs
│   │   │   ├── MidiDeviceOptionViewModel.cs
│   │   │   ├── RelayCommand.cs
│   │   │   └── ObservableObject.cs
│   │   ├── Views/
│   │   │   ├── MainWindow.xaml / .cs
│   │   │   ├── AppsListView.xaml / .cs
│   │   │   ├── MixerView.xaml / .cs
│   │   │   ├── ChannelStripView.xaml / .cs
│   │   │   ├── SettingsView.xaml / .cs
│   │   │   └── OsdWindow.xaml / .cs
│   │   ├── Converters/
│   │   │   ├── EnumEqualsConverter.cs
│   │   │   └── ProgressHeightConverter.cs
│   │   └── Resources/
│   │       └── Styles/
│   │           └── DarkTheme.xaml
│   │   └── Resources/
│   ├── loupedeck.png
│   ├── mic_logo.png
│   ├── game_logo.png
│   ├── chat_logo.png
│   ├── media_logo.png
│   ├── master_logo.png
│   └── music_logo.png
└── README.md
```

## Running

```powershell
# Build
dotnet build src\AudioManager\AudioManager.csproj

# Run the mixer
dotnet run --project src\AudioManager\AudioManager.csproj
```

## Usage Workflow

1. Start audio in target apps (Firefox, Discord, Spotify, game, etc.)
2. Open **Apps List** tab, press **Refresh Apps**
3. Assign each app to a channel via the dropdown
4. Switch to **Mixer** tab — use sliders, mute buttons, or MIDI controller
5. Configure MIDI bindings in **Settings** tab (Learn mode or manual)
6. Mute/volume hotkeys: configurable per channel

## Known Limitations

- Group control requires an active audio session per process at least once (icons are cached afterwards).
- Some apps spawn multiple sessions across endpoints — AudioManager consolidates by process, prefers highest-peak endpoint.
- Per-app output device switching not implemented.
- Virtual audio cables are not required but device names appear in session metadata.
- `IsEndpointSelectorVisible` is hardcoded `false` — endpoint ComboBox is hidden.

## TODO

### 1. EQ Popup for MIC and MASTER

The EQ button placeholders on the Mic and MASTER channel strips currently do nothing. Implement a popup with a basic equalizer (graphic EQ or simple band sliders). The popup should appear on click, be dismissable, and apply EQ settings to the endpoint device.

### 2. About Section in Settings

Add an ABOUT section to the Settings view displaying:
- App version (from assembly)
- Link to GitHub repository
- Credits / license info

### 3. App Icon from Assets

Add `icon.png` (or `.ico`) from `assets/` as the application icon for the compiled `AudioManager.exe` — visible in the taskbar, title bar, and file explorer.

### 4. OSD Popups for Volume and Mute

Implement on-screen popups (similar to Windows 11 brightness/volume flyout) for:
- **Volume up/down** via MIDI or keyboard — show current volume level per channel
- **Mute toggle** — show mute/unmute state per channel

Display at bottom-center of the screen. Style consistent with the app's dark theme. The existing `OsdService` and `OsdWindow` can be extended or replaced.

### 5. Installer and GitHub Release

Prepare a production-ready release:
- Create an installer (e.g. WiX, Inno Setup, or dotnet publish with self-contained deploy) targeting `Program Files`
- Set up GitHub repository with a proper README, license, contribution guide
- Create a GitHub release with the installer binary and changelog
