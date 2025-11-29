using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using SiMP3.Models;
using SiMP3.Services;

namespace SiMP3.Views;

public partial class FullPlayerPage : ContentPage
{
    private readonly MusicController _controller;
    private readonly PlaylistService _playlistService;
    private bool _subscribed;
    private bool _hasAnimatedIn;

    public FullPlayerPage()
    {
        InitializeComponent();
        _controller = App.Services.GetRequiredService<MusicController>();
        _playlistService = App.Services.GetRequiredService<PlaylistService>();
        Opacity = 0;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Subscribe();

        var track = _controller.CurrentTrack;
        if (track != null)
        {
            UpdateTrack(track);
        }

        UpdatePlayState(_controller.IsPlaying);
        UpdateRepeatAndShuffle();
        _ = AnimateInAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Unsubscribe();
    }

    public Task AnimateOutAsync()
    {
        var height = Height > 0 ? Height : (Application.Current?.MainPage?.Height ?? 800);
        return Task.WhenAll(
            RootGrid.TranslateTo(0, height, 220, Easing.SinIn),
            this.FadeTo(0.8, 220, Easing.SinIn));
    }

    private async Task AnimateInAsync()
    {
        if (_hasAnimatedIn)
            return;

        _hasAnimatedIn = true;
        var height = Height > 0 ? Height : (Application.Current?.MainPage?.Height ?? 800);
        RootGrid.TranslationY = height;
        Opacity = 0;
        await Task.WhenAll(
            RootGrid.TranslateTo(0, 0, 260, Easing.SinOut),
            this.FadeTo(1, 260, Easing.SinOut));
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        _controller.TrackChanged += OnTrackChanged;
        _controller.PlayStateChanged += OnPlayStateChanged;
        _controller.ProgressChanged += OnProgressChanged;
        _controller.VolumeStateChanged += OnVolumeStateChanged;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;

        _controller.TrackChanged -= OnTrackChanged;
        _controller.PlayStateChanged -= OnPlayStateChanged;
        _controller.ProgressChanged -= OnProgressChanged;
        _controller.VolumeStateChanged -= OnVolumeStateChanged;
        _subscribed = false;
    }

    private void OnTrackChanged(TrackModel track)
    {
        MainThread.BeginInvokeOnMainThread(() => UpdateTrack(track));
    }

    private void UpdateTrack(TrackModel? track)
    {
        if (track == null)
            return;

        PlayerCover.Source = track.Cover;
        PlayerTitle.Text = track.Title;
        PlayerArtist.Text = track.Artist;
        PlayerTimeStart.Text = "0:00";
        PlayerTimeEnd.Text = track.DurationString;
        PlayerProgressSlider.Value = 0;
        UpdateFavoriteIcon(track);
    }

    private void OnPlayStateChanged(bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(() => UpdatePlayState(isPlaying));
    }

    private void UpdatePlayState(bool isPlaying)
    {
        var icon = isPlaying ? "pause_btn.png" : "play_btn.png";
        PlayerPlayPauseBtn.Source = icon;
    }

    private void OnProgressChanged(TimeSpan current, TimeSpan total, double progress)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (total.TotalSeconds > 0)
            {
                PlayerProgressSlider.Value = progress;
                PlayerTimeStart.Text = current.ToString(@"m\:ss");
                PlayerTimeEnd.Text = total.ToString(@"m\:ss");
            }
        });
    }

    private void OnVolumeStateChanged(double volume, bool isMuted)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Math.Abs(VolumeSlider.Value - volume) > 0.001)
                VolumeSlider.Value = volume;
        });
    }

    private void OnPlayPauseClicked(object sender, EventArgs e) => _controller.TogglePlayPause();

    private void OnNextClicked(object sender, EventArgs e) => _controller.Next();

    private void OnPrevClicked(object sender, EventArgs e) => _controller.Prev();

    private void OnShuffleClicked(object sender, EventArgs e)
    {
        _controller.ToggleShuffle();
        UpdateRepeatAndShuffle();
    }

    private void OnRepeatClicked(object sender, EventArgs e)
    {
        _controller.CycleRepeatMode();
        UpdateRepeatAndShuffle();
    }

    private void UpdateRepeatAndShuffle()
    {
        PlayerShuffleBtn.Opacity = _controller.IsShuffle ? 1.0 : 0.5;
        switch (_controller.RepeatMode)
        {
            case 0:
                PlayerRepeatBtn.Source = "repeat.png";
                PlayerRepeatBtn.Opacity = 0.5;
                break;
            case 1:
                PlayerRepeatBtn.Source = "repeat.png";
                PlayerRepeatBtn.Opacity = 1.0;
                break;
            case 2:
                PlayerRepeatBtn.Source = "repeat_one.png";
                PlayerRepeatBtn.Opacity = 1.0;
                break;
        }
    }

    private void OnSeekChanged(object sender, ValueChangedEventArgs e)
        => _controller.SeekRelative(e.NewValue);

    private void OnVolumeChanged(object sender, ValueChangedEventArgs e)
    {
        _controller.SetVolume(e.NewValue);
    }

    private async void OnFavoriteClicked(object sender, EventArgs e)
    {
        await _playlistService.EnsureLoadedAsync();
        var track = _controller.CurrentTrack;
        if (track == null)
            return;

        if (_playlistService.IsFavorite(track))
            await _playlistService.RemoveFromFavorites(track);
        else
            await _playlistService.AddToFavorites(track);

        UpdateFavoriteIcon(track);
    }

    private void UpdateFavoriteIcon(TrackModel track)
    {
        FavoriteButton.Opacity = _playlistService.IsFavorite(track) ? 1.0 : 0.5;
    }
}