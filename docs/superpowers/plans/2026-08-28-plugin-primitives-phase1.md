# Plugin Primitives (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the plugin-management primitives — install from a zip URL, on-demand version-compare + in-place update, and per-plugin typed settings — that the Phase 2 marketplace layer will drive.

**Architecture:** Extend `PaneSettings` and `PluginMetadata` with settings support; add a `PluginFetcher` (HttpClient + zip extract) in `Pane.Core`; grow `PluginManager` with `InstallFromUrlAsync` / `CheckForUpdateAsync` / `UpdateAsync` / per-plugin settings APIs that reuse its existing load/unload machinery; render a settings form in the existing `PluginsPane.razor`. No new NuGet packages — `HttpClient` and `System.IO.Compression` ship in the framework.

**Tech Stack:** .NET 10, C#, Blazor (Photino), xUnit. Plugins load via collectible `AssemblyLoadContext` (`PluginLoader.CreateContext`).

**Spec:** `docs/superpowers/specs/2026-08-28-plugin-settings-and-url-install-design.md` (Phase 1 section)

## Global Constraints

- **Target framework:** `net10.0` for every project (matches existing).
- **No new NuGet packages.** Use framework `System.Net.Http.HttpClient` and `System.IO.Compression` / `System.IO.Compression.ZipFile`.
- **Test framework:** xUnit (`[Fact]`, `Assert.*`). New `Pane.Core` tests go in `tests/Pane.Core.Tests/`.
- **Backward compatibility:** an existing `~/.config/pane/settings.json` written by the current app (only `DisabledPlugins` + `Hotkey`) MUST still load — missing fields normalize to empty.
- **Temp/cleanup conventions:** temp dirs via `Path.Combine(Path.GetTempPath(), "pane-...-" + Guid.NewGuid().ToString("N"))`; best-effort deletes swallow exceptions (mirror `PluginManager.TryDelete`).
- **Settings values are strings:** Boolean = `"true"`/`"false"`, Number = invariant-culture string. Plugins parse as needed.
- **Fixtures are separate projects** listed in `Pane.slnx`; the test project excludes `Fixtures/**` from compile and resolves fixture dlls at runtime via `Fixtures/<name>/bin/<config>/net10.0`.

---

## File Structure

**Modified**
- `src/Pane.Abstractions/PluginMetadata.cs` — add `PluginSettingType`, `PluginSettingSpec`; add optional `Settings` field to `PluginMetadata`.
- `src/Pane.Core/Settings/SettingsStore.cs` — add `PluginSettings` to `PaneSettings`; null-normalize on load.
- `src/Pane.Core/Plugins/PluginManager.cs` — settings delivery/merge, `GetPluginSettings`, `UpdatePluginSettingsAsync`, generalized `Persist`, `InstallFromUrlAsync`, `CheckForUpdateAsync`, `UpdateAsync`, `FindPluginDll`, optional `PluginFetcher` ctor arg.
- `src/Pane.App/Program.cs` — register `HttpClient` + `PluginFetcher`; pass fetcher to `PluginManager`.
- `src/Pane.Ui/Settings/PluginsPane.razor` — per-plugin settings form.
- `src/Pane.Ui/wwwroot/pane.css` — settings-form styles.
- `Pane.slnx` — add the `TestPluginV2` fixture project.
- `tests/Pane.Core.Tests/SettingsStoreTests.cs` — new-field + legacy-load tests (and fix the existing `new PaneSettings(...)` call).

**Created**
- `src/Pane.Core/Plugins/PluginSettingsMerge.cs` — pure defaults∪stored merge.
- `src/Pane.Core/Plugins/PluginFetcher.cs` — download + extract.
- `src/Pane.Core/Plugins/PluginFetchException.cs` — typed fetch error.
- `src/Pane.Core/Plugins/UpdateCheck.cs` — `UpdateCheck` record + `PluginVersion` compare helper.
- `tests/Pane.Core.Tests/PluginSettingsMergeTests.cs`
- `tests/Pane.Core.Tests/PluginFetcherTests.cs`
- `tests/Pane.Core.Tests/PluginInstallUpdateTests.cs`
- `tests/Pane.Core.Tests/Fixtures/TestPluginV2/TestPluginV2.csproj` + `TestPlugin.cs` (assembly name `TestPlugin`, version `2.0`).

---

## Task 1: Extend PaneSettings with per-plugin settings (backward-compatible)

**Files:**
- Modify: `src/Pane.Core/Settings/SettingsStore.cs`
- Modify: `src/Pane.Core/Plugins/PluginManager.cs` (fix the one `new PaneSettings(...)` call site in `PersistDisabled`)
- Test: `tests/Pane.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Produces: `PaneSettings(HashSet<string> DisabledPlugins, string Hotkey, Dictionary<string, Dictionary<string,string>> PluginSettings)`; `SettingsStore.Load()` never returns null collections.

- [ ] **Step 1: Write the failing tests**

Add to `tests/Pane.Core.Tests/SettingsStoreTests.cs`:

```csharp
[Fact]
public void Roundtrips_per_plugin_settings()
{
    var path = Path.Combine(Path.GetTempPath(), $"pane-{Guid.NewGuid():N}.json");
    var store = new SettingsStore(path);
    store.Save(new PaneSettings(
        new HashSet<string>(), "Alt+Space",
        new Dictionary<string, Dictionary<string, string>>
        {
            ["p1"] = new() { ["k"] = "v" }
        }));
    var s = store.Load();
    Assert.Equal("v", s.PluginSettings["p1"]["k"]);
}

