using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using SiMP3.Services;
using SiMP3.Views;

namespace SiMP3;

    public partial class AppShell : Shell
{
    private readonly IPlayerOverlayService _overlayService;
    private readonly MusicController _musicController;
    private bool _isSearchVisible;

    public AppShell()
    {
        InitializeComponent();

        _overlayService = App.Services.GetRequiredService<IPlayerOverlayService>();
        _musicController = App.Services.GetRequiredService<MusicController>();
        _overlayService.AttachShell(this);

        Routing.RegisterRoute(nameof(FullPlayerPage), typeof(FullPlayerPage));

        Items.Add(new ShellContent
        {
            Route = nameof(AndroidMainPage),
            ContentTemplate = new DataTemplate(() => App.Services.GetRequiredService<AndroidMainPage>()),
            Title = "Домівка"
        });
    }

    protected override void OnNavigating(ShellNavigatingEventArgs args)
    {
        _overlayService.HandleNavigating(this, args);
        base.OnNavigating(args);
    }

    protected override void OnNavigated(ShellNavigatedEventArgs args)
    {
        base.OnNavigated(args);
        _overlayService.HandleNavigated(CurrentPage);
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        var settingsPage = App.Services.GetRequiredService<Views.SettingsPage>();
        await Navigation.PushAsync(settingsPage);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = e.NewTextValue ?? string.Empty;
        _musicController.SetFilter(query);

        if (CurrentPage is AndroidMainPage androidMainPage)
        {
            androidMainPage.ApplyGlobalSearch(query);
        }
    }

    private void OnSearchTapped(object sender, EventArgs e)
    {
        _isSearchVisible = !_isSearchVisible;
        SearchBarContainer.IsVisible = _isSearchVisible;

        if (_isSearchVisible)
        {
            GlobalSearchBar.Focus();
        }
        else
        {
            GlobalSearchBar.Text = string.Empty;
        }
    }
}
