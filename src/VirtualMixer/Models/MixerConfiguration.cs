namespace AudioManager.Models;

public sealed class MixerConfiguration
{
    public int SchemaVersion { get; set; } = 5;

    public bool RunOnStartup { get; set; }

    public bool StartInTray { get; set; }

    public bool MinimizeToTray { get; set; }

    public bool CloseToTray { get; set; }

    public bool AudioDiagnosticsEnabled { get; set; }

    public string? SelectedMidiDeviceId { get; set; }

    public bool MidiAutoConnect { get; set; }

    public List<AudioChannelConfig> Channels { get; set; } = CreateDefaultChannels();

    public List<MidiBinding> MidiBindings { get; set; } = [];

    public List<KeyboardBinding> KeyboardBindings { get; set; } = [];

    public Dictionary<string, string> ProcessExecutablePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static List<AudioChannelConfig> CreateDefaultChannels()
    {
        return
        [
            new() { Id = "mic", Name = "Mic", Role = AudioChannelRole.Microphone },
            new()
            {
                Id = AudioChannelConfig.MasterChannelId,
                Name = "MASTER",
                Role = AudioChannelRole.Master
            },
            new() { Id = "chat", Name = "CHAT", Role = AudioChannelRole.VirtualOutput },
            new() { Id = "game", Name = "GAME", Role = AudioChannelRole.VirtualOutput },
            new() { Id = "browser", Name = "MEDIA", Role = AudioChannelRole.VirtualOutput },
            new() { Id = "music", Name = "MUSIC", Role = AudioChannelRole.VirtualOutput }
        ];
    }
}
