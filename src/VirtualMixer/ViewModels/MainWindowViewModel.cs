using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows;
using VirtualMixer.Contracts;
using VirtualMixer.Models;
using VirtualMixer.Services;
using VirtualMixer.Services.Midi;
using WpfApplication = System.Windows.Application;

namespace VirtualMixer.ViewModels;

public enum ViewType
{
    Mixer,
    AppsList,
    Settings
}

public sealed class MainWindowViewModel : ObservableObject
{
    private const float VolumeStepPerTick = 0.01f;
    private static readonly string[] ChannelDisplayOrder = ["mic", AudioChannelConfig.MasterChannelId, "chat", "game", "browser", "music"];

    private readonly ISettingsService _settingsService;
    private readonly ICoreAudioManager _audioManager;
    private readonly IMidiListenerService _midiListener;
    private readonly IKeyboardHookService _keyboardHook;
    private readonly IOsdService _osdService;
    private readonly Dictionary<string, DateTimeOffset> _lastCcMuteToggleAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, DateTimeOffset> _lastKeyboardMuteToggleAt = [];
    private readonly Dictionary<string, CancellationTokenSource> _volumeApplyCtsByChannel = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _configurationSaveCts;
    private MixerConfiguration _configuration = new();
    private string _status = "Starting";
    private string _midiStatus = "Not connected";
    private string? _selectedMidiDeviceId;
    private bool _minimizeToTray;
    private bool _closeToTray;
    private bool _startInTray;
    private bool _isMidiConnected;
    private ViewType _currentViewType = ViewType.Mixer;
    private string? _manualBindingChannelId;
    private MidiBindingKind _manualBindingKind = MidiBindingKind.ControlChange;
    private MixerCommandKind _manualBindingCommand = MixerCommandKind.VolumeDelta;
    private int _manualMidiChannel = 1;
    private int _manualControllerOrNote = 70;
    private PendingMidiLearn? _pendingMidiLearn;
    private PendingKeyboardLearn? _pendingKeyboardLearn;
    private bool _isApplyingEndpointUpdate;
    private DateTimeOffset _ignoreMuteInputUntil = DateTimeOffset.MinValue;
    private string? _pendingMicMuteSource;
    private bool? _lastObservedMicMuteState;

    public ObservableCollection<ChannelStripViewModel> Channels { get; } = [];

    public ObservableCollection<ChannelOptionViewModel> ChannelOptions { get; } = [];

    public ObservableCollection<ChannelOptionViewModel> AssignableChannelOptions { get; } = [];

    public ObservableCollection<ActiveAudioAppViewModel> ActiveAudioApps { get; } = [];

    public ObservableCollection<MidiDeviceOptionViewModel> MidiDevices { get; } = [];

    public ObservableCollection<MidiBindingRowViewModel> MidiBindingRows { get; } = [];



    public IReadOnlyList<MidiBindingKind> ManualBindingKinds { get; } =
    [
        MidiBindingKind.ControlChange,
        MidiBindingKind.Note
    ];

    public IReadOnlyList<MixerCommandKind> ManualCommandKinds { get; } =
    [
        MixerCommandKind.VolumeDelta,
        MixerCommandKind.ToggleMute
    ];

    public ICommand SwitchViewCommand { get; }

    public ICommand RefreshMidiDevicesCommand { get; }

    public ICommand RefreshPlayingAudioCommand { get; }

    public ICommand ConnectMidiCommand { get; }

    public ICommand StopMidiCommand { get; }

    public ICommand ToggleMidiConnectionCommand { get; }

    public ICommand ToggleMinimizeToTrayCommand { get; }

    public ICommand ToggleCloseToTrayCommand { get; }

    public ICommand ToggleStartInTrayCommand { get; }

    public ICommand ResetAssignmentsCommand { get; }

    public ICommand LearnVolumeCommand { get; }

    public ICommand LearnMuteCommand { get; }

    public ICommand LearnMuteKeybindCommand { get; }

    public ICommand LearnVolumeUpKeybindCommand { get; }

    public ICommand LearnVolumeDownKeybindCommand { get; }

    public ICommand ClearChannelBindingsCommand { get; }

    public ICommand SaveManualBindingCommand { get; }

