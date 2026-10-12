using System.IO;
using System.Windows;
using System.Windows.Threading;
using WpfFontFamily = System.Windows.Media.FontFamily;
using Microsoft.Extensions.DependencyInjection;
using AudioManager.Contracts;
using AudioManager.Services;
using AudioManager.Services.Audio;
using AudioManager.Services.Input;
using AudioManager.Services.Midi;
using AudioManager.Services.Osd;
using AudioManager.Services.Settings;
using AudioManager.Services.Startup;
using AudioManager.ViewModels;
using AudioManager.Views;

namespace AudioManager;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\AudioManager.Instance";
    private const string ActivateEventName = @"Local\AudioManager.Activate";

    private ServiceProvider? _serviceProvider;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private volatile bool _exiting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ErrorLog.Write("Unhandled domain exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        if (!AcquireSingleInstance())
        {
            Shutdown(0);
            return;
        }

        var startedFromStartup = e.Args.Contains(RegistryStartupService.StartupArgument, StringComparer.OrdinalIgnoreCase);

        RegisterEmbeddedFonts();

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ICoreAudioManager, CoreAudioManager>();
        services.AddSingleton<IMidiListenerService, MidiListenerService>();
        services.AddSingleton<IKeyboardHookService, KeyboardHookService>();
        services.AddSingleton<IOsdService, OsdService>();
        services.AddSingleton<IStartupService, RegistryStartupService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        SessionEnding += OnSessionEnding;

        var viewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        try
        {
            await viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Startup failed", ex);
            System.Windows.MessageBox.Show($"Audio Manager could not start. Your settings have not been reset.\n\n{ex.Message}",
                "Audio Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow.Show();

        // Autostart launches silently in the tray; a manual launch always shows the window.
        var hideToTray = viewModel.StartInTray || (viewModel.RunOnStartup && startedFromStartup);
        if (hideToTray && MainWindow is Views.MainWindow mainWindow)
        {
            mainWindow.HideToTray();
        }
    }

    private bool AcquireSingleInstance()
    {
        _instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // Ask the running instance to show itself instead of starting a second mixer.
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ActivateEventName);
                existing.Set();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Could not signal the running instance", ex);
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
            return false;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var activateEvent = _activateEvent;
        var listener = new Thread(() =>
        {
            while (!_exiting)
            {
                activateEvent.WaitOne();
                if (_exiting)
                {
                    break;
                }

                Dispatcher.BeginInvoke(ActivateMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "AudioManager.InstanceActivation"
        };
        listener.Start();
        return true;
    }

    private void ActivateMainWindow()
    {
        if (MainWindow is Views.MainWindow mainWindow)
        {
            mainWindow.RestoreFromTray();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A tray utility should survive a faulty UI callback; the failure is logged for diagnosis.
        ErrorLog.Write("Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // Windows may kill the process right after logoff/shutdown; persist before that happens.
        _serviceProvider?.GetService<MainWindowViewModel>()?.FlushOnExit();
    }

    private void RegisterEmbeddedFonts()
    {
        var fontDirectory = Path.Combine(AppContext.BaseDirectory, "font");
        var cursiveFontPath = Path.Combine(fontDirectory, "VeniteAdoremus-rgRBA.ttf");
        var straightFontPath = Path.Combine(fontDirectory, "VeniteAdoremusStraight-Yzo6v.ttf");

        if (File.Exists(straightFontPath))
        {
            Resources["Vm.DisplayFont"] = new WpfFontFamily(new Uri(straightFontPath, UriKind.Absolute), "./#Venite Adoremus Straight");
        }

        if (File.Exists(cursiveFontPath))
        {
            Resources["Vm.DisplayFontStraight"] = new WpfFontFamily(new Uri(cursiveFontPath, UriKind.Absolute), "./#Venite Adoremus");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        try
        {
            _activateEvent?.Set();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Could not release the activation listener", ex);
        }

        if (_serviceProvider is not null)
        {
            // Order matters: persist first, then stop input sources, then tear down audio.
            try
            {
                _serviceProvider.GetService<MainWindowViewModel>()?.FlushOnExit();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Flush on exit failed", ex);
            }

            try
            {
                _serviceProvider.GetService<IKeyboardHookService>()?.Dispose();
                _serviceProvider.GetService<IMidiListenerService>()?.Dispose();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Input shutdown failed", ex);
            }

            try
            {
                if (_serviceProvider.GetService<ICoreAudioManager>() is { } audioManager)
                {
                    // Off the UI thread so audio teardown can never deadlock on the dispatcher.
                    Task.Run(async () => await audioManager.DisposeAsync()).Wait(TimeSpan.FromSeconds(5));
                }

                _serviceProvider.Dispose();
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Service shutdown failed", ex);
            }
        }

        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Mutex was not owned by this thread; the OS releases it at process exit.
        }

        base.OnExit(e);
    }
}
