namespace Pane.Core.Startup;

/// <summary>Whether Pane starts itself when the user logs in.</summary>
public interface ILoginItem
{
    /// <summary>False when there is nothing to register (dev run, wrong OS).</summary>
    bool CanManage { get; }

    /// <summary>Why <see cref="CanManage"/> is false, for the user to read.</summary>
    string? UnavailableReason { get; }

    /// <summary>True only when the agent on disk launches THIS bundle.</summary>
    bool IsEnabled { get; }

    void Enable();
    void Disable();
}

/// <summary>Stand-in where login items don't apply, so the UI can still render.</summary>
public sealed class UnsupportedLoginItem : ILoginItem
{
    public bool CanManage => false;
    public string? UnavailableReason => "Starting at login is only supported on macOS.";
    public bool IsEnabled => false;
    public void Enable() { }
    public void Disable() { }
}
