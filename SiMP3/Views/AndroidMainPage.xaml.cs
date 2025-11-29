using Microsoft.Extensions.DependencyInjection;
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
    private readonly PlaylistService _playlistService;
    private bool _autoImportAttempted;
    private bool _isUpdatingSelection;
    private bool _isOpeningPlaylists;
    private string _searchQuery = string.Empty;
    private string? _selectedArtist;
    private string? _selectedAlbum;
    private Views.PlaylistPage? _playlistPage;


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

        _controller = App.Services.GetService<MusicController>() ?? new MusicController(AudioManager.Current);
        _playlistService = App.Services.GetService<PlaylistService>() ?? new PlaylistService();
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

        App.Services.GetRequiredService<IPlayerOverlayService>()
            .Register(FullPlayerOverlay);

#if ANDROID
        if (!_autoImportAttempted)
        {
            _autoImportAttempted = true;

            var autoTracks = await _controller.FindLocalMusicAndroidAsync();
            _controller.AddTracks(autoTracks);
        }
#endif
        await _playlistService.EnsureLoadedAsync();
        UpdateFavoriteIcon(_controller.CurrentTrack);
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
        PlaylistView.SelectedItem = Tracks.Contains(t) ? t : null;
        _isUpdatingSelection = false;

        UpdateFavoriteIcon(t);
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
        var overlay = App.Services.GetRequiredService<IPlayerOverlayService>();
        overlay.Show();
    }

    private void OnCloseFullPlayer(object sender, EventArgs e)
    {
        var overlay = App.Services.GetRequiredService<IPlayerOverlayService>();
        overlay.Hide();
    }

    // ================= TAB SWITCHER =================
    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isOpeningPlaylists)
            return;

        if (e.CurrentSelection?.FirstOrDefault() is string tab)
        {
            if (tab == "Playlists")
            {
                _ = OpenPlaylistsAsync();
                return;
            }

            SelectedTab = tab;
            _ = ApplyTabSelectionAsync(tab);
        }
    }
    private async Task OpenPlaylistsAsync()
    {
        if (_isOpeningPlaylists)
            return;

        _isOpeningPlaylists = true;
        try
        {
            _playlistPage = new Views.PlaylistPage(_playlistService, _controller);
            _playlistPage.ApplySearch(SelectedTab == "Playlists" ? _searchQuery : null);
            await Navigation.PushModalAsync(new NavigationPage(_playlistPage)
            {
                BarBackgroundColor = Color.FromArgb("#0D0D0D"),
                BarTextColor = Colors.White
            });
        }
        finally
        {
            _isOpeningPlaylists = false;
            SelectedTab = Tabs.First();
            TabStrip.SelectedItem = SelectedTab;
        }
    }

    private async Task ApplyTabSelectionAsync(string tab)
    {
        _controller.SetArtistFilter(null);
        _controller.SetAlbumFilter(null);

        if (tab == "Artists")
        {
            var artists = _controller.GetAllTracksSnapshot().Select(t => t.Artist).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().OrderBy(a => a).ToArray();
            var choice = await DisplayActionSheet("Select artist", "Cancel", null, artists);
            if (string.IsNullOrWhiteSpace(choice) || choice == "Cancel")
            {
                SelectedTab = Tabs.First();
                TabStrip.SelectedItem = SelectedTab;
            }
            else
            {
                _selectedArtist = choice;
                _controller.SetArtistFilter(choice);
            }
        }
        else if (tab == "Albums")
        {
            var albums = _controller.GetAllTracksSnapshot().Select(t => t.Album).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().OrderBy(a => a).ToArray();
            var choice = await DisplayActionSheet("Select album", "Cancel", null, albums);
            if (string.IsNullOrWhiteSpace(choice) || choice == "Cancel")
            {
                SelectedTab = Tabs.First();
                TabStrip.SelectedItem = SelectedTab;
            }
            else
            {
                _selectedAlbum = choice;
                _controller.SetAlbumFilter(choice);
            }
        }
        else
        {
            _selectedAlbum = null;
            _selectedArtist = null;
        }

        _controller.SetFilter(_searchQuery);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        ApplySearchFilter(e.NewTextValue ?? string.Empty);
    }

    private void ApplySearchFilter(string query)
    {
        _searchQuery = query;
        if (string.IsNullOrWhiteSpace(query))
            _controller.SetFilter(string.Empty);
        else
            _controller.SetFilter(query);

        _playlistPage?.ApplySearch(SelectedTab == "Playlists" ? query : null);
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new NavigationPage(new Views.SettingsPage(_playlistService))
        {
            BarBackgroundColor = Color.FromArgb("#0D0D0D"),
            BarTextColor = Colors.White
        });
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

    private void UpdateFavoriteIcon(TrackModel? track)
    {
        if (track == null)
        {
            FavoriteButton.Opacity = 0.5;
            return;
        }

        FavoriteButton.Opacity = _playlistService.IsFavorite(track) ? 1.0 : 0.5;
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