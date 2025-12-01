using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using SiMP3.Models;
using SiMP3.Services;
using System;

namespace SiMP3.Views.Mobile;

public enum MobileScreen
{
    AllTracks,
    Artists,
    Albums,
    Playlists,
    PlaylistEditor
}

public partial class MobileNavigationHost : ContentView
{
    private readonly MusicController _controller;
    private AllTracksView? _allTracksView;
    private ArtistsView? _artistsView;
    private AlbumsView? _albumsView;
    private PlaylistsView? _playlistsView;
    private PlaylistEditorView? _playlistEditorView;
    private string _searchQuery = string.Empty;
    private MobileScreen _currentScreen = MobileScreen.AllTracks;

    public event Action? VisualizationRequested;
    public event Action? AddFilesRequested;

    public MobileNavigationHost()
    {
        InitializeComponent();
        _controller = App.Services.GetRequiredService<MusicController>();
        SwitchTo(MobileScreen.AllTracks);
    }

    public void SetVisualizationLabel(string text)
    {
        VisualizationButton.Text = text;
    }

    public void ApplyGlobalSearch(string query)
    {
        _searchQuery = query;
        switch (_currentScreen)
        {
            case MobileScreen.AllTracks:
                _allTracksView?.ApplySearch(query);
                break;
            case MobileScreen.Artists:
                _artistsView?.ApplySearch(query);
                break;
            case MobileScreen.Albums:
                _albumsView?.ApplySearch(query);
                break;
            case MobileScreen.Playlists:
                _playlistsView?.ApplySearch(query);
                break;
        }
    }

    public void SwitchTo(MobileScreen screen, PlaylistModel? playlist = null)
    {
        _currentScreen = screen;
        UpdateTabState(screen);

        switch (screen)
        {
            case MobileScreen.AllTracks:
                _controller.SetArtistFilter(null);
                _controller.SetAlbumFilter(null);
                _allTracksView ??= new AllTracksView();
                ContentHost.Content = _allTracksView;
                _allTracksView.ApplySearch(_searchQuery);
                break;
            case MobileScreen.Artists:
                _controller.SetAlbumFilter(null);
                _artistsView ??= new ArtistsView();
                ContentHost.Content = _artistsView;
                _artistsView.ApplySearch(_searchQuery);
                break;
            case MobileScreen.Albums:
                _controller.SetArtistFilter(null);
                _albumsView ??= new AlbumsView();
                ContentHost.Content = _albumsView;
                _albumsView.ApplySearch(_searchQuery);
                break;
            case MobileScreen.Playlists:
                _controller.SetArtistFilter(null);
                _controller.SetAlbumFilter(null);
                _controller.SetFilter(string.Empty);
                _playlistsView ??= new PlaylistsView();
                _playlistsView.PlaylistSelected -= OnPlaylistSelected;
                _playlistsView.PlaylistSelected += OnPlaylistSelected;
                ContentHost.Content = _playlistsView;
                _playlistsView.ApplySearch(_searchQuery);
                break;
            case MobileScreen.PlaylistEditor:
                if (playlist == null)
                    return;

                _playlistEditorView ??= new PlaylistEditorView();
                _playlistEditorView.CloseRequested -= OnPlaylistEditorCloseRequested;
                _playlistEditorView.CloseRequested += OnPlaylistEditorCloseRequested;
                _playlistEditorView.LoadPlaylist(playlist);
                ContentHost.Content = _playlistEditorView;
                break;
        }
    }

    private void OnPlaylistSelected(PlaylistModel playlist)
    {
        SwitchTo(MobileScreen.PlaylistEditor, playlist);
    }

    private void OnPlaylistEditorCloseRequested()
    {
        SwitchTo(MobileScreen.Playlists);
    }

    private void UpdateTabState(MobileScreen screen)
    {
        AllTab.TextColor = screen == MobileScreen.AllTracks ? (Color)Resources["PrimaryTextColor"] : (Color)Resources["MutedTextColor"];
        ArtistsTab.TextColor = screen == MobileScreen.Artists ? (Color)Resources["PrimaryTextColor"] : (Color)Resources["MutedTextColor"];
        AlbumsTab.TextColor = screen == MobileScreen.Albums ? (Color)Resources["PrimaryTextColor"] : (Color)Resources["MutedTextColor"];
        PlaylistsTab.TextColor = screen == MobileScreen.Playlists ? (Color)Resources["PrimaryTextColor"] : (Color)Resources["MutedTextColor"];
    }

    private void OnAllClicked(object sender, EventArgs e) => SwitchTo(MobileScreen.AllTracks);

    private void OnArtistsClicked(object sender, EventArgs e) => SwitchTo(MobileScreen.Artists);

    private void OnAlbumsClicked(object sender, EventArgs e) => SwitchTo(MobileScreen.Albums);

    private void OnPlaylistsClicked(object sender, EventArgs e) => SwitchTo(MobileScreen.Playlists);

    private void OnVisualizationClicked(object sender, EventArgs e) => VisualizationRequested?.Invoke();

    private void OnAddFilesClicked(object sender, EventArgs e) => AddFilesRequested?.Invoke();
}
