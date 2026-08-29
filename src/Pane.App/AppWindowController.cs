using Photino.NET;
using Pane.Core;

namespace Pane.App;

/// <summary>
/// IWindowController backed by a Photino window, registered in DI and attached
/// to the real window after Build().
///
/// NOTE: Photino (4.0.13) crashes natively if the window is moved off-screen or
/// if Left/Top are read, and if the window is configured hidden before Run().
/// So Hide/Show use SetMinimized, which is stable. A true Spotlight-style
/// off-screen vanish needs a different host (e.g. a menu-bar/NSPanel) — tracked
/// as a follow-up.
/// </summary>
internal sealed class AppWindowController : IWindowController
{
    const int OffScreen = -32000;
    const int MinHeight = 72;    // just the search row
    const int MaxHeight = 520;   // beyond this the results list scrolls

    private PhotinoWindow? _window;
    private volatile bool _isVisible;
    private int _height;

    /// <summary>Attach the real window once it exists (post-Build, pre-Run).</summary>
    public void Attach(PhotinoWindow window, bool startVisible)
    {
        _window = window;
        _isVisible = startVisible;
    }

    public bool IsVisible => _isVisible;

    // Callers are all on the main thread (Carbon hotkey / menu-bar handlers,
    // Blazor Escape, or marshalled via MainWindow.Invoke), so these are safe.
    public void Show()
    {
        if (_window is null) return;
        _isVisible = true;
        _window.Centered = true;    // re-centre on screen
        _window.SetTopMost(true);   // float above and take front
    }

    public void Hide()
    {
        if (_window is null) return;
        _isVisible = false;
        // Move off-screen so it vanishes instantly (no dock minimise).
        _window.SetLeft(OffScreen);
        _window.SetTop(OffScreen);
    }

    public void ToggleVisible()
    {
        if (_isVisible) Hide();
        else Show();
    }

    public void SetHeight(int px)
    {
        if (_window is null) return;
        var clamped = Math.Clamp(px, MinHeight, MaxHeight);
        if (clamped == _height) return;   // avoid redundant native calls (and re-centre jitter)
        _height = clamped;
        _window.SetHeight(clamped);
        if (_isVisible) _window.Centered = true;   // keep it centred as it grows/shrinks
    }
}
