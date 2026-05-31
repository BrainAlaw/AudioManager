namespace AudioManager.Contracts;

public interface IStartupService
{
    bool IsEnabled();

    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
