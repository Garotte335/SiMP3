using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Plugin.Maui.Audio;
using SiMP3.Models;

namespace SiMP3.Services
{
    /// <summary>
    /// Централізований контролер програвача.
    /// </summary>
    public class MusicController
    {
        private readonly IAudioManager _audioManager;
        private IAudioPlayer? _player;

        // "База" усіх треків по абсолютному шляху
        private readonly Dictionary<string, TrackModel> _allTracks =
            new(StringComparer.OrdinalIgnoreCase);

        // Для сортування "за датою додавання"
        private readonly Dictionary<string, DateTime> _addedAt =
            new(StringComparer.OrdinalIgnoreCase);

        // Те, до чого прив'язаний XAML (CollectionView)
        public ObservableCollection<TrackModel> Tracks { get; } = new();

        // Плейлисти користувача
        private readonly ObservableCollection<PlaylistDto> _playlists = new();
        public ReadOnlyObservableCollection<PlaylistDto> Playlists { get; }

        public string? ActivePlaylistName { get; private set; }

        private int _currentIndex = -1;
        private bool _isPlaying;
        private bool _isShuffle;
        private int _repeatMode; // 0 = off, 1 = all, 2 = one
        private bool _isSeeking;

        private bool _isMuted;
        private double _lastVolumeBeforeMute = 0.7;
        private bool _volumeInternalChange;

        private double _savedPositionSeconds; // для відновлення позиції після старту

        private string _filterQuery = string.Empty;
        private TrackSortMode _sortMode = TrackSortMode.ByTitle;

        public int CurrentIndex => _currentIndex;

        public TrackModel? CurrentTrack =>
            (_currentIndex >= 0 && _currentIndex < Tracks.Count ? Tracks[_currentIndex] : null);

        public bool IsPlaying => _isPlaying;
        public bool IsShuffle => _isShuffle;
        public int RepeatMode => _repeatMode;

        public MusicController(IAudioManager audioManager)
        {
            _audioManager = audioManager;
            Playlists = new ReadOnlyObservableCollection<PlaylistDto>(_playlists);

            Device.StartTimer(TimeSpan.FromMilliseconds(500), () =>
            {
                if (_player == null || !_isPlaying || _isSeeking)
                    return true;

                try
                {
                    double pos = _player.CurrentPosition;
                    double dur = _player.Duration;

                    if (dur > 0)
                    {
                        double progress = pos / dur;

                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            ProgressChanged?.Invoke(
                                TimeSpan.FromSeconds(pos),
                                TimeSpan.FromSeconds(dur),
                                progress);
                        });
                    }

                    // --- авто-перехід на наступний трек ---
                    if (dur > 0 && dur - pos < 0.25)
                    {
                        if (_repeatMode == 2)
                            PlayCurrentInternal();
                        else
                            Next();
                    }
                }
                catch
                {
                    // таймер не повинен падати
                }

                return true;
            });
        }

        #region ЗБЕРЕЖЕННЯ / ВІДНОВЛЕННЯ СТАНУ

        public async Task SaveStateAsync()
        {
            try
            {
                var state = new PlayerStateDto
                {
                    Tracks = _allTracks.Values
                        .Select(t => new TrackStateDto { Path = t.Path })
                        .ToList(),

                    CurrentIndex = CurrentTrack != null
                        ? _allTracks.Keys.ToList().IndexOf(CurrentTrack.Path)
                        : -1,

                    CurrentPositionSeconds = _player?.CurrentPosition ?? 0,
                    Volume = _player != null ? _player.Volume : _lastVolumeBeforeMute,
                    IsMuted = _isMuted,
                    IsShuffle = _isShuffle,
                    RepeatMode = _repeatMode
                };

                await PlayerStateStore.SaveAsync(state);
                await PlayerStateStore.SavePlaylistsAsync(_playlists.ToList());
            }
            catch
            {
                // падіння збереження не критичне
            }
        }

        public async Task LoadStateAsync()
        {
            try
            {
                var state = await PlayerStateStore.LoadAsync();
                var playlists = await PlayerStateStore.LoadPlaylistsAsync();

                _allTracks.Clear();
                _addedAt.Clear();
                Tracks.Clear();
                _playlists.Clear();

                if (playlists != null)
                {
                    foreach (var pl in playlists)
                        _playlists.Add(pl);
                }

                if (state == null)
                    return;

                if (state.Tracks != null)
                {
                    // Заново читаємо метадані, але без дублікатів
                    AddTracks(state.Tracks.Select(t => t.Path));
                }

                if (state.CurrentIndex >= 0 && state.CurrentIndex < _allTracks.Count)
                {
                    var allPaths = _allTracks.Keys.ToList();
                    var path = allPaths[state.CurrentIndex];

                    _savedPositionSeconds = state.CurrentPositionSeconds;
                    _isShuffle = state.IsShuffle;
                    _repeatMode = state.RepeatMode;
                    _lastVolumeBeforeMute = state.Volume;
                    _isMuted = state.IsMuted;

                    if (_allTracks.TryGetValue(path, out var track))
                    {
                        ApplyFilterAndSort();
                        _currentIndex = Tracks.IndexOf(track);
                        TrackChanged?.Invoke(track);
                    }
                }
                else
                {
                    ApplyFilterAndSort();
                }
            }
            catch
            {
                // якщо з JSON щось не так – стартуємо з чистого стану
            }
        }

        #endregion

        #region Події для UI

        public event Action<TrackModel>? TrackChanged;
        public event Action<bool>? PlayStateChanged;
        public event Action<TimeSpan, TimeSpan, double>? ProgressChanged;
        public event Action<double, bool>? VolumeStateChanged;

        #endregion

        #region Завантаження треків

        public IEnumerable<string> FindLocalMusicAndroid()
        {
#if ANDROID
            var result = new List<string>();

            string[] roots =
            {
                Android.OS.Environment.GetExternalStoragePublicDirectory(
                    Android.OS.Environment.DirectoryMusic)?.AbsolutePath,
                Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath
            };

            string[] exts = { ".mp3", ".wav", ".ogg", ".flac", ".m4a" };

            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
            {
                if (!Directory.Exists(root))
                    continue;

                try
                {
                    foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (exts.Contains(ext))
                            result.Add(f);
                    }
                }
                catch
                {
                    // якщо немає прав до якоїсь папки – просто скіпаємо
                }
            }

            return result;
