using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using SiMP3.Models;
using SiMP3.Services;

namespace SiMP3.Views.Mobile;

public partial class PlaylistEditorView : ContentView
{
    private PlaylistModel? _playlist;
    private readonly PlaylistService _playlistService;
    private readonly MusicController _musicController;
    private bool _suppressSelection;
    private bool _subscribed;

    public ObservableCollection<TrackModel> Tracks { get; } = new();

    public event Action? CloseRequested;

    public PlaylistEditorView()
    {
        InitializeComponent();
        _playlistService = App.Services.GetRequiredService<PlaylistService>();
        _musicController = App.Services.GetRequiredService<MusicController>();
        BindingContext = this;
    }

    public void LoadPlaylist(PlaylistModel playlist)
    {
        _playlist = playlist;
        PlaylistTitle.Text = _playlist.Name;

        Tracks.Clear();
        foreach (var track in _playlist.Tracks)
            Tracks.Add(track);

        if (!_subscribed)
        {
            _musicController.TrackChanged += OnControllerTrackChanged;
            _musicController.PlayStateChanged += OnControllerPlayStateChanged;
            _subscribed = true;
        }

        UpdateMiniPlayer(_musicController.CurrentTrack);
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent == null && _subscribed)
        {
            _musicController.TrackChanged -= OnControllerTrackChanged;
            _musicController.PlayStateChanged -= OnControllerPlayStateChanged;
            _subscribed = false;
        }
    }

    private async void OnAddTrackClicked(object sender, EventArgs e)
    {
        if (_playlist == null)
            return;

        var allTracks = _musicController.GetAllTracksSnapshot().ToList();
        if (allTracks.Count == 0)
        {
            await Application.Current!.MainPage.DisplayAlert(" ", " .", "OK");
            return;
        }

        var titles = allTracks.Select(t => t.Title).ToArray();
        var selected = await Application.Current!.MainPage.DisplayActionSheet(" ", "", null, titles);
        if (string.IsNullOrWhiteSpace(selected) || selected == "")
            return;

        var track = allTracks.FirstOrDefault(t => t.Title == selected);
        if (track != null)
        {
            await _playlistService.AddTrackAsync(_playlist, track);
            if (!Tracks.Contains(track))
                Tracks.Add(track);
        }
    }

    private async void OnTitleTapped(object sender, EventArgs e)
    {
        if (_playlist == null)
            return;

        var newName = await Application.Current!.MainPage.DisplayPromptAsync("", "  ", "", "", initialValue: _playlist.Name);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        await _playlistService.RenameAsync(_playlist, newName.Trim());
        PlaylistTitle.Text = _playlist.Name;
    }

    private async void OnDeleteClicked(object sender, EventArgs e)
    {
        if (_playlist == null)
            return;

        var confirm = await Application.Current!.MainPage.DisplayAlert("", $"  '{_playlist.Name}'?", "", "ͳ");
        if (!confirm)
            return;

        await _playlistService.DeleteAsync(_playlist);
        CloseRequested?.Invoke();
    }

    private async void OnRemoveTrackSwipe(object sender, EventArgs e)
    {
        if (_playlist == null)
            return;

        if (sender is SwipeItem swipe && swipe.BindingContext is TrackModel track)
        {
            await _playlistService.RemoveTrackAsync(_playlist, track);
            Tracks.Remove(track);
        }
    }

    private async void OnSortClicked(object sender, EventArgs e)
    {
        if (_playlist == null)
            return;

        var choice = await Application.Current!.MainPage.DisplayActionSheet("", "", null,
            " ", " ", " ", " ");

        if (string.IsNullOrWhiteSpace(choice) || choice == "")
            return;

        IOrderedEnumerable<TrackModel>? ordered = choice switch
        {
            " " => Tracks.OrderBy(t => t.Artist).ThenBy(t => t.Title),
            " " => Tracks.OrderBy(t => t.Album).ThenBy(t => t.Title),
            " " => Tracks.OrderBy(t => t.Duration).ThenBy(t => t.Title),
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
        if (_playlist == null)
            return;

        if (sender is Border border && border.BindingContext is TrackModel track)
        {
            _musicController.PlayTrackFromPlaylist(track, _playlist.Tracks.ToList());
        }
    }

    private void OnTrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_playlist == null)
            return;

        if (_suppressSelection)
            return;

        if (e.CurrentSelection?.FirstOrDefault() is TrackModel track)
        {
            _musicController.PlayTrackFromPlaylist(track, _playlist.Tracks.ToList());
        }
    }

    private void OnControllerTrackChanged(TrackModel track)
    {
        if (_playlist == null)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
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
