using Photino.NET;
using Pane.Core;

namespace Pane.App;

/// <summary>
/// IWindowController backed by a Photino window, registered in DI and attached
/// to the real window after Build().
///
/// NOTE: Photino (4.0.13) crashes natively if Left/Top are read, and if the
/// window is configured hidden before Run(). So "vanish" is done by parking the
/// window off-screen and hiding the whole app (NSApp hide:), which also hands
/// key focus back to the app the user was in. Showing reverses both and
/// activates us so the search box gets keystrokes.
/// </summary>
internal sealed class AppWindowController : IWindowController
{
    const int OffScreen = -32000;
    const int MinHeight = 72;    // just the search row
    const int MaxHeight = 520;   // beyond this the results list scrolls

    private PhotinoWindow? _window;
    private volatile bool _isVisible;
    private int _height;

    public event Action<bool>? VisibilityChanged;

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
        _window.Centered = true;    // re-centre on screen (also brings it back from off-screen)
        _window.SetTopMost(true);   // float above everything
        MacApp.Activate();          // take app focus so typing lands in the search box
        VisibilityChanged?.Invoke(true);
    }

    public void Hide()
    {
        if (_window is null) return;
        if (!_isVisible) return;    // already gone — don't steal focus from the front app
        _isVisible = false;
        // Move off-screen so it vanishes instantly (no dock minimise), then drop
        // the app to the background so the previous app regains key focus.
        _window.SetLeft(OffScreen);
        _window.SetTop(OffScreen);
        MacApp.Deactivate();
        VisibilityChanged?.Invoke(false);
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
