using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using VirtualMixer.Contracts;
using VirtualMixer.Models;
using VirtualMixer.Services;

namespace VirtualMixer.Services.Audio;

public sealed class CoreAudioManager : ICoreAudioManager
{
    private readonly object _gate = new();
    private readonly List<AudioChannelState> _channels = [];
    private readonly Dictionary<string, string> _processAssignments = new(StringComparer.OrdinalIgnoreCase);
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private List<CachedRenderSession> _cachedRenderSessions = [];
    private IReadOnlyList<ActiveAudioSessionInfo> _activeSessions = [];
    private bool _assignedSessionLevelsDirty = true;
    private volatile bool _isUiActive = true;
    private volatile bool _diagnosticsEnabled;
    private CancellationTokenSource? _watcherCts;
    private Task? _meterWatcherTask;

    public event EventHandler<AudioChannelChangedEventArgs>? ChannelChanged;
    public event EventHandler<ActiveAudioSessionsChangedEventArgs>? ActiveSessionsChanged;
    public event EventHandler<AudioFaultEventArgs>? Faulted;
    public event EventHandler<AudioDiagnosticsEventArgs>? DiagnosticsReported;

    public IReadOnlyList<AudioChannelState> Channels
    {
        get
        {
            lock (_gate)
            {
                return _channels.ToList();
            }
        }
    }

    public IReadOnlyList<ActiveAudioSessionInfo> ActiveSessions
    {
        get
        {
            lock (_gate)
            {
                return _activeSessions;
            }
        }
    }

