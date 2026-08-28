# Pane — Design Spec

**Date:** 2026-08-28
**Status:** Approved for implementation planning

## Summary

Pane is a cross-platform desktop launcher / command palette (a Sol/Raycast
alternative) built on .NET and Blazor. It presents a frameless, always-on-top
overlay summoned by a global hotkey; the user types a query and Pane shows
ranked results contributed by plugins. Plugins are independent DLLs loaded
in-process with isolation, installed and managed from a Settings page.

## Goals

- A fast, keyboard-driven launcher overlay summoned by a global hotkey.
- A DLL-based plugin system: each plugin is its own assembly implementing a
  shared contract, loaded in-process in an isolated, collectible load context.
- Ship four v1 plugins: Scripts, VSCode, App launcher, Calculator.
- Plugins can be installed, enabled/disabled, and uninstalled at runtime from a
  Settings page — no app restart.
- Plugin failures are silent in normal use (never crash Pane, never interrupt
  the user) and visible only in the Settings → Plugins pane.
- Cross-platform (macOS, Windows, Linux) from day one, built and verified on
  macOS with Windows/Linux paths behind abstractions.

## Non-Goals (v1)

- Out-of-process / sandboxed plugins (chosen in-process explicitly).
- A plugin marketplace / remote install (install is from a local folder/DLL).
- Themes/skinning beyond a single default look.
- Sync, telemetry, auto-update.

## Technology Choices

| Concern | Choice | Rationale |
|---|---|---|
| UI framework | Blazor | Requested. Component model for search + results + settings. |
| Native host | Photino.Blazor | Lightweight cross-platform native window hosting a WebView + Blazor. Ideal for a frameless overlay. |
| Plugin isolation | `AssemblyLoadContext` (collectible) + `AssemblyDependencyResolver` | Standard .NET plugin pattern; enables runtime load/unload and dependency isolation. |
| Global hotkey | SharpHook (libuiohook) behind `IGlobalHotkey` | One managed API for real system-wide hotkeys on macOS/Windows/Linux. |
| Tests | xUnit | Unit tests for pure logic; native/UI verified manually. |
| Target framework | net9.0 (host/plugins); `Pane.Abstractions` multi-targets to keep the contract minimal | Modern .NET, single toolchain. |

## Solution Structure

```
pane/
├── src/
│   ├── Pane.Abstractions/      # plugin contract — the ONLY DLL plugins reference
│   ├── Pane.Core/              # loader, PluginManager, query dispatch, ranking, settings
│   ├── Pane.Platform/          # IGlobalHotkey + IWindowController; SharpHook + per-OS impls
│   ├── Pane.Ui/                # Blazor RCL: launcher + settings components
│   ├── Pane.App/               # Photino.Blazor executable — DI wiring, window lifecycle
│   └── plugins/
│       ├── Pane.Plugins.Scripts/
│       ├── Pane.Plugins.VSCode/
│       ├── Pane.Plugins.Apps/
│       └── Pane.Plugins.Calculator/
└── tests/
    ├── Pane.Core.Tests/
    └── Pane.Plugins.*.Tests/
```

Dependency direction: everything points at `Pane.Abstractions`; `Abstractions`
depends on nothing. Plugins reference only `Abstractions`.

Each plugin builds into its own folder under a runtime `plugins/` directory
(next to the app), carrying its entry DLL plus any private dependencies.

## Plugin Contract (`Pane.Abstractions`)

```csharp
public interface IPlugin {
    PluginMetadata Metadata { get; }                        // id, name, icon, keyword prefix, version
    Task InitializeAsync(IPluginContext ctx);               // logger, settings, paths
    IAsyncEnumerable<PaneResult> QueryAsync(PaneQuery q, CancellationToken ct);
}

public record PluginMetadata(string Id, string Name, string Icon,
                             string? Keyword, string Version);

public record PaneQuery(string RawText, string? Keyword, string Terms);

public record PaneResult(string Title, string Subtitle, string Icon,
                         double Score, Func<Task> Activate); // Enter runs Activate()

public interface IPluginContext {
    IPluginLogger Logger { get; }
    string DataDirectory { get; }        // per-plugin writable dir
    IReadOnlyDictionary<string, string> Settings { get; }
}
```

Results carry their own activation delegate — the plugin decides what Enter
does. Because `Abstractions` is a shared assembly (see loading), these delegates
and records cross the load-context boundary as identical types.

## Plugin Loading (`Pane.Core`)

Discovery: scan the `plugins/` folder; each subfolder holds one plugin's entry
DLL and private deps.

For each enabled plugin, create a collectible `PluginLoadContext`:

```csharp
sealed class PluginLoadContext : AssemblyLoadContext {
    readonly AssemblyDependencyResolver _resolver;
    public PluginLoadContext(string pluginDllPath)
        : base(isCollectible: true) {
        _resolver = new AssemblyDependencyResolver(pluginDllPath);
    }
    protected override Assembly? Load(AssemblyName name) {
        if (name.Name == "Pane.Abstractions") return null;   // shared → default context
        var path = _resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
```

