using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using SiMP3.Models;
using SiMP3.Services;
using SiMP3;

namespace SiMP3.Views
{
    public partial class PlaylistPage : ContentPage
    {
        private readonly PlaylistService _playlistService;
        private readonly MusicController _musicController;
        private readonly ObservableCollection<PlaylistModel> _filtered = new();
        private bool _subscribed;

        public ObservableCollection<PlaylistModel> FilteredPlaylists => _filtered;
        public string? SearchQuery { get; set; }

        public PlaylistPage(PlaylistService playlistService, MusicController musicController)
        {
            InitializeComponent();
            _playlistService = playlistService;
            _musicController = musicController;
            BindingContext = this;
            _playlistService.Playlists.CollectionChanged += (_, __) => ApplyFilter();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _playlistService.EnsureLoadedAsync();
            ApplyFilter();

            if (!_subscribed)
            {
                _musicController.TrackChanged += OnTrackChanged;
                _musicController.PlayStateChanged += OnPlayStateChanged;
                _subscribed = true;
            }

            UpdateMiniPlayer(_musicController.CurrentTrack);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            if (_subscribed)
            {
                _musicController.TrackChanged -= OnTrackChanged;
                _musicController.PlayStateChanged -= OnPlayStateChanged;
                _subscribed = false;
            }
        }

        private async void OnAddPlaylistClicked(object sender, EventArgs e)
        {
            var name = await DisplayPromptAsync("New playlist", "Enter playlist name", "Create", "Cancel", "", 50);
            if (string.IsNullOrWhiteSpace(name))
                return;

            await _playlistService.CreateAsync(name.Trim());

            ApplyFilter();
        }

        private async void OnDeletePlaylistSwipe(object sender, EventArgs e)
        {
            if (sender is SwipeItem swipe && swipe.BindingContext is PlaylistModel playlist)
            {
                var confirm = await DisplayAlert("Delete", $"Delete playlist '{playlist.Name}'?", "Yes", "No");
                if (confirm)
                {
                    await _playlistService.DeleteAsync(playlist);
                    ApplyFilter();
                }
            }
        }

        private async void OnPlaylistTapped(object sender, TappedEventArgs e)
        {
            if (sender is Border border && border.BindingContext is PlaylistModel playlist)
            {
                await _playlistService.EnsureLoadedAsync();
                var editor = new PlaylistEditorPage(playlist, _playlistService, _musicController);
                await Navigation.PushModalAsync(new NavigationPage(editor)
                {
                    BarBackgroundColor = Color.FromArgb("#0D0D0D"),
                    BarTextColor = Colors.White
                });
            }
        }

        private void ApplyFilter()
        {
            var query = SearchQuery?.Trim();
            IEnumerable<PlaylistModel> source = _playlistService.Playlists;
            if (!string.IsNullOrEmpty(query))
            {
                source = source.Where(p => p.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            _filtered.Clear();
            foreach (var pl in source)
                _filtered.Add(pl);
        }

        public void ApplySearch(string? query)
        {
            SearchQuery = query;
            ApplyFilter();
        }

        private async void OnPlayPlaylistClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.BindingContext is PlaylistModel playlist)
            {
                await _playlistService.EnsureLoadedAsync();
                _musicController.PlayPlaylist(playlist.Tracks.ToList());
            }
        }

        private void OnTrackChanged(TrackModel track)
            {
                MainThread.BeginInvokeOnMainThread(() => UpdateMiniPlayer(track));
            }

        private void OnPlayStateChanged(bool isPlaying)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MiniPlayPauseBtn.Source = isPlaying ? "pause_btn.png" : "play_btn.png";
            });
        }

        private void UpdateMiniPlayer(TrackModel? track)
        {
            MiniPlayerBar.IsVisible = track != null;
            if (track == null)
                return;

            MiniCover.Source = track.Cover;
            MiniTitle.Text = track.Title;
            MiniArtist.Text = track.Artist;
            MiniPlayPauseBtn.Source = _musicController.IsPlaying ? "pause_btn.png" : "play_btn.png";
        }

        private void OnPlayPauseClicked(object sender, EventArgs e) => _musicController.TogglePlayPause();

        private void OnNextClicked(object sender, EventArgs e) => _musicController.Next();

        private void OnPrevClicked(object sender, EventArgs e) => _musicController.Prev();

        private void OnMiniPlayerTapped(object sender, TappedEventArgs e)
        {
            var overlay = App.Services.GetRequiredService<IPlayerOverlayService>();
            overlay.Show();
        }
    }
}