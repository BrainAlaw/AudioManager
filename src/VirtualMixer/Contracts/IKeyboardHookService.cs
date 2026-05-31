using VirtualMixer.Models;

namespace VirtualMixer.Contracts;

public interface IKeyboardHookService : IDisposable
{
    event EventHandler<KeyboardKeyEventArgs>? KeyPressed;

    bool IsRunning { get; }

    void Start();

    void Stop();
}
