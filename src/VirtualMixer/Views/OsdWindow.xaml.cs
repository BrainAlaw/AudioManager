using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using VirtualMixer.Models;

namespace VirtualMixer.Views;

public partial class OsdWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    public OsdWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyClickThroughStyles();
        Left = SystemParameters.PrimaryScreenWidth - Width - 48;
        Top = 72;
    }

    public void Update(AudioChannelState channel)
    {
        ChannelName.Text = channel.Name;
        var percent = (int)MathF.Round(channel.Volume * 100);
        VolumePercent.Text = $"{percent}%";
        VolumeBar.Value = percent;
        IconText.Text = channel.Name.Length >= 2 ? channel.Name[..2].ToUpperInvariant() : channel.Name.ToUpperInvariant();
    }

    public void FadeIn()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    public void FadeOut()
    {
        var animation = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240));
        animation.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, animation);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThroughStyles();
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
}