    public MainWindowViewModel(
        ISettingsService settingsService,
        ICoreAudioManager audioManager,
        IMidiListenerService midiListener,
        IKeyboardHookService keyboardHook,
        IOsdService osdService)
    {
        _settingsService = settingsService;
        _audioManager = audioManager;
        _midiListener = midiListener;
        _keyboardHook = keyboardHook;
        _osdService = osdService;

        _audioManager.ChannelChanged += OnAudioChannelChanged;
        _audioManager.ActiveSessionsChanged += OnActiveSessionsChanged;
        _audioManager.Faulted += OnAudioFaulted;
        _midiListener.ControlChanged += OnMidiControlChanged;
        _midiListener.NoteChanged += OnMidiNoteChanged;
        _keyboardHook.KeyPressed += OnKeyboardKeyPressed;

        SwitchViewCommand = new RelayCommand(parameter => SwitchView(parameter));
        RefreshMidiDevicesCommand = new RelayCommand(_ => RefreshMidiDevices());
        RefreshPlayingAudioCommand = new RelayCommand(async _ => await RefreshPlayingAudioAsync());
        ConnectMidiCommand = new RelayCommand(async _ => await ConnectMidiAsync());
        StopMidiCommand = new RelayCommand(async _ => await StopMidiAsync());
        ToggleMidiConnectionCommand = new RelayCommand(async _ => await ToggleMidiConnectionAsync());
        ToggleMinimizeToTrayCommand = new RelayCommand(async _ => await SaveTraySettingsAsync());
        ToggleCloseToTrayCommand = new RelayCommand(async _ => await SaveTraySettingsAsync());
        ToggleStartInTrayCommand = new RelayCommand(async _ => await SaveTraySettingsAsync());
        ResetAssignmentsCommand = new RelayCommand(async _ => await ResetAssignmentsAsync());
        LearnVolumeCommand = new RelayCommand(parameter => BeginMidiLearn(parameter, MixerCommandKind.VolumeDelta));
        LearnMuteCommand = new RelayCommand(parameter => BeginMidiLearn(parameter, MixerCommandKind.ToggleMute));
        LearnMuteKeybindCommand = new RelayCommand(parameter => BeginKeyboardLearn(parameter, MixerCommandKind.ToggleMute));
        LearnVolumeUpKeybindCommand = new RelayCommand(parameter => BeginKeyboardLearn(parameter, MixerCommandKind.VolumeUp));
        LearnVolumeDownKeybindCommand = new RelayCommand(parameter => BeginKeyboardLearn(parameter, MixerCommandKind.VolumeDown));
        ClearChannelBindingsCommand = new RelayCommand(async parameter => await ClearChannelBindingsAsync(parameter));
        SaveManualBindingCommand = new RelayCommand(async _ => await SaveManualBindingAsync());
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string MidiStatus
    {
        get => _midiStatus;
        private set => SetProperty(ref _midiStatus, value);
    }

    public bool IsMidiConnected
    {
        get => _isMidiConnected;
        private set => SetProperty(ref _isMidiConnected, value);
    }

    public string? SelectedMidiDeviceId
    {
        get => _selectedMidiDeviceId;
        set => SetProperty(ref _selectedMidiDeviceId, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetProperty(ref _closeToTray, value);
    }

    public bool StartInTray
    {
        get => _startInTray;
        set => SetProperty(ref _startInTray, value);
    }

    public ViewType CurrentViewType
    {
        get => _currentViewType;
        set => SetProperty(ref _currentViewType, value);
    }

    public string? ManualBindingChannelId
    {
        get => _manualBindingChannelId;
        set => SetProperty(ref _manualBindingChannelId, value);
    }

    public MidiBindingKind ManualBindingKind
    {
        get => _manualBindingKind;
        set => SetProperty(ref _manualBindingKind, value);
    }

    public MixerCommandKind ManualBindingCommand
    {
        get => _manualBindingCommand;
        set => SetProperty(ref _manualBindingCommand, value);
    }

    public int ManualMidiChannel
    {
        get => _manualMidiChannel;
        set => SetProperty(ref _manualMidiChannel, Math.Clamp(value, 1, 16));
    }

    public int ManualControllerOrNote
    {
        get => _manualControllerOrNote;
        set => SetProperty(ref _manualControllerOrNote, Math.Clamp(value, 0, 127));
    }

    public async Task InitializeAsync()
    {
        _configuration = await _settingsService.LoadAsync();
        await MigrateConfigurationAsync();

        foreach (var kvp in _configuration.ProcessExecutablePaths)
        {
            ProcessPresentationHelper.CachePath(kvp.Key, kvp.Value);
        }

        ProcessPresentationHelper.OnPathResolved = (processName, path) =>
        {
            if (!_configuration.ProcessExecutablePaths.ContainsKey(processName))
            {
                _configuration.ProcessExecutablePaths[processName] = path;
                QueueConfigurationSave();
            }
        };

        MinimizeToTray = _configuration.MinimizeToTray;
        CloseToTray = _configuration.CloseToTray;
        StartInTray = _configuration.StartInTray;
        _configuration.MidiAutoConnect = true;
        await _audioManager.InitializeAsync(_configuration);

        var renderEndpoints = await _audioManager.GetAvailableEndpointsAsync(AudioEndpointKind.Render);
        var captureEndpoints = await _audioManager.GetAvailableEndpointsAsync(AudioEndpointKind.Capture);

        Channels.Clear();
        foreach (var channel in GetOrderedChannels(_audioManager.Channels))
        {
            var endpointPool = channel.Role switch
            {
                AudioChannelRole.Microphone => captureEndpoints,
                _ => renderEndpoints
            };
            var endpointOptions = endpointPool.Select(endpoint =>
                new EndpointOptionViewModel(endpoint.Id, endpoint.FriendlyName, endpoint.Kind));

            var channelViewModel = new ChannelStripViewModel(channel, endpointOptions);
            channelViewModel.EndpointChangeRequested += OnEndpointChangeRequested;
            channelViewModel.VolumeChangeRequested += OnVolumeChangeRequested;
            channelViewModel.MuteChangeRequested += OnMuteChangeRequested;
            channelViewModel.RenameRequested += OnChannelRenameRequested;
            Channels.Add(channelViewModel);
        }

        RebuildChannelOptionLists();
        ManualBindingChannelId ??= ChannelOptions.FirstOrDefault()?.Id;

        RefreshActiveAudioApps(_audioManager.ActiveSessions);

        MidiBindingRows.Clear();
        foreach (var channel in GetOrderedChannels(_audioManager.Channels))
        {
            var row = new MidiBindingRowViewModel(channel.Id, channel.Name);
            row.UpdateFrom(_configuration.MidiBindings, _configuration.KeyboardBindings, channel.Name);
            MidiBindingRows.Add(row);
        }

        _midiListener.UpdateBindings(_configuration.MidiBindings);
        _keyboardHook.Start();
        RefreshMidiDevices();

        SelectedMidiDeviceId = ResolveAutoConnectDeviceId();
        await ConnectMidiAsync();

        await _audioManager.StartSessionWatcherAsync();
        Status = "Ready";
    }

    public async Task SaveImmediatelyAsync()
    {
        foreach (var pendingVolumeApply in _volumeApplyCtsByChannel.Values.ToList())
        {
            pendingVolumeApply.Cancel();
        }

        _volumeApplyCtsByChannel.Clear();
        _configurationSaveCts?.Cancel();
        _configurationSaveCts?.Dispose();
        _configurationSaveCts = null;

        _configuration.SchemaVersion = Math.Max(_configuration.SchemaVersion, 5);
        await _settingsService.SaveAsync(_configuration);
    }

    private void SwitchView(object? parameter)
    {
        if (parameter is ViewType viewType)
        {
            CurrentViewType = viewType;
            return;
        }

        if (parameter is string viewTypeName &&
            Enum.TryParse<ViewType>(viewTypeName, ignoreCase: true, out var parsedViewType))
        {
            CurrentViewType = parsedViewType;
        }
    }

    private async Task MigrateConfigurationAsync()
    {
        var changed = false;

        if (_configuration.SchemaVersion < 2)
        {
            foreach (var channel in _configuration.Channels)
            {
                if (Math.Abs(channel.Volume - 0.75f) < 0.0001f)
                {
                    channel.Volume = 1.0f;
                }
            }

            changed = true;
        }

        var master = _configuration.Channels.FirstOrDefault(channel =>
            string.Equals(channel.Id, AudioChannelConfig.MasterChannelId, StringComparison.OrdinalIgnoreCase));

        if (master is not null)
        {
            if (!string.Equals(master.Name, "MASTER", StringComparison.Ordinal))
            {
                master.Name = "MASTER";
                changed = true;
            }

            if (master.Role != AudioChannelRole.Master)
            {
                master.Role = AudioChannelRole.Master;
                changed = true;
            }
        }

        if (_configuration.SchemaVersion < 3)
        {
            changed = true;
        }

        if (_configuration.SchemaVersion < 4)
        {
            changed = true;
        }

        if (_configuration.SchemaVersion < 5)
        {
            changed = true;
        }

        changed |= RenameChannel("mic", "Mic");
        if (!changed)
        {
            return;
        }

        _configuration.SchemaVersion = 5;
        await _settingsService.SaveAsync(_configuration);
    }

    private bool RenameChannel(string channelId, string name)
    {
        var channel = _configuration.Channels.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, channelId, StringComparison.OrdinalIgnoreCase));

        if (channel is null || string.Equals(channel.Name, name, StringComparison.Ordinal))
        {
            return false;
        }

        channel.Name = name;
        return true;
    }