[Fact]
public void Loads_legacy_two_field_json_with_empty_plugin_settings()
{
    var path = Path.Combine(Path.GetTempPath(), $"pane-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, "{\"DisabledPlugins\":[\"x\"],\"Hotkey\":\"Alt+Space\"}");
    var store = new SettingsStore(path);
    var s = store.Load();
    Assert.Contains("x", s.DisabledPlugins);
    Assert.NotNull(s.PluginSettings);
    Assert.Empty(s.PluginSettings);
}
```

Also update the existing `Save_then_load_roundtrips` test's constructor call to the 3-arg form:

```csharp
store.Save(new PaneSettings(new HashSet<string> { "x" }, "Alt+Space", new()));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: compile failure / FAIL — `PaneSettings` has no 3rd parameter / `PluginSettings` not found.

- [ ] **Step 3: Extend `PaneSettings` and null-normalize in `Load`**

In `src/Pane.Core/Settings/SettingsStore.cs`:

```csharp
public record PaneSettings(
    HashSet<string> DisabledPlugins,
    string Hotkey,
    Dictionary<string, Dictionary<string, string>> PluginSettings);
```

Update `Load()` to normalize nulls (legacy files lack the new field, and could lack the others):

```csharp
public PaneSettings Load()
{
    if (!File.Exists(_path)) return new PaneSettings(new(), DefaultHotkey, new());
    try
    {
        var s = JsonSerializer.Deserialize<PaneSettings>(File.ReadAllText(_path));
        if (s is null) return new PaneSettings(new(), DefaultHotkey, new());
        return s with
        {
            DisabledPlugins = s.DisabledPlugins ?? new(),
            Hotkey = string.IsNullOrEmpty(s.Hotkey) ? DefaultHotkey : s.Hotkey,
            PluginSettings = s.PluginSettings ?? new()
        };
    }
    catch { return new PaneSettings(new(), DefaultHotkey, new()); }
}
```

- [ ] **Step 4: Fix the `PersistDisabled` call site in `PluginManager`**

In `src/Pane.Core/Plugins/PluginManager.cs`, `PersistDisabled()` currently does:

```csharp
var hotkey = _store.Load().Hotkey;
_store.Save(new PaneSettings(new HashSet<string>(_disabled), hotkey));
```

Change it to preserve all fields by round-tripping the loaded record:

```csharp
void PersistDisabled()
{
    if (_store is null) return;
    var current = _store.Load();
    _store.Save(current with { DisabledPlugins = new HashSet<string>(_disabled) });
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~SettingsStoreTests"`
Expected: PASS (all SettingsStore tests, including the updated existing one).

- [ ] **Step 6: Commit**

```bash
git add src/Pane.Core/Settings/SettingsStore.cs src/Pane.Core/Plugins/PluginManager.cs tests/Pane.Core.Tests/SettingsStoreTests.cs
git commit -m "feat(settings): per-plugin settings field with backward-compatible load"
```

---

## Task 2: Plugin settings schema types

**Files:**
- Modify: `src/Pane.Abstractions/PluginMetadata.cs`
- Test: `tests/Pane.Abstractions.Tests/ContractTests.cs`

**Interfaces:**
- Produces:
  - `enum PluginSettingType { Text, Boolean, Number, Choice }`
  - `record PluginSettingSpec(string Key, string Label, PluginSettingType Type, string? Default = null, string? Description = null, IReadOnlyList<string>? Choices = null)`
  - `PluginMetadata` gains trailing optional `IReadOnlyList<PluginSettingSpec>? Settings = null`.

- [ ] **Step 1: Write the failing test**

Add to `tests/Pane.Abstractions.Tests/ContractTests.cs`:

```csharp
[Fact]
public void PluginMetadata_defaults_settings_to_null_and_carries_a_schema()
{
    var bare = new PluginMetadata("id", "Name", "🧪", "1.0", "desc", new[] { "kw" });
    Assert.Null(bare.Settings);

    var withSchema = bare with
    {
        Settings = new[]
        {
            new PluginSettingSpec("apiKey", "API Key", PluginSettingType.Text),
            new PluginSettingSpec("theme", "Theme", PluginSettingType.Choice,
                Default: "dark", Choices: new[] { "light", "dark" })
        }
    };
    Assert.Equal("apiKey", withSchema.Settings![0].Key);
    Assert.Equal(PluginSettingType.Choice, withSchema.Settings![1].Type);
    Assert.Equal("dark", withSchema.Settings![1].Default);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Abstractions.Tests -c Debug --filter "FullyQualifiedName~PluginMetadata_defaults_settings"`
Expected: compile failure — `PluginSettingSpec` / `PluginSettingType` / `Settings` not defined.

- [ ] **Step 3: Add the types**

In `src/Pane.Abstractions/PluginMetadata.cs`:

```csharp
public enum PluginSettingType { Text, Boolean, Number, Choice }

public record PluginSettingSpec(
    string Key,
    string Label,
    PluginSettingType Type,
    string? Default = null,
    string? Description = null,
    IReadOnlyList<string>? Choices = null);

public record PluginMetadata(
    string Id,
    string Name,
    string Icon,
    string Version,
    string Description,
    IReadOnlyList<string> Keywords,
    string? Keyword = null,
    int Priority = 0,
    IReadOnlyList<PluginSettingSpec>? Settings = null);
```

(Leave `PaneQuery` and `PaneResult` in this file unchanged.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Pane.Abstractions.Tests -c Debug --filter "FullyQualifiedName~PluginMetadata_defaults_settings"`
Expected: PASS.

- [ ] **Step 5: Verify nothing else broke (all existing plugins still compile)**

Run: `dotnet build Pane.slnx -c Debug`
Expected: build succeeds — the new param is optional, so existing `new PluginMetadata(...)` calls are unaffected.

- [ ] **Step 6: Commit**

```bash
git add src/Pane.Abstractions/PluginMetadata.cs tests/Pane.Abstractions.Tests/ContractTests.cs
git commit -m "feat(abstractions): declarative plugin settings schema types"
```

---

## Task 3: Settings merge + delivery to plugins

**Files:**
- Create: `src/Pane.Core/Plugins/PluginSettingsMerge.cs`
- Modify: `src/Pane.Core/Plugins/PluginManager.cs`
- Create: `tests/Pane.Core.Tests/PluginSettingsMergeTests.cs`

**Interfaces:**
- Consumes: `PluginSettingSpec` (Task 2), `PaneSettings.PluginSettings` (Task 1).
- Produces:
  - `static Dictionary<string,string> PluginSettingsMerge.Merge(IReadOnlyList<PluginSettingSpec>? schema, IReadOnlyDictionary<string,string>? stored)`
  - `IReadOnlyDictionary<string,string> PluginManager.GetPluginSettings(string id)`
  - `PluginManager` now loads stored per-plugin settings in its constructor and delivers merged settings into every `PluginContext`.

- [ ] **Step 1: Write the failing merge tests**

Create `tests/Pane.Core.Tests/PluginSettingsMergeTests.cs`:

```csharp
using Pane.Abstractions;
using Pane.Core.Plugins;
using Xunit;

public class PluginSettingsMergeTests
{
    static PluginSettingSpec Spec(string key, string? def) =>
        new(key, key, PluginSettingType.Text, Default: def);

    [Fact]
    public void Uses_schema_defaults_when_nothing_stored()
    {
        var merged = PluginSettingsMerge.Merge(new[] { Spec("a", "1"), Spec("b", "2") }, null);
        Assert.Equal("1", merged["a"]);
        Assert.Equal("2", merged["b"]);
    }

    [Fact]
    public void Stored_values_override_defaults()
    {
        var merged = PluginSettingsMerge.Merge(
            new[] { Spec("a", "1") },
            new Dictionary<string, string> { ["a"] = "override" });
        Assert.Equal("override", merged["a"]);
    }

    [Fact]
    public void Keys_without_a_default_are_absent_until_stored()
    {
        var merged = PluginSettingsMerge.Merge(new[] { Spec("a", null) }, null);
        Assert.False(merged.ContainsKey("a"));
    }

    [Fact]
    public void Stored_keys_not_in_schema_pass_through()
    {
        var merged = PluginSettingsMerge.Merge(
            null, new Dictionary<string, string> { ["x"] = "y" });
        Assert.Equal("y", merged["x"]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~PluginSettingsMergeTests"`
Expected: compile failure — `PluginSettingsMerge` not defined.

- [ ] **Step 3: Implement the merge helper**

Create `src/Pane.Core/Plugins/PluginSettingsMerge.cs`:

```csharp
using Pane.Abstractions;

namespace Pane.Core.Plugins;

public static class PluginSettingsMerge
{
    public static Dictionary<string, string> Merge(
        IReadOnlyList<PluginSettingSpec>? schema,
        IReadOnlyDictionary<string, string>? stored)
    {
        var result = new Dictionary<string, string>();
        if (schema is not null)
            foreach (var spec in schema)
                if (spec.Default is not null)
                    result[spec.Key] = spec.Default;
        if (stored is not null)
            foreach (var kv in stored)
                result[kv.Key] = kv.Value;
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~PluginSettingsMergeTests"`
Expected: PASS.

- [ ] **Step 5: Wire delivery + `GetPluginSettings` into `PluginManager`**

In `src/Pane.Core/Plugins/PluginManager.cs`:

Add a field and load it in the constructor (alongside the existing `_disabled` load):

```csharp
readonly Dictionary<string, Dictionary<string, string>> _pluginSettings = new();
```

Inside the constructor's `if (_store is not null)` block, after loading disabled ids:

```csharp
foreach (var kv in _store.Load().PluginSettings)
    _pluginSettings[kv.Key] = new Dictionary<string, string>(kv.Value);
```

In `LoadOneAsync`, replace the empty-dictionary argument to `PluginContext`:

```csharp
// was: new Dictionary<string, string>()
var settings = PluginSettingsMerge.Merge(
    meta.Settings, _pluginSettings.GetValueOrDefault(id));
var pctx = new PluginContext(id, Path.Combine(_dataRoot, "data", id), settings, _matcher);
```

Add the accessor (used later by the UI):

```csharp
public IReadOnlyDictionary<string, string> GetPluginSettings(string id)
{
    var meta = _plugins.TryGetValue(id, out var p) ? p.Metadata : null;
    return PluginSettingsMerge.Merge(meta?.Settings, _pluginSettings.GetValueOrDefault(id));
}
```

- [ ] **Step 6: Verify build + full test suite still green**

Run: `dotnet test tests/Pane.Core.Tests -c Debug`
Expected: PASS (existing PluginManager tests unaffected — merge of null schema + null stored yields an empty dict, matching prior behavior).

- [ ] **Step 7: Commit**

```bash
git add src/Pane.Core/Plugins/PluginSettingsMerge.cs src/Pane.Core/Plugins/PluginManager.cs tests/Pane.Core.Tests/PluginSettingsMergeTests.cs
git commit -m "feat(plugins): merge and deliver per-plugin settings into PluginContext"
```

---

## Task 4: Persist + apply edited plugin settings

**Files:**
- Modify: `src/Pane.Core/Plugins/PluginManager.cs`
- Test: `tests/Pane.Core.Tests/PluginManagerTests.cs`

**Interfaces:**
- Consumes: `_pluginSettings`, `GetPluginSettings` (Task 3), `PersistDisabled` → generalized `Persist`.
- Produces: `Task PluginManager.UpdatePluginSettingsAsync(string id, IReadOnlyDictionary<string,string> values)` — persists values (preserving all `PaneSettings` fields) and reloads the plugin so it re-reads settings.

- [ ] **Step 1: Write the failing test**

Add to `tests/Pane.Core.Tests/PluginManagerTests.cs`:

```csharp
[Fact]
public async Task Update_plugin_settings_persists_and_keeps_plugin_active()
{
    var root = NewPluginsRoot();
    var settingsPath = Path.Combine(Path.GetTempPath(),
        "pane-test-settings-" + Guid.NewGuid().ToString("N") + ".json");
    var store = new SettingsStore(settingsPath);

    CopyPlugin(root, "TestPlugin");
    var mgr = new PluginManager(dataRoot: root, store: store);
    await mgr.LoadAllAsync(root);

    await mgr.UpdatePluginSettingsAsync("test",
        new Dictionary<string, string> { ["greeting"] = "hi" });

    // Persisted to disk under the plugin id.
    Assert.Equal("hi", store.Load().PluginSettings["test"]["greeting"]);
    // Retrievable via the accessor.
    Assert.Equal("hi", mgr.GetPluginSettings("test")["greeting"]);
    // Plugin remains loaded/active after the reload.
    Assert.Single(mgr.Active());
}

[Fact]
public async Task Editing_settings_preserves_disabled_state_in_store()
{
    var root = NewPluginsRoot();
    var settingsPath = Path.Combine(Path.GetTempPath(),
        "pane-test-settings-" + Guid.NewGuid().ToString("N") + ".json");
    var store = new SettingsStore(settingsPath);

    CopyPlugin(root, "TestPlugin");
    var mgr = new PluginManager(dataRoot: root, store: store);
    await mgr.LoadAllAsync(root);

    await mgr.DisableAsync("test");
    await mgr.UpdatePluginSettingsAsync("test",
        new Dictionary<string, string> { ["k"] = "v" });

    var s = store.Load();
    Assert.Contains("test", s.DisabledPlugins);      // not clobbered
    Assert.Equal("v", s.PluginSettings["test"]["k"]);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~Update_plugin_settings|FullyQualifiedName~Editing_settings"`
Expected: FAIL — `UpdatePluginSettingsAsync` not defined.

- [ ] **Step 3: Generalize persistence and add the update method**

In `src/Pane.Core/Plugins/PluginManager.cs`, replace `PersistDisabled()` with a `Persist()` that writes every field, and update its two existing callers (`DisableAsync`, `EnableAsync`):

```csharp
void Persist()
{
    if (_store is null) return;
    var current = _store.Load();
    _store.Save(current with
    {
        DisabledPlugins = new HashSet<string>(_disabled),
        PluginSettings = _pluginSettings.ToDictionary(
            e => e.Key, e => new Dictionary<string, string>(e.Value))
    });
}
```

(Rename the calls `PersistDisabled();` → `Persist();` in `DisableAsync` and `EnableAsync`. Task 1 already routed `PersistDisabled` through `current with {...}`; this replaces it wholesale — delete the old `PersistDisabled` body.)

Add the update method:

```csharp
public async Task UpdatePluginSettingsAsync(string id, IReadOnlyDictionary<string, string> values)
{
    _pluginSettings[id] = new Dictionary<string, string>(values);
    Persist();

    // Reload an enabled plugin so InitializeAsync sees the new settings.
    if (_plugins.TryGetValue(id, out var p) && p.State == PluginState.Enabled)
    {
        if (p.Instance is not null) { await p.Instance.DisposeAsync(); p.Instance = null; }
        p.Ctx?.Unload(); p.Ctx = null;
        await LoadOneAsync(p.DllPath);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~Update_plugin_settings|FullyQualifiedName~Editing_settings"`
Expected: PASS.

- [ ] **Step 5: Run the whole Core suite (regression check on enable/disable persistence)**

Run: `dotnet test tests/Pane.Core.Tests -c Debug`
Expected: PASS (including the existing `Uninstall_clears_disabled_state_from_settings`).

- [ ] **Step 6: Commit**

```bash
git add src/Pane.Core/Plugins/PluginManager.cs tests/Pane.Core.Tests/PluginManagerTests.cs
git commit -m "feat(plugins): persist and apply edited per-plugin settings"
```

---

## Task 5: PluginFetcher — download and extract a plugin zip

**Files:**
- Create: `src/Pane.Core/Plugins/PluginFetchException.cs`
- Create: `src/Pane.Core/Plugins/PluginFetcher.cs`
- Create: `tests/Pane.Core.Tests/PluginFetcherTests.cs`

**Interfaces:**
- Produces:
  - `class PluginFetchException : Exception` (message + optional inner).
  - `class PluginFetcher(HttpClient http)` with:
    - `static string ExtractToTempDir(byte[] zipBytes)` → extracted temp dir path; throws `PluginFetchException` on invalid/empty zip.
    - `Task<string> DownloadAndExtractAsync(string url, CancellationToken ct = default)` → temp dir path; throws `PluginFetchException` on network failure.

- [ ] **Step 1: Write the failing tests**

Create `tests/Pane.Core.Tests/PluginFetcherTests.cs`:

```csharp
using System.IO.Compression;
using System.Net;
using Pane.Core.Plugins;
using Xunit;

public class PluginFetcherTests
{
    static byte[] MakeZip(params (string name, string content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name);
                using var s = e.Open();
                using var w = new StreamWriter(s);
                w.Write(content);
            }
        return ms.ToArray();
    }

    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_fn(request));
    }

    [Fact]
    public void ExtractToTempDir_writes_entries_to_disk()
    {
        var dir = PluginFetcher.ExtractToTempDir(MakeZip(("a.txt", "hello")));
        try { Assert.Equal("hello", File.ReadAllText(Path.Combine(dir, "a.txt"))); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExtractToTempDir_throws_typed_on_garbage_bytes()
    {
        Assert.Throws<PluginFetchException>(() =>
            PluginFetcher.ExtractToTempDir(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void ExtractToTempDir_throws_typed_on_empty_archive()
    {
        Assert.Throws<PluginFetchException>(() =>
            PluginFetcher.ExtractToTempDir(MakeZip()));
    }

    [Fact]
    public async Task DownloadAndExtractAsync_fetches_then_extracts()
    {
        var bytes = MakeZip(("plugin.txt", "ok"));
        var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        var fetcher = new PluginFetcher(http);

        var dir = await fetcher.DownloadAndExtractAsync("https://example/plugin.zip");
        try { Assert.Equal("ok", File.ReadAllText(Path.Combine(dir, "plugin.txt"))); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DownloadAndExtractAsync_wraps_network_errors()
    {
        var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));
        var fetcher = new PluginFetcher(http);

        await Assert.ThrowsAsync<PluginFetchException>(() =>
            fetcher.DownloadAndExtractAsync("https://example/missing.zip"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~PluginFetcherTests"`
Expected: compile failure — `PluginFetcher` / `PluginFetchException` not defined.

- [ ] **Step 3: Implement the exception**

Create `src/Pane.Core/Plugins/PluginFetchException.cs`:

```csharp
namespace Pane.Core.Plugins;

public sealed class PluginFetchException : Exception
{
    public PluginFetchException(string message, Exception? inner = null)
        : base(message, inner) { }
}
```

- [ ] **Step 4: Implement the fetcher**

Create `src/Pane.Core/Plugins/PluginFetcher.cs`:

```csharp
using System.IO.Compression;

namespace Pane.Core.Plugins;

public sealed class PluginFetcher
{
    readonly HttpClient _http;
    public PluginFetcher(HttpClient http) => _http = http;

    public async Task<string> DownloadAndExtractAsync(string url, CancellationToken ct = default)
    {
        byte[] bytes;
        try
        {
            var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is not PluginFetchException)
        {
            throw new PluginFetchException($"Failed to download plugin from {url}", ex);
        }
        return ExtractToTempDir(bytes);
    }

    public static string ExtractToTempDir(byte[] zipBytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pane-fetch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            if (archive.Entries.Count == 0)
                throw new PluginFetchException("Downloaded archive is empty");
            archive.ExtractToDirectory(dir, overwriteFiles: true);
            return dir;
        }
        catch (InvalidDataException ex)
        {
            TryDelete(dir);
            throw new PluginFetchException("Downloaded file is not a valid zip archive", ex);
        }
        catch
        {
            TryDelete(dir);
            throw;
        }
    }

    static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* best-effort */ }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~PluginFetcherTests"`
Expected: PASS (all 5).

- [ ] **Step 6: Commit**

```bash
git add src/Pane.Core/Plugins/PluginFetchException.cs src/Pane.Core/Plugins/PluginFetcher.cs tests/Pane.Core.Tests/PluginFetcherTests.cs
git commit -m "feat(plugins): PluginFetcher downloads and extracts plugin zips"
```

---

## Task 6: Install a plugin from a URL

**Files:**
- Modify: `src/Pane.Core/Plugins/PluginManager.cs`
- Test: `tests/Pane.Core.Tests/PluginInstallUpdateTests.cs` (new file)

**Interfaces:**
- Consumes: `PluginFetcher` (Task 5), existing `LoadOneAsync` / `CopyDir` / `PluginLoader.CreateContext`.
- Produces:
  - `PluginManager` constructor gains optional `PluginFetcher? fetcher = null` (third arg, after `store`).
  - `Task<PluginEntry> PluginManager.InstallFromUrlAsync(string url, CancellationToken ct = default)`.
  - `static string? PluginManager.FindPluginDll(string dir)` (prefer `Pane.Plugins*.dll`, else first dll) — used here and in Task 7.

- [ ] **Step 1: Write the failing test**

Create `tests/Pane.Core.Tests/PluginInstallUpdateTests.cs`:

```csharp
using System.IO.Compression;
using System.Net;
using Pane.Core.Plugins;
using Xunit;

public class PluginInstallUpdateTests
{
    static string FixtureDir(string name)
    {
        var config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name, "bin", config, "net10.0"));
    }

    static byte[] ZipDir(string dir)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var f in Directory.GetFiles(dir))
            {
                var e = zip.CreateEntry(Path.GetFileName(f));
                using var es = e.Open();
                using var fs = File.OpenRead(f);
                fs.CopyTo(es);
            }
        return ms.ToArray();
    }

    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_fn(request));
    }

    static PluginManager MgrServing(string dataRoot, byte[] zipBytes)
    {
        var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) }));
        return new PluginManager(dataRoot, store: null, fetcher: new PluginFetcher(http));
    }

    static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pane-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public async Task InstallFromUrl_installs_and_loads_the_plugin()
    {
        var root = NewRoot();
        var zip = ZipDir(FixtureDir("TestPlugin"));
        var mgr = MgrServing(root, zip);

        var entry = await mgr.InstallFromUrlAsync("https://example/testplugin.zip");

        Assert.Equal("test", entry.Metadata.Id);
        Assert.Equal(PluginState.Enabled, entry.State);
        Assert.Single(mgr.Active());
        // Copied under plugins/<id>/
        Assert.True(File.Exists(Path.Combine(root, "plugins", "test", "TestPlugin.dll")));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~InstallFromUrl_installs"`
Expected: compile failure — `PluginManager` has no `fetcher` parameter / `InstallFromUrlAsync` not defined.

- [ ] **Step 3: Add the fetcher field and `FindPluginDll`, then `InstallFromUrlAsync`**

In `src/Pane.Core/Plugins/PluginManager.cs`, add a field and extend the constructor:

```csharp
readonly PluginFetcher? _fetcher;

public PluginManager(string dataRoot, SettingsStore? store = null, PluginFetcher? fetcher = null)
{
    _dataRoot = dataRoot;
    _store = store;
    _fetcher = fetcher;
    if (_store is not null)
    {
        foreach (var id in _store.Load().DisabledPlugins)
            _disabled.Add(id);
        foreach (var kv in _store.Load().PluginSettings)          // from Task 3
            _pluginSettings[kv.Key] = new Dictionary<string, string>(kv.Value);
    }
}
```

Add the shared dll-finder (same rule `LoadAllAsync` uses inline):

```csharp
public static string? FindPluginDll(string dir)
{
    var dlls = Directory.GetFiles(dir, "*.dll");
    return dlls.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).StartsWith("Pane.Plugins"))
           ?? dlls.FirstOrDefault();
}
```

Add the install method:

```csharp
public async Task<PluginEntry> InstallFromUrlAsync(string url, CancellationToken ct = default)
{
    if (_fetcher is null)
        throw new InvalidOperationException("PluginManager has no fetcher configured");

    var temp = await _fetcher.DownloadAndExtractAsync(url, ct);
    try
    {
        var dll = FindPluginDll(temp)
            ?? throw new PluginFetchException("No plugin dll found in the downloaded archive");

        // Read the plugin id from a throwaway load, then unload before copying.
        var (probe, pctx) = PluginLoader.CreateContext(dll);
        var id = probe.Metadata.Id;
        await probe.DisposeAsync();
        pctx.Unload();

        var pluginsRoot = Path.Combine(_dataRoot, "plugins");
        var dst = Path.Combine(pluginsRoot, id);
        Directory.CreateDirectory(dst);
        CopyDir(temp, dst);

        var installedDll = Path.Combine(dst, Path.GetFileName(dll));
        await LoadOneAsync(installedDll);
        return List().First(e => e.Metadata.Id == id);
    }
    finally
    {
        TryDelete(temp);
    }
}
```

(`CopyDir` and `TryDelete` already exist as private static helpers in this file.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~InstallFromUrl_installs"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Plugins/PluginManager.cs tests/Pane.Core.Tests/PluginInstallUpdateTests.cs
git commit -m "feat(plugins): install a plugin from a zip URL"
```

---

## Task 7: Version compare, update check, and in-place update

**Files:**
- Create: `src/Pane.Core/Plugins/UpdateCheck.cs`
- Modify: `src/Pane.Core/Plugins/PluginManager.cs`
- Create: `tests/Pane.Core.Tests/Fixtures/TestPluginV2/TestPluginV2.csproj`
- Create: `tests/Pane.Core.Tests/Fixtures/TestPluginV2/TestPlugin.cs`
- Modify: `Pane.slnx` (register the new fixture)
- Test: `tests/Pane.Core.Tests/PluginInstallUpdateTests.cs` (extend)

**Interfaces:**
- Consumes: `PluginFetcher`, `FindPluginDll`, `InstallFromUrlAsync` (Task 6).
- Produces:
  - `record UpdateCheck(bool Available, string InstalledVersion, string? RemoteVersion, string? Error)`
  - `static class PluginVersion { static bool IsUpdateAvailable(string installed, string remote) }`
  - `Task<UpdateCheck> PluginManager.CheckForUpdateAsync(string id, string sourceUrl, CancellationToken ct = default)`
  - `Task PluginManager.UpdateAsync(string id, string sourceUrl, CancellationToken ct = default)`

- [ ] **Step 1: Create the v2 test fixture (same assembly name, higher version)**

Create `tests/Pane.Core.Tests/Fixtures/TestPluginV2/TestPluginV2.csproj` (note `<AssemblyName>TestPlugin</AssemblyName>` so the produced dll is `TestPlugin.dll`, matching v1 — an update overwrites the same file):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
    <ImplicitUsings>enable</ImplicitUsings>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AssemblyName>TestPlugin</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../src/Pane.Abstractions/Pane.Abstractions.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
  </ItemGroup>
</Project>
```

Create `tests/Pane.Core.Tests/Fixtures/TestPluginV2/TestPlugin.cs` (id `test`, version `2.0`):

```csharp
using System.Runtime.CompilerServices;
using Pane.Abstractions;

public sealed class TestPlugin : IPlugin
{
    public PluginMetadata Metadata { get; } =
        new("test", "Test", "🧪", "2.0", "fixture v2", new[] { "test" });

    public Task InitializeAsync(IPluginContext ctx) => Task.CompletedTask;

    public async IAsyncEnumerable<PaneResult> QueryAsync(
        PaneQuery q, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new PaneResult("Echo2 " + q.Terms, "", "🧪", 1.0, () => Task.CompletedTask);
        await Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- [ ] **Step 2: Register the fixture in `Pane.slnx`**

In `Pane.slnx`, under `<Folder Name="/tests/Pane.Core.Tests/Fixtures/">`, add:

```xml
    <Project Path="tests/Pane.Core.Tests/Fixtures/TestPluginV2/TestPluginV2.csproj" />
```

- [ ] **Step 3: Write the failing version-compare + update tests**

Add to `tests/Pane.Core.Tests/PluginInstallUpdateTests.cs`:

```csharp
[Theory]
[InlineData("1.0", "2.0", true)]
[InlineData("2.0", "2.0", false)]
[InlineData("2.0", "1.0", false)]
[InlineData("1.0.0", "1.0.1", true)]
[InlineData("beta", "beta", false)]     // non-semver equal → no update
[InlineData("beta", "rc1", true)]        // non-semver differs → update
public void IsUpdateAvailable_compares_versions(string installed, string remote, bool expected)
    => Assert.Equal(expected, PluginVersion.IsUpdateAvailable(installed, remote));

// Serves v1 bytes for one url and v2 bytes for another.
static PluginManager MgrServingMany(string dataRoot, Dictionary<string, byte[]> byUrl)
{
    var http = new HttpClient(new StubHandler(req =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(byUrl[req.RequestUri!.ToString()])
        }));
    return new PluginManager(dataRoot, store: null, fetcher: new PluginFetcher(http));
}

[Fact]
public async Task CheckForUpdate_detects_a_newer_version()
{
    var root = NewRoot();
    var v1 = "https://example/v1.zip";
    var v2 = "https://example/v2.zip";
    var mgr = MgrServingMany(root, new()
    {
        [v1] = ZipDir(FixtureDir("TestPlugin")),
        [v2] = ZipDir(FixtureDir("TestPluginV2")),
    });

    await mgr.InstallFromUrlAsync(v1);
    var check = await mgr.CheckForUpdateAsync("test", v2);

    Assert.True(check.Available);
    Assert.Equal("1.0", check.InstalledVersion);
    Assert.Equal("2.0", check.RemoteVersion);
}

[Fact]
public async Task Update_replaces_the_plugin_in_place()
{
    var root = NewRoot();
    var v1 = "https://example/v1.zip";
    var v2 = "https://example/v2.zip";
    var mgr = MgrServingMany(root, new()
    {
        [v1] = ZipDir(FixtureDir("TestPlugin")),
        [v2] = ZipDir(FixtureDir("TestPluginV2")),
    });

    await mgr.InstallFromUrlAsync(v1);
    Assert.Equal("1.0", mgr.List().Single().Metadata.Version);

    await mgr.UpdateAsync("test", v2);

    Assert.Equal("2.0", mgr.List().Single().Metadata.Version);
    Assert.Single(mgr.Active());
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~IsUpdateAvailable|FullyQualifiedName~CheckForUpdate_detects|FullyQualifiedName~Update_replaces"`
Expected: compile failure — `PluginVersion` / `CheckForUpdateAsync` / `UpdateAsync` not defined.

- [ ] **Step 5: Implement `UpdateCheck` + `PluginVersion`**

Create `src/Pane.Core/Plugins/UpdateCheck.cs`:

```csharp
namespace Pane.Core.Plugins;

public record UpdateCheck(
    bool Available,
    string InstalledVersion,
    string? RemoteVersion,
    string? Error);

public static class PluginVersion
{
    public static bool IsUpdateAvailable(string installed, string remote)
    {
        if (System.Version.TryParse(installed, out var i) &&
            System.Version.TryParse(remote, out var r))
            return r > i;
        return !string.Equals(installed, remote, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 6: Implement `CheckForUpdateAsync` and `UpdateAsync` on `PluginManager`**

Add to `src/Pane.Core/Plugins/PluginManager.cs`:

```csharp
public async Task<UpdateCheck> CheckForUpdateAsync(string id, string sourceUrl, CancellationToken ct = default)
{
    if (_fetcher is null)
        throw new InvalidOperationException("PluginManager has no fetcher configured");
    if (!_plugins.TryGetValue(id, out var p))
        return new UpdateCheck(false, "", null, "plugin not installed");

    var installed = p.Metadata.Version;
    string temp;
    try { temp = await _fetcher.DownloadAndExtractAsync(sourceUrl, ct); }
    catch (PluginFetchException ex) { return new UpdateCheck(false, installed, null, ex.Message); }

    try
    {
        var dll = FindPluginDll(temp);
        if (dll is null) return new UpdateCheck(false, installed, null, "no plugin dll in archive");

        var (probe, pctx) = PluginLoader.CreateContext(dll);
        var remote = probe.Metadata.Version;
        await probe.DisposeAsync();
        pctx.Unload();

        return new UpdateCheck(PluginVersion.IsUpdateAvailable(installed, remote), installed, remote, null);
    }
    finally { TryDelete(temp); }
}

public async Task UpdateAsync(string id, string sourceUrl, CancellationToken ct = default)
{
    if (_fetcher is null)
        throw new InvalidOperationException("PluginManager has no fetcher configured");
    if (!_plugins.TryGetValue(id, out var p))
        throw new InvalidOperationException($"plugin '{id}' not installed");

    var temp = await _fetcher.DownloadAndExtractAsync(sourceUrl, ct);
    try
    {
        var dstDir = Path.GetDirectoryName(p.DllPath)!;

        // Unload the running plugin so its files can be overwritten.
        if (p.Instance is not null) { await p.Instance.DisposeAsync(); p.Instance = null; }
        p.Ctx?.Unload(); p.Ctx = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();

        await CopyDirWithRetryAsync(temp, dstDir);

        var dll = FindPluginDll(dstDir) ?? p.DllPath;
        await LoadOneAsync(dll);
    }
    finally { TryDelete(temp); }
}

static async Task CopyDirWithRetryAsync(string src, string dst)
{
    Directory.CreateDirectory(dst);
    foreach (var f in Directory.GetFiles(src))
    {
        var target = Path.Combine(dst, Path.GetFileName(f));
        Exception? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { File.Copy(f, target, true); last = null; break; }
            catch (IOException ex)
            {
                last = ex;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(100);
            }
        }
        if (last is not null)
            throw new PluginFetchException($"Could not overwrite '{target}' (file locked after unload)", last);
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~IsUpdateAvailable|FullyQualifiedName~CheckForUpdate_detects|FullyQualifiedName~Update_replaces"`
Expected: PASS. (If the fixture dll isn't found, run `dotnet build Pane.slnx -c Debug` first so `TestPluginV2` is built.)

- [ ] **Step 8: Commit**

```bash
git add src/Pane.Core/Plugins/UpdateCheck.cs src/Pane.Core/Plugins/PluginManager.cs Pane.slnx tests/Pane.Core.Tests/Fixtures/TestPluginV2 tests/Pane.Core.Tests/PluginInstallUpdateTests.cs
git commit -m "feat(plugins): on-demand update check and in-place update"
```

---

## Task 8: Wire HttpClient + PluginFetcher into the app

**Files:**
- Modify: `src/Pane.App/Program.cs`

**Interfaces:**
- Consumes: `PluginFetcher` (Task 5), `PluginManager(dataRoot, store, fetcher)` (Task 6).
- Produces: a running app whose `PluginManager` has a real fetcher (enables URL install/update at runtime).

- [ ] **Step 1: Register the services**

In `src/Pane.App/Program.cs`, in the DI section (after the `SettingsStore` registration, before the `PluginManager` registration), add:

```csharp
builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
builder.Services.AddSingleton(sp => new PluginFetcher(sp.GetRequiredService<HttpClient>()));
```

Change the existing `PluginManager` registration to pass the fetcher:

```csharp
builder.Services.AddSingleton(sp =>
    new PluginManager(dataRoot,
        sp.GetRequiredService<SettingsStore>(),
        sp.GetRequiredService<PluginFetcher>()));
```

Add the required using if not already present:

```csharp
using Pane.Core.Plugins;   // PluginFetcher, PluginManager
```

- [ ] **Step 2: Verify the whole solution builds**

Run: `dotnet build Pane.slnx -c Debug`
Expected: build succeeds with no errors.

- [ ] **Step 3: Verify the full test suite is green**

Run: `dotnet test Pane.slnx -c Debug`
Expected: all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/Pane.App/Program.cs
git commit -m "chore(app): register HttpClient and PluginFetcher for URL installs"
```

---

## Task 9: Per-plugin settings form in the UI

**Files:**
- Modify: `src/Pane.Ui/Settings/PluginsPane.razor`
- Modify: `src/Pane.Ui/wwwroot/pane.css`

**Interfaces:**
- Consumes: `PluginManager.List()`, `GetPluginSettings(id)`, `UpdatePluginSettingsAsync(id, values)` (Tasks 3–4); `PluginMetadata.Settings`, `PluginSettingType` (Task 2).
- Produces: an expandable settings form per plugin that declares a schema.

- [ ] **Step 1: Add the settings form to `PluginsPane.razor`**

Replace the contents of `src/Pane.Ui/Settings/PluginsPane.razor` with (keeps existing enable/disable/uninstall/install-folder behavior, adds the schema form):

```razor
@using Pane.Abstractions
@using Pane.Core.Plugins
@using Pane.Core
@inject PluginManager Manager
@inject IFilePicker FilePicker

<div class="plugins-pane">
    @foreach (var p in Manager.List())
    {
        <div class="plugin-row">
            <span class="icon">@p.Metadata.Icon</span>
            <span class="name">@p.Metadata.Name <small>@p.Metadata.Version</small></span>
            <span class="badge @p.State.ToString().ToLower()">
                @(p.State == PluginState.Errored ? $"Errored — {p.Error}" : p.State.ToString())
            </span>
            @if (p.State == PluginState.Enabled)
            {
                <button @onclick="() => Toggle(p.Metadata.Id, false)">Disable</button>
            }
            else if (p.State == PluginState.Disabled)
            {
                <button @onclick="() => Toggle(p.Metadata.Id, true)">Enable</button>
            }
            @if (p.Metadata.Settings is { Count: > 0 })
            {
                <button @onclick="() => ToggleSettings(p.Metadata.Id)">Settings</button>
            }
            <button @onclick="() => Uninstall(p.Metadata.Id)">Uninstall</button>
        </div>

        @if (_openSettings == p.Metadata.Id && p.Metadata.Settings is { Count: > 0 } schema)
        {
            <div class="plugin-settings">
                @foreach (var spec in schema)
                {
                    <label class="setting-field">
                        <span class="setting-label">
                            @spec.Label
                            @if (!string.IsNullOrEmpty(spec.Description))
                            {
                                <small class="setting-desc">@spec.Description</small>
                            }
                        </span>
                        @switch (spec.Type)
                        {
                            case PluginSettingType.Boolean:
                                <input type="checkbox" checked="@IsChecked(spec.Key)"
                                       @onchange="e => OnChange(spec.Key, e, isBool: true)" />
                                break;
                            case PluginSettingType.Number:
                                <input type="number" value="@Value(spec.Key)"
                                       @onchange="e => OnChange(spec.Key, e)" />
                                break;
                            case PluginSettingType.Choice:
                                <select @onchange="e => OnChange(spec.Key, e)">
                                    @foreach (var choice in spec.Choices ?? Array.Empty<string>())
                                    {
                                        <option value="@choice" selected="@IsSelected(spec.Key, choice)">@choice</option>
                                    }
                                </select>
                                break;
                            default:
                                <input type="text" value="@Value(spec.Key)"
                                       @onchange="e => OnChange(spec.Key, e)" />
                                break;
                        }
                    </label>
                }
                <div class="settings-actions">
                    <button @onclick="() => Save(p.Metadata.Id)">Save</button>
                    <button @onclick="() => ResetDraft(p.Metadata.Id, schema)">Reset</button>
                </div>
            </div>
        }
    }
    <button @onclick="Install">Install from folder…</button>
</div>

@code {
    string? _openSettings;
    Dictionary<string, string>? _draft;

    // Seed the draft with every schema key so the form always has a value to bind.
    void ToggleSettings(string id)
    {
        if (_openSettings == id) { _openSettings = null; _draft = null; return; }
        _openSettings = id;
        var current = Manager.GetPluginSettings(id);
        var meta = Manager.List().First(e => e.Metadata.Id == id).Metadata;
        _draft = new Dictionary<string, string>();
        foreach (var s in meta.Settings ?? new List<PluginSettingSpec>())
            _draft[s.Key] = current.TryGetValue(s.Key, out var v) ? v : (s.Default ?? "");
        foreach (var kv in current)   // include any stored keys not in the schema
            _draft[kv.Key] = kv.Value;
    }

    void ResetDraft(string id, IReadOnlyList<PluginSettingSpec> schema)
    {
        _draft = new Dictionary<string, string>();
        foreach (var s in schema)
            _draft[s.Key] = s.Default ?? "";
    }

    string Value(string key) => _draft is not null && _draft.TryGetValue(key, out var v) ? v : "";
    bool IsChecked(string key) => Value(key) == "true";
    bool IsSelected(string key, string choice) => Value(key) == choice;

    void OnChange(string key, ChangeEventArgs e, bool isBool = false)
    {
        _draft ??= new();
        _draft[key] = isBool
            ? (e.Value is bool b && b ? "true" : "false")
            : (e.Value?.ToString() ?? "");
    }

    async Task Save(string id)
    {
        if (_draft is not null)
            await Manager.UpdatePluginSettingsAsync(id, _draft);
        _openSettings = null;
        _draft = null;
        StateHasChanged();
    }

    async Task Toggle(string id, bool enable)
    {
        if (enable) await Manager.EnableAsync(id); else await Manager.DisableAsync(id);
        StateHasChanged();
    }

    async Task Uninstall(string id)
    {
        await Manager.UninstallAsync(id);
        StateHasChanged();
    }

    async Task Install()
    {
        var path = await FilePicker.PickFolderAsync();
        if (path is not null)
        {
            await Manager.InstallAsync(path);
            StateHasChanged();
        }
    }
}
```

The accessor helpers (`Value`/`IsChecked`/`IsSelected`) read from `_draft`, which `ToggleSettings` seeds with every schema key — so the form never binds a missing key, and no inline string literals appear inside Razor event-handler attributes.

- [ ] **Step 2: Add styles to `pane.css`**

Append to `src/Pane.Ui/wwwroot/pane.css` (reuse existing color variables like `--muted`, `--fg`, `--bg`):

```css
.plugin-settings {
    padding: 8px 12px 12px 40px;
    display: flex;
    flex-direction: column;
    gap: 10px;
}
.plugin-settings .setting-field {
    display: flex;
    flex-direction: column;
    gap: 4px;
}
.plugin-settings .setting-label {
    display: flex;
    flex-direction: column;
    font-size: 13px;
    color: var(--fg);
}
.plugin-settings .setting-desc {
    color: var(--muted);
    font-size: 11px;
}
.plugin-settings input[type="text"],
.plugin-settings input[type="number"],
.plugin-settings select {
    background: var(--bg);
    color: var(--fg);
    border: 1px solid var(--muted);
    border-radius: 6px;
    padding: 4px 8px;
    font-size: 13px;
}
.plugin-settings .settings-actions {
    display: flex;
    gap: 8px;
    margin-top: 4px;
}
```

- [ ] **Step 3: Verify the app builds**

Run: `dotnet build Pane.slnx -c Debug`
Expected: build succeeds (Razor compiles).

- [ ] **Step 4: Manual smoke test**

Run: `dotnet run --project src/Pane.App`
- Type `settings` in the launcher to open the settings page.
- Confirm the plugin list renders and existing Enable/Disable/Uninstall/Install-from-folder still work.
- (Bundled plugins declare no schema yet, so no "Settings" button appears — expected. Full form rendering is exercised in Phase 2 when a schema-bearing plugin ships, or verify by temporarily adding a `Settings:` schema to one bundled plugin's `Metadata` and confirming the form appears, saves, and reloads.)

Expected: settings page works; no regressions.

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Ui/Settings/PluginsPane.razor src/Pane.Ui/wwwroot/pane.css
git commit -m "feat(ui): per-plugin settings form in the plugins pane"
```

---

## Definition of Done (Phase 1)

- `dotnet test Pane.slnx -c Debug` is green, including new tests for settings roundtrip/legacy-load, settings merge, settings persistence+reload, fetcher extract/download, URL install, version compare, update check, and in-place update.
- `dotnet build Pane.slnx -c Debug` succeeds; the app runs and its settings page has no regressions.
- No new NuGet packages added.
- Phase 2 (marketplaces) builds on `InstallFromUrlAsync`, `CheckForUpdateAsync`, `UpdateAsync`, and the settings schema — none of which require changes to land Phase 2; the marketplace layer only *calls* them.
</content>
