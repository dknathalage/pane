# Drop the Plugin System — Single-App Consolidation

**Date:** 2026-09-02
**Status:** Approved design (pending spec review)

## Summary

Remove Pane's plugin architecture entirely and fold its five features
directly into the app so that everything is built, versioned, and
deployed as a single application. No dynamic DLL loading, no
marketplace, no per-plugin release/versioning. All five current
features are preserved as built-in functionality; only the plumbing is
removed.

## Motivation

The plugin system adds substantial machinery — a dedicated
abstractions layer, isolated `AssemblyLoadContext` loading/unloading,
a marketplace (fetch/cache/install/update/uninstall), a declarative
per-plugin settings schema, and a whole separate CI + distribution
pipeline (`release-please` `plugins-vX.Y.Z` component, per-plugin
release zips, `marketplace.json` URL sync, plugin deployment in
`install.sh`). All five plugins that exist are first-party and ship
with the app anyway, so the dynamic extensibility buys complexity
without a current payoff. Consolidating to a single app removes that
complexity and gives one build unit, one version, and one download.

## Decisions (locked)

1. **Keep all 5 features** — Apps, Files, Calculator, Scripts, VSCode
   — as built-in functionality.
2. **Fully collapse — no `IPlugin` abstraction.** Each feature is a
   concrete service class the dispatcher calls directly. No shared
   plugin interface, no reflection-based discovery.
3. **Keep settings, simplified.** Feature configuration is driven by a
   typed settings model in built-in code, not a declarative plugin
   schema. Includes enable/disable per feature.
4. **Feature code lives in `Pane.Core/Features/*`** — one build unit
   for all logic; the five separate `Pane.Plugins.*` projects are
   deleted.
5. **Delete everything else as listed** — no dormant marketplace code,
   no separate settings format preservation constraint beyond normal
   backward-compatible load.

## Architecture

### Target project graph

```
Pane.App          # entry point, DI wiring, window, launch modes (unchanged in shape)
Pane.Core         # dispatcher, ranking, settings, shared contracts, AND all 5 features
  Contracts/      # PaneQuery, PaneResult, IFuzzyMatcher, IPluginLogger (relocated)
  Query/          # QueryDispatcher, ResultRanker (kept)
  Settings/       # SettingsStore + typed PaneSettings model
  Features/
    Apps/         # app indexers (mac/win/linux), launch
    Files/        # OS file index search (mdfind/locate/win), debounce + wildcards
    Calculator/   # expression evaluation
    Scripts/      # script discovery + execution
    VSCode/       # repo scan + open in VS Code
Pane.Ui           # Launcher, ResultList, SettingsPage (reworked, no marketplace)
Pane.Platform     # macOS hotkeys, status bar, file picker (unchanged)
```

Deleted projects: `Pane.Abstractions`, `Pane.Plugins.Apps`,
`Pane.Plugins.Files`, `Pane.Plugins.Calculator`,
`Pane.Plugins.Scripts`, `Pane.Plugins.VSCode`.

### Removals

- **`Pane.Abstractions`** — deleted. `IPlugin`, `IPluginContext`,
  `PluginMetadata`, `PluginSettingSpec` are plugin-only concepts and
  are removed. The genuinely shared contracts (`PaneQuery`,
  `PaneResult`, `IFuzzyMatcher`, `IPluginLogger`) move into
  `Pane.Core/Contracts/`.
- **`Pane.Core/Loading/`** (`PluginLoader`, `PluginLoadContext`) —
  deleted. No DLL loading, no custom `AssemblyLoadContext`.
- **`Pane.Core/Plugins/PluginManager`** — deleted. Install / update /
  uninstall / hot-reload all removed.
- **`Pane.Core/Marketplace/`** — deleted in full (`MarketplaceService`,
  `MarketplaceConfigStore`, `InstalledStore`, `MarketplaceJson`, HTTP
  cache).
- **`Pane.Ui/MarketplacePane.razor`** — deleted. `PluginsPane`
  reworked into a plain feature-settings form.
- **Runtime dirs no longer used:** `~/.config/pane/plugins/`,
  `marketplaces.json`, `installed.json`, `marketplace-cache/`. The app
  stops reading/writing them. (No migration/cleanup of existing
  installs is required; stale dirs are simply ignored.)

### Features as concrete services

Each feature becomes a concrete class with a plain query method — no
shared interface:

- `AppsFeature`, `FilesFeature`, `CalculatorFeature`,
  `ScriptsFeature`, `VSCodeFeature`.
- Each is registered in DI as its concrete type.
- Each exposes an activation keyword where relevant (`/` → Files,
  `=` → Calculator) and a query method returning `IReadOnlyList<PaneResult>`.

The internal query method shape (not a shared interface — just a
consistent method signature per feature):

