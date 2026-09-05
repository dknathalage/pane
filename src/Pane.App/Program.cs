using Microsoft.Extensions.DependencyInjection;
using Pane.Core.Contracts;
using Pane.App;
using Pane.Core;
using Pane.Core.Features.Apps;
using Pane.Core.Features.Files;
using Pane.Core.Query;
using Pane.Core.Settings;
using Pane.Core.Startup;
using Pane.Core.Updates;
using Pane.Platform;
using Photino.Blazor;

// ── Paths ──────────────────────────────────────────────────────────────────
var home        = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var dataRoot    = Path.Combine(home, ".config", "pane");
Directory.CreateDirectory(dataRoot);

// ── Builder ────────────────────────────────────────────────────────────────
// Read our own flags first; Photino's CreateDefault crashes on ANY unknown
// CLI arg, so hand it an empty array.
var startHidden = args.Contains("--startup");
var builder = PhotinoBlazorAppBuilder.CreateDefault(Array.Empty<string>());

// ── DI registrations ───────────────────────────────────────────────────────
builder.Services.AddSingleton<IFuzzyMatcher, FuzzyMatcher>();

var settingsStorePath = Path.Combine(dataRoot, "settings.json");
var settingsStore = new SettingsStore(settingsStorePath);
builder.Services.AddSingleton(settingsStore);

// Features (built-in; no dynamic loading). They are registered behind
// IPaneFeature only — the dispatcher discovers them, configures them from
// settings, and asks each whether it can run on this machine.
builder.Services.AddSingleton<IPaneFeature, AppsFeature>();
builder.Services.AddSingleton<IPaneFeature, FilesFeature>();

builder.Services.AddSingleton(sp => new QueryDispatcher(
    sp.GetRequiredService<IFuzzyMatcher>(),
    sp.GetRequiredService<SettingsStore>(),
    sp.GetServices<IPaneFeature>()));

builder.Services.AddSingleton<IGlobalHotkey, SharpHookGlobalHotkey>();

// IFilePicker: osascript-based folder picker for macOS (no ObjC interop).
builder.Services.AddSingleton<IFilePicker, MacFilePicker>();

// ── Updates + login item ───────────────────────────────────────────────────
// Both are macOS-only and both refuse to act when Pane is not running from an
// installed .app (a `dotnet run` dev session), reporting why in Settings rather
// than corrupting a checkout.
builder.Services.AddSingleton<IReleaseSource>(_ => new GitHubReleaseSource());
builder.Services.AddSingleton<IUpdateInstaller>(_ => new MacUpdateInstaller(
    quitApp: () => MacApp.Terminate()));
builder.Services.AddSingleton(sp => new UpdateService(
    sp.GetRequiredService<IReleaseSource>(),
    sp.GetRequiredService<IUpdateInstaller>(),
    new UpdateState(Path.Combine(dataRoot, "update-state.json")),
    AppVersionSource.Current));
builder.Services.AddSingleton<ILoginItem>(_ =>
    OperatingSystem.IsMacOS() ? new MacLoginItem() : new UnsupportedLoginItem());

// Window controller: registered now, attached to the real window post-Build.
// Exposing it via DI lets Blazor (e.g. Escape) hide the window too.
builder.Services.AddSingleton<AppWindowController>();
builder.Services.AddSingleton<IWindowController>(sp => sp.GetRequiredService<AppWindowController>());

// ── Root component ─────────────────────────────────────────────────────────
// Pane.Ui.Launcher is the top-level Blazor component; mounts into <div id="app">.
builder.RootComponents.Add<Pane.Ui.Launcher>("#app");

// ── Build ──────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Window configuration (REAL Photino.NET 4.x API) ───────────────────────
// The brief's guesses were:
//   .SetChromeless(true)  <- CORRECT (fluent method, returns PhotinoWindow)
//   .SetTopMost(true)     <- CORRECT (fluent method, returns PhotinoWindow)
//   .SetSize(720, 480)    <- WRONG — no SetSize(); use SetWidth()/SetHeight() separately
//   .Center()             <- WRONG — no Center() method; Centered is a bool property
//   .SetMinimized(!v)     <- CORRECT as a fluent method; Minimized is also a settable property
app.MainWindow
    .SetTitle("Pane")
    .SetChromeless(true)
    .SetTopMost(true)
    .SetWidth(720)
    .SetHeight(480);
