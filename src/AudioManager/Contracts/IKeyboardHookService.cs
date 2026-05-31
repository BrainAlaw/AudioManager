using AudioManager.Models;

namespace AudioManager.Contracts;

public interface IKeyboardHookService : IDisposable
{
    event EventHandler<KeyboardKeyEventArgs>? KeyPressed;

    bool IsRunning { get; }

    void Start();

    void Stop();
}
