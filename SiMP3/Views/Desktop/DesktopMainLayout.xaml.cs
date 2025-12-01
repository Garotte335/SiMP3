using Microsoft.Maui.Controls;
using SiMP3.Views.Mobile;

namespace SiMP3.Views.Desktop;

public partial class DesktopMainLayout : ContentView
{
    public MobileNavigationHost NavigationHost => MobileHost;
    public ContentPresenter PlayerPresenter => PlayerContent;

    public DesktopMainLayout()
    {
        InitializeComponent();
    }
}
