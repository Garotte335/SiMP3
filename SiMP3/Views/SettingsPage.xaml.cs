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
            Title = "Налаштування";
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await App.Services.GetRequiredService<IPlayerOverlayService>().CloseAsync();
        }

        private async void OnClearPlaylists(object sender, EventArgs e)
        {
            var confirm = await DisplayAlert("Очистити плейлисти", "Видалити всі плейлисти, включно з улюбленими?", "Так", "Ні");
            if (!confirm)
                return;

            await _playlistService.ClearAll();
            await DisplayAlert("Готово", "Плейлисти очищено.", "OK");
        }

        private async void OnCheckUpdates(object sender, EventArgs e)
        {
            await DisplayAlert("Оновлення", "Перевірка оновлень скоро буде доступна.", "OK");
        }
    }
}