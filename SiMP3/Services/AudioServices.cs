using Plugin.Maui.Audio;

namespace SiMP3.Services
{
    public class AudioService
    {
        private readonly IAudioManager _audioManager;
        private IAudioPlayer _player;

        public AudioService(IAudioManager audioManager)
        {
            _audioManager = audioManager;
        }

        public async Task PlayAsync(string filePath)
        {
            if (_player != null)
            {
                await StopAsync();
            }

            using var fileStream = File.OpenRead(filePath);
            _player = _audioManager.CreatePlayer(fileStream);
            _player.Play();
        }

        public void Pause()
        {
            _player?.Pause();
        }

        public async Task StopAsync()
        {
            if (_player != null)
            {
                _player.Stop();
                await Task.Delay(100);
                _player.Dispose();
                _player = null;
            }
        }

        public bool IsPlaying => _player?.IsPlaying ?? false;
    }
}
