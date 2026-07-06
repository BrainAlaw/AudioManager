using System.Windows.Threading;
using System.Windows;
using AudioManager.Contracts;
using AudioManager.Models;
using AudioManager.Views;
using WpfApplication = System.Windows.Application;

namespace AudioManager.Services.Osd;

public sealed class OsdService : IOsdService
{
    private readonly object _pendingLock = new();
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _renderTimer;
    private AudioChannelState? _pendingChannel;
    private bool _pendingIsMute;
    private bool _ensureRenderQueued;
    private OsdWindow? _window;

    public OsdService()
    {
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _hideTimer.Tick += (_, _) => Hide();

        _renderTimer = new DispatcherTimer(DispatcherPriority.Background, WpfApplication.Current.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _renderTimer.Tick += (_, _) => FlushPending();
    }

    public void ShowVolumeChange(AudioChannelState channel)
    {
        Queue(channel, isMuted: false);
    }

    public void ShowMuteChange(AudioChannelState channel)
    {
        Queue(channel, isMuted: true);
    }

    public void Hide()
    {
        if (!WpfApplication.Current.Dispatcher.CheckAccess())
        {
            WpfApplication.Current.Dispatcher.BeginInvoke(Hide, DispatcherPriority.Background);
            return;
        }

        _hideTimer.Stop();
        _renderTimer.Stop();
        _window?.FadeOut();
    }

    private void Queue(AudioChannelState channel, bool isMuted)
    {
        lock (_pendingLock)
        {
            _pendingChannel = Snapshot(channel);
            _pendingIsMute = isMuted;
        }

        if (WpfApplication.Current.Dispatcher.CheckAccess())
        {
            EnsureRenderTimer();
            return;
        }

        lock (_pendingLock)
        {
            if (_ensureRenderQueued)
            {
                return;
            }

            _ensureRenderQueued = true;
        }

        WpfApplication.Current.Dispatcher.BeginInvoke(EnsureRenderTimer, DispatcherPriority.Render);
    }

    private void EnsureRenderTimer()
    {
        lock (_pendingLock)
        {
            _ensureRenderQueued = false;
        }

        if (!_renderTimer.IsEnabled)
        {
            _renderTimer.Start();
        }
    }

    private void FlushPending()
    {
        AudioChannelState? channel;
        bool isMuted;

        lock (_pendingLock)
        {
            channel = _pendingChannel;
            isMuted = _pendingIsMute;
            _pendingChannel = null;
        }

        if (channel is null)
        {
            _renderTimer.Stop();
            return;
        }

        _window ??= new OsdWindow();
        _window.ShowOrUpdate(channel, isMuted);

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private static AudioChannelState Snapshot(AudioChannelState channel)
    {
        var snapshot = new AudioChannelState
        {
            Id = channel.Id,
            Name = channel.Name,
            Role = channel.Role,
            IconPath = channel.IconPath,
            Endpoint = channel.Endpoint,
            Volume = channel.Volume,
            IsMuted = channel.IsMuted,
            PeakValue = channel.PeakValue
        };

        snapshot.AssignedProcesses.AddRange(channel.AssignedProcesses);
        return snapshot;
    }
}
