using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using SiMP3.Models;
using SiMP3.Services;

namespace SiMP3.Views
{
    public partial class PlaylistEditorPage : ContentPage
    {
        private readonly PlaylistModel _playlist;
        private readonly PlaylistService _playlistService;
        private readonly MusicController _musicController;
        private bool _suppressSelection;

        public TrackModel? CurrentTrack { get; private set; }
        public string? CurrentTrackPath { get; private set; }

        public ObservableCollection<TrackModel> Tracks => _playlist.Tracks;

        public PlaylistEditorPage(PlaylistModel playlist, PlaylistService playlistService, MusicController musicController)
        {
            InitializeComponent();
            _playlist = playlist;
            _playlistService = playlistService;
            _musicController = musicController;

            PlaylistTitle.Text = _playlist.Name;
            BindingContext = this;

            _musicController.TrackChanged += OnControllerTrackChanged;
            _musicController.PlayStateChanged += OnControllerPlayStateChanged;
            CurrentTrack = _musicController.CurrentTrack;
            CurrentTrackPath = _musicController.CurrentTrack?.Path;
            UpdateMiniPlayer(CurrentTrack);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _musicController.TrackChanged -= OnControllerTrackChanged;
            _musicController.PlayStateChanged -= OnControllerPlayStateChanged;
        }

        private async void OnAddTrackClicked(object sender, EventArgs e)
        {
            var allTracks = _musicController.GetAllTracksSnapshot().ToList();
            if (allTracks.Count == 0)
            {
                await DisplayAlert("Немає треків", "Бібліотека пуста.", "OK");
                return;
            }

            var titles = allTracks.Select(t => t.Title).ToArray();
            var selected = await DisplayActionSheet("Додати трек", "Скасувати", null, titles);
            if (string.IsNullOrWhiteSpace(selected) || selected == "Скасувати")
                return;

            var track = allTracks.FirstOrDefault(t => t.Title == selected);
            if (track != null)
            {
                await _playlistService.AddTrackAsync(_playlist, track);
            }
        }

        private async void OnTitleTapped(object sender, EventArgs e)
        {
            var newName = await DisplayPromptAsync("Перейменувати", "Нова назва плейлиста", "Зберегти", "Скасувати", initialValue: _playlist.Name);
            if (string.IsNullOrWhiteSpace(newName))
                return;

            await _playlistService.RenameAsync(_playlist, newName.Trim());
            PlaylistTitle.Text = _playlist.Name;
        }

        private async void OnDeleteClicked(object sender, EventArgs e)
        {
            var confirm = await DisplayAlert("Видалити", $"Видалити плейлист '{_playlist.Name}'?", "Так", "Ні");
            if (!confirm)
                return;

            await _playlistService.DeleteAsync(_playlist);
            await Shell.Current.Navigation.PopAsync();
        }

        private async void OnRemoveTrackSwipe(object sender, EventArgs e)
        {
            if (sender is SwipeItem swipe && swipe.BindingContext is TrackModel track)
            {
                await _playlistService.RemoveTrackAsync(_playlist, track);
            }
        }

        private async void OnSortClicked(object sender, EventArgs e)
        {
            var choice = await DisplayActionSheet("Сортування", "Скасувати", null,
                "За назвою", "За виконавцем", "За альбомом", "За тривалістю");

            if (string.IsNullOrWhiteSpace(choice) || choice == "Скасувати")
                return;

            IOrderedEnumerable<TrackModel>? ordered = choice switch
            {
                "За виконавцем" => Tracks.OrderBy(t => t.Artist).ThenBy(t => t.Title),
                "За альбомом" => Tracks.OrderBy(t => t.Album).ThenBy(t => t.Title),
                "За тривалістю" => Tracks.OrderBy(t => t.Duration).ThenBy(t => t.Title),
                _ => Tracks.OrderBy(t => t.Title)
            };

            if (ordered != null)
            {
                var sorted = ordered.ToList();
                Tracks.Clear();
                foreach (var t in sorted)
                    Tracks.Add(t);
                await _playlistService.UpdatePlaylistAsync(_playlist);
            }
        }

        private void OnTrackTapped(object sender, TappedEventArgs e)
        {
            if (sender is Border border && border.BindingContext is TrackModel track)
            {
                _musicController.PlayTrackFromPlaylist(track, _playlist.Tracks.ToList());
            }
        }

        private void OnTrackSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelection)
                return;

            if (e.CurrentSelection?.FirstOrDefault() is TrackModel track)
            {
                _musicController.PlayTrackFromPlaylist(track, _playlist.Tracks.ToList());
            }
        }

        private void OnControllerTrackChanged(TrackModel track)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CurrentTrack = track;
                CurrentTrackPath = track.Path;
                OnPropertyChanged(nameof(CurrentTrackPath));
                _suppressSelection = true;
                TrackList.SelectedItem = _playlist.Tracks.FirstOrDefault(t => string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase));
                _suppressSelection = false;
                UpdateMiniPlayer(_playlist.Tracks.Any(t => string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase)) ? track : null);
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
        }

        private void OnControllerPlayStateChanged(bool isPlaying)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MiniPlayPauseBtn.Source = isPlaying ? "pause_btn.png" : "play_btn.png";
            });
        }

        private void OnPlayPauseClicked(object sender, EventArgs e)
            => _musicController.TogglePlayPause();

        private void OnNextClicked(object sender, EventArgs e)
            => _musicController.Next();

        private void OnPrevClicked(object sender, EventArgs e)
            => _musicController.Prev();

        private async void OnMiniPlayerTapped(object sender, TappedEventArgs e)
        {
            var overlay = App.Services.GetRequiredService<IPlayerOverlayService>();
            await overlay.OpenAsync();
        }
    }
}