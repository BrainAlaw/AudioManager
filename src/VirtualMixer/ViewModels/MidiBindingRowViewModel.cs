using System.Linq;
using AudioManager.Models;

namespace AudioManager.ViewModels;

public sealed class MidiBindingRowViewModel : ObservableObject
{
    private string _channelName = string.Empty;
    private string _volumeMidiText = "Unassigned";
    private string _muteMidiText = "Unassigned";
    private string _muteKeybindText = "Unassigned";
    private string _volUpKeybindText = "Unassigned";
    private string _volDownKeybindText = "Unassigned";
    private bool _isLearningVolumeMidi;
    private bool _isLearningMuteMidi;
    private bool _isLearningMuteKeybind;
    private bool _isLearningVolUpKeybind;
    private bool _isLearningVolDownKeybind;

    public MidiBindingRowViewModel(string channelId, string channelName)
    {
        ChannelId = channelId;
        _channelName = channelName;
    }

    public string ChannelId { get; }

    public string ChannelName
    {
        get => _channelName;
        set
        {
            if (SetProperty(ref _channelName, value))
            {
                OnPropertyChanged(nameof(SpacedChannelName));
            }
        }
    }

    public string SpacedChannelName => ApplyDisplaySpacing(_channelName);

    public string VolumeMidiText
    {
        get => _volumeMidiText;
        private set
        {
            if (SetProperty(ref _volumeMidiText, value))
                OnPropertyChanged(nameof(SpacedVolumeMidiText));
        }
    }

    public string SpacedVolumeMidiText => ApplyDisplaySpacing(_volumeMidiText);

    public string MuteMidiText
    {
        get => _muteMidiText;
        private set
        {
            if (SetProperty(ref _muteMidiText, value))
                OnPropertyChanged(nameof(SpacedMuteMidiText));
        }
    }

    public string SpacedMuteMidiText => ApplyDisplaySpacing(_muteMidiText);

    public string MuteKeybindText
    {
        get => _muteKeybindText;
        private set
        {
            if (SetProperty(ref _muteKeybindText, value))
                OnPropertyChanged(nameof(SpacedMuteKeybindText));
        }
    }

    public string SpacedMuteKeybindText => ApplyDisplaySpacing(_muteKeybindText);

    public string VolUpKeybindText
    {
        get => _volUpKeybindText;
        private set
        {
            if (SetProperty(ref _volUpKeybindText, value))
                OnPropertyChanged(nameof(SpacedVolUpKeybindText));
        }
    }

    public string SpacedVolUpKeybindText => ApplyDisplaySpacing(_volUpKeybindText);

    public string VolDownKeybindText
    {
        get => _volDownKeybindText;
        private set
        {
            if (SetProperty(ref _volDownKeybindText, value))
                OnPropertyChanged(nameof(SpacedVolDownKeybindText));
        }
    }

    public string SpacedVolDownKeybindText => ApplyDisplaySpacing(_volDownKeybindText);

    public bool IsLearningVolumeMidi
    {
        get => _isLearningVolumeMidi;
        set => SetProperty(ref _isLearningVolumeMidi, value);
    }

    public bool IsLearningMuteMidi
    {
        get => _isLearningMuteMidi;
        set => SetProperty(ref _isLearningMuteMidi, value);
    }

    public bool IsLearningMuteKeybind
    {
        get => _isLearningMuteKeybind;
        set => SetProperty(ref _isLearningMuteKeybind, value);
    }

    public bool IsLearningVolUpKeybind
    {
        get => _isLearningVolUpKeybind;
        set => SetProperty(ref _isLearningVolUpKeybind, value);
    }

    public bool IsLearningVolDownKeybind
    {
        get => _isLearningVolDownKeybind;
        set => SetProperty(ref _isLearningVolDownKeybind, value);
    }

    public void UpdateFrom(IEnumerable<MidiBinding> midiBindings, IEnumerable<KeyboardBinding> keyboardBindings, string channelName)
    {
        ChannelName = channelName;

        var volMidi = midiBindings.FirstOrDefault(b =>
            b.ChannelId == ChannelId && b.Kind == MidiBindingKind.ControlChange && b.Command == MixerCommandKind.VolumeDelta);
        var muteMidi = midiBindings.FirstOrDefault(b =>
            b.ChannelId == ChannelId && b.Command == MixerCommandKind.ToggleMute);
        var muteKey = keyboardBindings.FirstOrDefault(b =>
            b.ChannelId == ChannelId && b.Command == MixerCommandKind.ToggleMute);
        var volUpKey = keyboardBindings.FirstOrDefault(b =>
            b.ChannelId == ChannelId && b.Command == MixerCommandKind.VolumeUp);
        var volDownKey = keyboardBindings.FirstOrDefault(b =>
            b.ChannelId == ChannelId && b.Command == MixerCommandKind.VolumeDown);

        VolumeMidiText = volMidi is null ? "BIND" : $"CC{volMidi.ControllerOrNote} / Ch{volMidi.MidiChannel}";
        MuteMidiText = muteMidi is null ? "BIND" : $"{(muteMidi.Kind == MidiBindingKind.ControlChange ? "CC" : "N")}{muteMidi.ControllerOrNote} / Ch{muteMidi.MidiChannel}";
        MuteKeybindText = muteKey is null ? "BIND" : muteKey.KeyName;
        VolUpKeybindText = volUpKey is null ? "BIND \u2191" : volUpKey.KeyName;
        VolDownKeybindText = volDownKey is null ? "BIND \u2193" : volDownKey.KeyName;
    }

    private static string ApplyDisplaySpacing(string value) =>
        string.Join('\u2009', value.Select(character => character == ' ' ? "\u2009 \u2009" : character.ToString()));
}
