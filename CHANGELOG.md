# Changelog

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
