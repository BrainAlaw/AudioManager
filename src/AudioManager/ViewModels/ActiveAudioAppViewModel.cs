using System.Linq;
using System.Windows.Media;
using AudioManager.Models;
using AudioManager.Services;

namespace AudioManager.ViewModels;

public sealed class ActiveAudioAppViewModel : ObservableObject
{
    private readonly Action<ActiveAudioAppViewModel, string?, string?> _assignmentChanged;
    private readonly RelayCommand _beginAssignmentCommand;
    private string? _selectedChannelId;
    private bool _isAssignmentEditorOpen;
    private bool _isActive;
    private float _peakValue;
    private string _displayName;
    private string _endpointName;
    private ImageSource? _icon;

    public ActiveAudioAppViewModel(
        ActiveAudioSessionInfo session,
        Action<ActiveAudioAppViewModel, string?, string?> assignmentChanged)
    {
        ProcessName = session.ProcessName;
        _displayName = session.DisplayName;
        _endpointName = session.EndpointName;
        _peakValue = session.PeakValue;
        _selectedChannelId = session.AssignedChannelId;
        _isAssignmentEditorOpen = string.IsNullOrWhiteSpace(session.AssignedChannelId);
        _isActive = true;
        _icon = ProcessPresentationHelper.GetProcessIcon(session.ProcessName);
        _assignmentChanged = assignmentChanged;
        _beginAssignmentCommand = new RelayCommand(_ => BeginAssignment());
    }

    public string ProcessName { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    public string DisplayNameSpaced => ApplyDisplaySpacing(DisplayName);

    public string DisplayNameTwoLines => ApplyDisplayTwoLineSpacing(DisplayName);

    public string EndpointName
    {
        get => _endpointName;
        private set => SetProperty(ref _endpointName, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    public float PeakValue
    {
        get => _peakValue;
        private set => SetProperty(ref _peakValue, Math.Clamp(value, 0f, 1f));
    }

    public bool HasAssignedChannel => !string.IsNullOrWhiteSpace(_selectedChannelId);

    public bool IsAssignmentEditorOpen
    {
        get => _isAssignmentEditorOpen;
        private set => SetProperty(ref _isAssignmentEditorOpen, value);
    }

    public bool IsAssignButtonVisible => !HasAssignedChannel && !IsAssignmentEditorOpen;

    public bool IsChannelPickerVisible => HasAssignedChannel || IsAssignmentEditorOpen;

    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    public RelayCommand BeginAssignmentCommand => _beginAssignmentCommand;

    public string? AssignedChannelId
    {
        get => SelectedChannelId;
        set => SelectedChannelId = value;
    }

    public string? SelectedChannelId
    {
        get => _selectedChannelId;
        set
        {
            var previousChannelId = _selectedChannelId;
            if (!SetProperty(ref _selectedChannelId, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasAssignedChannel));
            OnPropertyChanged(nameof(IsAssignButtonVisible));
            OnPropertyChanged(nameof(IsChannelPickerVisible));
            if (!string.IsNullOrWhiteSpace(value))
            {
                IsAssignmentEditorOpen = false;
            }

            _assignmentChanged(this, previousChannelId, value);
        }
    }

    public void UpdateFromSession(ActiveAudioSessionInfo session, string? assignedChannelId, bool isActive)
    {
        SetSelectedChannelIdFromModel(assignedChannelId);
        DisplayName = session.DisplayName;
        EndpointName = session.EndpointName;
        Icon ??= ProcessPresentationHelper.GetProcessIcon(session.ProcessName);
        IsActive = isActive;
        PeakValue = isActive ? session.PeakValue : 0f;
        OnPropertyChanged(nameof(DisplayNameSpaced));
        OnPropertyChanged(nameof(DisplayNameTwoLines));
    }

    private void BeginAssignment()
    {
        IsAssignmentEditorOpen = true;
        OnPropertyChanged(nameof(HasAssignedChannel));
        OnPropertyChanged(nameof(IsAssignButtonVisible));
        OnPropertyChanged(nameof(IsChannelPickerVisible));
    }

    private void SetSelectedChannelIdFromModel(string? value)
    {
        if (_selectedChannelId == value)
        {
            return;
        }

        _selectedChannelId = value;
        OnPropertyChanged(nameof(SelectedChannelId));
        OnPropertyChanged(nameof(AssignedChannelId));
        OnPropertyChanged(nameof(HasAssignedChannel));
        OnPropertyChanged(nameof(IsAssignButtonVisible));
        OnPropertyChanged(nameof(IsChannelPickerVisible));
    }

    private static string ApplyDisplaySpacing(string value) =>
        string.Join('\u2009', value.Select(character => character == ' ' ? "\u2009 \u2009" : character.ToString()));

    private static string ApplyDisplayTwoLineSpacing(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length > 1)
        {
            var midpoint = (words.Length + 1) / 2;
            var firstLine = string.Join(' ', words.Take(midpoint));
            var secondLine = string.Join(' ', words.Skip(midpoint));
            return $"{ApplyDisplaySpacing(firstLine)}{Environment.NewLine}{ApplyDisplaySpacing(secondLine)}";
        }

        if (value.Length <= 10)
        {
            return ApplyDisplaySpacing(value);
        }

        var splitIndex = Math.Clamp(value.Length / 2, 4, value.Length - 4);
        var prefix = value[..splitIndex].Trim();
        var suffix = value[splitIndex..].Trim();
        return $"{ApplyDisplaySpacing(prefix)}{Environment.NewLine}{ApplyDisplaySpacing(suffix)}";
    }
}
