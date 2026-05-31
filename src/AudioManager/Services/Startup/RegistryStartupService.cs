using Microsoft.Win32;
using AudioManager.Contracts;

namespace AudioManager.Services.Startup;

public sealed class RegistryStartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AudioManager";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open startup registry key.");

        if (enabled)
        {
            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Unable to resolve process path.");

            key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        return Task.CompletedTask;
    }
}
