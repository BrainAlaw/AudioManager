namespace AudioManager.Models;

public sealed class KeyboardBinding
{
    public int VirtualKey { get; set; }

    public string KeyName { get; set; } = string.Empty;

    public string ChannelId { get; set; } = string.Empty;

    public MixerCommandKind Command { get; set; } = MixerCommandKind.ToggleMute;
}

public sealed class KeyboardKeyEventArgs : EventArgs
{
    public required int VirtualKey { get; init; }

    public required string KeyName { get; init; }
}
