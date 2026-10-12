using AudioManager.Models;

namespace AudioManager.Contracts;

public interface IKeyboardHookService : IDisposable
{
    event EventHandler<KeyboardKeyEventArgs>? KeyPressed;

    bool IsRunning { get; }

    /// <summary>
    /// When true every key press is reported (used while learning a binding).
    /// </summary>
    bool CaptureAllKeys { get; set; }

    /// <summary>
    /// Restricts reporting to the given virtual keys so unbound typing never reaches the UI thread.
    /// </summary>
    void UpdateBoundKeys(IEnumerable<int> virtualKeys);

    void Start();

    void Stop();
}
