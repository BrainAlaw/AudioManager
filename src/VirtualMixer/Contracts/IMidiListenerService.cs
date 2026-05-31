using VirtualMixer.Models;

namespace VirtualMixer.Contracts;

/// <summary>
/// Listens to MIDI input devices and translates raw CC/Note messages into
/// normalized mixer commands. This contract intentionally has no WPF dependency.
/// </summary>
public interface IMidiListenerService : IDisposable
{
    /// <summary>
    /// Raised for MIDI Control Change messages, typically endless encoder movement.
    /// </summary>
    event EventHandler<MidiControlChangeEventArgs>? ControlChanged;

    /// <summary>
    /// Raised for MIDI Note On/Off messages, typically hardware button presses.
    /// </summary>
    event EventHandler<MidiNoteEventArgs>? NoteChanged;

    /// <summary>
    /// True when a MIDI input device is open and receiving messages.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Enumerates MIDI input devices currently exposed by Windows.
    /// </summary>
    IReadOnlyList<MidiDeviceInfo> GetInputDevices();

    /// <summary>
    /// Opens a MIDI input device by stable device id when possible.
    /// Implementations should close any previously opened device first.
    /// </summary>
    Task StartAsync(string midiDeviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops listening and releases the native MIDI handle.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Maps a hardware CC or Note message to a logical channel action.
    /// The returned command is null when no configured binding exists.
    /// </summary>
    MixerCommand? ResolveBinding(MidiMessageIdentity identity, IReadOnlyList<MidiBinding> bindings);

    /// <summary>
    /// Replaces the active in-memory bindings used to resolve incoming MIDI.
    /// </summary>
    void UpdateBindings(IReadOnlyList<MidiBinding> bindings);

    /// <summary>
    /// Clears remembered CC positions so the next encoder message re-arms without
    /// applying a spurious delta (e.g. after a manual slider move).
    /// </summary>
    void ResetEncoderBaselines(IEnumerable<MidiMessageIdentity> identities);
}
