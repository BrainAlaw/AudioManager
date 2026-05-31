using System.IO;
using System.Text.Json;
using VirtualMixer.Contracts;
using VirtualMixer.Models;

namespace VirtualMixer.Services.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly SemaphoreSlim SaveGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;

    public JsonSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsPath = Path.Combine(appData, "VirtualMixer", "settings.json");
    }

    public async Task<MixerConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new MixerConfiguration();
        }

        try
        {
            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<MixerConfiguration>(stream, JsonOptions, cancellationToken)
                   ?? new MixerConfiguration();
        }
        catch
        {
            // Corrupt settings should never prevent the mixer from launching.
            return new MixerConfiguration();
        }
    }

    public async Task SaveAsync(MixerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await SaveGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using var stream = new FileStream(
                _settingsPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
        }
        finally
        {
            SaveGate.Release();
        }
    }

    public async Task<MixerConfiguration> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<MixerConfiguration>(stream, JsonOptions, cancellationToken)
               ?? new MixerConfiguration();
    }

    public async Task ExportAsync(MixerConfiguration configuration, string filePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
    }
}
