using AudioManager.Models;
using AudioManager.Services.Settings;
using System.Text.Json;

var directory = Path.Combine(Path.GetTempPath(), "AudioManagerSettingsTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var path = Path.Combine(directory, "settings.json");
var service = new JsonSettingsService(path);

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine($"PASS: {message}");
}

Check((await service.LoadAsync()).Channels.Count > 0, "First launch uses defaults");
var configuration = new MixerConfiguration { SelectedMidiDeviceId = "first" };
await service.SaveAsync(configuration);
configuration.SelectedMidiDeviceId = "second";
await service.SaveAsync(configuration);
Check((await service.LoadAsync()).SelectedMidiDeviceId == "second", "Changes persist before exit");
Check(File.ReadAllText(path + ".bak").Contains("first"), "Previous configuration is backed up");
var before = File.ReadAllText(path);
configuration.Channels[0].Volume = float.NaN;
try
{
    await service.SaveAsync(configuration);
    throw new InvalidOperationException("Expected serialization failure");
}
catch (ArgumentException) { }
Check(File.ReadAllText(path) == before, "Failed serialization preserves settings");
configuration.Channels[0].Volume = 0.5f;
using var cancellation = new CancellationTokenSource();
cancellation.Cancel();
try
{
    await service.SaveAsync(configuration, cancellation.Token);
    throw new InvalidOperationException("Expected cancellation");
}
catch (OperationCanceledException) { }
Check(File.ReadAllText(path) == before, "Cancelled save preserves settings");
File.WriteAllText(path, "{incomplete");
Check((await service.LoadAsync()).SelectedMidiDeviceId == "first", "Corrupt settings recover from backup");
Check(Directory.GetFiles(directory, "*.corrupt.*").Length == 1, "Corrupt file is preserved for diagnosis");
File.WriteAllText(path, "null");
File.WriteAllText(path + ".bak", "{incomplete");
try
{
    await service.LoadAsync();
    throw new InvalidOperationException("Expected load failure");
}
catch (JsonException) { }
Check(File.ReadAllText(path) == "null", "Unreadable settings are not replaced with defaults");
Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Temporary files are cleaned up");

// --- Ordering: overlapping saves must end with the newest snapshot on disk.
var orderPath = Path.Combine(directory, "ordering", "settings.json");
var orderService = new JsonSettingsService(orderPath);
var orderConfiguration = new MixerConfiguration();
var pendingSaves = new List<Task>();
for (var index = 0; index < 40; index++)
{
    orderConfiguration.SelectedMidiDeviceId = "value-" + index;
    pendingSaves.Add(orderService.SaveAsync(orderConfiguration));
}
await Task.WhenAll(pendingSaves);
Check((await orderService.LoadAsync()).SelectedMidiDeviceId == "value-39", "Overlapping saves keep the newest snapshot");

// --- Snapshot is taken at call time, later mutation cannot leak into the file.
var snapshotConfiguration = new MixerConfiguration { SelectedMidiDeviceId = "before" };
var snapshotSave = orderService.SaveAsync(snapshotConfiguration);
snapshotConfiguration.SelectedMidiDeviceId = "after";
await snapshotSave;
Check((await orderService.LoadAsync()).SelectedMidiDeviceId == "before", "Save captures state at call time");

// --- Legacy file without schema version runs all migrations; paths stay case-insensitive.
var legacyPath = Path.Combine(directory, "legacy", "settings.json");
Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
File.WriteAllText(legacyPath, """{ "processExecutablePaths": { "Discord.exe": "C:/d.exe", "discord.exe": "C:/e.exe" } }""");
var legacy = await new JsonSettingsService(legacyPath).LoadAsync();
Check(legacy.SchemaVersion == 1, "Missing schema version is treated as legacy");
Check(legacy.ProcessExecutablePaths.ContainsKey("DISCORD.EXE") && legacy.ProcessExecutablePaths.Count == 1, "Executable path map is case-insensitive and de-duplicated");
Check(legacy.Channels.Count == 6, "Missing channels are restored to defaults");

// --- Normalizer invariants.
var broken = new MixerConfiguration
{
    Channels =
    [
        new() { Id = "game", Name = "GAME", Role = AudioChannelRole.VirtualOutput, Volume = float.NaN, AssignedProcesses = ["a.exe", "A.EXE", " b.exe ", "pid-42", ""] },
        new() { Id = "music", Name = "MUSIC", Role = AudioChannelRole.VirtualOutput, AssignedProcesses = ["a.exe", "c.exe"] },
        new() { Id = "GAME", Name = "duplicate", Role = AudioChannelRole.VirtualOutput }
    ],
    MidiBindings =
    [
        new() { Kind = MidiBindingKind.ControlChange, MidiChannel = 1, ControllerOrNote = 5, ChannelId = "game", Command = MixerCommandKind.VolumeDelta },
        new() { Kind = MidiBindingKind.ControlChange, MidiChannel = 1, ControllerOrNote = 5, ChannelId = "music", Command = MixerCommandKind.VolumeDelta },
        new() { Kind = MidiBindingKind.Note, MidiChannel = 1, ControllerOrNote = 9, ChannelId = "ghost", Command = MixerCommandKind.ToggleMute }
    ],
    KeyboardBindings =
    [
        new() { VirtualKey = 120, ChannelId = "game", Command = MixerCommandKind.ToggleMute },
        new() { VirtualKey = 120, ChannelId = "music", Command = MixerCommandKind.ToggleMute }
    ]
};
Check(ConfigurationNormalizer.Normalize(broken), "Normalizer reports repairs");
Check(broken.Channels.Count == 6 && broken.Channels.Select(c => c.Id.ToLowerInvariant()).Distinct().Count() == 6, "Channels are unique and complete");
var normalizedGame = broken.Channels.First(c => c.Id == "game");
Check(normalizedGame.Volume == 1f, "NaN volume is repaired");
Check(normalizedGame.AssignedProcesses.SequenceEqual(["a.exe", "b.exe"]), "Assignments are trimmed, de-duplicated and pid- names dropped");
Check(broken.Channels.First(c => c.Id == "music").AssignedProcesses.SequenceEqual(["c.exe"]), "A process has exactly one owning channel");
Check(broken.MidiBindings.Count == 1 && broken.MidiBindings[0].ChannelId == "music", "Duplicate and orphan MIDI bindings are removed (latest wins)");
Check(broken.KeyboardBindings.Count == 1 && broken.KeyboardBindings[0].ChannelId == "music", "Duplicate keyboard bindings are removed (latest wins)");
Check(!ConfigurationNormalizer.Normalize(broken), "Normalizer is idempotent");

Console.WriteLine($"Test artifacts: {directory}");
