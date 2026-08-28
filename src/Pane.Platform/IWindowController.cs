namespace Pane.Platform;

public interface IWindowController
{
    bool IsVisible { get; }
    void Show();
    void Hide();
    void ToggleVisible();
}
