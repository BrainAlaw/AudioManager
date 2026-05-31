using NAudio.Midi;
using VirtualMixer.Contracts;
using VirtualMixer.Models;
using System.Runtime.CompilerServices;

namespace VirtualMixer.Services.Midi;

public sealed class MidiListenerService : IMidiListenerService
{
    private readonly object _gate = new();
    private readonly List<MidiIn> _midiInputs = [];
    private readonly Dictionary<string, int> _lastControlValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastControlDirections = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<MidiBinding> _bindings = [];

    public event EventHandler<MidiControlChangeEventArgs>? ControlChanged;
    public event EventHandler<MidiNoteEventArgs>? NoteChanged;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _midiInputs.Count > 0;
            }
        }
    }

    public IReadOnlyList<MidiDeviceInfo> GetInputDevices()
    {
        var devices = new List<MidiDeviceInfo>();
        for (var index = 0; index < MidiIn.NumberOfDevices; index++)
        {
            var capabilities = MidiIn.DeviceInfo(index);
            devices.Add(new MidiDeviceInfo($"{index}:{capabilities.ProductName}", index, capabilities.ProductName));
        }

        return devices;
    }

    public Task StartAsync(string midiDeviceId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            StopInternal();

            var devices = ResolveDevicesToOpen(midiDeviceId);
            if (devices.Count == 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(midiDeviceId)
                    ? "No MIDI input devices were found."
                    : $"MIDI input device '{midiDeviceId}' was not found.");
            }

            foreach (var device in devices)
            {
                try
                {
                    var midiIn = new MidiIn(device.Index);
                    midiIn.MessageReceived += OnMessageReceived;
                    midiIn.ErrorReceived += OnErrorReceived;
                    midiIn.Start();
                    _midiInputs.Add(midiIn);
                }
                catch
                {
                    // Skip devices that fail to open. One broken endpoint should
                    // not prevent the rest of the MIDI inputs from being listened to.
                }
            }

            if (_midiInputs.Count == 0)
            {
                throw new InvalidOperationException("No MIDI input devices could be opened.");
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            StopInternal();
        }

        return Task.CompletedTask;
    }

    public MixerCommand? ResolveBinding(MidiMessageIdentity identity, IReadOnlyList<MidiBinding> bindings)
    {
        var binding = bindings.FirstOrDefault(candidate =>
            candidate.Kind == identity.Kind &&
            candidate.MidiChannel == identity.MidiChannel &&
            candidate.ControllerOrNote == identity.ControllerOrNote);

        if (binding is null)
        {
            return null;
        }

        var value = binding.Command switch
        {
            MixerCommandKind.VolumeDelta => binding.InvertDirection ? -binding.Step : binding.Step,
            MixerCommandKind.ToggleMute => 1f,
            MixerCommandKind.SetMute => 1f,
            MixerCommandKind.SetVolume => binding.Step,
            _ => 0f
        };

        return new MixerCommand(binding.Command, binding.ChannelId, value);
    }

    public void UpdateBindings(IReadOnlyList<MidiBinding> bindings)
    {
        _bindings = bindings;
    }

    public void ResetEncoderBaselines(IEnumerable<MidiMessageIdentity> identities)
    {
        lock (_gate)
        {
            foreach (var identity in identities)
            {
                if (identity.Kind != MidiBindingKind.ControlChange)
                {
                    continue;
                }

                var suffix = $":{identity.MidiChannel}:{identity.ControllerOrNote}";
                foreach (var key in _lastControlValues.Keys.Where(key => key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    _lastControlValues.Remove(key);
                }

                foreach (var key in _lastControlDirections.Keys.Where(key => key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    _lastControlDirections.Remove(key);
                }
            }
        }
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }

    private IReadOnlyList<MidiDeviceInfo> ResolveDevicesToOpen(string midiDeviceId)
    {
        var devices = GetInputDevices();
        if (string.IsNullOrWhiteSpace(midiDeviceId))
        {
            return devices;
        }

        var selected = devices.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, midiDeviceId, StringComparison.OrdinalIgnoreCase));

        if (selected is not null)
        {
            return [selected];
        }

        if (int.TryParse(midiDeviceId.Split(':')[0], out var parsedIndex))
        {
            var byIndex = devices.FirstOrDefault(candidate => candidate.Index == parsedIndex);
            if (byIndex is not null)
            {
                return [byIndex];
            }
        }

        return [];
    }

    private void StopInternal()
    {
        foreach (var midiIn in _midiInputs)
        {
            try
            {
                midiIn.Stop();
            }
            catch
            {
                // Ignore shutdown errors from already disconnected devices.
            }

            midiIn.MessageReceived -= OnMessageReceived;
            midiIn.ErrorReceived -= OnErrorReceived;
            midiIn.Dispose();
        }

        _midiInputs.Clear();
        _lastControlValues.Clear();
        _lastControlDirections.Clear();
    }

    private void OnMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        if (TryParseRawControlChange(e.RawMessage, out var rawMidiChannel, out var rawControllerNumber, out var rawControllerValue))
        {
            var sourceIndex = sender is MidiIn midiIn ? RuntimeHelpers.GetHashCode(midiIn) : -1;
            var ccIdentity = new MidiMessageIdentity(
                MidiBindingKind.ControlChange,
                rawMidiChannel,
                rawControllerNumber);

            var ccKey = $"{sourceIndex}:{rawMidiChannel}:{rawControllerNumber}";
            var delta = ComputeWrappedTickDelta(ccKey, rawControllerValue);

            var binding = _bindings.FirstOrDefault(candidate =>
                candidate.Kind == ccIdentity.Kind &&
                candidate.MidiChannel == ccIdentity.MidiChannel &&
                candidate.ControllerOrNote == ccIdentity.ControllerOrNote);

            if (binding?.InvertDirection == true)
            {
                delta = -delta;
            }

            var command = ResolveBinding(ccIdentity, _bindings);
            if (command?.Kind == MixerCommandKind.VolumeDelta)
            {
                command = delta == 0
                    ? null
                    : command with { Value = Math.Abs(command.Value) * delta };
            }

            ControlChanged?.Invoke(this, new MidiControlChangeEventArgs
            {
                Identity = ccIdentity,
                Value = rawControllerValue,
                Delta = delta,
                Command = command
            });
            return;
        }

        switch (e.MidiEvent)
        {
            case NoteEvent note:
                var noteIdentity = new MidiMessageIdentity(
                    MidiBindingKind.Note,
                    note.Channel,
                    note.NoteNumber);

                NoteChanged?.Invoke(this, new MidiNoteEventArgs
                {
                    Identity = noteIdentity,
                    Velocity = note.Velocity,
                    IsPressed = note.CommandCode == MidiCommandCode.NoteOn && note.Velocity > 0,
                    Command = ResolveBinding(noteIdentity, _bindings)
                });
                break;
        }
    }

    private static void OnErrorReceived(object? sender, MidiInMessageEventArgs e)
    {
        // Native MIDI errors are intentionally non-fatal here. They can be
        // surfaced later through diagnostics if needed.
    }

    private int ComputeWrappedTickDelta(string key, int currentValue)
    {
        if (!_lastControlValues.TryGetValue(key, out var previousValue))
        {
            _lastControlValues[key] = currentValue;
            return 0;
        }

        if (currentValue == previousValue)
        {
            return _lastControlDirections.TryGetValue(key, out var lastDirection)
                ? lastDirection
                : 0;
        }

        var rawDelta = currentValue - previousValue;
        if (rawDelta > 64)
        {
            rawDelta -= 128;
        }
        else if (rawDelta < -64)
        {
            rawDelta += 128;
        }

        _lastControlValues[key] = currentValue;

        var direction = rawDelta > 0 ? 1 : -1;
        _lastControlDirections[key] = direction;
        return direction;
    }

    private static bool TryParseRawControlChange(
        int rawMessage,
        out int midiChannel,
        out int controllerNumber,
        out int controllerValue)
    {
        var status = rawMessage & 0xFF;
        controllerNumber = (rawMessage >> 8) & 0xFF;
        controllerValue = (rawMessage >> 16) & 0xFF;

        if ((status & 0xF0) != 0xB0)
        {
            midiChannel = 0;
            controllerNumber = 0;
            controllerValue = 0;
            return false;
        }

        midiChannel = (status & 0x0F) + 1;
        return true;
    }
}
