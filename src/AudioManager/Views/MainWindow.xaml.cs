using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using AudioManager.ViewModels;

namespace AudioManager.Views;

public partial class MainWindow : Window
{
    private readonly NotifyIcon _notifyIcon;
    private bool _allowClose;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.SetShellVisible(true);
        this.Icon = LoadWindowIcon();

        _notifyIcon = new NotifyIcon
        {
            Text = "Audio Manager",
            Icon = LoadTrayIcon(),
            Visible = false,
            ContextMenuStrip = BuildTrayMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        StateChanged += OnWindowStateChanged;
        Closing += OnWindowClosing;
    }



    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ChannelStripView.CommitActiveRenameIfClickOutside(e.OriginalSource);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { MinimizeToTray: true })
        {
            MinimizeToTray();
            return;
        }

        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        return menu;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateShellVisibilityState();

        if (WindowState == WindowState.Minimized &&
            DataContext is MainWindowViewModel { MinimizeToTray: true })
        {
            MinimizeToTray();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            return;
        }

        if (DataContext is MainWindowViewModel { CloseToTray: true })
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void MinimizeToTray()
    {
        WindowState = WindowState.Normal;
        HideToTray();
    }

    internal void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        _notifyIcon.Visible = true;
        UpdateShellVisibilityState();
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
        _notifyIcon.Visible = false;
        UpdateShellVisibilityState();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        _notifyIcon.Visible = false;
        Close();
    }

    private void UpdateShellVisibilityState()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var isVisibleForUi = IsVisible && WindowState != WindowState.Minimized;
            viewModel.SetShellVisible(isVisibleForUi);
        }
    }

    private static BitmapImage LoadWindowIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "icon.png");
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Icons", "app.ico");
        if (File.Exists(path))
        {
            using var stream = File.OpenRead(path);
            using var icon = new System.Drawing.Icon(stream);
            return (System.Drawing.Icon)icon.Clone();
        }

        var processPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var associatedIcon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
            if (associatedIcon is not null)
            {
                return (System.Drawing.Icon)associatedIcon.Clone();
            }
        }

        return System.Drawing.SystemIcons.Application;
    }
}
