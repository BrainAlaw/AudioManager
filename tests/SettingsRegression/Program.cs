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
Console.WriteLine($"Test artifacts: {directory}");
