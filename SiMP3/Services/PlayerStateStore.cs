using System.Text.Json;

namespace SiMP3.Services
{
    /// <summary>
    /// Збереження стану програвача та плейлистів у JSON
    /// в локальній папці програми (FileSystem.AppDataDirectory).
    /// </summary>
    public static class PlayerStateStore
    {
        private const string PlayerStateFileName = "player_state.json";
        private const string PlaylistsFileName = "playlists.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static string PlayerStatePath =>
            Path.Combine(FileSystem.AppDataDirectory, PlayerStateFileName);

        private static string PlaylistsPath =>
            Path.Combine(FileSystem.AppDataDirectory, PlaylistsFileName);

        // ---------- СТАН ПРОГРАВАЧА ----------

        public static async Task SaveAsync(PlayerStateDto state)
        {
            try
            {
                var json = JsonSerializer.Serialize(state, JsonOptions);
                Directory.CreateDirectory(FileSystem.AppDataDirectory);
                await File.WriteAllTextAsync(PlayerStatePath, json);
            }
            catch
            {
                // ігноруємо – збереження не критичне
            }
        }

        public static async Task<PlayerStateDto?> LoadAsync()
        {
            try
            {
                if (!File.Exists(PlayerStatePath))
                    return null;

                var json = await File.ReadAllTextAsync(PlayerStatePath);
                return JsonSerializer.Deserialize<PlayerStateDto>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        // ---------- ПЛЕЙЛИСТИ ----------

        public static async Task SavePlaylistsAsync(List<PlaylistDto> playlists)
        {
            try
            {
                var json = JsonSerializer.Serialize(playlists ?? new List<PlaylistDto>(), JsonOptions);
                Directory.CreateDirectory(FileSystem.AppDataDirectory);
                await File.WriteAllTextAsync(PlaylistsPath, json);
            }
            catch
            {
            }
        }

        public static async Task<List<PlaylistDto>?> LoadPlaylistsAsync()
        {
            try
            {
                if (!File.Exists(PlaylistsPath))
                    return new List<PlaylistDto>();

                var json = await File.ReadAllTextAsync(PlaylistsPath);
                return JsonSerializer.Deserialize<List<PlaylistDto>>(json, JsonOptions)
                       ?? new List<PlaylistDto>();
            }
            catch
            {
                return new List<PlaylistDto>();
            }
        }
    }

    /// <summary>
    /// DTO для збереження стану програвача.
    /// </summary>
    public class PlayerStateDto
    {
        public List<TrackStateDto>? Tracks { get; set; }

        public int CurrentIndex { get; set; } = -1;

        public double CurrentPositionSeconds { get; set; }

        public double Volume { get; set; } = 0.7;

        public bool IsMuted { get; set; }

        public bool IsShuffle { get; set; }

        public int RepeatMode { get; set; }
    }

    public class TrackStateDto
    {
        public string Path { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO одного плейлиста (ім'я + шляхи до треків).
    /// </summary>
    public class PlaylistDto
    {
        public string Name { get; set; } = string.Empty;

        public List<string> TrackPaths { get; set; } = new();
    }
}
