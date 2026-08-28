using Photino.NET;
using Pane.Platform;

namespace Pane.App;

/// <summary>
/// IWindowController backed by a Photino PhotinoWindow.
/// Photino does not have a native Show/Hide API on all platforms;
/// we use SetMinimized(bool) to toggle visibility (minimized = hidden from user).
/// </summary>
internal sealed class AppWindowController : IWindowController
{
    private readonly PhotinoWindow _window;
    private volatile bool _isVisible = true;

    public AppWindowController(PhotinoWindow window)
    {
        _window = window;
    }

    public bool IsVisible => _isVisible;

    public void Show()
    {
        _isVisible = true;
        _window.SetMinimized(false);
    }

    public void Hide()
    {
        _isVisible = false;
        _window.SetMinimized(true);
    }

    public void ToggleVisible()
    {
        if (_isVisible)
            Hide();
        else
            Show();
    }
}
