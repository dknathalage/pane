using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Pane.Core.Features.Apps;

/// <summary>
/// Resolves a macOS .app bundle to a base64 PNG data-URI of its icon, cached
/// in memory and on disk. WebViews block file:// from a custom-scheme origin,
/// so a data-URI is the reliable way to show real app icons in the launcher.
/// </summary>
public sealed class MacAppIcons
{
    readonly string _cacheDir;
    readonly ConcurrentDictionary<string, string> _dataUris = new();  // appPath -> data-uri

    public MacAppIcons(string dataDirectory)
    {
        _cacheDir = Path.Combine(dataDirectory, "iconcache");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Returns a cached data-URI for the app, or null if not resolved yet.</summary>
    public string? TryGet(string appPath) =>
        _dataUris.TryGetValue(appPath, out var uri) ? uri : null;

    /// <summary>
    /// Load already-generated PNGs into memory synchronously (fast) so cached
    /// icons are available on the first render. Safe no-op off macOS.
    /// </summary>
    public void LoadCached(IEnumerable<string> appPaths)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
        foreach (var app in appPaths)
        {
            if (_dataUris.ContainsKey(app)) continue;
            var png = PngCachePath(app);
            if (File.Exists(png)) TryLoad(app, png);
        }
    }

    /// <summary>Generate any not-yet-cached icons in the background.</summary>
    public Task GenerateMissingAsync(IEnumerable<string> appPaths)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return Task.CompletedTask;
        var paths = appPaths.ToList();
        return Task.Run(() =>
        {
            foreach (var app in paths)
            {
                if (_dataUris.ContainsKey(app)) continue;
                try { Generate(app); } catch { /* icon is best-effort */ }
            }
        });
    }

    void Generate(string appPath)
    {
        var icns = ResolveIcns(appPath);
        if (icns is null) return;
        var png = PngCachePath(appPath);
        if (!File.Exists(png))
        {
            var ok = Run("sips", new[] { "-s", "format", "png", "-Z", "64", icns, "--out", png });
            if (!ok || !File.Exists(png)) return;
        }
        TryLoad(appPath, png);
    }

    void TryLoad(string appPath, string png)
    {
        try
        {
            var bytes = File.ReadAllBytes(png);
            if (bytes.Length == 0) return;
            _dataUris[appPath] = "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        catch { /* ignore */ }
    }

    // Find the .icns inside the bundle: CFBundleIconFile, else the first *.icns in Resources.
    static string? ResolveIcns(string appPath)
    {
        var resources = Path.Combine(appPath, "Contents", "Resources");
        if (!Directory.Exists(resources)) return null;

        var infoPlist = Path.Combine(appPath, "Contents", "Info.plist");
        var name = ReadPlistString(infoPlist, "CFBundleIconFile");
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (!name.EndsWith(".icns", StringComparison.OrdinalIgnoreCase)) name += ".icns";
            var direct = Path.Combine(resources, name);
            if (File.Exists(direct)) return direct;
        }

        return Directory.EnumerateFiles(resources, "*.icns").FirstOrDefault();
    }

    static string? ReadPlistString(string plist, string key)
    {
        if (!File.Exists(plist)) return null;
        var output = Capture("/usr/libexec/PlistBuddy", new[] { "-c", $"Print :{key}", plist });
        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    string PngCachePath(string appPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(appPath)))[..16];
        return Path.Combine(_cacheDir, hash + ".png");
    }

    static bool Run(string file, string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return false;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        return p.HasExited && p.ExitCode == 0;
    }

    static string Capture(string file, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return "";
            var outp = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(5_000);
            return p.ExitCode == 0 ? outp : "";
        }
        catch { return ""; }
    }
}
