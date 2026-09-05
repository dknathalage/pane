using System.Diagnostics;

namespace Pane.Core.Updates;

/// <summary>
/// Replaces the installed Pane.app with a downloaded release.
///
/// The swap is done by a detached shell helper AFTER this process exits. A
/// self-contained .NET app demand-loads dylibs out of Contents/MacOS for its
/// whole lifetime, so overwriting the bundle in place risks faulting the very
/// process doing the overwriting.
///
/// Trust model: this downloads and runs unsigned code, trusting TLS and
/// GitHub's control of the release assets — exactly the trust install.sh
/// already requires. It is NOT signature or checksum verification.
/// </summary>
public sealed class MacUpdateInstaller : IUpdateInstaller
{
    readonly HttpClient _http;
    readonly string? _bundle;
    readonly Action _quitApp;

    /// <param name="installedBundle">The .app to replace, or null when not installed.</param>
    /// <param name="quitApp">Terminates the app so the helper can take over.</param>
    public MacUpdateInstaller(HttpMessageHandler handler, string? installedBundle, Action quitApp)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        _bundle = installedBundle;
        _quitApp = quitApp;
    }

    public MacUpdateInstaller(Action quitApp)
        : this(new HttpClientHandler(), BundleLayout.CurrentBundle(), quitApp) { }

    public bool CanInstall => OperatingSystem.IsMacOS() && _bundle is not null;

    public string? UnavailableReason => CanInstall
        ? null
        : "Pane is not running from an installed .app bundle, so it cannot update itself. "
          + "Install it with install.sh first.";

    public async Task InstallAsync(ReleaseAsset asset, AppVersion mustExceed,
                                   IProgress<int> progress, CancellationToken ct)
    {
        // Must be the same gate as CanInstall, not just "_bundle is null": a
        // non-null bundle string alone does not mean we are actually on macOS,
        // and everything past this point (ditto, xattr, /bin/sh) only makes
        // sense there.
        if (!CanInstall) throw new InvalidOperationException(UnavailableReason);

        var work = Path.Combine(Path.GetTempPath(), $"pane-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            var zip = Path.Combine(work, asset.Name);
            await DownloadAsync(asset, zip, progress, ct);

            var extracted = Path.Combine(work, "extracted");
            Directory.CreateDirectory(extracted);
            await ExtractAsync(zip, extracted, ct);

            // ditto preserves the symlinks and xattrs inside a .app that plain
            // unzip flattens, so the extracted bundle is actually launchable.
            var newBundle = Path.Combine(extracted, "Pane.app");
            if (BundleLayout.Validate(newBundle, mustExceed) is { } reason)
                throw new InvalidOperationException(reason);

            // Nothing installed has been touched up to this point.
            Handoff(newBundle, work);
        }
        catch
        {
            TryDelete(work);
            throw;
        }
    }

    async Task DownloadAsync(ReleaseAsset asset, string destination,
                             IProgress<int> progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(destination);

        var buffer = new byte[81920];
        long written = 0;
        int lastReported = -1, read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;

            if (total <= 0) continue;
            var percent = (int)(written * 100 / total);
            if (percent == lastReported) continue;   // don't spam the UI per chunk
            lastReported = percent;
            progress.Report(percent);
        }
    }

    static async Task ExtractAsync(string zip, string destination, CancellationToken ct)
    {
        var info = new ProcessStartInfo("/usr/bin/ditto")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("-x");
        info.ArgumentList.Add("-k");
        info.ArgumentList.Add(zip);
        info.ArgumentList.Add(destination);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Could not run /usr/bin/ditto.");

        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"The downloaded archive could not be expanded. {error}".Trim());
    }

    void Handoff(string newBundle, string payloadDir)
    {
        // The script lives OUTSIDE payloadDir: it deletes that directory, and a
        // shell reading its own script incrementally must not have it removed
        // underneath. What it leaves behind is a few hundred bytes in the system
        // temp dir, which macOS reaps.
        var scriptDir = Path.Combine(Path.GetTempPath(), $"pane-swap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDir);

        try
        {
            var script = Path.Combine(scriptDir, "swap.sh");

            File.WriteAllText(script,
                BuildHelperScript(Environment.ProcessId, newBundle, _bundle!, payloadDir));

            // Reachable only when CanInstall is true, i.e. only on macOS —
            // enforced by InstallAsync's `if (!CanInstall)` guard above.
#pragma warning disable CA1416
            File.SetUnixFileMode(script,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#pragma warning restore CA1416

            // ArgumentList, not the "/bin/sh script" string overload: that
            // overload runs the path through .NET's argument splitter, so a
            // TMPDIR containing a space would split it into two arguments.
            var shInfo = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            shInfo.ArgumentList.Add(script);
            Process.Start(shInfo);
        }
        catch
        {
            // Unlike payloadDir, scriptDir is not cleaned up by the script
            // itself (the script can't delete the file it's still executing
            // from), so a failure here — /bin/sh missing, permission denied —
            // must clean it up here instead of leaking it.
            TryDelete(scriptDir);
            throw;
        }

        _quitApp();
    }

    /// <summary>
    /// The swap script. Pure so its safety properties are unit-tested: it waits
    /// for us to exit, moves the old bundle aside rather than deleting it, and
    /// puts it back if the copy fails.
    /// </summary>
    internal static string BuildHelperScript(int pid, string newBundle, string target,
                                             string payloadDir) => $"""
        #!/bin/sh
        # Once handoff happens the app is gone and the "every failure is a
        # readable status" contract ends, so log everything from here on —
        # a failed swap must still be diagnosable.
        LOGDIR="$TMPDIR"
        [ -z "$LOGDIR" ] && LOGDIR=/tmp
        exec >> "$LOGDIR/pane-update.log" 2>&1
        # Written by Pane to replace itself. Safe to delete.
        NEW={ShellQuote(newBundle)}
        TARGET={ShellQuote(target)}
        BACKUP="$TARGET.pane-old"
        PAYLOAD={ShellQuote(payloadDir)}

        # Wait for Pane to exit (bounded at ~30s so a wedged process can't hang us).
        i=0
        while kill -0 {pid} 2>/dev/null && [ $i -lt 60 ]; do
          sleep 0.5
          i=$((i + 1))
        done

        # If it is still alive after the wait, quitApp() failed to terminate it.
        # Swapping the bundle underneath a live self-contained .NET process is
        # exactly the hazard this detached-helper design exists to avoid, so
        # bail out without touching anything installed.
        if kill -0 {pid} 2>/dev/null; then
          echo "pane: gave up waiting for pid {pid}; not swapping" >&2
          rm -rf "$PAYLOAD"
          exit 1
        fi

        rm -rf "$BACKUP"
        if [ -d "$TARGET" ]; then
          mv "$TARGET" "$BACKUP" || exit 1
        fi

        # ditto (not cp -R) so symlinks and xattrs inside the .app survive the
        # copy the same way they survived extraction.
        if ditto "$NEW" "$TARGET"; then
          xattr -dr com.apple.quarantine "$TARGET" 2>/dev/null || true
          rm -rf "$BACKUP"
        else
          # Put the working app back rather than leaving the user with none.
          rm -rf "$TARGET"
          [ -d "$BACKUP" ] && mv "$BACKUP" "$TARGET"
        fi

        open "$TARGET"
        rm -rf "$PAYLOAD"
        """;

    /// <summary>
    /// Wraps a value in single quotes so the shell treats it as fully literal:
    /// unlike double quotes, single quotes also block $ parameter expansion and
    /// $(...)/backtick command substitution. The one character single quotes
    /// cannot themselves contain is escaped by closing the quote, inserting an
    /// escaped literal quote, and reopening it — the standard POSIX idiom.
    /// </summary>
    static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); } catch { }
    }
}
