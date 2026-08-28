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
public interface IPlugin : IAsyncDisposable {
    PluginMetadata Metadata { get; }                        // searchable descriptor of the plugin
    Task InitializeAsync(IPluginContext ctx);               // logger, settings, paths
    IAsyncEnumerable<PaneResult> QueryAsync(PaneQuery q, CancellationToken ct);
    // DisposeAsync (from IAsyncDisposable) is called on disable/uninstall,
    // before the load context is unloaded, so the plugin can release resources.
}

public record PluginMetadata(
    string Id,
    string Name,
    string Icon,
    string Version,
    string Description,                       // human summary; also fuzzy-searchable
    IReadOnlyList<string> Keywords,          // aliases the plugin answers to (routing/scoping)
    string? Keyword = null,                  // optional explicit prefix to scope to this plugin (e.g. ">")
    int Priority = 0);                        // tie-breaker / bias when scores are close

public record PaneQuery(string RawText, string? Keyword, string Terms);

public record PaneResult(
    string Title,
    string Subtitle,
    string Icon,
    double BaseScore,                        // plugin's own relevance (recency, usage, exact-ness)
    Func<Task> Activate,                     // Enter runs this
    string? SearchText = null);              // extra text to fuzzy-match beyond Title (path, tags, aliases)

public interface IPluginContext {
    IPluginLogger Logger { get; }
    string DataDirectory { get; }            // per-plugin writable dir
    IReadOnlyDictionary<string, string> Settings { get; }
    IFuzzyMatcher Matcher { get; }           // shared matcher plugins may reuse for pre-filtering
}
```

Results carry their own activation delegate — the plugin decides what Enter
does. Because `Abstractions` is a shared assembly (see loading), these delegates
and records cross the load-context boundary as identical types.

The metadata is deliberately rich: `Name`, `Description`, and `Keywords` make
the plugin **itself** findable, so a query like `calc` or `repo` can route to or
bias the right plugin without the host hard-coding any knowledge of it. Each
`PaneResult.SearchText` lets a result be matched on more than its display title
(e.g. a repo's full path, an app's bundle id, a script's tags).

## Plugin Integration Surface

A plugin author touches exactly one assembly — `Pane.Abstractions` — and nothing
else in Pane. The complete surface:

- **References:** `Pane.Abstractions` only (not copied locally — it is the shared
  contract). `Pane.Core`, `Pane.App`, and `Pane.Ui` are not visible.
- **Implements:** `IPlugin` (one public non-abstract type per DLL, parameterless
  constructor). No other required types.
- **Receives (inbound):** an `IPluginContext` at `InitializeAsync` (logger,
  per-plugin `DataDirectory`, `Settings`, shared `Matcher`); a `PaneQuery` and a
  `CancellationToken` per keystroke.
- **Returns (outbound):** a stream of `PaneResult`. The only action surface is
  each result's `Activate: Func<Task>` delegate (Enter invokes it); the plugin
  does whatever it wants inside — launch a process, copy to clipboard, open a
  folder. There is no separate command/action API.
- **Packaging:** builds to `plugins/<PluginName>/` (entry DLL + private deps).
  No manifest file — the code-returned `PluginMetadata` is the manifest.
- **Lifecycle:** `construct → InitializeAsync (once) → QueryAsync (many,
  concurrency-safe) → DisposeAsync (on disable/uninstall, before ALC unload)`.
  For clean unload, a plugin must hold no static references back into itself and
  should release resources in `DisposeAsync`.
- **Explicitly NOT in the surface:** no Blazor/UI access (plugins return data,
  they do not render), no access to other plugins, no host internals, no global
  process/window control. This keeps plugins simple and the host safe from a
  misbehaving one (see Failure Handling).

A short "authoring a plugin" guide will accompany the code, but the above is the
whole contract.

## Fuzzy Matcher (`Pane.Abstractions`)

A single canonical matcher lives in `Abstractions` and is used everywhere —
by Core to rank results and by plugins to pre-filter large candidate sets — so
ranking is consistent across the whole app.

```csharp
public interface IFuzzyMatcher {
    // Returns false if `query` is not a subsequence of `target`.
    // On success, `score` ranks quality and `positions` are the matched indices
    // in `target` (for highlighting in the UI).
    bool TryMatch(string query, string target,
                  out double score, out IReadOnlyList<int> positions);
}
```

Algorithm (fzf/Sublime-style subsequence scoring):

- Case-insensitive subsequence match: every query char must appear in `target`
  in order. No subsequence → no match, result dropped.
- Score rewards: **consecutive** matched chars, matches at **word boundaries**
  (after space/`-`/`_`/`/`/`.`) and **camelCase** boundaries, and matches near
  the **start** of the target. It penalizes gaps and leading unmatched chars.
- An exact substring (and especially a prefix) beats a scattered subsequence.
- Empty query matches everything with score `0` (so plugins can show default
  items ranked by `BaseScore`).

The default implementation is public so plugins can reuse it via
`IPluginContext.Matcher`. It is pure and fully unit-tested.

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
`(IPlugin instance, PluginLoadContext alc, PluginStatus status)`. On disable or
uninstall, Core calls the plugin's `DisposeAsync`, drops its references, then
calls `alc.Unload()`.

## Query & Activation Flow

1. Keystroke in the launcher → debounce.
2. `Core` parses the raw text into a `PaneQuery`. If it starts with a plugin's
   explicit `Keyword` prefix (e.g. `>`), the query is **scoped** to that plugin;
   otherwise it goes to all enabled plugins.
3. **Plugin routing** — for unscoped queries, Core fuzzy-matches the query
   against each plugin's `Name`/`Keywords`/`Description`. Plugins are still all
   queried (fan-out), but a metadata match applies a routing bias so the right
   plugin's results float up (typing `calc` biases the Calculator plugin).
4. Core fans the query out to the selected plugins in parallel, each with a
   `CancellationToken` and a per-plugin timeout.
5. Each plugin streams `PaneResult`s carrying a `BaseScore` and `SearchText`.
6. **Ranking** — for each result Core runs `IFuzzyMatcher.TryMatch` over
   `Title` (and `SearchText`), dropping non-matches, and computes a final score:

   ```
   final = fuzzyScore * W_FUZZY
         + normalize(BaseScore) * W_BASE
         + routingBias(plugin) + Priority
   ```

   The matched positions are kept for highlighting.
7. Results are merged, sorted by final score, and rendered by Blazor with the
   matched characters highlighted. Arrow keys navigate; Enter calls the selected
   result's `Activate()`; Escape hides the window.

When the query is empty, fuzzy score is `0` and results rank purely by
`BaseScore`/`Priority`, so each plugin can present sensible defaults.

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

- The fuzzy matcher: subsequence correctness, boundary/consecutive/prefix
  scoring order, matched-position output, and empty-query behavior.
- Plugin routing (metadata match biases the right plugin) and final-score
  composition.
- Query aggregation and merge ordering.
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
