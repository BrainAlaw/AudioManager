using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AudioManager.Models;
using AudioManager.Services;

namespace AudioManager.ViewModels;

public sealed class ChannelStripViewModel : ObservableObject
{
    private float _volume;
    private bool _isMuted;
    private float _peakValue;
    private float _displayPeakValue;
    private string? _selectedEndpointId;
    private bool _isRenaming;
    private string _pendingName;
    private bool _isUpdatingFromModel;
    private readonly DispatcherTimer _peakAnimationTimer;

    private static readonly bool EnablePeakSmoothing = true;
    private const float PeakAttackFactor = 0.55f;
    private const float PeakReleaseFactor = 0.14f;
    private const float PeakSnapThreshold = 0.0025f;

    public ChannelStripViewModel(AudioChannelState state, IEnumerable<EndpointOptionViewModel> endpointOptions)
    {
        Id = state.Id;
        Name = state.Name;
        _pendingName = state.Name;
        Role = state.Role;
        LocksEndpoint = state.LocksEndpoint;
        IconPath = state.IconPath;
        EndpointName = FormatEndpointLabel(state);
        SelectedEndpointId = state.Endpoint?.Id;
        foreach (var option in endpointOptions)
        {
            EndpointOptions.Add(option);
        }

        _peakAnimationTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(24)
        };
        _peakAnimationTimer.Tick += OnPeakAnimationTick;

        Update(state);

