using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace SiMP3.Services
{
    public interface IPlayerOverlayService
    {
        void Register(Grid overlay);
        void Show();
        void Hide();
    }

    public class PlayerOverlayService : IPlayerOverlayService
    {
        private Grid? _overlay;

        public void Register(Grid overlay)
        {
            _overlay = overlay;
        }

        public void Show()
        {
            if (_overlay == null)
                return;

            MainThread.BeginInvokeOnMainThread(() => _overlay.IsVisible = true);
        }

        public void Hide()
        {
            if (_overlay == null)
                return;

            MainThread.BeginInvokeOnMainThread(() => _overlay.IsVisible = false);
        }
    }
}