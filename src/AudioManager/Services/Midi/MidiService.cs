using System.Threading;
using NAudio;
using NAudio.Midi;

namespace AudioManager.Services.Midi;

/// <summary>
/// Dedicated MIDI controller service for a six-channel mixer controlled by a
/// Loupedeck-compatible MIDI device.
/// </summary>
public sealed class MidiService : IDisposable
{
    private const int FirstVolumeControl = 70;
    private const int LastVolumeControl = 75;
    private const int VolumeStep = 2;

    private readonly object _syncRoot = new();
    private readonly SynchronizationContext? _callbackContext;
    private MidiIn? _midiIn;
    private bool _disposed;

    /// <summary>
    /// Raised when a supported relative encoder is turned.
    /// Parameters are: channel index 0..5, volume delta +2 or -2.
    /// </summary>
    public event Action<int, int>? VolumeChanged;

    public event Action<int, int>? VolumeAdjusted;

    /// <summary>
    /// Raised when the selected device cannot be found, opened, or reports a
    /// native MIDI error. UI can surface this without crashing the application.
    /// </summary>
    public event Action<string, Exception?>? DeviceError;

    public bool IsInitialized
    {
        get
        {
            lock (_syncRoot)
            {
                return _midiIn is not null;
            }
        }
    }

    public MidiService()
    {
        // In WPF this is normally the UI SynchronizationContext when constructed
        // on the UI thread. It lets subscribers update bound state safely.
        _callbackContext = SynchronizationContext.Current;
    }

    /// <summary>
    /// Opens the first available MIDI input device and starts listening for raw
    /// Control Change messages.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no MIDI input is available or the device cannot be opened.
    /// </exception>
    public void Initialize()
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            StopCore();

            var deviceIndex = FindFirstAvailableDeviceIndex();
            if (deviceIndex < 0)
            {
                var message = "No MIDI input device was found.";
                PublishDeviceError(message, null);
                throw new InvalidOperationException(message);
            }

            try
            {
                _midiIn = new MidiIn(deviceIndex);
                _midiIn.MessageReceived += OnMessageReceived;
                _midiIn.ErrorReceived += OnErrorReceived;
                _midiIn.Start();
            }
            catch (Exception ex) when (ex is MmException or InvalidOperationException)
            {
                StopCore();

                var message = "The MIDI input could not be opened. It may be disconnected or already in use.";
                PublishDeviceError(message, ex);
                throw new InvalidOperationException(message, ex);
            }
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            StopCore();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }

    private static int FindFirstAvailableDeviceIndex()
    {
        return MidiIn.NumberOfDevices > 0 ? 0 : -1;
    }

    private void OnMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        if (e.MidiEvent is not ControlChangeEvent controlChange)
        {
            return;
        }

        // MIDI-OX reports this as Controller Number. NAudio exposes it through
        // ControlChangeEvent.Controller; cast it to int and ignore enum names.
        var controllerNumber = (int)controlChange.Controller;
        if (controllerNumber < FirstVolumeControl || controllerNumber > LastVolumeControl)
        {
            return;
        }

        var controllerValue = controlChange.ControllerValue;
        var delta = controllerValue switch
        {
            >= 1 and <= 63 => VolumeStep,
            >= 65 and <= 127 => -VolumeStep,
            _ => 0
        };

        // Relative mode: values 1-63 mean increment and 65-127 mean decrement.
        // Values 0 and 64 are neutral/no movement and are intentionally ignored.
        if (delta == 0)
        {
            return;
        }

        var channelIndex = controllerNumber - FirstVolumeControl;
        PublishVolumeAdjusted(channelIndex, delta);
    }

    private void OnErrorReceived(object? sender, MidiInMessageEventArgs e)
    {
        var exception = new InvalidOperationException($"Native MIDI input error: {e.RawMessage}");
        PublishDeviceError("The MIDI input device reported an input error.", exception);

        lock (_syncRoot)
        {
            StopCore();
        }
    }

    private void PublishVolumeAdjusted(int channelIndex, int delta)
    {
        var changedHandler = VolumeChanged;
        var adjustedHandler = VolumeAdjusted;
        if (changedHandler is null && adjustedHandler is null)
        {
            return;
        }

        if (_callbackContext is not null)
        {
            _callbackContext.Post(_ =>
            {
                changedHandler?.Invoke(channelIndex, delta);
                adjustedHandler?.Invoke(channelIndex, delta);
            }, null);
            return;
        }

        changedHandler?.Invoke(channelIndex, delta);
        adjustedHandler?.Invoke(channelIndex, delta);
    }

    private void PublishDeviceError(string message, Exception? exception)
    {
        var handler = DeviceError;
        if (handler is null)
        {
            return;
        }

        if (_callbackContext is not null)
        {
            _callbackContext.Post(_ => handler(message, exception), null);
            return;
        }

        handler(message, exception);
    }

    private void StopCore()
    {
        if (_midiIn is null)
        {
            return;
        }

        try
        {
            _midiIn.Stop();
        }
        catch (MmException ex)
        {
            PublishDeviceError("The Loupedeck MIDI input could not be stopped cleanly.", ex);
        }
        finally
        {
            _midiIn.MessageReceived -= OnMessageReceived;
            _midiIn.ErrorReceived -= OnErrorReceived;
            _midiIn.Dispose();
            _midiIn = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
