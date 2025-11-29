using Microsoft.Maui.Controls;
using SiMP3.Services;

namespace SiMP3.Views
{
    public partial class SettingsPage : ContentPage
    {
        private readonly PlaylistService _playlistService;

        public SettingsPage(PlaylistService playlistService)
        {
            InitializeComponent();
            _playlistService = playlistService;
            Title = "Settings";
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await App.Services.GetRequiredService<IPlayerOverlayService>().CloseAsync();
        }

        private async void OnClearPlaylists(object sender, EventArgs e)
        {
            var confirm = await DisplayAlert("Clear playlists", "Remove all playlists including favorites?", "Yes", "No");
            if (!confirm)
                return;

            await _playlistService.ClearAll();
            await DisplayAlert("Done", "Playlists cleared.", "OK");
        }

        private async void OnCheckUpdates(object sender, EventArgs e)
        {
            await DisplayAlert("Updates", "Update check will be available soon.", "OK");
        }
    }
}