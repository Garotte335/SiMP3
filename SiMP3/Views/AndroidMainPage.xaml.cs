using Microsoft.Maui.Controls;
using Plugin.Maui.Audio;
using SiMP3.Models;
using SiMP3.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;

namespace SiMP3;

public partial class AndroidMainPage : ContentPage, INotifyPropertyChanged
{
    private readonly MusicController _controller;
    private bool _autoImportAttempted;
    private bool _isUpdatingSelection;


    private string _selectedTab = string.Empty;
    public ObservableCollection<TrackModel> Tracks => _controller.Tracks;
    public ObservableCollection<string> Tabs { get; } = new(new[] { "All", "Artists", "Albums", "Playlists" });

    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public AndroidMainPage()
    {
        InitializeComponent();

        _controller = new MusicController(AudioManager.Current);
        SelectedTab = Tabs.First();
        BindingContext = this;

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Sort",
            IconImageSource = "menu.png",
            Order = ToolbarItemOrder.Primary,
            Command = new Command(async () => await ShowSortMenuAsync())
        });

        // Add Cancel Import toolbar/menu item on Android using Command
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Cancel import",
            IconImageSource = "cancel.png",
            Order = ToolbarItemOrder.Primary,
            Command = new Command(async () =>
            {
                _controller.CancelImport();
                await DisplayAlert("Імпорт", "Імпорт скасовано.", "OK");
            })
        });

        _controller.TrackChanged += Controller_TrackChanged;
        _controller.PlayStateChanged += Controller_PlayStateChanged;
        _controller.ProgressChanged += Controller_ProgressChanged;
        _controller.VolumeStateChanged += Controller_VolumeStateChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

#if ANDROID
        if (!_autoImportAttempted)
        {
            _autoImportAttempted = true;

            var autoTracks = await _controller.FindLocalMusicAndroidAsync();
            _controller.AddTracks(autoTracks);
        }
#endif
    }

    // ================= TRACK CHANGED =================
    private void Controller_TrackChanged(TrackModel t)
    {
        // mini player
        MiniPlayerBar.IsVisible = true;
        MiniCover.Source = t.Cover;
        MiniTitle.Text = t.Title;
        MiniArtist.Text = t.Artist;

        // full player
        PlayerCover.Source = t.Cover;
        PlayerTitle.Text = t.Title;
        PlayerArtist.Text = t.Artist;

        PlayerTimeStart.Text = "0:00";
        PlayerTimeEnd.Text = t.DurationString;
        PlayerProgressSlider.Value = 0;

        _isUpdatingSelection = true;
        PlaylistView.SelectedItem = t;
        _isUpdatingSelection = false;
    }

    // ================= PLAY / PAUSE =================
    private void Controller_PlayStateChanged(bool isPlaying)
    {
        var icon = isPlaying ? "pause_btn.png" : "play_btn.png";
        MiniPlayPauseBtn.Source = icon;
        PlayerPlayPauseBtn.Source = icon;
    }

    private void OnPlayPauseClicked(object sender, EventArgs e)
        => _controller.TogglePlayPause();

    // ================= NEXT / PREV =================
    private void OnNextClicked(object sender, EventArgs e)
        => _controller.Next();

    private void OnPrevClicked(object sender, EventArgs e)
        => _controller.Prev();

    // ================= SHUFFLE / REPEAT =================
    private void OnShuffleClicked(object sender, EventArgs e)
    {
        _controller.ToggleShuffle();
        PlayerShuffleBtn.Opacity = _controller.IsShuffle ? 1.0 : 0.5;
    }

    private void OnRepeatClicked(object sender, EventArgs e)
    {
        _controller.CycleRepeatMode();

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

    // ================= PROGRESS =================
    private void Controller_ProgressChanged(TimeSpan current, TimeSpan total, double progress)
    {
        PlayerProgressSlider.Value = progress;
        PlayerTimeStart.Text = current.ToString(@"m\:ss");
        PlayerTimeEnd.Text = total.ToString(@"m\:ss");
    }

    private void OnSeekChanged(object sender, ValueChangedEventArgs e)
        => _controller.SeekRelative(e.NewValue);

    // ================= VOLUME (Android можна ігнорити) =================
    private void Controller_VolumeStateChanged(double volume, bool isMuted)
    {
        // На Android не показуємо слайдер гучності, можна нічого не робити
    }

    // ================= PLAYLIST SELECTION =================
    private void OnTrackSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection)
            return;

        if (e.CurrentSelection?.FirstOrDefault() is TrackModel track)
        {
            _controller.PlayTrack(track);
        }
    }

    // ================= MINI PLAYER TAP =================
    private void OnMiniPlayerTapped(object sender, TappedEventArgs e)
    {
        FullPlayerOverlay.IsVisible = true;
    }

    private void OnCloseFullPlayer(object sender, EventArgs e)
        => FullPlayerOverlay.IsVisible = false;

    // ================= TAB SWITCHER =================
    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.FirstOrDefault() is string tab)
        {
            SelectedTab = tab;
        }
    }
    private async Task ShowSortMenuAsync()
    {
        var choice = await DisplayActionSheet("Сортування", "Скасувати", null,
            "За назвою", "За артистом", "За тривалістю", "За датою додавання");

        if (string.IsNullOrWhiteSpace(choice) || choice == "Скасувати")
            return;

        var mode = choice switch
        {
            "За артистом" => TrackSortMode.ByArtist,
            "За тривалістю" => TrackSortMode.ByDuration,
            "За датою додавання" => TrackSortMode.ByAdded,
            _ => TrackSortMode.ByTitle
        };

        _controller.SetSortMode(mode);
    }

    // ================= PROPERTY CHANGED =================
    public new event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "", Action? onChanged = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;

        backingStore = value;
        onChanged?.Invoke();
        OnPropertyChanged(propertyName);
        return true;
    }

    protected new void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}