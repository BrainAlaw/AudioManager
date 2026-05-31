using AudioManager.Models;

namespace AudioManager.Contracts;

public interface ICoreAudioManager : IAsyncDisposable
{
    event EventHandler<AudioChannelChangedEventArgs>? ChannelChanged;

    event EventHandler<ActiveAudioSessionsChangedEventArgs>? ActiveSessionsChanged;

    event EventHandler<AudioFaultEventArgs>? Faulted;

    event EventHandler<AudioDiagnosticsEventArgs>? DiagnosticsReported;

    IReadOnlyList<AudioChannelState> Channels { get; }

    IReadOnlyList<ActiveAudioSessionInfo> ActiveSessions { get; }

    Task<IReadOnlyList<AudioEndpointInfo>> GetAvailableEndpointsAsync(
        AudioEndpointKind kind,
        CancellationToken cancellationToken = default);

    Task InitializeAsync(MixerConfiguration configuration, CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task SetChannelVolumeAsync(string channelId, float normalizedVolume, CancellationToken cancellationToken = default);

    Task AdjustChannelVolumeAsync(string channelId, float normalizedDelta, CancellationToken cancellationToken = default);

    Task SetChannelMuteAsync(string channelId, bool isMuted, CancellationToken cancellationToken = default);

    Task ToggleChannelMuteAsync(string channelId, CancellationToken cancellationToken = default);

    Task SetChannelEndpointAsync(
        string channelId,
        string endpointId,
        CancellationToken cancellationToken = default);

    Task RenameChannelAsync(
        string channelId,
        string name,
        CancellationToken cancellationToken = default);

    Task AssignProcessToChannelAsync(
        string processName,
        string channelId,
        CancellationToken cancellationToken = default);

    Task RemoveProcessAssignmentAsync(
        string processName,
        string channelId,
        CancellationToken cancellationToken = default);

    void SetUiActive(bool isActive);

    Task StartSessionWatcherAsync(CancellationToken cancellationToken = default);

    Task StopSessionWatcherAsync(CancellationToken cancellationToken = default);

    void SetDiagnosticsEnabled(bool enabled);
}
