using Microsoft.Extensions.DependencyInjection;
using Pane.Abstractions;
using Pane.App;
using Pane.Core;
using Pane.Core.Plugins;
using Pane.Core.Query;
using Pane.Core.Settings;
using Pane.Platform;
using Photino.Blazor;

// ── Paths ──────────────────────────────────────────────────────────────────
var home        = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var dataRoot    = Path.Combine(home, ".config", "pane");
var pluginsRoot = Path.Combine(dataRoot, "plugins");
Directory.CreateDirectory(dataRoot);
Directory.CreateDirectory(pluginsRoot);

// ── Builder ────────────────────────────────────────────────────────────────
var builder = PhotinoBlazorAppBuilder.CreateDefault(args);

// ── DI registrations ───────────────────────────────────────────────────────
builder.Services.AddSingleton<IFuzzyMatcher, FuzzyMatcher>();

// SettingsStore is a plain class; register as singleton instance so PluginManager can share it.
var settingsStorePath = Path.Combine(dataRoot, "settings.json");
var settingsStore = new SettingsStore(settingsStorePath);
builder.Services.AddSingleton(settingsStore);

builder.Services.AddSingleton(sp =>
    new PluginManager(dataRoot, sp.GetRequiredService<SettingsStore>()));

builder.Services.AddSingleton(sp =>
    new QueryDispatcher(sp.GetRequiredService<IFuzzyMatcher>()));

builder.Services.AddSingleton<IGlobalHotkey, SharpHookGlobalHotkey>();

// IFilePicker: osascript-based folder picker for macOS (no ObjC interop).
builder.Services.AddSingleton<IFilePicker, MacFilePicker>();

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

// When launched at login (--startup), start hidden so it doesn't flash a
// window every boot; the user summons it with the global hotkey. Minimized is
// a safe pre-Run property (unlike SetLeft/SetTop, which crash before Run).
var startHidden = args.Contains("--startup");
if (startHidden) app.MainWindow.Minimized = true;

// Attach the DI-registered controller to the real window now that it exists.
var windowController = app.Services.GetRequiredService<AppWindowController>();
windowController.Attach(app.MainWindow, startVisible: !startHidden);

// ── Post-startup tasks ─────────────────────────────────────────────────────
// Load plugins before the message loop starts.
var pluginManager = app.Services.GetRequiredService<PluginManager>();
await pluginManager.LoadAllAsync(pluginsRoot);

// ── Global hotkey ──────────────────────────────────────────────────────────
var settings = settingsStore.Load();
var hotkey   = app.Services.GetRequiredService<IGlobalHotkey>();

hotkey.Register(settings.Hotkey);
hotkey.Pressed += () =>
{
    // Photino's native message loop owns the window; SetMinimized() is thread-safe
    // per Photino.NET docs (it posts to the native queue). No marshal needed.
    windowController.ToggleVisible();
};

// ── Run ────────────────────────────────────────────────────────────────────
// app.Run() is synchronous — it enters the native message loop and returns
// only when the window is closed.
try
{
    app.Run();
}
finally
{
    // Cleanup: dispose hotkey (stops the SharpHook background thread).
    hotkey.Dispose();
}
