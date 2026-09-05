using System.Runtime.InteropServices;

namespace Pane.Core.Updates;

/// <summary>
/// Decides whether an update exists and drives installing it. The only place
/// that compares versions or picks an asset.
/// </summary>
public sealed class UpdateService
{
    /// <summary>How stale a check may be before an auto-check runs again.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    readonly IReleaseSource _source;
    readonly IUpdateInstaller _installer;
    readonly UpdateState _state;
    readonly AppVersion _current;

    public UpdateService(IReleaseSource source, IUpdateInstaller installer,
                         UpdateState state, AppVersion current)
    {
        _source = source;
        _installer = installer;
        _state = state;
        _current = current;
    }

    public AppVersion CurrentVersion => _current;
    public UpdateStatus Status { get; private set; } = new UpdateStatus.Idle();
    public event Action? StatusChanged;

    public bool CanInstall => _installer.CanInstall;
    public string? InstallUnavailableReason => _installer.UnavailableReason;

    /// <summary>The release asset this machine can actually run, or null.</summary>
    public static string? AssetNameForCurrentMachine() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "Pane-osx-arm64.zip",
            Architecture.X64 => "Pane-osx-x64.zip",
            _ => null,
        };

    public async Task CheckAsync(CancellationToken ct)
    {
        Set(new UpdateStatus.Checking());
        try
        {
            var release = await _source.FetchLatestAsync(ct);
            var asset = release is null ? null : AssetFor(release);

            // A newer release with no asset for this machine is not actionable,
            // so it is reported as up to date rather than as a broken update.
            if (release is not null && asset is not null && release.Version > _current)
            {
                _state.SaveLastCheck(DateTimeOffset.UtcNow);
                Set(new UpdateStatus.Available(release, asset));
                return;
            }

            var now = DateTimeOffset.UtcNow;
            _state.SaveLastCheck(now);
            Set(new UpdateStatus.UpToDate(now));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Set(new UpdateStatus.Idle());
        }
        catch (Exception ex)
        {
            // Note: the last-check time is deliberately NOT advanced, so a
            // transient outage does not suppress checks for another 24h.
            Set(new UpdateStatus.Failed(Describe(ex)));
        }
    }

    /// <summary>Checks only if enabled and the previous check has gone stale.</summary>
    public async Task MaybeAutoCheckAsync(bool autoCheckEnabled, CancellationToken ct)
    {
        if (!autoCheckEnabled) return;

        var last = _state.LoadLastCheck();
        if (last is { } when && DateTimeOffset.UtcNow - when < CheckInterval) return;

        await CheckAsync(ct);
    }

    public async Task InstallAsync(CancellationToken ct)
    {
        if (Status is not UpdateStatus.Available available) return;
        if (!_installer.CanInstall)
        {
            Set(new UpdateStatus.Failed(
                _installer.UnavailableReason ?? "This copy of Pane cannot update itself."));
            return;
        }

        var progress = new SynchronousProgress<int>(pct => Set(new UpdateStatus.Downloading(pct)));
        try
        {
            Set(new UpdateStatus.Downloading(0));
            await _installer.InstallAsync(available.Asset, available.Release.Version, progress, ct);
            Set(new UpdateStatus.Installing());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Set(available);   // still offered, nothing was changed
        }
        catch (Exception ex)
        {
            Set(new UpdateStatus.Failed(Describe(ex)));
        }
    }

    ReleaseAsset? AssetFor(ReleaseInfo release) =>
        AssetNameForCurrentMachine() is { } name ? release.FindAsset(name) : null;

    static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden } =>
            "GitHub rate-limited the update check. Try again later.",
        HttpRequestException => "Could not reach GitHub to check for updates.",
        TaskCanceledException => "The update check timed out.",
        _ => ex.Message,
    };

    void Set(UpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    /// <summary>
    /// An IProgress&lt;T&gt; that invokes its callback inline rather than via
    /// SynchronizationContext.Post (what Progress&lt;T&gt; does), so progress is
    /// observed deterministically instead of racing a queued callback.
    /// </summary>
    sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