    public async Task<IReadOnlyList<AudioEndpointInfo>> GetAvailableEndpointsAsync(
        AudioEndpointKind kind,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dataFlow = kind == AudioEndpointKind.Render ? DataFlow.Render : DataFlow.Capture;
                var defaultId = TryGetDefaultDeviceId(dataFlow);

                using var enumerator = new MMDeviceEnumerator();
                return enumerator
                    .EnumerateAudioEndPoints(dataFlow, DeviceState.Active)
                    .Select(device => new AudioEndpointInfo(
                        device.ID,
                        FormatEndpointDisplayName(device.FriendlyName),
                        kind,
                        string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                        true))
                    .ToList()
                    .AsReadOnly();
            }
            catch (Exception ex)
            {
                Faulted?.Invoke(this, new AudioFaultEventArgs
                {
                    Message = $"Unable to enumerate {kind} audio endpoints.",
                    Exception = ex
                });
                return (IReadOnlyList<AudioEndpointInfo>)Array.Empty<AudioEndpointInfo>();
            }
        }, cancellationToken);
    }

    public async Task InitializeAsync(MixerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var renderEndpoints = await GetAvailableEndpointsAsync(AudioEndpointKind.Render, cancellationToken);
        var captureEndpoints = await GetAvailableEndpointsAsync(AudioEndpointKind.Capture, cancellationToken);

        lock (_gate)
        {
            _channels.Clear();
            _processAssignments.Clear();

            foreach (var channelConfig in configuration.Channels.Take(6))
            {
                var role = channelConfig.Id == AudioChannelConfig.MasterChannelId
                    ? AudioChannelRole.Master
                    : channelConfig.Role;

                AudioEndpointInfo? endpoint;
                if (role == AudioChannelRole.Master)
                {
                    endpoint = ResolveEndpoint(null, renderEndpoints, preferDefault: true);
                }
                else if (role == AudioChannelRole.Microphone)
                {
                    endpoint = ResolveEndpoint(null, captureEndpoints, preferDefault: true);
                }
                else
                {
                    endpoint = ResolveEndpoint(channelConfig.EndpointId, renderEndpoints);
                }

                var state = new AudioChannelState
                {
                    Id = channelConfig.Id,
                    Name = channelConfig.Name,
                    Role = role,
                    IconPath = channelConfig.IconPath,
                    Endpoint = endpoint,
                    Volume = Clamp01(channelConfig.Volume),
                    IsMuted = channelConfig.IsMuted
                };

                state.AssignedProcesses.AddRange(channelConfig.AssignedProcesses);
                foreach (var processName in channelConfig.AssignedProcesses)
                {
                    _processAssignments[processName] = channelConfig.Id;
                }

                _channels.Add(state);
            }
        }

        ApplyAllChannelOutputs();
        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            Measure("RefreshPeaks", () => RefreshPeaks(cancellationToken));
            Measure("RefreshActiveSessions", () => RefreshActiveSessions(cancellationToken));
        }, cancellationToken);
    }

    public Task SetChannelVolumeAsync(string channelId, float normalizedVolume, CancellationToken cancellationToken = default)
    {
        var channel = FindChannel(channelId);
        if (channel is null)
        {
            PublishMissingChannel(channelId);
            return Task.CompletedTask;
        }

        channel.Volume = Clamp01(normalizedVolume);
        MarkAssignedSessionLevelsDirty(channel);
        ApplyChannelOutput(channel);
        PublishChanged(channel, "Volume");
        return Task.CompletedTask;
    }

    public Task AdjustChannelVolumeAsync(string channelId, float normalizedDelta, CancellationToken cancellationToken = default)
    {
        var channel = FindChannel(channelId);
        if (channel is null)
        {
            PublishMissingChannel(channelId);
            return Task.CompletedTask;
        }

        return SetChannelVolumeAsync(channelId, channel.Volume + normalizedDelta, cancellationToken);
    }

    public Task SetChannelMuteAsync(string channelId, bool isMuted, CancellationToken cancellationToken = default)
    {
        var channel = FindChannel(channelId);
        if (channel is null)
        {
            PublishMissingChannel(channelId);
            return Task.CompletedTask;
        }

        channel.IsMuted = isMuted;
        MarkAssignedSessionLevelsDirty(channel);
        ApplyChannelOutput(channel);
        PublishChanged(channel, "Mute");
        return Task.CompletedTask;
    }

    public Task ToggleChannelMuteAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var channel = FindChannel(channelId);
        return channel is null
            ? Task.CompletedTask
            : SetChannelMuteAsync(channelId, !channel.IsMuted, cancellationToken);
    }

    public async Task SetChannelEndpointAsync(
        string channelId,
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        var channel = FindChannel(channelId);
        if (channel is null)
        {
            PublishMissingChannel(channelId);
            return;
        }

        if (channel.LocksEndpoint)
        {
            return;
        }

        var endpointKind = channel.Role == AudioChannelRole.Microphone
            ? AudioEndpointKind.Capture
            : AudioEndpointKind.Render;

        var endpoint = (await GetAvailableEndpointsAsync(endpointKind, cancellationToken))
            .FirstOrDefault(candidate => string.Equals(candidate.Id, endpointId, StringComparison.OrdinalIgnoreCase));

        if (endpoint is null)
        {
            Faulted?.Invoke(this, new AudioFaultEventArgs
            {
                ChannelId = channelId,
                Message = $"Endpoint '{endpointId}' was not found for {channel.Name}."
            });
            return;
        }

        channel.Endpoint = endpoint;
        ApplyChannelOutput(channel);
        PublishChanged(channel, "Endpoint");
        await RefreshAsync(cancellationToken);
    }

    public Task RenameChannelAsync(
        string channelId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var channel = FindChannel(channelId);
        if (channel is null)
        {
            PublishMissingChannel(channelId);
            return Task.CompletedTask;
        }

        channel.Name = name;
        PublishChanged(channel, "Name");
        return Task.CompletedTask;
    }

    public Task AssignProcessToChannelAsync(
        string processName,
        string channelId,
        CancellationToken cancellationToken = default)
    {
        var channel = FindChannel(channelId);
        if (channel is null || channel.Role is AudioChannelRole.Master or AudioChannelRole.Microphone)
        {
            PublishMissingChannel(channelId);
            return Task.CompletedTask;
        }

        RemoveProcessFromAllChannels(processName);

        if (!channel.AssignedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            channel.AssignedProcesses.Add(processName);
        }

        _processAssignments[processName] = channelId;
        MarkAssignedSessionLevelsDirty(channel);
        ApplyChannelOutput(channel);
        PublishChanged(channel, "ProcessAssigned");
        return RefreshAsync(cancellationToken);
    }

    public Task RemoveProcessAssignmentAsync(
        string processName,
        string channelId,
        CancellationToken cancellationToken = default)
    {
        var channel = FindChannel(channelId);
        if (channel is not null)
        {
            channel.AssignedProcesses.RemoveAll(name => string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));
            MarkAssignedSessionLevelsDirty(channel);
            PublishChanged(channel, "ProcessRemoved");
        }

        if (_processAssignments.TryGetValue(processName, out var assignedChannelId) &&
            string.Equals(assignedChannelId, channelId, StringComparison.OrdinalIgnoreCase))
        {
            _processAssignments.Remove(processName);
        }

        return RefreshAsync(cancellationToken);
    }

    public void SetUiActive(bool isActive)
    {
        _isUiActive = isActive;
    }

    public void SetDiagnosticsEnabled(bool enabled)
    {
        _diagnosticsEnabled = enabled;
    }

    public Task StartSessionWatcherAsync(CancellationToken cancellationToken = default)
    {
        if (_meterWatcherTask is { IsCompleted: false })
        {
            return Task.CompletedTask;
        }

        _watcherCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _meterWatcherTask = Task.Run(async () =>
        {
            while (!_watcherCts.IsCancellationRequested)
            {
                try
                {
                    if (_isUiActive)
                    {
                        Measure("PeakWatcher", () => RefreshPeaks(_watcherCts.Token));
                    }

                    var meterDelay = _isUiActive ? 100 : 1000;
                    await Task.Delay(TimeSpan.FromMilliseconds(meterDelay), _watcherCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Faulted?.Invoke(this, new AudioFaultEventArgs
                    {
                        Message = "Peak meter watcher failed; it will retry.",
                        Exception = ex
                    });
                    await Task.Delay(TimeSpan.FromSeconds(1), _watcherCts.Token);
                }
            }
        }, _watcherCts.Token);

        return Task.CompletedTask;
    }

    public async Task StopSessionWatcherAsync(CancellationToken cancellationToken = default)
    {
        if (_watcherCts is null)
        {
            return;
        }

        await _watcherCts.CancelAsync();
        var runningTasks = new[] { _meterWatcherTask }
            .Where(task => task is not null)
            .Cast<Task>()
            .ToArray();

        if (runningTasks.Length > 0)
        {
            await Task.WhenAny(Task.WhenAll(runningTasks), Task.Delay(TimeSpan.FromSeconds(2), cancellationToken));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopSessionWatcherAsync();
        DisposeCachedRenderSessions(SwapCachedRenderSessions([]));
        _watcherCts?.Dispose();
        _deviceEnumerator.Dispose();
    }

    private void SyncMasterEndpoint()
    {
        var master = FindChannel(AudioChannelConfig.MasterChannelId);
        if (master is null)
        {
            return;
        }

        try
        {
            using var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var endpoint = new AudioEndpointInfo(
                defaultDevice.ID,
                FormatEndpointDisplayName(defaultDevice.FriendlyName),
                AudioEndpointKind.Render,
                true,
                true);

            if (!string.Equals(master.Endpoint?.Id, endpoint.Id, StringComparison.OrdinalIgnoreCase))
            {
                master.Endpoint = endpoint;
                ApplyEndpointVolume(master);
                if (master.IsMuted)
                {
                    ApplyEndpointMute(master);
                }

                PublishChanged(master, "DefaultEndpoint");
            }
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(this, new AudioFaultEventArgs
            {
                ChannelId = master.Id,
                Message = "Unable to resolve the Windows default audio output.",
                Exception = ex
            });
        }
    }

    private void SyncMicrophoneEndpoint()
    {
        var mic = FindChannel("mic");
        if (mic is null)
        {
            return;
        }

        try
        {
            using var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            var endpoint = new AudioEndpointInfo(
                defaultDevice.ID,
                FormatEndpointDisplayName(defaultDevice.FriendlyName),
                AudioEndpointKind.Capture,
                true,
                true);

            if (!string.Equals(mic.Endpoint?.Id, endpoint.Id, StringComparison.OrdinalIgnoreCase))
            {
                mic.Endpoint = endpoint;
                ApplyEndpointVolume(mic);
                ApplyEndpointMute(mic);

                PublishChanged(mic, "DefaultEndpoint");
            }
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(this, new AudioFaultEventArgs
            {
                ChannelId = mic.Id,
                Message = "Unable to resolve the Windows default audio input.",
                Exception = ex
            });
        }
    }

    private void RefreshChannel(
        AudioChannelState channel,
        IReadOnlyDictionary<string, float>? sessionPeaksByProcess = null)
    {
        channel.PeakValue = 0;

        if (channel.Role == AudioChannelRole.Master || channel.Role == AudioChannelRole.Microphone)
        {
            if (channel.Endpoint is not null)
            {
                try
                {
                    using var device = _deviceEnumerator.GetDevice(channel.Endpoint.Id);
                    var endpointMuted = device.AudioEndpointVolume.Mute;
                    if (channel.IsMuted != endpointMuted)
                    {
                        channel.IsMuted = endpointMuted;
                        PublishChanged(channel, "EndpointMuteObserved");
                    }

                    channel.PeakValue = device.AudioMeterInformation.MasterPeakValue;
                }
                catch (Exception ex)
                {
                    PublishEndpointFault(channel, "refresh peak", ex);
                }
            }

            return;
        }

        if (channel.Endpoint is null)
        {
            return;
        }

        try
        {
            using var device = _deviceEnumerator.GetDevice(channel.Endpoint.Id);
            var endpointPeak = device.AudioMeterInformation.MasterPeakValue;
            var sessionPeak = GetAssignedProcessesPeak(channel, sessionPeaksByProcess);
            channel.PeakValue = Math.Max(endpointPeak, sessionPeak);
        }
        catch (Exception ex)
        {
            PublishEndpointFault(channel, "refresh endpoint peak", ex);
        }
    }

    private void RefreshPeaks(CancellationToken cancellationToken)
    {
        List<AudioChannelState> snapshot;
        List<CachedRenderSession> cachedSessions;
        lock (_gate)
        {
            snapshot = _channels.ToList();
            cachedSessions = _cachedRenderSessions.ToList();
        }

        var sessionPeaksByProcess = BuildSessionPeakSnapshot(cachedSessions);

        foreach (var channel in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var oldPeak = channel.PeakValue;
            RefreshChannel(channel, sessionPeaksByProcess);

            if (Math.Abs(oldPeak - channel.PeakValue) > 0.001f)
            {
                ChannelChanged?.Invoke(this, new AudioChannelChangedEventArgs
                {
                    Channel = channel,
                    Reason = "PeakRefresh"
                });
            }
        }
    }

    private void RefreshActiveSessions(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SyncMasterEndpoint();
        SyncMicrophoneEndpoint();

        var cacheChanged = Measure("RefreshRenderSessionCache", () => RefreshRenderSessionCache());
        IReadOnlyList<ActiveAudioSessionInfo> activeSessions;
        lock (_gate)
        {
            activeSessions = _activeSessions;
        }

        ActiveSessionsChanged?.Invoke(this, new ActiveAudioSessionsChangedEventArgs
        {
            Sessions = activeSessions
        });

        if (cacheChanged || ShouldApplyAssignedSessionLevels())
        {
            ApplyAllAssignedSessionLevels();
        }
    }

    private float GetAssignedProcessesPeak(
        AudioChannelState channel,
        IReadOnlyDictionary<string, float>? sessionPeaksByProcess = null)
    {
        if (channel.AssignedProcesses.Count == 0)
        {
            return 0f;
        }

        if (sessionPeaksByProcess is not null)
        {
            var peakFromCache = 0f;
            foreach (var processName in channel.AssignedProcesses)
            {
                if (sessionPeaksByProcess.TryGetValue(processName, out var processPeak) &&
                    processPeak > peakFromCache)
                {
                    peakFromCache = processPeak;
                }
            }

            return peakFromCache;
        }

        List<CachedRenderSession> cachedSessions;
        lock (_gate)
        {
            cachedSessions = _cachedRenderSessions.ToList();
        }

        return GetAssignedProcessesPeak(channel, BuildSessionPeakSnapshot(cachedSessions));
    }

    private Dictionary<string, float> BuildSessionPeakSnapshot(IReadOnlyList<CachedRenderSession> cachedSessions)
    {
        var peaks = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        foreach (var cachedSession in cachedSessions)
        {
            var sessionPeak = cachedSession.GetPeakValue();
            if (!peaks.TryGetValue(cachedSession.ProcessName, out var existingPeak) || sessionPeak > existingPeak)
            {
                peaks[cachedSession.ProcessName] = sessionPeak;
            }
        }

        return peaks;
    }

    private bool RefreshRenderSessionCache()
    {
        var freshSessions = new List<CachedRenderSession>();

        try
        {
            var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                var endpointName = FormatEndpointDisplayName(device.FriendlyName);
                try
                {
                    var deviceSessions = device.AudioSessionManager.Sessions;
                    for (var index = 0; index < deviceSessions.Count; index++)
                    {
                        var session = deviceSessions[index];
                        var processId = (int)session.GetProcessID;
                        if (processId == 0)
                        {
                            continue;
                        }

                        var processName = TryGetProcessName(processId);
                        var displayName = ProcessPresentationHelper.GetFriendlyName(
                            processName,
                            string.IsNullOrWhiteSpace(session.DisplayName) ? null : session.DisplayName);
                        _processAssignments.TryGetValue(processName, out var assignedChannelId);
                        freshSessions.Add(new CachedRenderSession(
                            processId,
                            processName,
                            displayName,
                            device.ID,
                            endpointName,
                            assignedChannelId,
                            session));
                    }
                }
                catch
                {
                    // Ignore single device errors.
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(this, new AudioFaultEventArgs
            {
                Message = "Unable to collect active audio sessions.",
                Exception = ex
            });
        }

        List<CachedRenderSession> mergedCache;
        List<CachedRenderSession> staleCache;
        lock (_gate)
        {
            var existingByKey = _cachedRenderSessions.ToDictionary(session => session.CacheKey, StringComparer.OrdinalIgnoreCase);
            mergedCache = [];
            foreach (var freshSession in freshSessions)
            {
                if (existingByKey.Remove(freshSession.CacheKey, out var existingSession))
                {
                    existingSession.UpdateMetadata(
                        freshSession.ProcessName,
                        freshSession.DisplayName,
                        freshSession.EndpointId,
                        freshSession.EndpointName,
                        freshSession.AssignedChannelId);
                    freshSession.Session.Dispose();
                    mergedCache.Add(existingSession);
                }
                else
                {
                    mergedCache.Add(freshSession);
                }
            }

            staleCache = existingByKey.Values.ToList();
            _cachedRenderSessions = mergedCache;
            _activeSessions = BuildActiveSessionsSnapshot(mergedCache);
        }

        DisposeCachedRenderSessions(staleCache);
        return staleCache.Count > 0;
    }

    private IReadOnlyList<ActiveAudioSessionInfo> BuildActiveSessionsSnapshot(IReadOnlyList<CachedRenderSession> cachedSessions)
    {
        return cachedSessions
            .GroupBy(session => session.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var primaryEndpoint = group
                    .GroupBy(session => session.EndpointId, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(endpointGroup => endpointGroup.Max(session => session.GetPeakValue()))
                    .ThenByDescending(endpointGroup => endpointGroup.Count())
                    .First()
                    .OrderByDescending(session => session.GetPeakValue())
                    .First();

                return new ActiveAudioSessionInfo(
                    primaryEndpoint.ProcessName,
                    ProcessPresentationHelper.GetFriendlyName(primaryEndpoint.ProcessName, primaryEndpoint.DisplayName),
                    primaryEndpoint.EndpointId,
                    primaryEndpoint.EndpointName,
                    group.Max(session => session.GetPeakValue()),
                    primaryEndpoint.AssignedChannelId);
            })
            .OrderBy(session => session.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private void ApplyAllChannelOutputs()
    {
        foreach (var channel in Channels)
        {
            ApplyChannelOutput(channel);
        }
    }

    private void ApplyChannelOutput(AudioChannelState channel)
    {
        if (channel.Role == AudioChannelRole.Master || channel.Role == AudioChannelRole.Microphone)
        {
            ApplyEndpointVolume(channel);
            ApplyEndpointMute(channel);
            return;
        }

        ApplyAssignedSessionLevels(channel);
    }

    private void ApplyAssignedSessionLevels(AudioChannelState channel)
    {
        if (channel.AssignedProcesses.Count == 0)
        {
            return;
        }

        List<CachedRenderSession> cachedSessions;
        lock (_gate)
        {
            cachedSessions = _cachedRenderSessions.ToList();
        }

        foreach (var cachedSession in cachedSessions)
        {
            if (!channel.AssignedProcesses.Contains(cachedSession.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                cachedSession.Session.SimpleAudioVolume.Volume = channel.Volume;
                cachedSession.Session.SimpleAudioVolume.Mute = channel.IsMuted;
            }
            catch
            {
                // Ignore single session errors.
            }
        }
    }

    private void ApplyAllAssignedSessionLevels()
    {
        List<AudioChannelState> snapshot;
        lock (_gate)
        {
            snapshot = _channels
                .Where(channel => channel.Role == AudioChannelRole.VirtualOutput)
                .ToList();
        }

        foreach (var channel in snapshot)
        {
            Measure($"ApplyAssignedSessionLevels:{channel.Id}", () => ApplyAssignedSessionLevels(channel));
        }

        lock (_gate)
        {
            _assignedSessionLevelsDirty = false;
        }
    }

    private List<CachedRenderSession> SwapCachedRenderSessions(List<CachedRenderSession> newCache)
    {
        lock (_gate)
        {
            var staleCache = _cachedRenderSessions;
            _cachedRenderSessions = newCache;
            return staleCache;
        }
    }

    private bool ShouldApplyAssignedSessionLevels()
    {
        lock (_gate)
        {
            return _assignedSessionLevelsDirty;
        }
    }

    private void MarkAssignedSessionLevelsDirty(AudioChannelState channel)
    {
        if (channel.Role != AudioChannelRole.VirtualOutput)
        {
            return;
        }

        lock (_gate)
        {
            _assignedSessionLevelsDirty = true;
        }
    }

    private static string BuildRenderSessionCacheKey(IReadOnlyList<CachedRenderSession> cachedSessions)
    {
        return string.Join(
            '\n',
            cachedSessions
                .Select(session => $"{session.ProcessName}|{session.EndpointId}|{session.DisplayName}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static void DisposeCachedRenderSessions(IEnumerable<CachedRenderSession> cachedSessions)
    {
        foreach (var cachedSession in cachedSessions)
        {
            try
            {
                cachedSession.Session.Dispose();
            }
            catch
            {
                // Ignore stale COM session disposal faults.
            }
        }
    }

    private void ApplyEndpointVolume(AudioChannelState channel)
    {
        if (channel.Endpoint is null)
        {
            return;
        }

        try
        {
            using var device = _deviceEnumerator.GetDevice(channel.Endpoint.Id);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = channel.Volume;
        }
        catch (Exception ex)
        {
            PublishEndpointFault(channel, "set endpoint volume", ex);
        }
    }

    private void ApplyEndpointMute(AudioChannelState channel)
    {
        if (channel.Endpoint is null)
        {
            return;
        }

        try
        {
            using var device = _deviceEnumerator.GetDevice(channel.Endpoint.Id);
            device.AudioEndpointVolume.Mute = channel.IsMuted;
        }
        catch (Exception ex)
        {
            PublishEndpointFault(channel, "set endpoint mute", ex);
        }
    }

    private void RemoveProcessFromAllChannels(string processName)
    {
        lock (_gate)
        {
            foreach (var channel in _channels)
            {
                channel.AssignedProcesses.RemoveAll(name =>
                    string.Equals(name, processName, StringComparison.OrdinalIgnoreCase));
            }

            _processAssignments.Remove(processName);
        }
    }

    private AudioChannelState? FindChannel(string channelId)
    {
        lock (_gate)
        {
            return _channels.FirstOrDefault(channel =>
                string.Equals(channel.Id, channelId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static AudioEndpointInfo? ResolveEndpoint(
        string? endpointId,
        IReadOnlyList<AudioEndpointInfo> endpoints,
        bool preferDefault = false)
    {
        if (endpoints.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(endpointId))
        {
            var selected = endpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Id, endpointId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        if (preferDefault)
        {
            return endpoints.FirstOrDefault(endpoint => endpoint.IsDefault) ?? endpoints[0];
        }

        return endpoints.FirstOrDefault(endpoint => endpoint.IsDefault) ?? endpoints[0];
    }

    private static string FormatEndpointDisplayName(string value)
    {
        var cleaned = Regex.Replace(value, @"\s*\([^)]*\)\s*$", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? value : cleaned;
    }

    private static string? TryGetDefaultDeviceId(DataFlow dataFlow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia).ID;
        }
        catch
        {
            return null;
        }
    }

    private static string TryGetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return $"{process.ProcessName}.exe";
        }
        catch
        {
            return $"pid-{processId}";
        }
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    private void PublishChanged(AudioChannelState channel, string reason)
    {
        ChannelChanged?.Invoke(this, new AudioChannelChangedEventArgs
        {
            Channel = channel,
            Reason = reason
        });
    }

    private void PublishMissingChannel(string channelId)
    {
        Faulted?.Invoke(this, new AudioFaultEventArgs
        {
            ChannelId = channelId,
            Message = $"Channel '{channelId}' was not found."
        });
    }

    private void PublishEndpointFault(AudioChannelState channel, string operation, Exception exception)
    {
        Faulted?.Invoke(this, new AudioFaultEventArgs
        {
            ChannelId = channel.Id,
            Message = $"Unable to {operation} for {channel.Name}.",
            Exception = exception
        });
    }

    private T Measure<T>(string scope, Func<T> action)
    {
        if (!_diagnosticsEnabled)
        {
            return action();
        }

        var stopwatch = Stopwatch.StartNew();
        var result = action();
        stopwatch.Stop();
        DiagnosticsReported?.Invoke(this, new AudioDiagnosticsEventArgs
        {
            Scope = scope,
            Duration = stopwatch.Elapsed
        });
        return result;
    }

    private void Measure(string scope, Action action)
    {
        if (!_diagnosticsEnabled)
        {
            action();
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        action();
        stopwatch.Stop();
        DiagnosticsReported?.Invoke(this, new AudioDiagnosticsEventArgs
        {
            Scope = scope,
            Duration = stopwatch.Elapsed
        });
    }

    private sealed class CachedRenderSession
    {
        public CachedRenderSession(
            int processId,
            string processName,
            string displayName,
            string endpointId,
            string endpointName,
            string? assignedChannelId,
            AudioSessionControl session)
        {
            ProcessId = processId;
            ProcessName = processName;
            DisplayName = displayName;
            EndpointId = endpointId;
            EndpointName = endpointName;
            AssignedChannelId = assignedChannelId;
            Session = session;
        }

        public int ProcessId { get; private set; }

        public string ProcessName { get; private set; }

        public string DisplayName { get; private set; }

        public string EndpointId { get; private set; }

        public string EndpointName { get; private set; }

        public string? AssignedChannelId { get; private set; }

        public AudioSessionControl Session { get; private set; }

        public string CacheKey => $"{ProcessId}|{EndpointId}";

        public void UpdateMetadata(
            string processName,
            string displayName,
            string endpointId,
            string endpointName,
            string? assignedChannelId)
        {
            ProcessName = processName;
            DisplayName = displayName;
            EndpointId = endpointId;
            EndpointName = endpointName;
            AssignedChannelId = assignedChannelId;
        }

        public float GetPeakValue()
        {
            try
            {
                return Session.AudioMeterInformation.MasterPeakValue;
            }
            catch
            {
                return 0f;
            }
        }
    }
}
