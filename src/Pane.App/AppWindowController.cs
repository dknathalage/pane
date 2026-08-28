using Photino.NET;
using Pane.Core;

namespace Pane.App;

/// <summary>
/// IWindowController backed by a Photino window, registered in DI and attached
/// to the real window after Build(). Hide moves the window off-screen so it
/// disappears instantly (Spotlight-style) rather than animating to the dock.
/// </summary>
internal sealed class AppWindowController : IWindowController
{
    const int OffScreen = -32000;

    private PhotinoWindow? _window;
    private volatile bool _isVisible;
    private int _savedLeft, _savedTop;

    /// <summary>Attach the real window once it exists (post-Build).</summary>
    public void Attach(PhotinoWindow window, bool startVisible)
    {
        _window = window;
        _isVisible = startVisible;
        if (!startVisible) Hide();
    }

    public bool IsVisible => _isVisible;

    public void Show()
    {
        if (_window is null) return;
        _isVisible = true;
        // Restore to the last on-screen position (fall back to re-centering).
        if (_savedTop == 0 && _savedLeft == 0)
            _window.Centered = true;
        else
        {
            _window.SetLeft(_savedLeft);
            _window.SetTop(_savedTop);
        }
        _window.SetMinimized(false);
        _window.SetTopMost(true);   // float above everything and take front
    }

    public void Hide()
    {
        if (_window is null) return;
        _isVisible = false;
        // Remember where it was, then move it fully off-screen so it vanishes.
        _savedLeft = _window.Left;
        _savedTop = _window.Top;
        _window.SetTop(OffScreen);
        _window.SetLeft(OffScreen);
    }

    public void ToggleVisible()
    {
        if (_isVisible) Hide();
        else Show();
    }
}
