using System.Windows.Threading;
using System.Windows;
using AudioManager.Contracts;
using AudioManager.Models;
using AudioManager.Views;
using WpfApplication = System.Windows.Application;

namespace AudioManager.Services.Osd;

public sealed class OsdService : IOsdService
{
    private readonly DispatcherTimer _hideTimer;
    private OsdWindow? _window;

    public OsdService()
    {
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _hideTimer.Tick += (_, _) => Hide();
    }

    public void ShowVolumeChange(AudioChannelState channel)
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(() => ShowVolumeChange(channel));
            return;
        }

        _window ??= new OsdWindow();
        _window.ShowOrUpdate(channel);

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void ShowMuteChange(AudioChannelState channel)
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(() => ShowMuteChange(channel));
            return;
        }

        _window ??= new OsdWindow();
        _window.ShowOrUpdate(channel, isMuted: true);

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void Hide()
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.Invoke(Hide);
            return;
        }

        _hideTimer.Stop();
        _window?.FadeOut();
    }
}