        BeginRenameCommand = new RelayCommand(_ => BeginRename(), _ => CanRenameChannel);
        CommitRenameCommand = new RelayCommand(_ => CommitRename(), _ => CanRenameChannel);
        CancelRenameCommand = new RelayCommand(_ => CancelRename(), _ => CanRenameChannel);
    }

    public event EventHandler<string>? EndpointChangeRequested;

    public event EventHandler<float>? VolumeChangeRequested;

    public event EventHandler<bool>? MuteChangeRequested;

    public event EventHandler<string>? RenameRequested;

    public string Id { get; }

    public string Name { get; private set; }

    public string SpacedName => ApplyDisplaySpacing(Name);

    public AudioChannelRole Role { get; }

    public bool LocksEndpoint { get; }

    public bool CanRenameChannel => Role == AudioChannelRole.VirtualOutput;

    public bool CanShowAssignedApps => Role == AudioChannelRole.VirtualOutput;

    public bool CanShowEqualizerPlaceholder => Role is AudioChannelRole.Microphone or AudioChannelRole.Master;

    public bool HasAssignedApps => AssignedProcessIcons.Count > 0;

    public bool HasNoAssignedApps => CanShowAssignedApps && !HasAssignedApps;

    public bool HasHiddenAssignedApps => HiddenAssignedProcessIcons.Count > 0;

    public bool HasSingleRowAssignedApps => AssignedProcessIcons.Count <= 7;

    public bool HasMultiRowAssignedApps => AssignedProcessIcons.Count > 7;

    public bool IsEndpointSelectorVisible => false;

    public string? IconPath { get; private set; }

    public string EndpointName { get; private set; }

    public ObservableCollection<AssignedProcessIconViewModel> AssignedProcessIcons { get; } = [];

    public ObservableCollection<AssignedProcessIconViewModel> VisibleAssignedProcessIcons { get; } = [];

    public ObservableCollection<AssignedProcessIconViewModel> HiddenAssignedProcessIcons { get; } = [];

    public ObservableCollection<EndpointOptionViewModel> EndpointOptions { get; } = [];

    public ICommand BeginRenameCommand { get; }

    public ICommand CommitRenameCommand { get; }

    public ICommand CancelRenameCommand { get; }

    public bool IsRenaming
    {
        get => _isRenaming;
        private set
        {
            if (SetProperty(ref _isRenaming, value))
            {
                OnPropertyChanged(nameof(IsNotRenaming));
            }
        }
    }

    public bool IsNotRenaming => !IsRenaming;

    public string PendingName
    {
        get => _pendingName;
        set => SetProperty(ref _pendingName, value);
    }

    public string? SelectedEndpointId
    {
        get => _selectedEndpointId;
        set
        {
            if (LocksEndpoint || !SetProperty(ref _selectedEndpointId, value) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!_isUpdatingFromModel)
            {
                EndpointChangeRequested?.Invoke(this, value);
            }
        }
    }

    public int VolumePercent => (int)MathF.Round(_volume * 100);

    public string SpacedVolumePercent => ApplyDisplaySpacing($"{VolumePercent}%");

    public float Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, Math.Clamp(value, 0f, 1f)))
            {
                OnPropertyChanged(nameof(VolumePercent));
                OnPropertyChanged(nameof(SpacedVolumePercent));
                EnsurePeakAnimation();
                if (!_isUpdatingFromModel)
                {
                    VolumeChangeRequested?.Invoke(this, _volume);
                }
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value) && !_isUpdatingFromModel)
            {
                MuteChangeRequested?.Invoke(this, value);
            }

            EnsurePeakAnimation();
        }
    }

    public float PeakValue
    {
        get => _peakValue;
        set
        {
            if (SetProperty(ref _peakValue, Math.Clamp(value, 0f, 1f)))
            {
                if (!EnablePeakSmoothing)
                {
                    _displayPeakValue = AudioMeterScaler.ToDisplayLevel(GetEffectivePeakValue());
                    OnPropertyChanged(nameof(DisplayPeakValue));
                }
                else
                {
                    EnsurePeakAnimation();
                }
            }
        }
    }

    public float DisplayPeakValue => _displayPeakValue;

    public void Update(AudioChannelState state, bool updateVolume = true)
    {
        _isUpdatingFromModel = true;
        try
        {
            Name = state.Name;
            if (!IsRenaming)
            {
                PendingName = state.Name;
            }
            IconPath = state.IconPath;
            EndpointName = FormatEndpointLabel(state);
            _selectedEndpointId = state.Endpoint?.Id;

            AssignedProcessIcons.Clear();
            VisibleAssignedProcessIcons.Clear();
            HiddenAssignedProcessIcons.Clear();
            foreach (var processName in state.AssignedProcesses)
            {
                var icon = ProcessPresentationHelper.GetProcessIcon(processName);
                if (icon is not null)
                {
                    AssignedProcessIcons.Add(new AssignedProcessIconViewModel(processName, icon));
                }
            }

            RebuildAssignedProcessPresentation();

            if (updateVolume)
            {
                Volume = state.Volume;
            }

            IsMuted = state.IsMuted;
            PeakValue = state.PeakValue;
        }
        finally
        {
            _isUpdatingFromModel = false;
        }

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(SpacedName));
        OnPropertyChanged(nameof(PendingName));
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(EndpointName));
        OnPropertyChanged(nameof(SelectedEndpointId));
        OnPropertyChanged(nameof(DisplayPeakValue));
        OnPropertyChanged(nameof(HasAssignedApps));
        OnPropertyChanged(nameof(HasNoAssignedApps));
        OnPropertyChanged(nameof(HasHiddenAssignedApps));
        OnPropertyChanged(nameof(HasSingleRowAssignedApps));
        OnPropertyChanged(nameof(HasMultiRowAssignedApps));
    }

    public void SetVolumeFromController(float volume)
    {
        _isUpdatingFromModel = true;
        try
        {
            Volume = volume;
        }
        finally
        {
            _isUpdatingFromModel = false;
        }
    }

    private void EnsurePeakAnimation()
    {
        if (!EnablePeakSmoothing)
        {
            return;
        }

        if (!_peakAnimationTimer.IsEnabled)
        {
            _peakAnimationTimer.Start();
        }
    }

    private void OnPeakAnimationTick(object? sender, EventArgs e)
    {
        var targetDisplayPeak = AudioMeterScaler.ToDisplayLevel(GetEffectivePeakValue());
        var delta = targetDisplayPeak - _displayPeakValue;

        if (MathF.Abs(delta) <= PeakSnapThreshold)
        {
            if (_displayPeakValue != targetDisplayPeak)
            {
                _displayPeakValue = targetDisplayPeak;
                OnPropertyChanged(nameof(DisplayPeakValue));
            }

            if (targetDisplayPeak <= PeakSnapThreshold)
            {
                _peakAnimationTimer.Stop();
            }

            return;
        }

        var factor = delta > 0f ? PeakAttackFactor : PeakReleaseFactor;
        _displayPeakValue = Math.Clamp(_displayPeakValue + (delta * factor), 0f, 1f);
        OnPropertyChanged(nameof(DisplayPeakValue));
    }

    private static string FormatEndpointLabel(AudioChannelState state)
    {
        if (state.Role == AudioChannelRole.VirtualOutput)
        {
            return "Application group";
        }

        var name = state.Endpoint?.FriendlyName ?? "Endpoint missing";
        return state.LocksEndpoint
            ? $"Windows default: {name}"
            : name;
    }

    private static string ApplyDisplaySpacing(string value) =>
        string.Join('\u2009', value.Select(character => character == ' ' ? "\u2009 \u2009" : character.ToString()));

    private float GetEffectivePeakValue()
    {
        return _isMuted ? 0f : Math.Clamp(_peakValue * _volume, 0f, 1f);
    }

    private void RebuildAssignedProcessPresentation()
    {
        VisibleAssignedProcessIcons.Clear();
        HiddenAssignedProcessIcons.Clear();

        foreach (var icon in AssignedProcessIcons.Take(7))
        {
            VisibleAssignedProcessIcons.Add(icon);
        }

        foreach (var icon in AssignedProcessIcons.Skip(7))
        {
            HiddenAssignedProcessIcons.Add(icon);
        }

        OnPropertyChanged(nameof(HasAssignedApps));
        OnPropertyChanged(nameof(HasNoAssignedApps));
        OnPropertyChanged(nameof(HasHiddenAssignedApps));
        OnPropertyChanged(nameof(HasSingleRowAssignedApps));
        OnPropertyChanged(nameof(HasMultiRowAssignedApps));
    }

    private void BeginRename()
    {
        if (!CanRenameChannel)
        {
            return;
        }

        PendingName = SpacedName;
        IsRenaming = true;
    }

    private void CommitRename()
    {
        if (!CanRenameChannel)
        {
            return;
        }

        var compactName = RemoveDisplaySpacing(PendingName);
        var normalizedName = string.IsNullOrWhiteSpace(compactName)
            ? Name
            : compactName.Trim();

        PendingName = ApplyDisplaySpacing(normalizedName);
        IsRenaming = false;

        if (!string.Equals(Name, normalizedName, StringComparison.Ordinal))
        {
            RenameRequested?.Invoke(this, normalizedName);
        }
    }

    private void CancelRename()
    {
        PendingName = SpacedName;
        IsRenaming = false;
    }

    private static string RemoveDisplaySpacing(string value) =>
        value.Replace("\u2009", string.Empty, StringComparison.Ordinal);
}

public sealed record AssignedProcessIconViewModel(string ProcessName, ImageSource Icon);
