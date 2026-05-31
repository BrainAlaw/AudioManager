using System.IO;
using System.Windows;
using WpfFontFamily = System.Windows.Media.FontFamily;
using Microsoft.Extensions.DependencyInjection;
using VirtualMixer.Contracts;
using VirtualMixer.Services.Audio;
using VirtualMixer.Services.Input;
using VirtualMixer.Services.Midi;
using VirtualMixer.Services.Osd;
using VirtualMixer.Services.Settings;
using VirtualMixer.ViewModels;
using VirtualMixer.Views;

namespace VirtualMixer;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegisterEmbeddedFonts();

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ICoreAudioManager, CoreAudioManager>();
        services.AddSingleton<IMidiListenerService, MidiListenerService>();
        services.AddSingleton<IKeyboardHookService, KeyboardHookService>();
        services.AddSingleton<IOsdService, OsdService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var viewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        await viewModel.InitializeAsync();

        MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow.Show();

        if (viewModel.StartInTray && MainWindow is Views.MainWindow mainWindow)
        {
            mainWindow.HideToTray();
        }
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

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            if (_serviceProvider.GetService<MainWindowViewModel>() is { } viewModel)
            {
                await viewModel.SaveImmediatelyAsync();
            }

            if (_serviceProvider.GetService<ICoreAudioManager>() is { } audioManager)
            {
                await audioManager.DisposeAsync();
            }

            _serviceProvider.Dispose();
        }

        base.OnExit(e);
    }
}
