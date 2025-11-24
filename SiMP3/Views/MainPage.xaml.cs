using Microsoft.Maui.Controls;
using Plugin.Maui.Audio;
using SiMP3.Models;
using SiMP3.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace SiMP3;

public partial class MainPage : ContentPage
{
    private readonly MusicController _controller;

    public ObservableCollection<TrackModel> Tracks => _controller.Tracks;

    // Стан плейліста
    private bool _isPlaylistVisible = true;
    private const double PlaylistWidthExpanded = 260;
    private const double PlaylistWidthCollapsed = 0;

    public MainPage()
    {
        InitializeComponent();

        _controller = new MusicController(AudioManager.Current);
        BindingContext = this;

        _controller.TrackChanged += Controller_TrackChanged;
        _controller.PlayStateChanged += Controller_PlayStateChanged;
        _controller.ProgressChanged += Controller_ProgressChanged;
        _controller.VolumeStateChanged += Controller_VolumeStateChanged;
    }


    // ============================
    // ВИБІР ФАЙЛІВ
    // ============================
    private async void OnPickFilesClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Оберіть аудіофайли"
            });

            if (result == null) return;

            var paths = result.Select(r => r.FullPath);
            _controller.AddTracks(paths);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Помилка", ex.Message, "OK");
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // завантажуємо стан плеєра
        await _controller.LoadStateAsync();
    }

    protected override async void OnDisappearing()
    {
        // зберігаємо поточний стан (плейлист, трек, позиція, гучність)
        await _controller.SaveStateAsync();

        base.OnDisappearing();
    }


    // ============================
    // ЗМІНА ТРЕКУ
    // ============================
    private void Controller_TrackChanged(TrackModel t)
    {
        lblTitle.Text = lblTitleBar.Text = t.Title;
        lblArtist.Text = lblArtistBar.Text = t.Artist;

        CoverImage.Source = t.Cover;
        SmallCover.Source = t.Cover;

        lblAlbum.Text = t.Album;
        lblGenre.Text = t.Genre;
        lblYear.Text = t.Year;
        lblTrackNumber.Text = $"Track #{t.TrackNumber}";

        lblTimeStart.Text = "0:00";
        lblTimeEnd.Text = t.DurationString;
        progressSlider.Value = 0;
    }


    // ============================
    // PLAY / PAUSE
    // ============================
    private void OnPlayPauseClicked(object sender, EventArgs e)
    {
        _controller.TogglePlayPause();
    }

    private void Controller_PlayStateChanged(bool isPlaying)
    {
        PlayPauseBtnImg.Source = isPlaying ? "pause_btn.png" : "play_btn.png";
    }


    // ============================
    // NEXT / PREV
    // ============================
    private void OnNextClicked(object sender, EventArgs e)
    {
        _controller.Next();
    }

    private void OnPrevClicked(object sender, EventArgs e)
    {
        _controller.Prev();
    }


    // ============================
    // SHUFFLE
    // ============================
    private void OnShuffleClicked(object sender, EventArgs e)
    {
        _controller.ToggleShuffle();
        btnShuffle.Opacity = _controller.IsShuffle ? 1.0 : 0.5;
    }


    // ============================
    // REPEAT
    // ============================
    private void OnRepeatClicked(object sender, EventArgs e)
    {
        _controller.CycleRepeatMode();

        switch (_controller.RepeatMode)
        {
            case 0:
                btnRepeat.Source = "repeat.png";
                btnRepeat.Opacity = 0.5;
                break;

            case 1:
                btnRepeat.Source = "repeat.png";
                btnRepeat.Opacity = 1.0;
                break;

            case 2:
                btnRepeat.Source = "repeat_one.png";
                btnRepeat.Opacity = 1.0;
                break;
        }
    }


    // ============================
    // ПРОГРЕС
    // ============================
    private void Controller_ProgressChanged(TimeSpan current, TimeSpan total, double progress)
    {
        progressSlider.Value = progress;
        lblTimeStart.Text = current.ToString(@"m\:ss");
        lblTimeEnd.Text = total.ToString(@"m\:ss");
    }

    private void OnSeekChanged(object sender, ValueChangedEventArgs e)
    {
        _controller.SeekRelative(e.NewValue);
    }


    // ============================
    // ГУЧНІСТЬ
    // ============================
    private void OnVolumeChanged(object sender, ValueChangedEventArgs e)
    {
        _controller.SetVolume(e.NewValue);
    }

    private void Controller_VolumeStateChanged(double volume, bool isMuted)
    {
        if (Math.Abs(volumeSlider.Value - volume) > 0.001)
            volumeSlider.Value = volume;
    }

    private void OnMuteClicked(object sender, EventArgs e)
    {
        _controller.ToggleMute();
    }


    // ============================
    // ВИБІР ТРЕКУ ЗІ СПИСКУ
    // ============================
    private void OnTrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0)
            return;

        if (e.CurrentSelection[0] is TrackModel track)
            _controller.PlayTrack(track);
    }


    // =====================================================
    // ANIMATION: ПРИХОВУВАНИЙ ПЛЕЙЛІСТ ДЛЯ WINDOWS
    // =====================================================
    private void OnTogglePlaylistClicked(object sender, EventArgs e)
    {
        double from = _isPlaylistVisible ? PlaylistWidthExpanded : PlaylistWidthCollapsed;
        double to = _isPlaylistVisible ? PlaylistWidthCollapsed : PlaylistWidthExpanded;

        var anim = new Animation(v =>
        {
            PlaylistColumn.Width = new GridLength(v);

            if (v < 5)
                PlaylistBorder.IsVisible = false;
            else
                PlaylistBorder.IsVisible = true;

        }, from, to);

        anim.Commit(this, "PlaylistSlide", 16, 250, Easing.CubicOut);

        _isPlaylistVisible = !_isPlaylistVisible;
    }
}
