using VirtualMixer.Models;

namespace VirtualMixer.Contracts;

public interface ISettingsService
{
    Task<MixerConfiguration> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(MixerConfiguration configuration, CancellationToken cancellationToken = default);

    Task<MixerConfiguration> ImportAsync(string filePath, CancellationToken cancellationToken = default);

    Task ExportAsync(MixerConfiguration configuration, string filePath, CancellationToken cancellationToken = default);
}
