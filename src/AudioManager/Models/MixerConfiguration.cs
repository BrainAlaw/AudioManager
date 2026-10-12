namespace AudioManager.Models;

public sealed class MixerConfiguration
{
    public const int CurrentSchemaVersion = 7;

    private Dictionary<string, string> _processExecutablePaths = new(StringComparer.OrdinalIgnoreCase);

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool RunOnStartup { get; set; }

    public bool StartInTray { get; set; }

    public bool MinimizeToTray { get; set; }

    public bool CloseToTray { get; set; }

    public bool AudioDiagnosticsEnabled { get; set; }

    public bool NotificationsEnabled { get; set; } = true;

    public string? SelectedMidiDeviceId { get; set; }

    public bool MidiAutoConnect { get; set; }

    public List<AudioChannelConfig> Channels { get; set; } = CreateDefaultChannels();

    public List<MidiBinding> MidiBindings { get; set; } = [];

    public List<KeyboardBinding> KeyboardBindings { get; set; } = [];

    /// <summary>
    /// Always case-insensitive, even after deserialization replaces the instance.
    /// </summary>
    public Dictionary<string, string> ProcessExecutablePaths
    {
        get => _processExecutablePaths;
        set
        {
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (value is not null)
            {
                foreach (var (key, path) in value)
                {
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(path))
                    {
                        normalized[key] = path;
                    }
                }
            }

            _processExecutablePaths = normalized;
        }
    }

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
