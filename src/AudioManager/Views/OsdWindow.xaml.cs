using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using AudioManager.Models;
using AudioManager.Services;

namespace AudioManager.Views;

public partial class OsdWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private OsdNotificationKind _currentKind = OsdNotificationKind.None;
    private bool? _currentMuteState;
    private int _transitionVersion;
    private string? _lastVolumeDetailKey;

    public OsdWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyClickThroughStyles();
        SetVolumeLayout();
        UpdatePosition();
    }

    public void Update(AudioChannelState channel, bool isMuted = false)
    {
        var accentBrush = (System.Windows.Media.Brush)FindResource("Vm.AccentBrush");
        var warningBrush = (System.Windows.Media.Brush)FindResource("Vm.WarningBrush");
        var greenBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 180, 96));

        if (isMuted)
        {
            SetMuteLayout(channel.IsMuted);
            MuteSourceText.Text = ApplyDisplaySpacing(channel.Name);
            MuteStateText.Text = ApplyDisplaySpacing(channel.IsMuted ? "MUTED" : "UNMUTED");
            MuteStateText.Foreground = channel.IsMuted ? warningBrush : greenBrush;
        }
        else
        {
            SetVolumeLayout();
            ChannelName.Text = ApplyDisplaySpacing(channel.Name);
            var percent = (int)MathF.Round(channel.Volume * 100);
            VolumePercent.Text = ApplyDisplaySpacing($"{percent}%");
            VolumePercent.Foreground = accentBrush;
            AnimateVolumeProgress(Math.Clamp(channel.Volume, 0f, 1f));

            var detailKey = BuildVolumeDetailKey(channel);
            if (!string.Equals(_lastVolumeDetailKey, detailKey, StringComparison.Ordinal))
            {
                _lastVolumeDetailKey = detailKey;
                UpdateVolumeDetail(channel, warningBrush);
            }
        }

        UpdatePosition();
    }

    public void ShowOrUpdate(AudioChannelState channel, bool isMuted = false)
    {
        var nextKind = isMuted ? OsdNotificationKind.Mute : OsdNotificationKind.Volume;
        var shouldTransition =
            IsVisible &&
            Opacity > 0.01 &&
            (_currentKind != nextKind || (isMuted && _currentMuteState != channel.IsMuted));

        if (!shouldTransition)
        {
            Update(channel, isMuted);
            _currentKind = nextKind;
            _currentMuteState = isMuted ? channel.IsMuted : null;
            ShowActivated = false;
            if (!IsVisible || Opacity <= 0.01)
            {
                Show();
                FadeIn();
                return;
            }

            Opacity = 1;

            return;
        }

        _transitionVersion++;
        var transitionVersion = _transitionVersion;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(90));
        fadeOut.Completed += (_, _) =>
        {
            if (transitionVersion != _transitionVersion)
            {
                return;
            }

            Update(channel, isMuted);
            _currentKind = nextKind;
            _currentMuteState = isMuted ? channel.IsMuted : null;
            Opacity = 0;
            FadeIn();
        };

        BeginAnimation(OpacityProperty, fadeOut);
    }

    public void FadeIn()
    {
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    public void FadeOut()
    {
        _transitionVersion++;
        _currentKind = OsdNotificationKind.None;
        _currentMuteState = null;
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240));
        var transitionVersion = _transitionVersion;
        animation.Completed += (_, _) =>
        {
            if (transitionVersion == _transitionVersion)
            {
                BeginAnimation(OpacityProperty, null);
                Hide();
            }
        };
        BeginAnimation(OpacityProperty, animation);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThroughStyles();
    }

    private void SetVolumeLayout()
    {
        Width = 336;
        Height = 118;
        Root.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 24, 24));
        Root.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51));
        VolumePanel.Visibility = Visibility.Visible;
        MutePanel.Visibility = Visibility.Collapsed;
    }

    private void SetMuteLayout(bool isMuted)
    {
        Width = 214;
        Height = 68;
        _lastVolumeDetailKey = null;
        VolumePanel.Visibility = Visibility.Collapsed;
        MutePanel.Visibility = Visibility.Visible;
    }

    private void AnimateVolumeProgress(float targetValue)
    {
        var animation = new DoubleAnimation
        {
            To = targetValue,
            Duration = TimeSpan.FromMilliseconds(75),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        VolumeProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static string BuildVolumeDetailKey(AudioChannelState channel)
    {
        var assignedProcesses = channel.AssignedProcesses.Count == 0
            ? string.Empty
            : string.Join('|', channel.AssignedProcesses.OrderBy(process => process, StringComparer.OrdinalIgnoreCase));

        return string.Join(
            "::",
            channel.Id,
            channel.Role,
            channel.IsMuted,
            channel.Endpoint?.FriendlyName ?? string.Empty,
            assignedProcesses);
    }

    private void UpdateVolumeDetail(AudioChannelState channel, System.Windows.Media.Brush warningBrush)
    {
        VolumeAppIconsPanel.ItemsSource = null;
        VolumeAppIconsPanel.Visibility = Visibility.Collapsed;
        VolumeEndpointText.Visibility = Visibility.Collapsed;
        VolumeMuteStateText.Visibility = Visibility.Collapsed;

        if (channel.IsMuted)
        {
            VolumeMuteStateText.Text = ApplyDisplaySpacing("MUTED");
            VolumeMuteStateText.Foreground = warningBrush;
            VolumeMuteStateText.Visibility = Visibility.Visible;
            return;
        }

        if (channel.Role == AudioChannelRole.VirtualOutput)
        {
            var processIcons = channel.AssignedProcesses
                .Select(ProcessPresentationHelper.GetProcessIcon)
                .OfType<System.Windows.Media.ImageSource>()
                .ToList();

            if (processIcons.Count > 0)
            {
                VolumeAppIconsPanel.ItemsSource = processIcons;
                VolumeAppIconsPanel.Visibility = Visibility.Visible;
            }
        }
        else if (channel.Role is AudioChannelRole.Microphone or AudioChannelRole.Master)
        {
            VolumeEndpointText.Text = ApplyDisplaySpacing(channel.Endpoint?.FriendlyName ?? "Endpoint missing");
            VolumeEndpointText.Visibility = Visibility.Visible;
        }
    }

    private void UpdatePosition()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = SystemParameters.PrimaryScreenHeight - Height - 72;
    }

    private void ApplyClickThroughStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var styles = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, styles | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static string ApplyDisplaySpacing(string value) =>
        string.Join('\u2009', value.Select(character => character == ' ' ? "\u2009 \u2009" : character.ToString()));

    private enum OsdNotificationKind
    {
        None,
        Volume,
        Mute
    }
}
