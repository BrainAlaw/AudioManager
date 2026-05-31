namespace VirtualMixer.Models;

public enum AudioEndpointKind
{
    Render,
    Capture
}

public enum AudioChannelRole
{
    Microphone,
    VirtualOutput,
    Master
}

public sealed record AudioEndpointInfo(
    string Id,
    string FriendlyName,
    AudioEndpointKind Kind,
    bool IsDefault,
    bool IsAvailable);

public sealed record ActiveAudioSessionInfo(
    string ProcessName,
    string DisplayName,
    string EndpointId,
    string EndpointName,
    float PeakValue,
    string? AssignedChannelId);

public sealed class AudioChannelConfig
{
    public const string MasterChannelId = "system";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Channel";

    public AudioChannelRole Role { get; set; } = AudioChannelRole.VirtualOutput;

    public string? EndpointId { get; set; }

    public string? IconPath { get; set; }

    public float Volume { get; set; } = 1.0f;

    public bool IsMuted { get; set; }

    public List<string> AssignedProcesses { get; set; } = [];
}

public sealed class AudioChannelState
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public AudioChannelRole Role { get; init; }

    public bool LocksEndpoint => Role is AudioChannelRole.Master or AudioChannelRole.Microphone;

    public string? IconPath { get; set; }

    public AudioEndpointInfo? Endpoint { get; set; }

    public float Volume { get; set; }

    public bool IsMuted { get; set; }

    public float PeakValue { get; set; }

    public List<string> AssignedProcesses { get; } = [];
}

public sealed class AudioChannelChangedEventArgs : EventArgs
{
    public required AudioChannelState Channel { get; init; }

    public string Reason { get; init; } = "Updated";
}

public sealed class ActiveAudioSessionsChangedEventArgs : EventArgs
{
    public required IReadOnlyList<ActiveAudioSessionInfo> Sessions { get; init; }
}

public sealed class AudioFaultEventArgs : EventArgs
{
    public string? ChannelId { get; init; }

    public required string Message { get; init; }

    public Exception? Exception { get; init; }
}

public sealed class AudioDiagnosticsEventArgs : EventArgs
{
    public required string Scope { get; init; }

    public required TimeSpan Duration { get; init; }

    public string? Details { get; init; }
}
