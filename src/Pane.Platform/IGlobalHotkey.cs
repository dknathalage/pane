namespace Pane.Platform;

public interface IGlobalHotkey : IDisposable
{
    event Action? Pressed;
    void Register(string combo);
}
