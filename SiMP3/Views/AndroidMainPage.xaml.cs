using Microsoft.Maui.Controls;
using Plugin.Maui.Audio;
using SiMP3.Models;
using SiMP3.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace SiMP3;

public partial class AndroidMainPage : TabbedPage
{
    private readonly MusicController _controller;
    private bool _musicInitialized;

    public ObservableCollection<TrackModel> Tracks => _controller.Tracks;

    public AndroidMainPage()
    {
        InitializeComponent();

        _controller = new MusicController(AudioManager.Current);
        BindingContext = this;
        _musicInitialized = false;

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

#if ANDROID
        // Request permissions and auto-scan for local music asynchronously
        _ = InitializeAndScanMusicAsync();
#endif

        _controller.TrackChanged += Controller_TrackChanged;
        _controller.PlayStateChanged += Controller_PlayStateChanged;
        _controller.ProgressChanged += Controller_ProgressChanged;
        _controller.VolumeStateChanged += Controller_VolumeStateChanged;
    }

#if ANDROID
    /// <summary>
    /// Initializes permissions and scans for music files on Android.
    /// Implements a robust search mechanism across multiple directories.
    /// </summary>
    private async Task InitializeAndScanMusicAsync()
    {
        try
        {
            // Check and request storage permissions
            var permissionStatus = await CheckAndRequestStoragePermissionAsync();

            if (permissionStatus != PermissionStatus.Granted)
            {
                System.Diagnostics.Debug.WriteLine("[AndroidMainPage] Storage permission denied!");
                await DisplayAlert("Дозвіл", "Потрібен дозвіл на доступ до сховища для сканування музики.", "OK");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[AndroidMainPage] Storage permission granted, scanning for music...");

            // Perform music scan
            var musicFiles = await ScanMusicFilesAsync();

            System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] Found {musicFiles.Count()} music files total");

            if (musicFiles.Any())
            {
                _controller.AddTracks(musicFiles);
                _musicInitialized = true;
                System.Diagnostics.Debug.WriteLine("[AndroidMainPage] Music library initialized successfully");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[AndroidMainPage] No music files found during scan");
                await DisplayAlert("Результат", "Музичні файли не знайдені на пристрої.", "OK");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] Error during initialization: {ex}");
            await DisplayAlert("Помилка", $"Помилка сканування: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Checks and requests storage read permissions for Android 13+.
    /// Uses Permissions.Media for Android 13+ and Permissions.StorageRead for older versions.
    /// </summary>
    private async Task<PermissionStatus> CheckAndRequestStoragePermissionAsync()
    {
        try
        {
            // For Android 13+, request READ_MEDIA_AUDIO specifically
            if (DeviceInfo.Version.Major >= 13)
            {
                var mediaStatus = await Permissions.CheckStatusAsync<Permissions.Media>();
                if (mediaStatus != PermissionStatus.Granted)
                {
                    mediaStatus = await Permissions.RequestAsync<Permissions.Media>();
                }
                return mediaStatus;
            }
            else
            {
                // For older Android versions, use StorageRead
                var storageStatus = await Permissions.CheckStatusAsync<Permissions.StorageRead>();
                if (storageStatus != PermissionStatus.Granted)
                {
                    storageStatus = await Permissions.RequestAsync<Permissions.StorageRead>();
                }
                return storageStatus;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] Permission check error: {ex}");
            return PermissionStatus.Denied;
        }
    }

    /// <summary>
    /// Scans for music files using MediaStore (primary method) with file system fallback.
    /// Searches in Music, Downloads, Documents, DCIM, Movies, and root directories.
    /// </summary>
    private async Task<IEnumerable<string>> ScanMusicFilesAsync()
    {
        var allFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Primary method: MediaStore query
        var mediaStoreFiles = await ScanViaMediaStoreAsync();
        if (mediaStoreFiles.Any())
        {
            foreach (var file in mediaStoreFiles)
                allFiles.Add(file);
            System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] MediaStore found {mediaStoreFiles.Count()} files");
            return allFiles;
        }

        // Fallback method: File system scan
        System.Diagnostics.Debug.WriteLine("[AndroidMainPage] MediaStore scan returned no results, using file system fallback");
        var fsFiles = await ScanViaFileSystemAsync();
        foreach (var file in fsFiles)
            allFiles.Add(file);

        System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] File system scan found {fsFiles.Count()} files");
        return allFiles;
    }

    /// <summary>
    /// Scans for music using Android MediaStore (works with scoped storage).
    /// This is the preferred method for Android 11+.
    /// </summary>
    private async Task<List<string>> ScanViaMediaStoreAsync()
    {
        var result = new List<string>();

        try
        {
            var resolver = Android.App.Application.Context.ContentResolver;
            var uri = Android.Provider.MediaStore.Audio.Media.ExternalContentUri;

            string[] projection =
            {
                Android.Provider.MediaStore.MediaColumns.Data,
                Android.Provider.MediaStore.Audio.AudioColumns.IsMusic,
                Android.Provider.MediaStore.Audio.AudioColumns.MimeType
            };

            // Only query for music files
            string selection = Android.Provider.MediaStore.Audio.AudioColumns.IsMusic + " != 0";

            using (var cursor = resolver.Query(uri, projection, selection, null, null))
            {
                if (cursor != null && cursor.Count > 0)
                {
                    int dataIndex = cursor.GetColumnIndex(Android.Provider.MediaStore.MediaColumns.Data);

                    while (cursor.MoveToNext())
                    {
                        try
                        {
                            var path = cursor.GetString(dataIndex);
                            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                            {
                                result.Add(path);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] Error reading MediaStore entry: {ex}");
                        }
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] MediaStore query completed with {result.Count} entries");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] MediaStore query failed: {ex}");
        }

        return result;
    }

    /// <summary>
    /// Scans for music files using file system enumeration.
    /// Covers Music, Downloads, Documents, DCIM, Movies, and external storage root.
    /// </summary>
    private async Task<List<string>> ScanViaFileSystemAsync()
    {
        var result = new List<string>();
        var supportedExtensions = new[] { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".opus", ".wma" };

        // Define search directories with fallback options
        var searchDirectories = new List<string>();

        try
        {
            // Primary directories
            var externalRoot = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath;
            if (!string.IsNullOrEmpty(externalRoot))
                searchDirectories.Add(externalRoot);

            var musicDir = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryMusic)?.AbsolutePath;
            if (!string.IsNullOrEmpty(musicDir))
                searchDirectories.Add(musicDir);

            var downloadDir = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
            if (!string.IsNullOrEmpty(downloadDir))
                searchDirectories.Add(downloadDir);

            var documentsDir = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryDocuments)?.AbsolutePath;
            if (!string.IsNullOrEmpty(documentsDir))
                searchDirectories.Add(documentsDir);

            var dcimDir = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryDcim)?.AbsolutePath;
            if (!string.IsNullOrEmpty(dcimDir))
                searchDirectories.Add(dcimDir);

            var moviesDir = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryMovies)?.AbsolutePath;
            if (!string.IsNullOrEmpty(moviesDir))
                searchDirectories.Add(moviesDir);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] Error enumerating standard directories: {ex}");
        }

        // Scan each directory
        foreach (var directory in searchDirectories.Where(d => !string.IsNullOrEmpty(d)).Distinct())
        {
            await ScanDirectoryAsync(directory, supportedExtensions, result);
        }

        return result;
    }

    /// <summary>
    /// Recursively scans a directory for music files.
    /// </summary>
    private async Task ScanDirectoryAsync(string directoryPath, string[] supportedExtensions, List<string> results)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
                return;

            // Scan files in current directory
            try
            {
                var files = Directory.GetFiles(directoryPath);
                foreach (var file in files)
                {
                    try
                    {
                        var extension = Path.GetExtension(file).ToLowerInvariant();
                        if (supportedExtensions.Contains(extension) && !results.Contains(file, StringComparer.OrdinalIgnoreCase))
                        {
                            results.Add(file);
                        }
                    }
                    catch
                    {
                        // Skip individual files that cause errors
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] Access denied to {directoryPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] Error scanning directory {directoryPath}: {ex}");
            }

            // Recursively scan subdirectories (with depth limit to avoid deep recursion)
            try
            {
                var subdirectories = Directory.GetDirectories(directoryPath);
                foreach (var subdir in subdirectories.Take(20)) // Limit subdirectory depth
                {
                    try
                    {
                        await ScanDirectoryAsync(subdir, supportedExtensions, results);
                    }
                    catch
                    {
                        // Continue scanning other directories
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have permission to access
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] Error scanning subdirectories of {directoryPath}: {ex}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidMainPage] Error in ScanDirectoryAsync for {directoryPath}: {ex}");
        }
    }
#endif

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
        if (e.CurrentSelection?.FirstOrDefault() is TrackModel track)
        {
            _controller.PlayTrack(track);
        }
    }

    // ================= MINI PLAYER TAP =================
    private void OnMiniPlayerTapped(object sender, TappedEventArgs e)
    {
        // Перехід на вкладку Player
        if (Children.Count > 1)
            CurrentPage = Children[1];
    }
}
