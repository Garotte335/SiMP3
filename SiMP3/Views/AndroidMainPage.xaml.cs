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

        _controller = App.Services.GetRequiredService<MusicController>();
        _playlistService = App.Services.GetRequiredService<PlaylistService>();
        SelectedTab = Tabs.First();
        BindingContext = this;

        _controller.TrackChanged += Controller_TrackChanged;
        _controller.PlayStateChanged += Controller_PlayStateChanged;
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
        await _playlistService.EnsureLoadedAsync();
    }

    // ================= TRACK CHANGED =================
    private void Controller_TrackChanged(TrackModel t)
    {
        // mini player
        MiniPlayerBar.IsVisible = true;
        MiniCover.Source = t.Cover;
        MiniTitle.Text = t.Title;
        MiniArtist.Text = t.Artist;

        _isUpdatingSelection = true;
        PlaylistView.SelectedItem = Tracks.Contains(t) ? t : null;
        _isUpdatingSelection = false;

    }

    // ================= PLAY / PAUSE =================
    private void Controller_PlayStateChanged(bool isPlaying)
    {
        var icon = isPlaying ? "pause_btn.png" : "play_btn.png";
        MiniPlayPauseBtn.Source = icon;
    }

    private void OnPlayPauseClicked(object sender, EventArgs e)
        => _controller.TogglePlayPause();

    // ================= NEXT / PREV =================
    private void OnNextClicked(object sender, EventArgs e)
        => _controller.Next();

    private void OnPrevClicked(object sender, EventArgs e)
        => _controller.Prev();

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
    private async void OnMiniPlayerTapped(object sender, TappedEventArgs e)
    {
        var overlay = App.Services.GetRequiredService<IPlayerOverlayService>();
        await overlay.OpenAsync();
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
            _playlistPage = App.Services.GetRequiredService<Views.PlaylistPage>();
            _playlistPage.ApplySearch(SelectedTab == "Playlists" ? _searchQuery : null);
            await Shell.Current.Navigation.PushAsync(_playlistPage);
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

    public void ApplyGlobalSearch(string query)
    {
        _searchQuery = query;
        if (string.IsNullOrWhiteSpace(query))
            _controller.SetFilter(string.Empty);
        else
            _controller.SetFilter(query);

        _playlistPage?.ApplySearch(SelectedTab == "Playlists" ? query : null);
    }

    private async Task ShowSortMenuAsync()
    {
        var choice = await DisplayActionSheet("Сортування", "Скасувати", null,
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