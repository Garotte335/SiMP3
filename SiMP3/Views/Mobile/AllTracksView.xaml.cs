using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using SiMP3.Models;
using SiMP3.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SiMP3.Views.Mobile;

public partial class AllTracksView : ContentView
{
    private readonly MusicController _controller;
    private readonly PlaylistService _playlistService;
    private bool _autoImportAttempted;
    private bool _isUpdatingSelection;
    private string _searchQuery = string.Empty;

    public ObservableCollection<TrackModel> Tracks => _controller.Tracks;

    public AllTracksView()
    {
        InitializeComponent();

        _controller = App.Services.GetRequiredService<MusicController>();
        _playlistService = App.Services.GetRequiredService<PlaylistService>();
        BindingContext = this;

        _controller.TrackChanged += Controller_TrackChanged;
        _controller.PlayStateChanged += Controller_PlayStateChanged;
    }

    protected override async void OnParentSet()
    {
        base.OnParentSet();

        if (Parent != null)
        {
#if ANDROID
            if (!_autoImportAttempted)
            {
                _autoImportAttempted = true;
                var autoTracks = await _controller.FindLocalMusicAndroidAsync();
                _controller.AddTracks(autoTracks);
            }
#endif
            await _playlistService.EnsureLoadedAsync();
            UpdateMiniPlayer(_controller.CurrentTrack);
        }
        else
        {
            _controller.TrackChanged -= Controller_TrackChanged;
            _controller.PlayStateChanged -= Controller_PlayStateChanged;
        }
    }

    private void Controller_TrackChanged(TrackModel t)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MiniPlayerBar.IsVisible = true;
            MiniCover.Source = t.Cover;
            MiniTitle.Text = t.Title;
            MiniArtist.Text = t.Artist;

            _isUpdatingSelection = true;
            PlaylistView.SelectedItem = Tracks.Contains(t) ? t : null;
            _isUpdatingSelection = false;
        });
    }

    private void Controller_PlayStateChanged(bool isPlaying)
    {
        var icon = isPlaying ? "pause_btn.png" : "play_btn.png";
        MainThread.BeginInvokeOnMainThread(() => MiniPlayPauseBtn.Source = icon);
    }

    private void OnPlayPauseClicked(object sender, EventArgs e) => _controller.TogglePlayPause();

    private void OnNextClicked(object sender, EventArgs e) => _controller.Next();

    private void OnPrevClicked(object sender, EventArgs e) => _controller.Prev();

    private void OnTrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection)
            return;

        if (e.CurrentSelection?.FirstOrDefault() is TrackModel track)
        {
            _controller.PlayTrack(track);
        }
    }

    private async void OnMiniPlayerTapped(object sender, TappedEventArgs e)
    {
        var overlay = App.Services.GetRequiredService<IPlayerOverlayService>();
        await overlay.OpenAsync();
    }

    private void UpdateMiniPlayer(TrackModel? track)
    {
        MiniPlayerBar.IsVisible = track != null;
        if (track == null)
            return;

        MiniCover.Source = track.Cover;
        MiniTitle.Text = track.Title;
        MiniArtist.Text = track.Artist;
        MiniPlayPauseBtn.Source = _controller.IsPlaying ? "pause_btn.png" : "play_btn.png";
    }

    public void ApplySearch(string query)
    {
        _searchQuery = query;
        if (string.IsNullOrWhiteSpace(query))
            _controller.SetFilter(string.Empty);
        else
            _controller.SetFilter(query);
    }

    private async Task ShowSortMenuAsync()
    {
        var choice = await Application.Current!.MainPage.DisplayActionSheet("Сортування", "Скасувати", null,
            "За назвою", "За виконавцем", "За альбомом", "За тривалістю");

        if (string.IsNullOrWhiteSpace(choice) || choice == "Скасувати")
            return;

        var mode = choice switch
        {
            "За виконавцем" => TrackSortMode.ByArtist,
            "За альбомом" => TrackSortMode.ByAlbum,
            "За тривалістю" => TrackSortMode.ByDuration,
            _ => TrackSortMode.ByTitle
        };

        _controller.SetSortMode(mode);
    }

    private async void OnSortClicked(object sender, EventArgs e)
    {
        await ShowSortMenuAsync();
    }
}