app.MainWindow.Centered = true;   // property, not a method

// When launched at login (--startup) we want it to run in the background.
// Photino crashes if the window is configured hidden BEFORE Run, so we start
// visible and dismiss it just after the window comes up (see below).

// Attach the DI-registered controller to the real window now that it exists.
var windowController = app.Services.GetRequiredService<AppWindowController>();
windowController.Attach(app.MainWindow, startVisible: true);

// Photino calls setActivationPolicy: while creating the window, overriding the
// bundle's LSUIElement. Correcting it here — the moment the native window exists —
// makes Pane a background launcher (no Dock tile, no Command-Tab entry, floats
// over full-screen apps) before a tile can appear. Doing it on a timer instead
// would show the icon for as long as the timer ran. MacApp.Activate() re-asserts
// the window style on every show, since Photino resets the level with topmost.
app.MainWindow.RegisterWindowCreatedHandler((_, _) => MacApp.ConfigureAsLauncher());

// ── Post-startup tasks ─────────────────────────────────────────────────────
// Initialize built-in features before the message loop starts. This also
// resolves each feature's settings and probes its availability.
await app.Services.GetRequiredService<QueryDispatcher>()
    .InitializeAsync(new FeatureContext(Path.Combine(dataRoot, "data"), home), CancellationToken.None);

// ── Global hotkey + menu bar ───────────────────────────────────────────────
var settings = settingsStore.Load();
IGlobalHotkey? hotkey = null;

if (OperatingSystem.IsMacOS())
{
    // Carbon hotkey needs NO Accessibility permission (unlike a keyboard tap),
    // and a menu-bar item as a click-to-open fallback. Both handlers fire on
    // the main thread, so window calls are safe without marshalling.
    if (!MacGlobalHotkey.Register(settings.Hotkey, () => windowController.ToggleVisible()))
        Console.Error.WriteLine($"pane: could not register hotkey '{settings.Hotkey}'");
    try { MacStatusBar.Setup("Pane", () => windowController.ToggleVisible()); }
    catch (Exception ex) { Console.Error.WriteLine("pane: menu bar setup failed: " + ex.Message); }
}
else
{
    // Other platforms: SharpHook keyboard hook, marshalled to the UI thread.
    hotkey = app.Services.GetRequiredService<IGlobalHotkey>();
    hotkey.Register(settings.Hotkey);
    hotkey.Pressed += () => app.MainWindow.Invoke(() => windowController.ToggleVisible());
}

// Background start: once the window is up, dismiss it so Pane sits quietly
// until the hotkey. Runs on a background thread after Run() begins — the
// native window is live by then, so Hide() (off-screen) is safe.
if (startHidden)
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(1500);
        // Marshal to the main thread — window calls crash off-thread.
        try { app.MainWindow.Invoke(() => windowController.Hide()); } catch { /* best-effort */ }
    });
}

// ── Update check ───────────────────────────────────────────────────────────
// On a background task so a slow or offline network never delays startup, and
// re-armed every 6h so a long-running instance eventually crosses the 24h
// staleness boundary rather than checking once per launch and never again.
var updates = app.Services.GetRequiredService<UpdateService>();
_ = Task.Run(async () =>
{
    while (true)
    {
        try { await updates.MaybeAutoCheckAsync(settingsStore.Load().AutoCheckUpdates, CancellationToken.None); }
        catch { /* a failed check is already a status; never take the app down */ }
        await Task.Delay(TimeSpan.FromHours(6));
    }
});

// ── Run ────────────────────────────────────────────────────────────────────
// app.Run() is synchronous — it enters the native message loop and returns
// only when the window is closed.
try
{
    app.Run();
}
finally
{
    // Cleanup: dispose the SharpHook hotkey if we used one (non-macOS).
    hotkey?.Dispose();
}
