using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using SiMP3.Models;
using SiMP3.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SiMP3.Views.Mobile;

public partial class AlbumsView : ContentView
{
    private readonly MusicController _controller;
    private readonly PlaylistService _playlistService;
    private bool _isUpdatingSelection;
    private string? _selectedAlbum;

    public ObservableCollection<TrackModel> Tracks => _controller.Tracks;

    public AlbumsView()
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
            await _playlistService.EnsureLoadedAsync();
            await PromptAlbumAsync();
            UpdateMiniPlayer(_controller.CurrentTrack);
        }
        else
        {
            _controller.TrackChanged -= Controller_TrackChanged;
            _controller.PlayStateChanged -= Controller_PlayStateChanged;
        }
    }

    private async Task PromptAlbumAsync()
    {
        var albums = _controller.GetAllTracksSnapshot()
            .Select(t => t.Album)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct()
            .OrderBy(a => a)
            .ToArray();

        var choice = await Application.Current!.MainPage.DisplayActionSheet("Обрати альбом", "Скасувати", null, albums);
        if (string.IsNullOrWhiteSpace(choice) || choice == "Скасувати")
        {
            _selectedAlbum = null;
            _controller.SetAlbumFilter(null);
            _controller.SetFilter(string.Empty);
            return;
        }

        _selectedAlbum = choice;
        _controller.SetAlbumFilter(choice);
        _controller.SetFilter(string.Empty);
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

    public void ApplySearch(string query)
    {
        if (!string.IsNullOrWhiteSpace(_selectedAlbum))
            _controller.SetAlbumFilter(_selectedAlbum);

        _controller.SetFilter(query ?? string.Empty);
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
}
