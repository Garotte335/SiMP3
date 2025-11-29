using System;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using SiMP3.Views;

namespace SiMP3.Services;

public interface IPlayerOverlayService
{
    bool IsOverlayOpen { get; }

    Task OpenAsync();

    Task CloseAsync();

    void AttachShell(Shell shell);

    void HandleNavigating(Shell shell, ShellNavigatingEventArgs args);

    void HandleNavigated(Page? currentPage);
}

public class PlayerOverlayService : IPlayerOverlayService
{
    private Shell? _shell;
    private FullPlayerPage? _currentOverlay;
    private bool _isClosing;
    private bool _isReNavigating;

    public bool IsOverlayOpen => _currentOverlay != null;

    public void AttachShell(Shell shell)
    {
        _shell = shell;
    }

    public void HandleNavigated(Page? currentPage)
    {
        if (currentPage is FullPlayerPage fullPlayerPage)
        {
            _currentOverlay = fullPlayerPage;
            return;
        }

        _currentOverlay = null;
    }

    public void HandleNavigating(Shell shell, ShellNavigatingEventArgs args)
    {
        _shell ??= shell;

        if (args.Target == null)
            return;

        var route = args.Target.Location.ToString();
        if (IsFullPlayerRoute(route))
        {
            if (IsOverlayOpen)
            {
                args.Cancel();
            }
            return;
        }

        if (IsOverlayOpen)
        {
            args.Cancel();
            _ = CloseAndNavigateAsync(route);
        }
    }

    public async Task OpenAsync()
    {
        if (_shell == null)
            return;

        if (IsOverlayOpen || _isClosing)
            return;

        if (_shell.CurrentPage is SettingsPage)
            return;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await _shell.GoToAsync(nameof(FullPlayerPage), false);
        });
    }

    public async Task CloseAsync()
    {
        if (_shell == null || _currentOverlay == null || _isClosing)
            return;

        _isClosing = true;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await _currentOverlay.AnimateOutAsync();
                if (_shell.Navigation.NavigationStack.LastOrDefault() == _currentOverlay)
                {
                    await _shell.Navigation.PopAsync(false);
                }
            });
        }
        finally
        {
            _isClosing = false;
            _currentOverlay = null;
        }
    }
        private async Task CloseAndNavigateAsync(string route)
        {
        if (_shell == null || _isReNavigating)
            return;

        _isReNavigating = true;
        try
        {
            await CloseAsync();
            await MainThread.InvokeOnMainThreadAsync(() => _shell!.GoToAsync(route));
        }
        finally
        {
            _isReNavigating = false;
        }
    }

    private static bool IsFullPlayerRoute(string route)
        => route.Contains(nameof(FullPlayerPage), StringComparison.OrdinalIgnoreCase);
    }