using System.Collections.ObjectModel;
using System.Text.Json;
using System.Linq;
using Microsoft.Maui.Storage;
using SiMP3.Models;

namespace SiMP3.Services
{
    public class PlaylistService
    {
        private const string FileName = "playlists.json";
        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private bool _initialized;
        public ObservableCollection<PlaylistModel> Playlists { get; } = new();

        public PlaylistService()
        {
            _filePath = Path.Combine(FileSystem.AppDataDirectory, FileName);
        }

        public async Task EnsureLoadedAsync()
        {
            if (_initialized)
                return;

            await LoadPlaylistsAsync();
            _initialized = true;
        }

        public async Task LoadPlaylistsAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return;

                var json = await File.ReadAllTextAsync(_filePath);
                var data = JsonSerializer.Deserialize<List<SerializablePlaylist>>(json, _jsonOptions);

                Playlists.Clear();
                if (data != null)
                {
                    foreach (var pl in data)
                    {
                        var playlist = new PlaylistModel
                        {
                            Name = pl.Name,
                            Tracks = new ObservableCollection<TrackModel>(pl.Tracks.Select(ToTrackModel))
                        };
                        Playlists.Add(playlist);
                    }
                }
            }
            catch
            {
                // ignore corrupted store and start fresh
                Playlists.Clear();
            }
        }

        private static TrackModel ToTrackModel(SerializableTrack track)
        {
            return new TrackModel
            {
                Path = track.Path,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                Genre = track.Genre,
                Year = track.Year,
                TrackNumber = track.TrackNumber,
                Duration = TimeSpan.FromTicks(track.DurationTicks),
                DurationString = track.DurationString,
                Cover = "default_cover.png",
                DateAdded = track.DateAdded
            };
        }

        public async Task SavePlaylistsAsync()
        {
            try
            {
                var serializable = Playlists.Select(pl => new SerializablePlaylist
                {
                    Name = pl.Name,
                    Tracks = pl.Tracks.Select(t => new SerializableTrack(t)).ToList()
                }).ToList();

                Directory.CreateDirectory(FileSystem.AppDataDirectory);
                var json = JsonSerializer.Serialize(serializable, _jsonOptions);
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch
            {
                // saving errors are non-critical
            }
        }

        public Task SaveAsync() => SavePlaylistsAsync();

        public async Task<PlaylistModel> CreateAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            var existing = Playlists.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                return existing;

            var playlist = new PlaylistModel { Name = name };
            Playlists.Add(playlist);
            await SavePlaylistsAsync();
            return playlist;
        }

        public async Task RenameAsync(PlaylistModel playlist, string newName)
        {
            if (playlist == null || string.IsNullOrWhiteSpace(newName))
                return;

            playlist.Name = newName;
            await SavePlaylistsAsync();
        }

        public async Task DeleteAsync(PlaylistModel? playlist)
        {
            if (playlist == null)
                return;

            Playlists.Remove(playlist);
            await SavePlaylistsAsync();
        }

        public async Task AddTrackAsync(PlaylistModel playlist, TrackModel track)
        {
            if (playlist == null || track == null)
                return;

            if (!playlist.Tracks.Any(t => string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase)))
            {
                playlist.Tracks.Add(track);
                await SavePlaylistsAsync();
            }
        }

        public async Task RemoveTrackAsync(PlaylistModel playlist, TrackModel track)
        {
            if (playlist == null || track == null)
                return;

            var existing = playlist.Tracks.FirstOrDefault(t =>
                string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                playlist.Tracks.Remove(existing);
                await SavePlaylistsAsync();
            }
        }

        public async Task UpdatePlaylistAsync(PlaylistModel playlist)
        {
            if (playlist == null)
                return;

            await SavePlaylistsAsync();
        }

        public async Task ClearAll()
        {
            Playlists.Clear();
            try
            {
                if (File.Exists(_filePath))
                    File.Delete(_filePath);
            }
            catch
            {
                // ignore cleanup failures
            }
        }

        public async Task AddToFavorites(TrackModel track)
        {
            if (track == null)
                return;

            await EnsureLoadedAsync();
            var favorites = await CreateAsync("Favorites");
            await AddTrackAsync(favorites, track);
        }

        public async Task RemoveFromFavorites(TrackModel track)
        {
            if (track == null)
                return;

            await EnsureLoadedAsync();
            var favorites = Playlists.FirstOrDefault(p => string.Equals(p.Name, "Favorites", StringComparison.OrdinalIgnoreCase));
            if (favorites != null)
            {
                await RemoveTrackAsync(favorites, track);
            }
        }

        public bool IsFavorite(TrackModel? track)
        {
            if (track == null)
                return false;

            var favorites = Playlists.FirstOrDefault(p => string.Equals(p.Name, "Favorites", StringComparison.OrdinalIgnoreCase));
            return favorites?.Tracks.Any(t => string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private class SerializablePlaylist
        {
            public string Name { get; set; } = string.Empty;

            public List<SerializableTrack> Tracks { get; set; } = new();
        }

        private class SerializableTrack
        {
            public SerializableTrack()
            {
            }

            public SerializableTrack(TrackModel model)
            {
                Path = model.Path;
                Title = model.Title;
                Artist = model.Artist;
                Album = model.Album;
                Genre = model.Genre;
                Year = model.Year;
                TrackNumber = model.TrackNumber;
                DurationTicks = model.Duration.Ticks;
                DurationString = model.DurationString;
                DateAdded = model.DateAdded;
            }

            public string Path { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
            public string Album { get; set; } = string.Empty;
            public string Genre { get; set; } = string.Empty;
            public string Year { get; set; } = string.Empty;
            public uint TrackNumber { get; set; }
            public long DurationTicks { get; set; }
            public string DurationString { get; set; } = "0:00";
            public DateTime DateAdded { get; set; } = DateTime.UtcNow;
        }
    }
}