```csharp
Task<IReadOnlyList<PaneResult>> QueryAsync(PaneQuery query, CancellationToken ct);
```

Feature-specific dependencies (platform indexers, expression parser,
script runner, repo scanner) move with each feature into its folder.
The `IPluginContext` conveniences (logger, data directory, settings,
fuzzy matcher) are provided to each feature by ordinary constructor
injection instead of a context object.

### Query dispatch

`QueryDispatcher` takes the five feature services as explicit
constructor dependencies and calls them directly. Behavior preserved:

- **Prefix routing:** a leading `/` restricts to Files; `=` restricts
  to Calculator; otherwise all enabled non-keyword features are queried.
- **Per-feature timeout:** 2s cap per feature (as today).
- **Error isolation:** an exception from one feature is caught and
  yields no results from that feature; others are unaffected.
- **Enable/disable:** disabled features (per settings) are skipped.

`ResultRanker` and `IFuzzyMatcher` are unchanged; ranking still uses
`PaneResult.BaseScore`, fuzzy distance, and a per-feature priority
constant (priority becomes a constant on each feature instead of
`PluginMetadata.Priority`).

### Settings

Replace the declarative `PluginSettingSpec` schema with a typed
record:

```csharp
public record PaneSettings
{
    public string Hotkey { get; init; } = "Alt+Space";
    public AppsSettings Apps { get; init; } = new();
    public FilesSettings Files { get; init; } = new();
    public CalculatorSettings Calculator { get; init; } = new();
    public ScriptsSettings Scripts { get; init; } = new();
    public VSCodeSettings VSCode { get; init; } = new();
    // each *Settings has an Enabled flag + that feature's real options
}
```

- `SettingsStore` continues to persist to
  `~/.config/pane/settings.json`, using backward-compatible load
  (unknown/legacy keys such as `PluginSettings` are ignored; missing
  keys fall back to defaults).
- The settings page is a **hand-written Blazor form** over
  `PaneSettings` (one section per feature) — no schema-driven
  rendering.
- On save, features read updated settings via the settings store /
  constructor-injected snapshot; no assembly reload is involved.

### Build & deployment → one app

- **`build/package-plugins.sh`** and **`build/sync-marketplace.sh`** —
  deleted.
- **`build/make-app.sh`** — simplified to publish `Pane.App` and
  assemble the `.app` bundle only; no plugin staging into
  `dist/plugins/`.
- **`marketplace.json`** — deleted.
- **`install.sh`** — simplified to download (or `--local` build) and
  install only the app; no plugin deployment to
  `~/.config/pane/plugins/`. LaunchAgent registration and quarantine
  clearing are retained.
- **`release-please`** — collapse the `plugins` component to a single
  app version. CI builds one `Pane-<rid>.zip` per release and uploads
  it; no per-plugin zips, no `marketplace.json` sync step.

### Tests

- **Deleted:** plugin-loading tests, marketplace tests, and the
  `TestPlugin` / `TestPluginV2` / `ThrowingPlugin` fixtures.
- **Migrated:** the existing per-plugin feature tests
  (`Pane.Plugins.*.Tests`) are moved to exercise the new feature
  classes in `Pane.Core` (same assertions, updated construction — no
  `IPluginContext`, direct constructor injection).
- **Kept:** `QueryDispatcher` and `ResultRanker` tests, updated to
  construct the dispatcher with concrete feature dependencies (or test
  doubles for them).

## Non-goals

- No new features or behavior changes to the five existing features
  (the recent Files debounce + wildcard work is preserved as-is).
- No third-party extensibility. This change intentionally removes the
  ability to load external plugins.
- No migration tooling for existing on-disk plugin installs; stale
  `~/.config/pane/plugins/` content is simply ignored.

## Risks & mitigations

- **Large mechanical diff across many files.** Mitigate by sequencing:
  relocate shared contracts first, fold one feature at a time behind
  the dispatcher, then delete the plugin/marketplace layers, then
  update build/CI last. Keep the app building at each step.
- **Settings backward compatibility.** Existing `settings.json` files
  contain a `PluginSettings` map; the new loader must ignore it
  gracefully and not throw. Covered by a load test with a legacy file.
- **CI/release regression.** The release pipeline changes shape;
  validate a dry-run release build produces a single working
  `Pane-<rid>.zip` before relying on it.

## Success criteria

- Solution builds with `Pane.Abstractions` and all `Pane.Plugins.*`
  projects removed.
- All five features work from a fresh install with an empty
  `~/.config/pane` (no `plugins/`, `marketplace*`, `installed.json`).
- Settings page shows per-feature configuration and enable/disable;
  disabling a feature removes its results.
- A release build produces one app zip; `install.sh` installs a
  working app with no plugin steps.
- Test suite passes with migrated feature tests and no plugin/marketplace tests.
