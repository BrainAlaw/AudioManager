# Changelog

## 1.0.4 - 2026-10-12

### Fixed

- Settings: the configuration is snapshotted before writing, so UI changes can no longer corrupt a save in progress; overlapping saves can no longer write an older state over a newer one; saves retry when the file is briefly locked.
- Settings: pending changes are flushed synchronously on exit and on Windows logoff/shutdown; a failed startup load never overwrites the real settings file.
- Settings: configuration is validated on load (unique channels, one owning channel per app, valid MIDI/keyboard bindings, NaN volumes, legacy files without a schema version, case-insensitive executable paths).
- Settings: save failures no longer crash the app; they are shown in the status bar, retried with backoff and written to `%APPDATA%\AudioManager\error.log`.
- Assignments: apps are now re-attached to their channel automatically when they start or restart, without pressing Refresh.
- Assignments: unassigning an app restores its volume and mute state; rapid re-assignments are serialized; transient empty selections no longer silently unassign an app; the configuration is the single source of truth, so the Apps list can no longer show a stale channel.
- Assignments: audio state changes are published as snapshots and all assignment data is accessed under a lock (previously a race with the meter watcher).
- Master and microphone now mirror the Windows endpoint state at startup and follow external volume/mute changes, instead of overwriting them with stored values.
- Run on startup: the Windows autostart entry is reconciled with the setting, failures roll the toggle back, and only autostart launches hide to the tray (manual launches show the window).
- A second launch now activates the running instance instead of starting a duplicate (duplicate keyboard hooks, competing settings writers).
- The tray icon is removed on a normal window close.
- MIDI: bindings are snapshotted for the MIDI thread, encoder state is locked, a device is re-found by name when Windows renumbers MIDI inputs, and the selected device is only saved when it changes.

### Performance

- Mixer strips no longer rebuild app icons on every meter update (the main source of UI lag); icons and friendly names are cached, process handles are released.
- Audio events are coalesced to the newest state per channel and no longer block the audio thread on the UI thread.
- Keyboard hook only forwards bound keys to the app; held volume keys repeat smoothly and held mute keys no longer flicker.
- Repeated identical audio faults are throttled.

## 1.0.3 - 2026-09-27

### Fixed

- Save settings atomically after changes instead of writing during application shutdown.
- Keep the previous settings in a backup and recover it when the main configuration is unreadable.
- Preserve unreadable configuration files for diagnosis and show a startup error if recovery fails, instead of overwriting settings with defaults.
- Save debounced settings on the UI context and report background save errors in the status bar.

### Validation

- Added settings regression checks to the release pipeline.

## 1.0.2 - 2026-08-11

### Fixed

- Improved mixer app icon presentation with stable app chips and overflow handling.
- Added friendly app-name tooltips for mixer app icons.
- Improved microphone peak metering by using an internal WASAPI capture meter instead of relying only on the Windows sound panel meter.
- Fixed Apps List scrollbar scaling and mixer group meters showing activity on unrelated channels.
- Reduced UI-thread blocking from audio/session updates.

## 1.0.1 - 2026-07-06

### Fixed

- Improved OSD responsiveness during rapid MIDI volume changes.
- Reduced UI-thread pressure from OSD updates by coalescing rapid volume events.
- Fixed OSD volume bar fill so it reflects the current channel volume.
- Restored smooth OSD volume knob animation and prevented clipping at the bottom of the control.
- Fixed mixer assignment sync so app icons move immediately when an app is reassigned to another channel.

### Changed

- OSD volume display now uses a lightweight, non-interactive progress presentation instead of an interactive slider control.
- Added an implementation note for the OSD and assignment sync fixes in `OSD_AND_ASSIGNMENT_FIX_PLAN.md`.

## 1.0.0 - 2026-07-06

### Added

- Initial public release of Audio Manager.
