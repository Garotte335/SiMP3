using Microsoft.Maui.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace SiMP3
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; }

        public App(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            Services = serviceProvider;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
#if ANDROID
            return new Window(new AndroidMainPage());
#else
            return new Window(new MainPage());
#endif
        }
    }
}
