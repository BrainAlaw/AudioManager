using AudioManager.Models;

namespace AudioManager.Contracts;

public interface IOsdService
{
    /// <summary>
    /// Shows or updates the non-focusable click-through volume overlay.
    /// Repeated calls reset the fade-out timer.
    /// </summary>
    void ShowVolumeChange(AudioChannelState channel);

    void ShowMuteChange(AudioChannelState channel);

    void Hide();
}