    private static IEnumerable<AudioChannelState> GetOrderedChannels(IEnumerable<AudioChannelState> channels)
    {
        return channels
            .OrderBy(channel =>
            {
                var index = Array.FindIndex(ChannelDisplayOrder, id =>
                    string.Equals(id, channel.Id, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            });
    }

    private void RebuildChannelOptionLists()
    {
        SyncChannelOptionList(
            ChannelOptions,
            GetOrderedChannels(_audioManager.Channels));

        SyncChannelOptionList(
            AssignableChannelOptions,
            GetOrderedChannels(_audioManager.Channels).Where(channel => channel.Role == AudioChannelRole.VirtualOutput));

        if (AssignableChannelOptions.FirstOrDefault(o => string.IsNullOrEmpty(o.Id)) is null)
        {
            AssignableChannelOptions.Add(new ChannelOptionViewModel(string.Empty, "UNASSIGNED"));
        }
    }

    private static void SyncChannelOptionList(
        ObservableCollection<ChannelOptionViewModel> target,
        IEnumerable<AudioChannelState> channels)
    {
        var orderedChannels = channels.ToList();

        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!orderedChannels.Any(channel => string.Equals(channel.Id, target[index].Id, StringComparison.OrdinalIgnoreCase)))
            {
                target.RemoveAt(index);
            }
        }

        for (var index = 0; index < orderedChannels.Count; index++)
        {
            var channel = orderedChannels[index];
            var existing = target.FirstOrDefault(option =>
                string.Equals(option.Id, channel.Id, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                target.Insert(index, new ChannelOptionViewModel(channel.Id, channel.Name));
                continue;
            }

            existing.Name = channel.Name;
            var currentIndex = target.IndexOf(existing);
            if (currentIndex != index)
            {
                target.Move(currentIndex, index);
            }
        }
    }

    private async Task RefreshPlayingAudioAsync()
    {
        await _audioManager.RefreshAsync();
        Status = $"Playing audio refreshed ({_audioManager.ActiveSessions.Count} apps).";
    }

    private void RefreshMidiDevices()
    {
        MidiDevices.Clear();
        foreach (var device in _midiListener.GetInputDevices())
        {
            MidiDevices.Add(new MidiDeviceOptionViewModel(device.Id, device.Name));
        }

        if (!string.IsNullOrWhiteSpace(SelectedMidiDeviceId) &&
            !MidiDevices.Any(device => device.Id == SelectedMidiDeviceId))
        {
            SelectedMidiDeviceId = null;
        }

        MidiStatus = MidiDevices.Count == 0
            ? "No MIDI input devices found"
            : $"Found {MidiDevices.Count} MIDI input device(s)";
    }

    private string? ResolveAutoConnectDeviceId()
    {
        if (!string.IsNullOrWhiteSpace(_configuration.SelectedMidiDeviceId) &&
            MidiDevices.Any(device => device.Id == _configuration.SelectedMidiDeviceId))
        {
            return _configuration.SelectedMidiDeviceId;
        }

        return MidiDevices
            .FirstOrDefault(device => device.Name.Contains("Loupedeck", StringComparison.OrdinalIgnoreCase))
            ?.Id
            ?? MidiDevices.FirstOrDefault()?.Id;
    }

    private async Task ConnectMidiAsync()
    {
        try
        {
            _ignoreMuteInputUntil = DateTimeOffset.UtcNow.AddMilliseconds(800);
            await _midiListener.StartAsync(SelectedMidiDeviceId ?? string.Empty);
            _configuration.SelectedMidiDeviceId = SelectedMidiDeviceId;
            await _settingsService.SaveAsync(_configuration);
            IsMidiConnected = true;
            MidiStatus = string.IsNullOrWhiteSpace(SelectedMidiDeviceId)
                ? $"Listening on all MIDI inputs ({MidiDevices.Count} available)."
                : $"Connected to {MidiDevices.FirstOrDefault(device => device.Id == SelectedMidiDeviceId)?.Name ?? SelectedMidiDeviceId}";
        }
        catch (Exception ex)
        {
            IsMidiConnected = false;
            MidiStatus = $"MIDI connect failed: {ex.Message}";
        }
    }

    private async Task SaveTraySettingsAsync()
    {
        _configuration.MinimizeToTray = MinimizeToTray;
        _configuration.CloseToTray = CloseToTray;
        _configuration.StartInTray = StartInTray;
        await _settingsService.SaveAsync(_configuration);
        Status = $"Tray behavior updated. Minimize: {(MinimizeToTray ? "tray" : "taskbar")}, Close: {(CloseToTray ? "tray" : "exit")}.";
    }

    private async Task ToggleMidiConnectionAsync()
    {
        if (IsMidiConnected)
        {
            await _midiListener.StopAsync();
            IsMidiConnected = false;
            MidiStatus = "Disconnected";
        }
        else
        {
            await ConnectMidiAsync();
        }
    }

    private async Task StopMidiAsync()
    {
        await _midiListener.StopAsync();
        IsMidiConnected = false;
        MidiStatus = "MIDI stopped";
    }

    private void BeginMidiLearn(object? parameter, MixerCommandKind command)
    {
        if (parameter is not string channelId)
        {
            return;
        }

        ClearAllLearningStates();

        var matchingRow = MidiBindingRows.FirstOrDefault(r => r.ChannelId == channelId);
        if (matchingRow is not null)
        {
            if (command == MixerCommandKind.VolumeDelta)
                matchingRow.IsLearningVolumeMidi = true;
            else
                matchingRow.IsLearningMuteMidi = true;
        }

        var channelName = _audioManager.Channels.FirstOrDefault(channel => channel.Id == channelId)?.Name ?? channelId;
        _pendingMidiLearn = new PendingMidiLearn(channelId, command);
        _pendingKeyboardLearn = null;
        MidiStatus = command == MixerCommandKind.VolumeDelta
            ? $"Learning volume control for {channelName}. Move the knob now."
            : $"Learning mute button for {channelName}. Press the button now.";
    }

    private void BeginKeyboardLearn(object? parameter, MixerCommandKind command)
    {
        if (parameter is not string channelId)
        {
            return;
        }

        ClearAllLearningStates();

        var matchingRow = MidiBindingRows.FirstOrDefault(r => r.ChannelId == channelId);
        if (matchingRow is not null)
        {
            if (command == MixerCommandKind.ToggleMute)
                matchingRow.IsLearningMuteKeybind = true;
            else if (command == MixerCommandKind.VolumeUp)
                matchingRow.IsLearningVolUpKeybind = true;
            else if (command == MixerCommandKind.VolumeDown)
                matchingRow.IsLearningVolDownKeybind = true;
        }

        var channelName = _audioManager.Channels.FirstOrDefault(channel => channel.Id == channelId)?.Name ?? channelId;
        _pendingKeyboardLearn = new PendingKeyboardLearn(channelId, command);
        _pendingMidiLearn = null;
        var label = command switch
        {
            MixerCommandKind.ToggleMute => "mute key",
            MixerCommandKind.VolumeUp => "volume up key",
            MixerCommandKind.VolumeDown => "volume down key",
            _ => "key"
        };
        MidiStatus = $"Learning {label} for {channelName}. Press the key now.";
    }

    private async Task ResetAssignmentsAsync()
    {
        foreach (var channel in _configuration.Channels)
        {
            channel.AssignedProcesses.Clear();
        }

        foreach (var channel in _audioManager.Channels)
        {
            foreach (var process in channel.AssignedProcesses.ToList())
            {
                await _audioManager.RemoveProcessAssignmentAsync(process, channel.Id);
            }
        }

        await _settingsService.SaveAsync(_configuration);
        await _audioManager.RefreshAsync();
        RefreshActiveAudioApps(_audioManager.ActiveSessions);
        Status = "All app assignments cleared.";
    }

    private async Task ClearChannelBindingsAsync(object? parameter)
    {
        if (parameter is not string channelId)
        {
            return;
        }

        _configuration.MidiBindings.RemoveAll(binding => binding.ChannelId == channelId);
        _configuration.KeyboardBindings.RemoveAll(binding => binding.ChannelId == channelId);
        _midiListener.UpdateBindings(_configuration.MidiBindings);
        await _settingsService.SaveAsync(_configuration);
        RefreshMidiBindingRows();
        MidiStatus = "Bindings cleared.";
    }

    private async Task SaveLearnedBindingAsync(
        MidiMessageIdentity identity,
        MixerCommandKind command,
        string channelId,
        int learnedDelta = 1)
    {
        if (command == MixerCommandKind.VolumeDelta && identity.Kind != MidiBindingKind.ControlChange)
        {
            MidiStatus = "Expected a CC encoder movement.";
            return;
        }

        await SaveBindingAsync(identity.Kind, identity.MidiChannel, identity.ControllerOrNote, command, channelId, false);
    }

    private async Task SaveManualBindingAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualBindingChannelId))
        {
            MidiStatus = "Select a channel first.";
            return;
        }

        if (ManualBindingCommand == MixerCommandKind.VolumeDelta &&
            ManualBindingKind != MidiBindingKind.ControlChange)
        {
            MidiStatus = "Volume encoders must use CC.";
            return;
        }

        await SaveBindingAsync(
            ManualBindingKind,
            ManualMidiChannel,
            ManualControllerOrNote,
            ManualBindingCommand,
            ManualBindingChannelId,
            false);
    }

    private async Task SaveBindingAsync(
        MidiBindingKind kind,
        int midiChannel,
        int controllerOrNote,
        MixerCommandKind command,
        string channelId,
        bool invertDirection)
    {
        var normalizedMidiChannel = Math.Clamp(midiChannel, 1, 16);
        var normalizedControllerOrNote = Math.Clamp(controllerOrNote, 0, 127);

        _configuration.MidiBindings.RemoveAll(binding =>
            binding.ChannelId == channelId &&
            binding.Command == command);

        _configuration.MidiBindings.RemoveAll(binding =>
            binding.Kind == kind &&
            binding.MidiChannel == normalizedMidiChannel &&
            binding.ControllerOrNote == normalizedControllerOrNote);

        _configuration.MidiBindings.Add(new MidiBinding
        {
            Kind = kind,
            MidiChannel = normalizedMidiChannel,
            ControllerOrNote = normalizedControllerOrNote,
            ChannelId = channelId,
            Command = command,
            Step = 0.02f,
            InvertDirection = invertDirection
        });

        _midiListener.UpdateBindings(_configuration.MidiBindings);
        await _settingsService.SaveAsync(_configuration);
        RefreshMidiBindingRows();

        var channelName = _audioManager.Channels.FirstOrDefault(channel => channel.Id == channelId)?.Name ?? channelId;
        var directionText = invertDirection ? " inverted" : string.Empty;
        MidiStatus = $"{channelName} {command} bound to {kind} {normalizedControllerOrNote}{directionText}.";
    }

    private void ClearAllLearningStates()
    {
        foreach (var row in MidiBindingRows)
        {
            row.IsLearningVolumeMidi = false;
            row.IsLearningMuteMidi = false;
            row.IsLearningMuteKeybind = false;
            row.IsLearningVolUpKeybind = false;
            row.IsLearningVolDownKeybind = false;
        }
    }

    private void RefreshMidiBindingRows()
    {
        ClearAllLearningStates();
        foreach (var row in MidiBindingRows)
        {
            var channelName = _audioManager.Channels
                .FirstOrDefault(c => c.Id == row.ChannelId)?.Name ?? row.ChannelId;
            row.UpdateFrom(_configuration.MidiBindings, _configuration.KeyboardBindings, channelName);
        }
    }

    private async Task SaveKeyboardBindingAsync(KeyboardKeyEventArgs key, string channelId, MixerCommandKind command)
    {
        _configuration.KeyboardBindings.RemoveAll(binding =>
            binding.ChannelId == channelId &&
            binding.Command == command);

        _configuration.KeyboardBindings.RemoveAll(binding => binding.VirtualKey == key.VirtualKey);

        _configuration.KeyboardBindings.Add(new KeyboardBinding
        {
            VirtualKey = key.VirtualKey,
            KeyName = key.KeyName,
            ChannelId = channelId,
            Command = command
        });

        await _settingsService.SaveAsync(_configuration);
        RefreshMidiBindingRows();

        var channelName = _audioManager.Channels.FirstOrDefault(channel => channel.Id == channelId)?.Name ?? channelId;
        MidiStatus = $"{channelName} {command} bound to key {key.KeyName}.";
    }

    private async void OnAppAssignmentChanged(ActiveAudioAppViewModel app, string? previousChannelId, string? channelId)
    {
        foreach (var channel in _configuration.Channels)
        {
            channel.AssignedProcesses.RemoveAll(candidate =>
                string.Equals(candidate, app.ProcessName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(channelId))
        {
            var configChannel = _configuration.Channels.FirstOrDefault(channel =>
                channel.Role == AudioChannelRole.VirtualOutput &&
                string.Equals(channel.Id, channelId, StringComparison.OrdinalIgnoreCase));

            if (configChannel is not null)
            {
                configChannel.AssignedProcesses.Add(app.ProcessName);
                await _audioManager.AssignProcessToChannelAsync(app.ProcessName, configChannel.Id);
                Status = $"{app.DisplayName} assigned to {configChannel.Name}.";
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(previousChannelId))
            {
                await _audioManager.RemoveProcessAssignmentAsync(app.ProcessName, previousChannelId);
            }

            Status = $"{app.DisplayName} unassigned.";
        }

        await _settingsService.SaveAsync(_configuration);
    }

    private async void OnEndpointChangeRequested(object? sender, string endpointId)
    {
        if (_isApplyingEndpointUpdate || sender is not ChannelStripViewModel channel || channel.LocksEndpoint)
        {
            return;
        }

        _isApplyingEndpointUpdate = true;
        try
        {
            await _audioManager.SetChannelEndpointAsync(channel.Id, endpointId);

            var configChannel = _configuration.Channels.FirstOrDefault(candidate => candidate.Id == channel.Id);
            if (configChannel is not null)
            {
                configChannel.EndpointId = endpointId;
                await _settingsService.SaveAsync(_configuration);
            }

            Status = $"{channel.Name} endpoint changed.";
        }
        finally
        {
            _isApplyingEndpointUpdate = false;
        }
    }

    private async void OnChannelRenameRequested(object? sender, string name)
    {
        if (sender is not ChannelStripViewModel channel || !channel.CanRenameChannel)
        {
            return;
        }

        var normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return;
        }

        var configChannel = _configuration.Channels.FirstOrDefault(candidate =>
            candidate.Role == AudioChannelRole.VirtualOutput &&
            string.Equals(candidate.Id, channel.Id, StringComparison.OrdinalIgnoreCase));

        if (configChannel is null)
        {
            return;
        }

        configChannel.Name = normalizedName;
        await _audioManager.RenameChannelAsync(channel.Id, normalizedName);
        RebuildChannelOptionLists();
        RefreshMidiBindingRows();
        await _settingsService.SaveAsync(_configuration);
        Status = $"{normalizedName} renamed.";
    }

    private void OnVolumeChangeRequested(object? sender, float volume)
    {
        if (sender is not ChannelStripViewModel channel)
        {
            return;
        }

        QueueChannelVolumeApply(channel.Id, volume);

        var configChannel = _configuration.Channels.FirstOrDefault(candidate => candidate.Id == channel.Id);
        if (configChannel is not null)
        {
            configChannel.Volume = volume;
            QueueConfigurationSave();
        }
    }

    private async void OnMuteChangeRequested(object? sender, bool isMuted)
    {
        if (sender is not ChannelStripViewModel channel)
        {
            return;
        }

        RememberMicMuteSource(channel.Id, "UI mute toggle");

        await _audioManager.SetChannelMuteAsync(channel.Id, isMuted);

        var configChannel = _configuration.Channels.FirstOrDefault(candidate => candidate.Id == channel.Id);
        if (configChannel is not null)
        {
            configChannel.IsMuted = isMuted;
            QueueConfigurationSave();
        }

        Status = isMuted ? $"{channel.Name} muted." : $"{channel.Name} unmuted.";
    }

    private async void OnMidiControlChanged(object? sender, MidiControlChangeEventArgs e)
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            await WpfApplication.Current.Dispatcher.InvokeAsync(() => OnMidiControlChanged(sender, e));
            return;
        }



        if (_pendingMidiLearn is { } pending &&
            (pending.Command == MixerCommandKind.VolumeDelta || pending.Command == MixerCommandKind.ToggleMute))
        {
            _pendingMidiLearn = null;
            await SaveLearnedBindingAsync(e.Identity, pending.Command, pending.ChannelId, e.Delta);
            return;
        }

        if (e.Command is null)
        {
            return;
        }

        if (e.Command.Kind == MixerCommandKind.VolumeDelta)
        {
            if (e.Delta == 0)
            {
                return;
            }

            var normalizedDelta = e.Delta * VolumeStepPerTick;
            var channelViewModel = Channels.FirstOrDefault(candidate => candidate.Id == e.Command.ChannelId);
        if (channelViewModel is not null)
        {
            channelViewModel.SetVolumeFromController(Math.Clamp(channelViewModel.Volume + normalizedDelta, 0f, 1f));
            QueueChannelVolumeApply(channelViewModel.Id, channelViewModel.Volume);

                var configChannel = _configuration.Channels.FirstOrDefault(candidate => candidate.Id == channelViewModel.Id);
                if (configChannel is not null)
                {
                    configChannel.Volume = channelViewModel.Volume;
                    QueueConfigurationSave();
                }

                var channel = _audioManager.Channels.FirstOrDefault(candidate => candidate.Id == e.Command.ChannelId);
                if (channel is not null)
                {
                    channel.Volume = channelViewModel.Volume;
                    _osdService.ShowVolumeChange(channel);
                }
            }
            return;
        }

        if (e.Command.Kind == MixerCommandKind.ToggleMute && CanToggleCcMute(e.Identity))
        {
            if (ShouldIgnoreStartupMuteInput())
            {
                Status = "Ignored startup MIDI mute input.";
                return;
            }

            RememberMicMuteSource(
                e.Command.ChannelId,
                $"MIDI CC {e.Identity.ControllerOrNote} / Ch {e.Identity.MidiChannel}");
            await _audioManager.ToggleChannelMuteAsync(e.Command.ChannelId);
            SyncMuteToConfiguration(e.Command.ChannelId);
            var channelName = _audioManager.Channels.FirstOrDefault(c => c.Id == e.Command.ChannelId)?.Name ?? e.Command.ChannelId;
            var muted = _audioManager.Channels.FirstOrDefault(c => c.Id == e.Command.ChannelId)?.IsMuted ?? false;
            Status = muted ? $"{channelName} muted (MIDI)." : $"{channelName} unmuted (MIDI).";
        }
    }

    private async void OnMidiNoteChanged(object? sender, MidiNoteEventArgs e)
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            await WpfApplication.Current.Dispatcher.InvokeAsync(() => OnMidiNoteChanged(sender, e));
            return;
        }



        if (_pendingMidiLearn is { Command: MixerCommandKind.ToggleMute } pending)
        {
            _pendingMidiLearn = null;
            await SaveLearnedBindingAsync(e.Identity, pending.Command, pending.ChannelId);
            return;
        }

        if (!e.IsPressed || e.Command is null)
        {
            return;
        }

        if (e.Command.Kind == MixerCommandKind.ToggleMute)
        {
            if (ShouldIgnoreStartupMuteInput())
            {
                Status = "Ignored startup MIDI note mute input.";
                return;
            }

            RememberMicMuteSource(
                e.Command.ChannelId,
                $"MIDI note {e.Identity.ControllerOrNote} / Ch {e.Identity.MidiChannel}");
            await _audioManager.ToggleChannelMuteAsync(e.Command.ChannelId);
            SyncMuteToConfiguration(e.Command.ChannelId);
            var channelName = _audioManager.Channels.FirstOrDefault(c => c.Id == e.Command.ChannelId)?.Name ?? e.Command.ChannelId;
            var muted = _audioManager.Channels.FirstOrDefault(c => c.Id == e.Command.ChannelId)?.IsMuted ?? false;
            Status = muted ? $"{channelName} muted (MIDI note)." : $"{channelName} unmuted (MIDI note).";
        }
    }

    private bool CanToggleCcMute(MidiMessageIdentity identity)
    {
        var key = $"{identity.MidiChannel}:{identity.ControllerOrNote}";
        var now = DateTimeOffset.UtcNow;

        if (_lastCcMuteToggleAt.TryGetValue(key, out var lastToggleAt) &&
            now - lastToggleAt < TimeSpan.FromMilliseconds(250))
        {
            return false;
        }

        _lastCcMuteToggleAt[key] = now;
        return true;
    }

    private bool ShouldIgnoreStartupMuteInput() => DateTimeOffset.UtcNow < _ignoreMuteInputUntil;

    private bool CanToggleKeyboardMute(int virtualKey)
    {
        var now = DateTimeOffset.UtcNow;

        if (_lastKeyboardMuteToggleAt.TryGetValue(virtualKey, out var lastToggleAt) &&
            now - lastToggleAt < TimeSpan.FromMilliseconds(300))
        {
            return false;
        }

        _lastKeyboardMuteToggleAt[virtualKey] = now;
        return true;
    }

    private async void OnKeyboardKeyPressed(object? sender, KeyboardKeyEventArgs e)
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            await WpfApplication.Current.Dispatcher.InvokeAsync(() => OnKeyboardKeyPressed(sender, e));
            return;
        }

        if (_pendingKeyboardLearn is { } pending)
        {
            _pendingKeyboardLearn = null;
            await SaveKeyboardBindingAsync(e, pending.ChannelId, pending.Command);
            return;
        }

        var binding = _configuration.KeyboardBindings.FirstOrDefault(candidate =>
            candidate.VirtualKey == e.VirtualKey);

        if (binding is null)
        {
            return;
        }

        if (!CanToggleKeyboardMute(e.VirtualKey))
        {
            return;
        }

        if (binding.Command == MixerCommandKind.ToggleMute)
        {
            RememberMicMuteSource(binding.ChannelId, $"Keyboard {e.KeyName}");
            await _audioManager.ToggleChannelMuteAsync(binding.ChannelId);
            SyncMuteToConfiguration(binding.ChannelId);
            var channelName = _audioManager.Channels.FirstOrDefault(c => c.Id == binding.ChannelId)?.Name ?? binding.ChannelId;
            var muted = _audioManager.Channels.FirstOrDefault(c => c.Id == binding.ChannelId)?.IsMuted ?? false;
            Status = muted ? $"{channelName} muted ({e.KeyName})." : $"{channelName} unmuted ({e.KeyName}).";
            return;
        }

        if (binding.Command is MixerCommandKind.VolumeUp or MixerCommandKind.VolumeDown)
        {
            var step = binding.Command == MixerCommandKind.VolumeUp ? 0.05f : -0.05f;
            var channelViewModel = Channels.FirstOrDefault(c => c.Id == binding.ChannelId);
            if (channelViewModel is not null)
            {
                channelViewModel.SetVolumeFromController(Math.Clamp(channelViewModel.Volume + step, 0f, 1f));
                QueueChannelVolumeApply(channelViewModel.Id, channelViewModel.Volume);

                var configChannel = _configuration.Channels.FirstOrDefault(c => c.Id == binding.ChannelId);
                if (configChannel is not null)
                {
                    configChannel.Volume = channelViewModel.Volume;
                    QueueConfigurationSave();
                }

                var channel = _audioManager.Channels.FirstOrDefault(c => c.Id == binding.ChannelId);
                if (channel is not null)
                {
                    channel.Volume = channelViewModel.Volume;
                    _osdService.ShowVolumeChange(channel);
                }

                var direction = binding.Command == MixerCommandKind.VolumeUp ? "up" : "down";
                Status = $"{channelViewModel.Name} volume {direction} ({e.KeyName}).";
            }
            return;
        }
    }

    private void OnAudioChannelChanged(object? sender, AudioChannelChangedEventArgs e)
    {
        WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            var viewModel = Channels.FirstOrDefault(channel => channel.Id == e.Channel.Id);
            viewModel?.Update(e.Channel, updateVolume: !_volumeApplyCtsByChannel.ContainsKey(e.Channel.Id));
            ReportMicMuteTransition(e.Channel, e.Reason);
        });
    }

    private void OnActiveSessionsChanged(object? sender, ActiveAudioSessionsChangedEventArgs e)
    {
        WpfApplication.Current.Dispatcher.Invoke(() => RefreshActiveAudioApps(e.Sessions));
    }

    private void RefreshActiveAudioApps(IReadOnlyList<ActiveAudioSessionInfo> sessions)
    {
        var incoming = sessions.ToDictionary(session => session.ProcessName, StringComparer.OrdinalIgnoreCase);

        for (var index = ActiveAudioApps.Count - 1; index >= 0; index--)
        {
            var app = ActiveAudioApps[index];
            if (!incoming.ContainsKey(app.ProcessName) && !app.HasAssignedChannel)
            {
                ActiveAudioApps.RemoveAt(index);
            }
        }

        foreach (var session in sessions)
        {
            var existing = ActiveAudioApps.FirstOrDefault(app =>
                string.Equals(app.ProcessName, session.ProcessName, StringComparison.OrdinalIgnoreCase));
            var assignedChannelId = ResolveAssignedChannelId(session.ProcessName) ?? session.AssignedChannelId;
            var viewModelSession = session with { AssignedChannelId = assignedChannelId };

            if (existing is not null)
            {
                existing.UpdateFromSession(viewModelSession, assignedChannelId, true);
                continue;
            }

            ActiveAudioApps.Add(new ActiveAudioAppViewModel(viewModelSession, OnAppAssignmentChanged));
        }

        // Add assigned-but-closed processes from config that were never seen running
        foreach (var configChannel in _configuration.Channels)
        {
            if (configChannel.Role != AudioChannelRole.VirtualOutput)
            {
                continue;
            }

            foreach (var processName in configChannel.AssignedProcesses)
            {
                if (ActiveAudioApps.Any(a => string.Equals(a.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var displayName = ProcessPresentationHelper.GetFriendlyName(processName);
                var session = new ActiveAudioSessionInfo(
                    processName,
                    displayName,
                    string.Empty,
                    string.Empty,
                    0f,
                    configChannel.Id);

                ActiveAudioApps.Add(new ActiveAudioAppViewModel(session, OnAppAssignmentChanged));
            }
        }

        foreach (var app in ActiveAudioApps)
        {
            if (incoming.ContainsKey(app.ProcessName))
            {
                continue;
            }

            var assignedChannelId = ResolveAssignedChannelId(app.ProcessName);
            if (assignedChannelId is null)
            {
                continue;
            }

            app.UpdateFromSession(
                new ActiveAudioSessionInfo(
                    app.ProcessName,
                    app.DisplayName,
                    string.Empty,
                    app.EndpointName,
                    app.PeakValue,
                    assignedChannelId),
                assignedChannelId,
                false);
        }
    }

    private string? ResolveAssignedChannelId(string processName)
    {
        return _configuration.Channels
            .FirstOrDefault(channel =>
                channel.Role == AudioChannelRole.VirtualOutput &&
                channel.AssignedProcesses.Contains(processName, StringComparer.OrdinalIgnoreCase))
            ?.Id;
    }

    private void OnAudioFaulted(object? sender, AudioFaultEventArgs args)
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(() => OnAudioFaulted(sender, args));
            return;
        }

        Status = args.Message;
    }



    private void SyncVolumeToConfiguration(string channelId)
    {
        var channel = _audioManager.Channels.FirstOrDefault(candidate => candidate.Id == channelId);
        var configChannel = _configuration.Channels.FirstOrDefault(candidate => candidate.Id == channelId);

        if (channel is null || configChannel is null)
        {
            return;
        }

        configChannel.Volume = channel.Volume;
        QueueConfigurationSave();
    }

    private void SyncMuteToConfiguration(string channelId)
    {
        var channel = _audioManager.Channels.FirstOrDefault(candidate => candidate.Id == channelId);
        var configChannel = _configuration.Channels.FirstOrDefault(candidate => candidate.Id == channelId);

        if (channel is null || configChannel is null)
        {
            return;
        }

        configChannel.IsMuted = channel.IsMuted;
        QueueConfigurationSave();
    }

    private void QueueConfigurationSave()
    {
        _configurationSaveCts?.Cancel();
        _configurationSaveCts?.Dispose();
        _configurationSaveCts = new CancellationTokenSource();
        var token = _configurationSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
                await _settingsService.SaveAsync(_configuration, token);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void QueueChannelVolumeApply(string channelId, float volume)
    {
        if (_volumeApplyCtsByChannel.Remove(channelId, out var existingCts))
        {
            existingCts.Cancel();
        }

        var cts = new CancellationTokenSource();
        _volumeApplyCtsByChannel[channelId] = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(35, token);
                await _audioManager.SetChannelVolumeAsync(channelId, volume, token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                var dispatcher = WpfApplication.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted)
                {
                    cts.Dispose();
                }
                else
                {
                    dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (_volumeApplyCtsByChannel.TryGetValue(channelId, out var currentCts) &&
                                ReferenceEquals(currentCts, cts))
                            {
                                _volumeApplyCtsByChannel.Remove(channelId);
                            }
                        }
                        finally
                        {
                            cts.Dispose();
                        }
                    });
                }
            }
        }, token);
    }

    private void ResetVolumeEncoderBaselines(string channelId)
    {
        var identities = _configuration.MidiBindings
            .Where(binding =>
                binding.ChannelId == channelId &&
                binding.Command == MixerCommandKind.VolumeDelta &&
                binding.Kind == MidiBindingKind.ControlChange)
            .Select(binding => new MidiMessageIdentity(
                binding.Kind,
                binding.MidiChannel,
                binding.ControllerOrNote));

        _midiListener.ResetEncoderBaselines(identities);
    }

    private static string GetMidiCcKey(MidiMessageIdentity identity) =>
        $"{identity.MidiChannel}:{identity.ControllerOrNote}";

    public void SetShellVisible(bool isVisible)
    {
        _audioManager.SetUiActive(isVisible);
    }

    private void RememberMicMuteSource(string channelId, string source)
    {
        if (string.Equals(channelId, "mic", StringComparison.OrdinalIgnoreCase))
        {
            _pendingMicMuteSource = source;
        }
    }

    private void ReportMicMuteTransition(AudioChannelState channel, string reason)
    {
        if (!string.Equals(channel.Id, "mic", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_lastObservedMicMuteState == channel.IsMuted)
        {
            return;
        }

        _lastObservedMicMuteState = channel.IsMuted;
        var source = reason == "EndpointMuteObserved"
            ? "External endpoint state"
            : _pendingMicMuteSource ?? $"Backend ({reason})";
        _pendingMicMuteSource = null;

        var message = channel.IsMuted
            ? $"Mic muted [{source}]"
            : $"Mic unmuted [{source}]";

        Status = message;
    }

    private sealed record PendingMidiLearn(string ChannelId, MixerCommandKind Command);

    private sealed record PendingKeyboardLearn(string ChannelId, MixerCommandKind Command);
}
