namespace VirtualMixer.Models;

public enum MidiBindingKind
{
    ControlChange,
    Note
}

public enum MixerCommandKind
{
    VolumeDelta,
    SetVolume,
    ToggleMute,
    SetMute,
    VolumeUp,
    VolumeDown
}

public sealed record MidiDeviceInfo(string Id, int Index, string Name);

public sealed record MidiMessageIdentity(
    MidiBindingKind Kind,
    int MidiChannel,
    int ControllerOrNote);

public sealed class MidiBinding
{
    public MidiBindingKind Kind { get; set; }

    public int MidiChannel { get; set; }

    public int ControllerOrNote { get; set; }

    public string ChannelId { get; set; } = string.Empty;

    public MixerCommandKind Command { get; set; }

    public float Step { get; set; } = 0.02f;

    public bool InvertDirection { get; set; }
}

public sealed record MixerCommand(
    MixerCommandKind Kind,
    string ChannelId,
    float Value);

public sealed class MidiControlChangeEventArgs : EventArgs
{
    public required MidiMessageIdentity Identity { get; init; }

    public required int Value { get; init; }

    public required int Delta { get; init; }

    public MixerCommand? Command { get; init; }
}

public sealed class MidiNoteEventArgs : EventArgs
{
    public required MidiMessageIdentity Identity { get; init; }

    public required int Velocity { get; init; }

    public required bool IsPressed { get; init; }

    public MixerCommand? Command { get; init; }
}
