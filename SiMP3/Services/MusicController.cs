using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
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
        private AudioVisualizationSampler? _visualizationSampler;

        // new: захист доступу до _player та збереження потоку
        private readonly object _playerLock = new();
        private Stream? _playerStream;

        // "База" усіх треків по абсолютному шляху
        private readonly Dictionary<string, TrackModel> _allTracks =
            new(StringComparer.OrdinalIgnoreCase);

        // lock для словників треків
        private readonly object _tracksLock = new();

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
        private List<TrackModel>? _currentPlaylist;
        private int _currentPlaylistIndex = -1;
        private bool _isPlaying;
        private bool _isShuffle;
        private int _repeatMode; // 0 = off, 1 = all, 2 = one
        private bool _isSeeking;

        private bool _isMuted;
        private double _lastVolumeBeforeMute = 0.7;
        private bool _volumeInternalChange;

        private double _savedPositionSeconds; // для відновлення позиції після старту

        private string _filterQuery = string.Empty;
        private string? _artistFilter;
        private string? _albumFilter;
        private TrackSortMode _sortMode = TrackSortMode.ByTitle;

        // CancellationTokenSource для імпорту
        private CancellationTokenSource? _importCts;

        public int CurrentIndex => _currentIndex;



        public TrackModel? CurrentTrack
        {
            get
            {
                if (_currentPlaylist != null && _currentPlaylistIndex >= 0 && _currentPlaylistIndex < _currentPlaylist.Count)
                    return _currentPlaylist[_currentPlaylistIndex];

                return _currentIndex >= 0 && _currentIndex < Tracks.Count
                    ? Tracks[_currentIndex]
                    : null;
            }
        }

        private void UpdateIsCurrentFlags()
        {
            var currentPath = CurrentTrack?.Path;
            foreach (var t in Tracks)
                t.IsCurrent = (t.Path == currentPath);
        }

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

                    // --- авто-перехід на наступний трек (резервна логіка) ---
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
                // snapshot під lock, щоб уникнути race з паралельним AddTracksAsync
                List<TrackStateDto> snapshotTracks;
                int snapshotCurrentIndex;
                double snapshotPosition;
                double snapshotVolume;
                bool snapshotIsMuted;
                bool snapshotIsShuffle;
                int snapshotRepeatMode;

                lock (_tracksLock)
                {
                    // Order snapshot according to the current Tracks collection to keep indexes consistent
                    var orderedPaths = Tracks
                        .Select(t => t.Path)
                        .ToList();

                    var orderedSet = new HashSet<string>(orderedPaths, StringComparer.OrdinalIgnoreCase);
                    var remainingPaths = _allTracks.Keys
                        .Where(p => !orderedSet.Contains(p))
                        .ToList();

                    snapshotTracks = orderedPaths
                        .Concat(remainingPaths)
                        .Select(p => new TrackStateDto { Path = p })
                        .ToList();

                    snapshotCurrentIndex =
                        _currentIndex >= 0 && _currentIndex < orderedPaths.Count
                            ? _currentIndex
                            : -1;

                    snapshotPosition = _player?.CurrentPosition ?? 0;
                    snapshotVolume = _player != null ? _player.Volume : _lastVolumeBeforeMute;
                    snapshotIsMuted = _isMuted;
                    snapshotIsShuffle = _isShuffle;
                    snapshotRepeatMode = _repeatMode;
                }

                var state = new PlayerStateDto
                {
                    Tracks = snapshotTracks,
                    CurrentIndex = snapshotCurrentIndex,
                    CurrentPositionSeconds = snapshotPosition,
                    Volume = snapshotVolume,
                    IsMuted = snapshotIsMuted,
                    IsShuffle = snapshotIsShuffle,
                    RepeatMode = snapshotRepeatMode
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

                lock (_tracksLock)
                {
                    _allTracks.Clear();
                    _addedAt.Clear();
                }

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

                if (state.Tracks != null &&
                    state.CurrentIndex >= 0 &&
                    state.CurrentIndex < state.Tracks.Count)
                {
                    var path = state.Tracks[state.CurrentIndex].Path;

                    _savedPositionSeconds = state.CurrentPositionSeconds;
                    _isShuffle = state.IsShuffle;
                    _repeatMode = state.RepeatMode;
                    _lastVolumeBeforeMute = state.Volume;
                    _isMuted = state.IsMuted;

                    if (_allTracks.TryGetValue(path, out var track))
                    {
                        ApplyFilterAndSort();
                        _currentIndex = Tracks.IndexOf(track);
                        UpdateIsCurrentFlags();
                        NotifyTrackChanged(track);
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
        public event Action<float[]>? SpectrumFrameAvailable;

        private void NotifyTrackChanged(TrackModel track)
        {
            UpdateCurrentTrackIndicator(track);
            TrackChanged?.Invoke(track);
        }

        private void UpdateCurrentTrackIndicator(TrackModel? current)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                lock (_tracksLock)
                {
                    foreach (var track in _allTracks.Values)
                    {
                        track.IsCurrent = current != null &&
                            string.Equals(track.Path, current.Path, StringComparison.OrdinalIgnoreCase);
                    }
                }
            });
        }

        #endregion

        #region Завантаження треків

        public async Task<IEnumerable<string>> FindLocalMusicAndroidAsync()
        {
#if ANDROID
            var result = new List<string>();

            PermissionStatus status;

            if (DeviceInfo.Version.Major >= 13)
                status = await Permissions.RequestAsync<Permissions.Media>();
            else
                status = await Permissions.RequestAsync<Permissions.StorageRead>();

            if (status != PermissionStatus.Granted)
                return result;

            var resolver = Platform.AppContext.ContentResolver;
            var audioUri = Android.Provider.MediaStore.Audio.Media.ExternalContentUri;

            string[] projection =
            {
                Android.Provider.MediaStore.Audio.Media.InterfaceConsts.Data
            };

            using var cursor = resolver.Query(audioUri, projection, null, null, null);
            if (cursor != null)
            {
                int dataIndex = cursor.GetColumnIndex(Android.Provider.MediaStore.Audio.Media.InterfaceConsts.Data);
                while (cursor.MoveToNext())
                {
                    try
                    {
                        var path = cursor.GetString(dataIndex);
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            result.Add(Path.GetFullPath(path));
                    }
                    catch
                    {
                        // skip corrupted entries
                    }
                }
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
#else
            return Enumerable.Empty<string>();
#endif
        }

        /// <summary>
        /// Додає ОДИН трек у "базу" (без дублікатів по повному шляху).
        /// Якщо трек вже був – повертає існуючу модель.
        /// Параметр refresh дозволяє уникнути багаторазових ApplyFilterAndSort під час пакетного додавання.
        /// </summary>
        public TrackModel AddTrack(string path, bool refresh = true)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            path = Path.GetFullPath(path);

            lock (_tracksLock)
            {
                if (_allTracks.TryGetValue(path, out var existing))
                    return existing;
            }

            TrackModel model = CreateTrackModelFromPath(path);

            bool doRefresh = refresh;

            lock (_tracksLock)
            {
                _allTracks[path] = model;
                _addedAt[path] = model.DateAdded;
            }

            // Перераховуємо "вид" лише якщо потрібно
            if (doRefresh)
                ApplyFilterAndSort();

            // Якщо нічого не було – робимо цей трек поточним
            if (_currentIndex == -1 && Tracks.Count > 0)
            {
                _currentIndex = Tracks.IndexOf(model);
                UpdateIsCurrentFlags();
                NotifyTrackChanged(model);
            }

            return model;
        }

        private TrackModel CreateTrackModelFromPath(string path)
        {
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
                var duration = tagFile.Properties?.Duration ?? TimeSpan.Zero;

                return new TrackModel
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
                return new TrackModel
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
        }

        /// <summary>
        /// Додає набір треків. Дублікат по тому самому шляху просто ігнорується.
        /// Оптимізовано: один виклик ApplyFilterAndSort() після усіх доданих треків.
        /// </summary>
        public void AddTracks(IEnumerable<string> paths)
        {
            // відміняємо попередній імпорт (якщо існує) і стартуємо новий
            lock (_tracksLock)
            {
                if (_importCts != null)
                {
                    try
                    {
                        _importCts.Cancel();
                    }
                    catch { }
                    _importCts.Dispose();
                    _importCts = null;
                }
                _importCts = new CancellationTokenSource();
            }

            _ = AddTracksAsync(paths, _importCts.Token);
        }

        public void CancelImport()
        {
            lock (_tracksLock)
            {
                if (_importCts != null && !_importCts.IsCancellationRequested)
                {
                    Trace.WriteLine($"[{DateTime.UtcNow:O}] Import cancelled by user.");
                    try { _importCts.Cancel(); } catch { }
                    _importCts.Dispose();
                    _importCts = null;
                }
            }
        }

        private async Task AddTracksAsync(IEnumerable<string> paths, CancellationToken ct, int batchSize = 50)
        {
            if (paths == null) return;

            Trace.WriteLine($"[{DateTime.UtcNow:O}] AddTracksAsync start. Count ~ {(paths as ICollection<string>)?.Count ?? -1}");

            var toProcess = paths.Where(p => !string.IsNullOrWhiteSpace(p))
                                 .Select(p => Path.GetFullPath(p))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .Where(fp =>
                                 {
                                     lock (_tracksLock)
                                     {
                                         return !_allTracks.ContainsKey(fp);
                                     }
                                 })
                                 .ToList();

            if (toProcess.Count == 0) return;

            var added = new List<TrackModel>();
            var semaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount));

            var tasks = toProcess.Select(async fp =>
            {
                ct.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    // парсимо метадані в фоновому потоці
                    var model = await Task.Run(() =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return CreateTrackModelFromPath(fp);
                    }, ct).ConfigureAwait(false);

                    lock (_tracksLock)
                    {
                        _allTracks[fp] = model;
                        _addedAt[fp] = model.DateAdded;
                    }
                    lock (added)
                    {
                        added.Add(model);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToArray();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Trace.WriteLine($"[{DateTime.UtcNow:O}] AddTracksAsync cancelled.");
                return;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[{DateTime.UtcNow:O}] AddTracksAsync error: {ex}");
            }

            // Оновлюємо UI один раз (на MainThread)
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ApplyFilterAndSort();
                if (_currentIndex == -1 && Tracks.Count > 0)
                {
                    _currentIndex = 0;
                    UpdateIsCurrentFlags();
                    NotifyTrackChanged(track);
                }
            });

            Trace.WriteLine($"[{DateTime.UtcNow:O}] AddTracksAsync finished. Added {added.Count} tracks.");
        }

        #endregion

        #region Відтворення

        public void PlayTrack(TrackModel track)
        {
            if (track == null)
                return;

            int index = Tracks.IndexOf(track);
            _currentPlaylist = null;
            _currentPlaylistIndex = -1;

            if (index < 0)
                return;

            _currentIndex = index;
            UpdateIsCurrentFlags();
            PlayCurrentInternal();
        }

        public void PlayTrackFromPlaylist(TrackModel track, IList<TrackModel> playlist)
        {
            if (track == null || playlist == null)
                return;

            _currentPlaylist = playlist.Where(t => t != null).ToList();
            _currentPlaylistIndex = _currentPlaylist.FindIndex(t => string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase));
            _currentIndex = Tracks.IndexOf(track);

            if (_currentPlaylistIndex < 0)
            {
                _currentPlaylist = null;
                _currentPlaylistIndex = -1;
                return;
            }

            UpdateIsCurrentFlags();
            PlayCurrentInternal();
        }

        public void PlayPlaylist(IEnumerable<TrackModel> tracks)
        {
            if (tracks == null)
                return;

            var list = tracks.Where(t => t != null).ToList();
            if (list.Count == 0)
                return;

            _currentPlaylist = list;
            _currentPlaylistIndex = 0;
            _currentIndex = -1;
            UpdateIsCurrentFlags();
            PlayCurrentInternal();
        }

        public void TogglePlayPause()
        {
            if (_player == null)
            {
                if (_currentPlaylist != null)
                {
                    if (_currentPlaylistIndex == -1 && _currentPlaylist.Count > 0)
                        _currentPlaylistIndex = 0;
                }
                else if (_currentIndex == -1 && Tracks.Count > 0)
                {
                    _currentIndex = 0;
                }

                UpdateIsCurrentFlags();
                PlayCurrentInternal();
                return;
            }

            if (_isPlaying)
            {
                try { _player.Pause(); } catch { }
                _isPlaying = false;
                _visualizationSampler?.Pause(true);
            }
            else
            {
                try { _player.Play(); } catch { }
                _isPlaying = true;
                _visualizationSampler?.Pause(false);
            }

            PlayStateChanged?.Invoke(_isPlaying);
        }

        private TrackModel? GetCurrentTrackInternal()
        {
            if (_currentPlaylist != null)
            {
                if (_currentPlaylistIndex < 0 || _currentPlaylistIndex >= _currentPlaylist.Count)
                    return null;
                return _currentPlaylist[_currentPlaylistIndex];
            }

            if (_currentIndex < 0 || _currentIndex >= Tracks.Count)
                return null;

            return Tracks[_currentIndex];
        }

        private void PlayCurrentInternal()
        {
            var track = GetCurrentTrackInternal();
            if (track == null)
                return;

            UpdateIsCurrentFlags();
            try
            {
                // Safely dispose previous player and stream, then create new ones
                lock (_playerLock)
                {
                    if (_player != null)
                    {
                        Trace.WriteLine($"[{DateTime.UtcNow:O}] Disposing previous player. Thread:{Environment.CurrentManagedThreadId}\n{Environment.StackTrace}");
                        try { _player.PlaybackEnded -= OnPlayerPlaybackEnded; } catch { }
                        try { _player.Stop(); } catch (Exception ex) { Trace.WriteLine($"Stop error: {ex}"); }
                        try { _player.Dispose(); } catch (Exception ex) { Trace.WriteLine($"Dispose error: {ex}"); }
                        _player = null;
                    }

                    if (_playerStream != null)
                    {
                        try { _playerStream.Dispose(); } catch (Exception ex) { Trace.WriteLine($"Stream dispose error: {ex}"); }
                        _playerStream = null;
                    }

                    _playerStream = File.OpenRead(track.Path);
                    Trace.WriteLine($"[{DateTime.UtcNow:O}] Creating player for {track.Path}");
                    _player = _audioManager.CreatePlayer(_playerStream);
                    _player.Volume = (float)_lastVolumeBeforeMute;
                    try { _player.PlaybackEnded += OnPlayerPlaybackEnded; } catch { }
                }

                StartVisualizationSampler(track.Path);

                // restore saved position if any (outside lock)
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
                NotifyTrackChanged(track);
            }
            catch
            {
                // якщо файл не прочитався – пробуємо перейти на наступний
                try { Next(); } catch { }
            }
        }

        private void StartVisualizationSampler(string path)
        {
            try
            {
                _visualizationSampler?.Dispose();
                _visualizationSampler = new AudioVisualizationSampler(path);
                _visualizationSampler.SpectrumAvailable += data => SpectrumFrameAvailable?.Invoke(data);
                _visualizationSampler.Start();
                if (!_isPlaying)
                    _visualizationSampler.Pause(true);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Visualization sampler error: {ex}");
            }
        }

        public void Next()
        {
            if (_currentPlaylist != null && _currentPlaylist.Count > 0)
            {
                if (_isShuffle)
                    _currentPlaylistIndex = Random.Shared.Next(_currentPlaylist.Count);
                else
                    _currentPlaylistIndex++;

                if (_currentPlaylistIndex >= _currentPlaylist.Count)
                {
                    if (_repeatMode == 1)
                        _currentPlaylistIndex = 0;
                    else
                    {
                        Stop();
                        return;
                    }
                }

                UpdateIsCurrentFlags();
                PlayCurrentInternal();
                return;
            }

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

            UpdateIsCurrentFlags();
            PlayCurrentInternal();
        }

        public void Prev()
        {
            if (_currentPlaylist != null && _currentPlaylist.Count > 0)
            {
                if (_isShuffle)
                    _currentPlaylistIndex = Random.Shared.Next(_currentPlaylist.Count);
                else
                    _currentPlaylistIndex--;

                if (_currentPlaylistIndex < 0)
                {
                    if (_repeatMode == 1)
                        _currentPlaylistIndex = _currentPlaylist.Count - 1;
                    else
                        _currentPlaylistIndex = 0;
                }

                UpdateIsCurrentFlags();
                PlayCurrentInternal();
                return;
            }

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

            UpdateIsCurrentFlags();
            PlayCurrentInternal();
        }

        public void Stop()
        {
            lock (_playerLock)
            {
                if (_player != null)
                {
                    Trace.WriteLine($"[{DateTime.UtcNow:O}] Stop called. Disposing player. Thread:{Environment.CurrentManagedThreadId}\n{Environment.StackTrace}");
                    try { _player.PlaybackEnded -= OnPlayerPlaybackEnded; } catch { }
                    try { _player.Stop(); } catch (Exception ex) { Trace.WriteLine($"Stop error: {ex}"); }
                    try { _player.Dispose(); } catch (Exception ex) { Trace.WriteLine($"Dispose error: {ex}"); }
                    _player = null;
                }

                if (_playerStream != null)
                {
                    try { _playerStream.Dispose(); } catch (Exception ex) { Trace.WriteLine($"Stream dispose error: {ex}"); }
                    _playerStream = null;
                }
            }

            _visualizationSampler?.Dispose();
            _visualizationSampler = null;

            _isPlaying = false;
            PlayStateChanged?.Invoke(false);

            UpdateCurrentTrackIndicator(null);
        }

        public void SetVisualizationActive(bool enabled)
        {
            if (_visualizationSampler == null)
                return;

            _visualizationSampler.Pause(!enabled || !_isPlaying);
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
            try { _player.Seek(newPos); } catch { }

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
            {
                try { _player.Volume = (float)value; } catch { }
            }

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
                {
                    try { _player.Volume = 0f; } catch { }
                }
                VolumeStateChanged?.Invoke(0.0, true);
                _volumeInternalChange = false;
            }
            else
            {
                _isMuted = false;
                double restore = _lastVolumeBeforeMute <= 0 ? 0.6 : _lastVolumeBeforeMute;

                _volumeInternalChange = true;
                if (_player != null)
                {
                    try { _player.Volume = (float)restore; } catch { }
                }
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

        public void SetArtistFilter(string? artist)
        {
            _artistFilter = string.IsNullOrWhiteSpace(artist) ? null : artist;
            ApplyFilterAndSort();
        }

        public void SetAlbumFilter(string? album)
        {
            _albumFilter = string.IsNullOrWhiteSpace(album) ? null : album;
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

        public List<TrackModel> GetAllTracksSnapshot()
        {
            lock (_tracksLock)
            {
                return _allTracks.Values.ToList();
            }
        }

        // Replace ApplyFilterAndSort() with this corrected and thread-safe version:
        private void ApplyFilterAndSort()
        {
            IEnumerable<TrackModel> baseSet;

            // Take snapshot under lock to avoid races with AddTracksAsync
            lock (_tracksLock)
            {
                if (!string.IsNullOrEmpty(ActivePlaylistName))
                {
                    var pl = _playlists.FirstOrDefault(p =>
                        string.Equals(p.Name, ActivePlaylistName, StringComparison.OrdinalIgnoreCase));

                    if (pl != null)
                    {
                        baseSet = pl.TrackPaths
                            .Where(p => _allTracks.TryGetValue(p, out _))
                            .Select(p => _allTracks[p])
                            .ToList();
                    }
                    else
                    {
                        baseSet = _allTracks.Values.ToList();
                    }
                }
                else
                {
                    baseSet = _allTracks.Values.ToList();
                }
            }

            if (!string.IsNullOrWhiteSpace(_artistFilter))
            {
                baseSet = baseSet.Where(t => string.Equals(t.Artist, _artistFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(_albumFilter))
            {
                baseSet = baseSet.Where(t => string.Equals(t.Album, _albumFilter, StringComparison.OrdinalIgnoreCase));
            }

            // Фільтр по тексту (оптимізовано: IndexOf з StringComparison)
            if (!string.IsNullOrEmpty(_filterQuery))
            {
                var q = _filterQuery;
                baseSet = baseSet.Where(t =>
                    (!string.IsNullOrEmpty(t.Title) && t.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(t.Artist) && t.Artist.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(t.Album) && t.Album.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // Сортування
            baseSet = _sortMode switch
            {
                TrackSortMode.ByArtist =>
                    baseSet.OrderBy(t => t.Artist).ThenBy(t => t.Title),
                TrackSortMode.ByDuration =>
                    baseSet.OrderBy(t => t.Duration).ThenBy(t => t.Title),
                TrackSortMode.ByAlbum =>
                    baseSet.OrderBy(t => t.Album).ThenBy(t => t.Title),
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

            UpdateIsCurrentFlags();
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

        private void OnPlayerPlaybackEnded(object? sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_repeatMode == 2)
                    PlayCurrentInternal();
                else
                    Next();
            });
        }
    }

    public enum TrackSortMode
    {
        ByTitle,
        ByArtist,
        ByAlbum,
        ByDuration,
        ByAdded
    }
}