#else
            return Enumerable.Empty<string>();
#endif
        }

        /// <summary>
        /// Додає ОДИН трек у "базу" (без дублікатів по повному шляху).
        /// Якщо трек уже був – повертає існуючу модель.
        /// </summary>
        public TrackModel AddTrack(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            path = Path.GetFullPath(path);

            if (_allTracks.TryGetValue(path, out var existing))
                return existing;

            TrackModel model;

            try
            {
                using var tagFile = TagLib.File.Create(path);

                ImageSource coverImg = "default_cover.png";
                if (tagFile.Tag?.Pictures?.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    coverImg = ImageSource.FromStream(() => new MemoryStream(pic.Data.Data));
                }

                string title = string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                    ? Path.GetFileNameWithoutExtension(path)
                    : tagFile.Tag.Title;

                string artist = tagFile.Tag.FirstPerformer ?? "Unknown Artist";
                string album = tagFile.Tag.Album ?? "Unknown Album";
                string genre = tagFile.Tag.FirstGenre ?? "Unknown Genre";
                string year = tagFile.Tag.Year == 0 ? "Unknown Year" : tagFile.Tag.Year.ToString();
                uint trackNum = tagFile.Tag.Track;

                var duration = tagFile.Properties.Duration;

                model = new TrackModel
                {
                    Path = path,
                    Title = title,
                    Artist = artist,
                    Album = album,
                    Genre = genre,
                    Year = year,
                    TrackNumber = trackNum,
                    Duration = duration,
                    DurationString = duration.ToString(@"m\:ss"),
                    Cover = coverImg,
                    DateAdded = DateTime.UtcNow
                };
            }
            catch
            {
                model = new TrackModel
                {
                    Path = path,
                    Title = Path.GetFileNameWithoutExtension(path),
                    Artist = "Unknown Artist",
                    Duration = TimeSpan.Zero,
                    DurationString = "0:00",
                    Cover = "default_cover.png",
                    DateAdded = DateTime.UtcNow
                };
            }

            _allTracks[path] = model;
            _addedAt[path] = model.DateAdded;

            // Перераховуємо "вид" з урахуванням фільтрів/сортування
            ApplyFilterAndSort();

            // Якщо нічого не було – робимо цей трек поточним
            if (_currentIndex == -1 && Tracks.Count > 0)
            {
                _currentIndex = Tracks.IndexOf(model);
                TrackChanged?.Invoke(model);
            }

            return model;
        }

        /// <summary>
        /// Додає набір треків. Дублікат по тому самому шляху просто ігнорується.
        /// </summary>
        public void AddTracks(IEnumerable<string> paths)
        {
            if (paths == null) return;

            foreach (var path in paths)
            {
                try
                {
                    AddTrack(path);
                }
                catch
                {
                    // один із файлів міг не прочитатися – ігноруємо
                }
            }
        }

        #endregion

        #region Відтворення

        public void PlayTrack(TrackModel track)
        {
            if (track == null)
                return;

            int index = Tracks.IndexOf(track);
            if (index < 0)
                return;

            _currentIndex = index;
            PlayCurrentInternal();
        }

        public void TogglePlayPause()
        {
            if (_player == null)
            {
                if (_currentIndex == -1 && Tracks.Count > 0)
                    _currentIndex = 0;

                PlayCurrentInternal();
                return;
            }

            if (_isPlaying)
            {
                _player.Pause();
                _isPlaying = false;
            }
            else
            {
                _player.Play();
                _isPlaying = true;
            }

            PlayStateChanged?.Invoke(_isPlaying);
        }

        private void PlayCurrentInternal()
        {
            if (_currentIndex < 0 || _currentIndex >= Tracks.Count)
                return;

            var track = Tracks[_currentIndex];

            try
            {
                _player?.Stop();
                _player?.Dispose();

                var stream = File.OpenRead(track.Path);
                _player = _audioManager.CreatePlayer(stream);
                _player.Volume = (float)_lastVolumeBeforeMute;

                // якщо є збережена позиція – відновлюємо один раз
                if (_savedPositionSeconds > 0)
                {
                    try
                    {
                        var dur = _player.Duration;
                        var target = Math.Min(_savedPositionSeconds, Math.Max(0, dur - 0.5));
                        if (target > 0 && dur > 0)
                            _player.Seek(target);
                    }
                    catch { }

                    _savedPositionSeconds = 0;
                }

                _player.Play();
                _isPlaying = true;
                PlayStateChanged?.Invoke(true);
                TrackChanged?.Invoke(track);
            }
            catch
            {
                // якщо файл не прочитався – пробуємо перейти на наступний
                Next();
            }
        }

        public void Next()
        {
            if (Tracks.Count == 0)
                return;

            if (_isShuffle)
                _currentIndex = Random.Shared.Next(Tracks.Count);
            else
                _currentIndex++;

            if (_currentIndex >= Tracks.Count)
            {
                if (_repeatMode == 1)
                    _currentIndex = 0;
                else
                {
                    Stop();
                    return;
                }
            }

            PlayCurrentInternal();
        }

        public void Prev()
        {
            if (Tracks.Count == 0)
                return;

            if (_isShuffle)
                _currentIndex = Random.Shared.Next(Tracks.Count);
            else
                _currentIndex--;

            if (_currentIndex < 0)
            {
                if (_repeatMode == 1)
                    _currentIndex = Tracks.Count - 1;
                else
                    _currentIndex = 0;
            }

            PlayCurrentInternal();
        }

        public void Stop()
        {
            _player?.Stop();
            _isPlaying = false;
            PlayStateChanged?.Invoke(false);
        }

        #endregion

        #region Режими (shuffle / repeat)

        public void ToggleShuffle() => _isShuffle = !_isShuffle;

        public void CycleRepeatMode()
        {
            _repeatMode++;
            if (_repeatMode > 2)
                _repeatMode = 0;
        }

        #endregion

        #region Seek

        public async void SeekRelative(double relative)
        {
            if (_player == null)
                return;

            double dur = _player.Duration;
            if (dur <= 0)
                return;

            _isSeeking = true;

            double newPos = relative * dur;
            _player.Seek(newPos);

            await Task.Delay(150);
            _isSeeking = false;
        }

        #endregion

        #region Volume

        public void SetVolume(double value)
        {
            if (_volumeInternalChange)
                return;

            if (_isMuted && value > 0)
                _isMuted = false;

            _lastVolumeBeforeMute = value;

            if (_player != null)
                _player.Volume = (float)value;

            VolumeStateChanged?.Invoke(value, _isMuted);
        }

        public void ToggleMute()
        {
            if (!_isMuted)
            {
                _isMuted = true;
                _lastVolumeBeforeMute = Math.Max(_lastVolumeBeforeMute, 0);

                _volumeInternalChange = true;
                if (_player != null)
                    _player.Volume = 0f;
                VolumeStateChanged?.Invoke(0.0, true);
                _volumeInternalChange = false;
            }
            else
            {
                _isMuted = false;
                double restore = _lastVolumeBeforeMute <= 0 ? 0.6 : _lastVolumeBeforeMute;

                _volumeInternalChange = true;
                if (_player != null)
                    _player.Volume = (float)restore;
                VolumeStateChanged?.Invoke(restore, false);
                _volumeInternalChange = false;
            }
        }

        #endregion

        #region Фільтрація / сортування

        public void SetFilter(string? query)
        {
            _filterQuery = query?.Trim() ?? string.Empty;
            ApplyFilterAndSort();
        }

        public void SetSortMode(TrackSortMode mode)
        {
            _sortMode = mode;
            ApplyFilterAndSort();
        }

        public void SetActivePlaylist(string? name)
        {
            ActivePlaylistName = string.IsNullOrWhiteSpace(name) ? null : name;
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            IEnumerable<TrackModel> baseSet;

            if (!string.IsNullOrEmpty(ActivePlaylistName))
            {
                var pl = _playlists.FirstOrDefault(p =>
                    string.Equals(p.Name, ActivePlaylistName, StringComparison.OrdinalIgnoreCase));

                if (pl != null)
                {
                    baseSet = pl.TrackPaths
                        .Where(p => _allTracks.TryGetValue(p, out _))
                        .Select(p => _allTracks[p]);
                }
                else
                {
                    baseSet = _allTracks.Values;
                }
            }
            else
            {
                baseSet = _allTracks.Values;
            }

            // Фільтр по тексту
            if (!string.IsNullOrEmpty(_filterQuery))
            {
                var q = _filterQuery.ToLowerInvariant();
                baseSet = baseSet.Where(t =>
                    (!string.IsNullOrEmpty(t.Title) && t.Title.ToLowerInvariant().Contains(q)) ||
                    (!string.IsNullOrEmpty(t.Artist) && t.Artist.ToLowerInvariant().Contains(q)) ||
                    (!string.IsNullOrEmpty(t.Album) && t.Album.ToLowerInvariant().Contains(q)));
            }

            // Сортування
            baseSet = _sortMode switch
            {
                TrackSortMode.ByArtist =>
                    baseSet.OrderBy(t => t.Artist).ThenBy(t => t.Title),
                TrackSortMode.ByDuration =>
                    baseSet.OrderBy(t => t.Duration).ThenBy(t => t.Title),
                TrackSortMode.ByAdded =>
                    baseSet.OrderBy(t => _addedAt.TryGetValue(t.Path, out var dt) ? dt : DateTime.MinValue),
                _ => // ByTitle
                    baseSet.OrderBy(t => t.Title)
            };

            var currentPath = CurrentTrack?.Path;

            Tracks.Clear();
            foreach (var t in baseSet)
                Tracks.Add(t);

            if (currentPath != null)
            {
                _currentIndex = -1;
                for (int i = 0; i < Tracks.Count; i++)
                {
                    if (string.Equals(Tracks[i].Path, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentIndex = i;
                        break;
                    }
                }
            }
            else if (Tracks.Count == 0)
            {
                _currentIndex = -1;
            }
        }

        #endregion

        #region Плейлисти

        public PlaylistDto CreatePlaylist(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (_playlists.Any(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return _playlists.First(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            var pl = new PlaylistDto { Name = name };
            _playlists.Add(pl);
            return pl;
        }

        public void RenamePlaylist(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return;

            var pl = _playlists.FirstOrDefault(p =>
                string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase));

            if (pl == null)
                return;

            pl.Name = newName;
        }

        public void DeletePlaylist(string name)
        {
            var pl = _playlists.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (pl == null)
                return;

            _playlists.Remove(pl);

            if (string.Equals(ActivePlaylistName, name, StringComparison.OrdinalIgnoreCase))
            {
                ActivePlaylistName = null;
                ApplyFilterAndSort();
            }
        }

        public void AddTrackToPlaylist(string playlistName, TrackModel track)
        {
            if (track == null || string.IsNullOrWhiteSpace(playlistName))
                return;

            var pl = _playlists.FirstOrDefault(p =>
                string.Equals(p.Name, playlistName, StringComparison.OrdinalIgnoreCase))
                     ?? CreatePlaylist(playlistName);

            if (!pl.TrackPaths.Contains(track.Path, StringComparer.OrdinalIgnoreCase))
            {
                pl.TrackPaths.Add(track.Path);
            }
        }

        public void RemoveTrackFromPlaylist(string playlistName, TrackModel track)
        {
            if (track == null || string.IsNullOrWhiteSpace(playlistName))
                return;

            var pl = _playlists.FirstOrDefault(p =>
                string.Equals(p.Name, playlistName, StringComparison.OrdinalIgnoreCase));

            if (pl == null)
                return;

            pl.TrackPaths.RemoveAll(p =>
                string.Equals(p, track.Path, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }

    public enum TrackSortMode
    {
        ByTitle,
        ByArtist,
        ByDuration,
        ByAdded
    }
}
