# Audio Manager: OSD Performance, Notification Bar, Assignment Sync Fixes

## Summary

Fix three current issues in Audio Manager:

- OSD lags when another application is fullscreen.
- The notification volume bar should fill with the app accent red according to current volume.
- After moving an app from one channel to another, the mixer icon remains on the previous channel until restart.

## Key Changes

### OSD Fullscreen Performance

- Reduce OSD render work during rapid volume changes:
  - do not run full opacity transitions on every volume update,
  - update the existing visible OSD content in place,
  - restart only the hide timer on repeated volume changes,
  - run fade-in only when the OSD was hidden.
- Change `OsdService` dispatcher calls from synchronous `Dispatcher.Invoke` to asynchronous `BeginInvoke` so MIDI/audio input does not block on the UI thread.
- Keep the focus-protection flags:
  - `WS_EX_TRANSPARENT`,
  - `WS_EX_TOOLWINDOW`,
  - `WS_EX_NOACTIVATE`,
  - `Topmost`,
  - `ShowActivated = false`.
- Add a default low-animation OSD behavior:
  - volume update: no fade transition,
  - mute/volume kind switch: short fade only when notification type changes,
  - hide: short fade or immediate `Hide()` if fullscreen lag remains visible.

### Notification Volume Bar

- Replace the OSD volume `Slider` with a non-interactive `ProgressBar`.
- Use `Vm.AccentBrush` for the filled volume portion.
- Bind/update the value as `channel.Volume * 100`.
- Remove the thumb from the OSD volume notification because the OSD is not interactive.
- Use the existing `Vm.OsdHorizontalProgressBar` if its width converter is registered correctly; otherwise add/register the missing converter or use a template that binds `PART_Indicator`.

### Assignment Icon Sync

- After app assignment changes, update mixer channel icons immediately instead of waiting for restart or a later full session refresh.
- In `MainWindowViewModel.OnAppAssignmentChanged`:
  - remove the process from all config channel assignments,
  - add it to the selected target channel if present,
  - call the audio manager assignment/removal method,
  - refresh the old and new channel strip view models.
- Preferred implementation:
  - add `RefreshChannelAssignmentsFromConfiguration(params string?[] channelIds)` in `MainWindowViewModel`,
  - resolve each affected `ChannelStripViewModel`,
  - update its assigned process icon list from `_configuration.Channels`,
  - preserve current volume/mute/peak state from `_audioManager.Channels`.
- Alternative:
  - update `CoreAudioManager.AssignProcessToChannelAsync` so `ChannelChanged` is published for both the old source channel and the new target channel.

## Test Plan

- Build the current solution with `dotnet build`.
- OSD performance:
  - launch a fullscreen application,
  - rapidly change volume through MIDI for 10-20 seconds,
  - verify OSD does not visibly lag and input remains responsive.
- Notification bar:
  - verify 0%, 25%, 50%, and 100% show proportional red fill,
  - verify mute notification still renders correctly.
- Assignment sync:
  - assign an active app from `MUSIC` to `GAME`,
  - verify the icon disappears from `MUSIC` and appears under `GAME` without restart,
  - repeat with an assigned but currently closed app.
- Regression:
  - MIDI volume still works,
  - mute still works,
  - Apps List dropdown still saves assignment,
  - settings file still persists assignments.

## Assumptions

- OSD is non-interactive, so a `ProgressBar` is the correct control for volume display.
- Smooth fullscreen behavior is more important than decorative OSD animation.
- `ChannelId` remains the source of truth for assignments; channel display names do not affect routing.
