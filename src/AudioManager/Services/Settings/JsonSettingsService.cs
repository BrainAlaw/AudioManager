using System.IO;
using System.Text.Json;
using AudioManager.Contracts;
using AudioManager.Models;

namespace AudioManager.Services.Settings;

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
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioManager", "settings.json"))
    {
    }

    public JsonSettingsService(string settingsPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath);
    }

    public async Task<MixerConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath) && !File.Exists(_settingsPath + ".bak"))
        {
            return new MixerConfiguration();
        }

        try
        {
            return await ReadConfigurationAsync(_settingsPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            if (!File.Exists(_settingsPath + ".bak"))
            {
                throw;
            }

            var recovered = await ReadConfigurationAsync(_settingsPath + ".bak", cancellationToken);
            // Preserve the failed file for diagnosis before restoring the backup.
            if (File.Exists(_settingsPath))
            {
                File.Copy(_settingsPath, _settingsPath + ".corrupt." + Guid.NewGuid().ToString("N"));
            }

            var recoveryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(_settingsPath + ".bak", recoveryPath);
                if (File.Exists(_settingsPath))
                {
                    File.Replace(recoveryPath, _settingsPath, null);
                }
                else
                {
                    File.Move(recoveryPath, _settingsPath);
                }
            }
            finally
            {
                if (File.Exists(recoveryPath))
                {
                    File.Delete(recoveryPath);
                }
            }
            return recovered;
        }
    }

    private static async Task<MixerConfiguration> ReadConfigurationAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MixerConfiguration>(stream, JsonOptions, cancellationToken)
               ?? throw new JsonException("Settings must contain a configuration object.");
    }

    public async Task SaveAsync(MixerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await SaveGate.WaitAsync(cancellationToken);
        var temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_settingsPath))
            {
                File.Replace(temporaryPath, _settingsPath, _settingsPath + ".bak");
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            finally
            {
                SaveGate.Release();
            }
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
