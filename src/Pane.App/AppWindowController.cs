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
    private PhotinoWindow? _window;
    private volatile bool _isVisible;

    /// <summary>Attach the real window once it exists (post-Build, pre-Run).</summary>
    public void Attach(PhotinoWindow window, bool startVisible)
    {
        _window = window;
        _isVisible = startVisible;
    }

    public bool IsVisible => _isVisible;

    public void Show()
    {
        if (_window is null) return;
        _isVisible = true;
        _window.SetMinimized(false);
        _window.SetTopMost(true);   // float above and take front
    }

    public void Hide()
    {
        if (_window is null) return;
        _isVisible = false;
        _window.SetMinimized(true);
    }

    public void ToggleVisible()
    {
        if (_isVisible) Hide();
        else Show();
    }
}
