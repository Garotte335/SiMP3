using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Maui.Audio;
using SiMP3.Services;

namespace SiMP3
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });
            // AUDIO MANAGER
            builder.Services.AddSingleton(AudioManager.Current);

            // MAIN MUSIC CONTROLLER
            builder.Services.AddSingleton<MusicController>();
            builder.Services.AddSingleton<PlaylistService>();
            builder.Services.AddSingleton<IPlayerOverlayService, PlayerOverlayService>();

            // PAGES
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<Views.SettingsPage>();
            builder.Services.AddTransient<Views.FullPlayerPage>();

            builder.ConfigureLifecycleEvents(events =>
            {
#if WINDOWS
                events.AddWindows(w =>
                    w.OnWindowCreated(window =>
                    {
                        var winuiWindow = (Microsoft.UI.Xaml.Window)window;
                        winuiWindow.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                    }));
#endif
            });


#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