Critical rule: `Pane.Abstractions` (and other shared runtime types) MUST resolve
from the default context by returning `null` in `Load`. Otherwise host and
plugin get distinct `IPlugin` types and the cast fails.

Instantiation: load the entry DLL, reflect for a public non-abstract `IPlugin`,
construct it, call `InitializeAsync`. Core keeps a registry of
`(IPlugin instance, PluginLoadContext alc, PluginStatus status)`.

## Query & Activation Flow

1. Keystroke in the launcher → debounce.
2. `Core` builds a `PaneQuery` (raw text, optional keyword prefix, terms) and
   fans it out to all enabled plugins in parallel, each with a `CancellationToken`
   and a per-plugin timeout.
3. Each plugin streams scored `PaneResult`s.
4. The aggregator merges results, applies fuzzy ranking, and sorts by score.
5. Blazor renders the list; arrow keys navigate; Enter calls the selected
   result's `Activate()`; Escape hides the window.
6. An optional keyword prefix (e.g. `>` for scripts) scopes the query to a
   single plugin.

## The Four v1 Plugins

- **Scripts** — scans `~/.config/pane/scripts` (and Sol's `~/.config/sol/scripts`
  for compatibility), parses `# name:` / `# icon:` header comments, runs the
  script on Enter. Mirrors Sol's script convention.
- **VSCode** — lists immediate subfolders of `~/repos`, opens the selected one
  in VSCode via the `code` binary. (Direct port of the existing shell script.)
- **Apps** — indexes installed applications behind an `IAppIndexer` abstraction:
  macOS `/Applications/*.app`, Windows Start-Menu `.lnk`, Linux `.desktop`.
  Fuzzy-search and launch.
- **Calculator** — evaluates an inline expression as the user types; Enter copies
  the result to the clipboard.

## Native / Cross-Platform Concerns

- **Global hotkey** — SharpHook behind `IGlobalHotkey`. On macOS the first run
  requires an Accessibility-permission grant; the app must detect "not yet
  granted" and degrade gracefully (show guidance in Settings rather than crash).
- **Overlay window** — frameless, always-on-top, centered, hide-on-focus-loss
  via Photino window controls. May need small per-OS nudges to reliably take
  focus. Settings is a separate normal (resizable, chromed) window.
- **Per-OS binaries/paths** — `code` discovery and app enumeration differ per
  platform; isolated inside the VSCode and Apps plugins.

## Failure Handling — Silent & Resilient

- **Load time** — a plugin that throws in its constructor or `InitializeAsync`
  is caught, skipped, and marked `Errored`. Startup continues.
- **Query time** — each plugin's `QueryAsync` runs in its own try/catch with a
  timeout. A throwing or hanging plugin contributes no results for that query;
  other plugins render normally. No exception reaches the launcher UI.
- **Visibility** — failures are logged to a file and surfaced only in the
  Settings → Plugins pane as a status badge (`Errored — <reason>`). Silent in
  normal use, visible when the user looks.

## Settings Page + Plugins Pane

Two window modes from one Photino/Blazor app:

- **Launcher** — frameless, always-on-top overlay, hotkey-summoned.
- **Settings** — normal resizable window with chrome, opened from the launcher
  (a `settings` keyword or gear action). Routed Blazor page.

The **Plugins pane** lists every plugin in `plugins/`, each row showing icon,
name, version, and a status badge (`Enabled` / `Disabled` / `Errored — reason`).
Actions:

- **Enable/disable** — toggle persisted in `settings.json`. Disabled plugins are
  skipped by the loader (no ALC created); toggling loads/unloads live.
- **Install** — pick a plugin folder/`.dll`; Pane copies it into
  `plugins/<name>/` and loads it into a fresh ALC immediately (no restart).
- **Uninstall** — unload the plugin's collectible ALC, then delete its folder.

`Pane.Core` exposes a `PluginManager` (list / enable / disable / install /
uninstall + the errored-status registry) that the Blazor Settings page binds to.
The collectible `AssemblyLoadContext` is what makes all of these work at runtime
without restarting Pane.

## Testing Strategy (TDD)

Pure logic is unit-tested without UI or native dependencies:

- Fuzzy ranking / query aggregation and merge ordering.
- The ALC plugin loader and `PluginManager` lifecycle (using a fixture
  test-plugin), including the shared-`Abstractions` rule and unload.
- Script-header parsing (Scripts plugin) and repo enumeration (VSCode plugin).
- Failure-handling: a deliberately throwing fixture plugin must not break load
  or query, and must be marked `Errored`.

Native hotkey/window behavior and the Blazor UI are verified manually on macOS.
xUnit throughout.

## Open Risks

- SharpHook macOS Accessibility permission UX — needs a clean first-run flow.
- Photino focus/always-on-top behavior may need per-OS tuning.
- Runtime unload correctness (no lingering references) for hot uninstall — the
  loader must hold only weak/disposable references to plugin instances.
