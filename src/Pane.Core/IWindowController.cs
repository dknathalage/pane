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
}
