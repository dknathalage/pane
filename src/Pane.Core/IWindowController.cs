namespace Pane.Core;

/// <summary>Controls the launcher window; implemented by the host (Pane.App).</summary>
public interface IWindowController
{
    bool IsVisible { get; }
    void Show();
    void Hide();
    void ToggleVisible();

    /// <summary>Resize the window height to fit content, clamped to sane bounds, and re-centre.</summary>
    void SetHeight(int px);

    /// <summary>
    /// Raised (on the main thread) after the window is shown (true) or dismissed (false),
    /// so the UI can reset its query and refocus the search box.
    /// </summary>
    event Action<bool>? VisibilityChanged;
}
