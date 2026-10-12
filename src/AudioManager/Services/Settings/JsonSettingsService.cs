using System.IO;
using System.Text.Json;
using AudioManager.Contracts;
using AudioManager.Models;

namespace AudioManager.Services.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private const int MaxReplaceAttempts = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly string _settingsPath;
    private long _issuedTicket;
    private long _writtenTicket;
    private byte[]? _lastWrittenBytes;

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
            return await ReadConfigurationAsync(_settingsPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            if (!File.Exists(_settingsPath + ".bak"))
            {
                throw;
            }

            var recovered = await ReadConfigurationAsync(_settingsPath + ".bak", cancellationToken).ConfigureAwait(false);
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
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Deserialize(bytes);
    }

    private static MixerConfiguration Deserialize(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Settings must contain a configuration object.");
        }

        var hasSchemaVersion = document.RootElement.EnumerateObject()
            .Any(property => string.Equals(property.Name, nameof(MixerConfiguration.SchemaVersion), StringComparison.OrdinalIgnoreCase));

        var configuration = document.RootElement.Deserialize<MixerConfiguration>(JsonOptions)
                            ?? throw new JsonException("Settings must contain a configuration object.");

        // Files written before schema versioning must run every migration step.
        if (!hasSchemaVersion)
        {
            configuration.SchemaVersion = 1;
        }

        ConfigurationNormalizer.Normalize(configuration);
        return configuration;
    }

    public async Task SaveAsync(MixerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        // Snapshot synchronously on the caller's thread so concurrent UI mutation cannot tear the file.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(configuration, JsonOptions);
        var ticket = Interlocked.Increment(ref _issuedTicket);

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // A newer snapshot was already written; writing this one would roll settings back.
            if (ticket < Volatile.Read(ref _writtenTicket))
            {
                return;
            }

            if (_lastWrittenBytes is not null &&
                File.Exists(_settingsPath) &&
                _lastWrittenBytes.AsSpan().SequenceEqual(bytes))
            {
                Volatile.Write(ref _writtenTicket, ticket);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await ReplaceWithRetryAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            _lastWrittenBytes = bytes;
            Volatile.Write(ref _writtenTicket, ticket);
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
                _saveGate.Release();
            }
        }
    }

    private async Task ReplaceWithRetryAsync(string temporaryPath, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    File.Replace(temporaryPath, _settingsPath, _settingsPath + ".bak");
                }
                else
                {
                    File.Move(temporaryPath, _settingsPath);
                }

                return;
            }
            catch (Exception ex) when (attempt < MaxReplaceAttempts && ex is IOException or UnauthorizedAccessException)
            {
                // Antivirus, indexers and sync clients briefly lock the file; back off and retry.
                await Task.Delay(40 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<MixerConfiguration> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        return Deserialize(bytes);
    }

    public async Task ExportAsync(MixerConfiguration configuration, string filePath, CancellationToken cancellationToken = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(configuration, JsonOptions);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
