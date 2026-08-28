# Plugin Marketplaces, URL Install, Updates, and Per-Plugin Settings

Date: 2026-08-28
Status: Approved for planning (Phase 1 first)

## Summary

Turn Pane's settings page into a plugin-management surface modeled on Claude
Code's plugin/marketplace system. Users do not add plugins one at a time; they
add **marketplaces** — JSON indexes that each list many plugins — and install
from an aggregated, browsable catalog. Pane ships a **default marketplace
(`marketplace.json` at the repo root)** listing the official plugins, whose
`source` points at **GitHub release zip assets**. Users may add additional
marketplace URLs (e.g. a team's private index).

The work decomposes into two sequential phases:

- **Phase 1 — Primitives** (implement first): install a plugin from a zip URL
  or local path, on-demand version-compare + in-place update, and per-plugin
  settings declared by a typed schema. This is the machinery the marketplace
  drives.
- **Phase 2 — Marketplaces**: the `marketplace.json` schema, the default
  marketplace at the repo root, a `marketplaces.json` add/remove list,
  fetch/aggregate/cache, the gallery + "Manage marketplaces" UI, installed-plugin
  tracking, and index-driven update checks.

All UI lives in the existing settings page (reached by typing `settings`; no new
window or route). No hotkey/app-level settings change in this work.

## Background / Current State

- **App**: macOS Spotlight-style launcher. .NET 10, Blazor components in
  Photino.Blazor. Plugins are separate dlls loaded via `AssemblyLoadContext`.
- **Settings page**: `Pane.Ui/Settings/SettingsPage.razor` renders a single
  `PluginsPane.razor` (enable/disable/uninstall + install-from-local-folder).
  The `<nav>` is single-tab today but built to hold more.
- **Plugin lifecycle**: `Pane.Core/Plugins/PluginManager.cs` loads dlls from
  `~/.config/pane/plugins/<name>/`, supports Enable/Disable/Uninstall and
  `InstallAsync(localPath)`.
- **Bundled plugins**: `build/copy-plugins.sh` compiles `Pane.Plugins.*` and
  copies each dll into `~/.config/pane/plugins/Pane.Plugins.<name>/`. There is
  no marketplace concept yet — plugins simply appear in that folder.
- **Settings storage**: `SettingsStore` serializes
  `PaneSettings(HashSet<string> DisabledPlugins, string Hotkey)` to
  `~/.config/pane/settings.json`.
- **Plugin contract**: `IPluginContext.Settings`
  (`IReadOnlyDictionary<string,string>`) is already threaded into every plugin,
  but `PluginManager` always passes an **empty** dictionary. Delivery exists;
  declaration, storage, and UI do not.

## Model (adapted from Claude Code)

| Claude Code | Pane |
|---|---|
| `.claude-plugin/marketplace.json` in a repo | `marketplace.json` at Pane's **repo root** = default marketplace |
| `{ name, description, owner, plugins: [...] }` | same shape |
| plugin entry `{ name, description, version, author, category, source, homepage }` | same, plus `icon` (Pane shows emoji icons) |
| `source: "./path"` or `{ source, url, ref, sha }` | `source: { type: "url", url: "…/plugin.zip" }` (GitHub release asset) or `{ type: "local", path }` for dev |
| `known_marketplaces.json` | `~/.config/pane/marketplaces.json` |
| `installed_plugins.json` | `~/.config/pane/installed.json` |
| `/plugin marketplace add`, `/plugin install` | Marketplace tab: add marketplace, one-click install |

## Goals

- Users install plugins from a curated, aggregated catalog, not by pasting
  per-plugin URLs.
- Pane ships a default marketplace at the repo root; official plugins download
  as GitHub release zip assets.
- Users can add/remove additional marketplaces by URL.
- On-demand update checks compare installed vs. marketplace-advertised versions
  and update in place without a restart.
- Plugins declare a typed settings schema; users edit values in settings and
  changes apply immediately.
- Full backward compatibility with existing `settings.json` and with plugins
  that declare no schema.

## Non-Goals

- No automatic/background update checking (on-demand only).
- No signature verification / sandboxing of downloaded plugins (flagged risk;
  future work).
- No per-plugin custom UI components (declarative schema only).
- No hotkey or other app-level settings changes here.
- Phase 2's detailed UI copy/layout is refined during Phase 2 planning.

---

## Phase 1 — Primitives

### 1.1 Data model

`PaneSettings` gains per-plugin settings; sources are tracked in Phase 2's
`installed.json`, but Phase 1 introduces `PluginSettings` now:

```csharp
public record PaneSettings(
    HashSet<string> DisabledPlugins,
    string Hotkey,
    Dictionary<string, Dictionary<string, string>> PluginSettings); // id → (key → value)
```

`SettingsStore.Load()` must tolerate legacy files: missing properties
deserialize to null, so `Load()` normalizes nulls to empty collections.
`PersistDisabled()` (and any settings save) must preserve **all** fields — never
reconstruct a partial `PaneSettings` — or toggling a plugin would wipe settings.

Installed **version** is read live from `PluginMetadata.Version`; not stored in
Phase 1.

### 1.2 Plugin settings schema (declaration)

`Pane.Abstractions` gains:

```csharp
public enum PluginSettingType { Text, Boolean, Number, Choice }

public record PluginSettingSpec(
    string Key, string Label, PluginSettingType Type,
    string? Default = null, string? Description = null,
    IReadOnlyList<string>? Choices = null);   // required when Type == Choice
```

`PluginMetadata` gains one optional trailing field so existing constructor calls
keep compiling:

```csharp
public record PluginMetadata(
    string Id, string Name, string Icon, string Version,
    string Description, IReadOnlyList<string> Keywords,
    string? Keyword = null, int Priority = 0,
    IReadOnlyList<PluginSettingSpec>? Settings = null);   // null → no settings
```

All four field types ship in v1. Values are stored/delivered as strings
(Boolean = `"true"`/`"false"`, Number = invariant-culture string); plugins parse
as needed.

### 1.3 Settings delivery

`PluginManager.LoadOneAsync` builds the dictionary handed to `PluginContext` by
overlaying stored values (`PaneSettings.PluginSettings[id]`) on schema defaults,
so a plugin always sees every declared key. No schema + no stored values ⇒ empty
dictionary (unchanged behavior).

### 1.4 Fetch / install / update subsystem

New `PluginFetcher` in `Pane.Core` wraps `HttpClient` +
`System.IO.Compression.ZipFile`:

- `Task<string> DownloadAndExtractAsync(string url, CancellationToken ct)` —
  download zip → temp file → extract to a fresh temp dir → return the dir path.
  Throws typed `PluginFetchException` on network / not-a-zip / empty-archive.

`HttpClient` (with a sane timeout) is registered as a singleton in `Program.cs`
and injected into `PluginFetcher`; `PluginFetcher` is injected into
`PluginManager`.

`PluginManager` gains, reusing existing copy/unload/`LoadOneAsync` helpers:

- `Task<PluginEntry> InstallFromUrlAsync(string url, CancellationToken ct)` —
  fetch+extract → locate dll (prefer `Pane.Plugins*.dll`, else first) → read
  metadata for id → copy extracted folder into `plugins/<id>/` → `LoadOneAsync`.
  Only copies into `plugins/` after the dll validates, so failures leave nothing
  behind.
- `Task<UpdateCheck> CheckForUpdateAsync(string id, string sourceUrl, CancellationToken ct)`
  — fetch+extract the source → read remote `Version` → compare to installed →
  return `UpdateCheck(bool Available, string InstalledVersion, string? RemoteVersion, string? Error)`.
  Installs nothing. (Phase 2 replaces the per-zip fetch with an index read.)
  Version compare: `System.Version` when both parse; else ordinal
  "differs ⇒ update available" so non-semver strings work.
- `Task UpdateAsync(string id, string sourceUrl, CancellationToken ct)` —
  fetch+extract → unload the running plugin (dispose instance, `Ctx.Unload()`,
  null both) → `GC.Collect(); GC.WaitForPendingFinalizers();` → overwrite files
  in `plugins/<id>/` with a **bounded retry loop** (≈5 attempts, ≈100ms apart)
  to ride out post-unload file locks → `LoadOneAsync` to reload. On retry
  exhaustion, surface an error and leave the old version loaded.
- Settings API: `UpdatePluginSettingsAsync(id, values)` (persist + dispose/reload
  so the plugin picks up new values) and `GetPluginSettings(id)` (stored ∪
  defaults, for the form).

Temp dirs cleaned in `finally`.

### 1.5 Phase 1 UI (`PluginsPane.razor`)

- Keep the **Install from folder…** button (plugin developers testing locally).
  No per-plugin URL paste box (superseded by marketplaces).
- Each plugin row gains a **Settings** disclosure *if* the plugin declares a
  non-empty schema. Expanding renders a form: Text → text input, Boolean →
  checkbox, Number → number input, Choice → `<select>`. Each shows `Label` +
  optional `Description`. **Save** → `UpdatePluginSettingsAsync`; **Reset** →
  schema defaults. Number inputs validated before Save.
- CSS additions in `pane.css` for the settings form, reusing existing tokens.

Update UI is wired into the marketplace flow in Phase 2 (the primitive
`CheckForUpdateAsync`/`UpdateAsync` exist after Phase 1 and are unit-tested).

---

## Phase 2 — Marketplaces

### 2.1 Marketplace schema

`marketplace.json` (root of repo = default marketplace):

```json
{
  "name": "Pane Official",
  "description": "Official Pane plugins",
  "owner": { "name": "pane", "url": "https://github.com/dknathalage/pane" },
  "plugins": [
    {
      "id": "com.pane.apps",
      "name": "Apps",
      "description": "Launch installed applications",
      "icon": "🚀",
      "version": "1.0.0",
      "category": "system",
      "author": "pane",
      "homepage": "https://github.com/dknathalage/pane",
      "source": {
        "type": "url",
        "url": "https://github.com/dknathalage/pane/releases/download/plugins-v1.0.0/Pane.Plugins.Apps.zip"
      }
    }
  ]
}
```

`source.type` is `"url"` (GitHub release zip) for official/remote plugins, or
`"local"` `{ path }` for developer marketplaces. New types can be added later.

### 2.2 Marketplace sources config

`~/.config/pane/marketplaces.json` lists configured marketplaces:

```json
{
  "marketplaces": [
    { "name": "Pane Official", "source": "https://raw.githubusercontent.com/dknathalage/pane/main/marketplace.json", "builtIn": true },
    { "name": "My Team", "source": "https://…/team-marketplace.json" }
  ]
}
```

The built-in default entry is seeded on first run and cannot be removed (only
disabled). Users add/remove their own entries. `source` is a URL to a
`marketplace.json` (or a local path for dev).

### 2.3 Installed-plugin tracking

`~/.config/pane/installed.json` records provenance so updates know where to look:

```json
{ "plugins": { "com.pane.apps": { "marketplace": "Pane Official", "sourceUrl": "https://…/Pane.Plugins.Apps.zip", "version": "1.0.0" } } }
```

Written on install/update; read for update checks and gallery "installed" state.

### 2.4 Fetch / aggregate / cache

`MarketplaceService` (new, `Pane.Core`):
- Fetch each configured `marketplace.json` (HTTP or local) via the Phase 1
  `HttpClient`; parse; cache the last good copy on disk (`marketplaces-cache/`)
  so the gallery works offline and tolerates a flaky source.
- Aggregate all marketplaces into one plugin list; annotate each entry with its
  marketplace + installed/available/update state (join against `installed.json`).
- Duplicate plugin `id` across marketplaces: first configured marketplace wins;
  surface a note (mirrors PluginManager's existing duplicate-id guard).

### 2.5 Index-driven updates

`CheckForUpdateAsync` in Phase 2 reads the aggregated index (one fetch covers all
plugins) instead of downloading each zip: compare `installed.json` version vs.
the marketplace entry's `version`. `UpdateAsync` then downloads only the entry's
`source.url` and applies via the Phase 1 in-place path.

### 2.6 Phase 2 UI

- Add a **Marketplace** tab beside **Plugins** in `SettingsPage.razor` (`<nav>`
  becomes multi-tab with active-tab state).
- **Marketplace tab**: browse the aggregated catalog (icon, name, description,
  version, source marketplace, category). Each entry shows **Install**,
  **Installed**, or **Update → vX.Y**. One-click actions call
  `InstallFromUrlAsync` / `UpdateAsync`. Loading + inline error states.
- **Manage marketplaces** (section or sub-view): list configured marketplaces,
  add by URL, remove user ones, refresh. The built-in default is shown as
  non-removable.
- **Plugins tab** stays as the installed-plugin manager (enable/disable/uninstall
  + per-plugin settings from Phase 1), now also showing "Update available" badges
  fed by the index.
- CSS for the gallery grid/cards + manage-marketplaces list in `pane.css`.

---

## Data Flow

- **Add marketplace**: user enters URL → `MarketplaceService` fetches + caches →
  gallery re-aggregates.
- **Install from gallery**: entry → `InstallFromUrlAsync(source.url)` → copy to
  `plugins/<id>/` → `LoadOneAsync` → record in `installed.json` → row flips to
  "Installed".
- **Update**: index compare → "Update → vX.Y" → `UpdateAsync(id, source.url)`
  (download, unload, overwrite w/ retry, reload) → bump `installed.json`.
- **Per-plugin settings**: plugin declares schema → form seeded by
  `GetPluginSettings` → Save → `UpdatePluginSettingsAsync` persists + reloads →
  `LoadOneAsync` merges values into `PluginContext.Settings` → plugin reads them
  in `InitializeAsync`.

## Error Handling

- Network / non-zip / no-dll-in-archive → `PluginFetchException`; UI shows inline
  error; nothing partial left on disk.
- Marketplace fetch failure → fall back to on-disk cache; badge the marketplace
  as "offline / cached".
- Update file-lock exhaustion after retries → error surfaced; old version stays
  loaded.
- Duplicate plugin id (within/among marketplaces, or on install) → existing
  duplicate-id guard; first wins.
- Invalid Number setting → validated before Save.
- Legacy `settings.json` / missing config files → normalized to empty/defaults.

## Testing

**Phase 1**
- `SettingsStore`: round-trips `PluginSettings`; loads a legacy 2-field JSON to
  empty collections.
- Settings merge: defaults-only, stored-overrides-default, unknown-stored-key,
  no-schema.
- `PluginFetcher`: extracts a valid zip; typed errors for bad-url / non-zip /
  empty (via `file://` or an `HttpMessageHandler` test double).
- Version compare: semver newer/older/equal; non-semver "differs".
- `InstallFromUrlAsync`: installs from a local test zip; plugin becomes queryable.
- `UpdateAsync`: v1 → v2 test zip, asserts new version loaded; retry loop covered.
- UI: PluginsPane renders each field type; Save calls manager + reload (bUnit if
  available, else manager-level tests).

**Phase 2**
- Marketplace parse: valid schema; rejects malformed; unknown `source.type`
  handled.
- Aggregation: merges multiple marketplaces; duplicate-id resolution; installed
  vs. available vs. update-available join.
- Cache: serves last-good copy when a source is unreachable.
- `marketplaces.json` / `installed.json` round-trip; built-in default seeded and
  non-removable.
- Index-driven `CheckForUpdateAsync` needs no per-zip download.
- UI: gallery Install/Installed/Update states; add/remove marketplace.

## Risks / Open Considerations

- **Security**: downloading + loading arbitrary dlls from a marketplace URL is
  unsandboxed code execution. Out of scope now; flagged for a future signing /
  trust-prompt / checksum story (the Claude model carries a `sha` — Pane could
  add an optional checksum per entry later).
- **File locks on update**: mitigated by GC + bounded retry; residual small race
  documented; stage-and-swap-on-launch is the fallback if it proves flaky.
- **`PersistDisabled` partial-write**: must preserve all `PaneSettings` fields.
- **Release hosting**: official plugin zips depend on a GitHub releases pipeline
  (build → zip each `Pane.Plugins.*` → attach to a `plugins-vX.Y.Z` release).
  A release/packaging step is a Phase 2 prerequisite for real official installs;
  local-folder install covers dev in the meantime.

## Affected / New Files

**Phase 1**
- `Pane.Abstractions/PluginMetadata.cs` — `PluginSettingType`,
  `PluginSettingSpec`; optional `Settings` on `PluginMetadata`.
- `Pane.Core/Settings/SettingsStore.cs` — extend `PaneSettings`, null-normalize.
- `Pane.Core/Plugins/PluginManager.cs` — URL/local install, update check/apply,
  settings get/set, merged delivery, full-record persistence.
- `Pane.Core/Plugins/PluginFetcher.cs` — **new**.
- `Pane.Core/Plugins/UpdateCheck.cs`, `PluginFetchException.cs` — **new**.
- `Pane.App/Program.cs` — register `HttpClient` / `PluginFetcher`.
- `Pane.Ui/Settings/PluginsPane.razor` + `pane.css` — settings form.
- Tests under `tests/`.

**Phase 2**
- `marketplace.json` — **new**, repo root (default marketplace).
- `Pane.Core/Marketplace/MarketplaceModels.cs` (schema records),
  `MarketplaceService.cs`, config/installed stores — **new**.
- `Pane.Ui/Settings/SettingsPage.razor` — multi-tab nav.
- `Pane.Ui/Settings/MarketplacePane.razor`, `ManageMarketplaces.razor` — **new**.
- `pane.css` — gallery + manage styles.
- `build/` — plugin zip/release packaging step.
- Tests under `tests/`.
</content>
