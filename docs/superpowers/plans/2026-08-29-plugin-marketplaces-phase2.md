# Plugin Marketplaces (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a marketplace layer on top of the Phase 1 primitives: users add **marketplaces** (JSON indexes, addressable by a git repo link), browse an aggregated one-click gallery, and get index-driven update checks — no more installing plugins one file at a time or from a local folder.

**Architecture:** A `marketplace.json` is a JSON index of plugins; each plugin entry's `source` is a release-zip URL installed via the Phase 1 `PluginManager.InstallFromUrlAsync`. A `MarketplaceConfigStore` (marketplaces.json) holds the configured sources — a source may be a GitHub repo link that resolves to that repo's `marketplace.json`. A `MarketplaceService` fetches, caches, aggregates, and annotates catalog state by joining against installed plugins. The settings page gains a **Marketplace** tab (gallery + manage-sources) beside **Plugins**.

**Tech Stack:** .NET 10, C#, Blazor (Photino), xUnit. `System.Text.Json`, framework `HttpClient` (already a DI singleton). No new NuGet packages.

**Spec:** `docs/superpowers/specs/2026-08-28-plugin-settings-and-url-install-design.md` (Phase 2 section)

## Global Constraints

- **Target framework:** `net10.0` for every project.
- **No new NuGet packages.** Use `System.Text.Json` and the framework `HttpClient`.
- **Test framework:** xUnit (`[Fact]`/`[Theory]`). New `Pane.Core` tests go in `tests/Pane.Core.Tests/`.
- **Data root:** `~/.config/pane` (call it `dataRoot`). New stores live there: `marketplaces.json`, `installed.json`, and a `marketplace-cache/` directory.
- **Reuse Phase 1, do not reimplement it:** `PluginManager.InstallFromUrlAsync(url, ct)`, `UpdateAsync(id, sourceUrl, ct)`, `UninstallAsync(id)`, `List()`, and `PluginVersion.IsUpdateAvailable(installed, remote)` already exist and are tested. Phase 2 orchestrates them; it does not touch their internals.
- **JSON parsing:** `JsonSerializerOptions { PropertyNameCaseInsensitive = true }` — marketplace files use lowercase keys (`name`, `plugins`, `id`, `source`, …).
- **Backward compatibility:** absent `marketplaces.json` / `installed.json` seed to defaults; a malformed marketplace file must not crash the app — it degrades to "offline/cached" for that source.
- **Git-repo-link resolution (a ruling carried from the design conversation):** a marketplace source that is a GitHub repo URL (`https://github.com/<owner>/<repo>` optionally `.git`) resolves to `https://raw.githubusercontent.com/<owner>/<repo>/HEAD/marketplace.json`. A source already ending in `.json` is used verbatim. A source that is an existing local path reads the file (dev).
- **Default marketplace:** a `marketplace.json` checked in at the repo root lists the official plugins; each `source.url` is a GitHub **release** asset zip. The default source is seeded into `marketplaces.json` on first run and cannot be removed (only user-added sources can).

---

## File Structure

**Created**
- `src/Pane.Core/Marketplace/MarketplaceModels.cs` — `Marketplace`, `MarketplaceOwner`, `MarketplacePlugin`, `PluginSource` records + a static `MarketplaceJson.Parse(string)`.
- `src/Pane.Core/Marketplace/MarketplaceSource.cs` — resolve a configured source string to a fetchable marketplace.json location.
- `src/Pane.Core/Marketplace/MarketplaceConfigStore.cs` — `marketplaces.json` (seed default, add, remove, list).
- `src/Pane.Core/Marketplace/InstalledStore.cs` — `installed.json` provenance (record/get/remove/all).
- `src/Pane.Core/Marketplace/MarketplaceService.cs` — fetch/cache/aggregate/annotate + install/update/uninstall orchestration.
- `src/Pane.Ui/Settings/MarketplacePane.razor` — gallery + manage-sources UI.
- `marketplace.json` — repo root, the default marketplace.
- `build/package-plugins.sh` — per-plugin release zips (`Pane.Plugins.<name>.zip`).
- Tests: `MarketplaceModelsTests.cs`, `MarketplaceSourceTests.cs`, `MarketplaceConfigStoreTests.cs`, `InstalledStoreTests.cs`, `MarketplaceServiceTests.cs`.

**Modified**
- `src/Pane.App/Program.cs` — register the stores + `MarketplaceService`; seed the default marketplace source.
- `src/Pane.Ui/Settings/SettingsPage.razor` — multi-tab nav (Plugins | Marketplace).
- `src/Pane.Ui/Settings/PluginsPane.razor` — remove "Install from folder…" (superseded by the marketplace); record uninstall in `InstalledStore`.
- `src/Pane.Ui/wwwroot/pane.css` — gallery grid/cards + manage-sources styles (tab nav already styled).

---

## Task 1: Marketplace models + JSON parsing

**Files:**
- Create: `src/Pane.Core/Marketplace/MarketplaceModels.cs`
- Create: `tests/Pane.Core.Tests/MarketplaceModelsTests.cs`

**Interfaces:**
- Produces:
  - `record Marketplace(string Name, string? Description, MarketplaceOwner? Owner, IReadOnlyList<MarketplacePlugin> Plugins)`
  - `record MarketplaceOwner(string? Name, string? Url)`
  - `record MarketplacePlugin(string Id, string Name, string? Description, string? Icon, string Version, string? Category, string? Author, string? Homepage, PluginSource Source)`
  - `record PluginSource(string Type, string? Url, string? Path)`
  - `static Marketplace MarketplaceJson.Parse(string json)` — throws `MarketplaceParseException` on malformed input.

- [ ] **Step 1: Write the failing tests**

Create `tests/Pane.Core.Tests/MarketplaceModelsTests.cs`:

```csharp
using Pane.Core.Marketplace;
using Xunit;

public class MarketplaceModelsTests
{
    const string Sample = """
    {
      "name": "Pane Official",
      "description": "Official plugins",
      "owner": { "name": "pane", "url": "https://github.com/dknathalage/pane" },
      "plugins": [
        {
          "id": "com.pane.apps",
          "name": "Apps",
          "description": "Launch apps",
          "icon": "🚀",
          "version": "1.0.0",
          "category": "system",
          "author": "pane",
          "homepage": "https://example",
          "source": { "type": "url", "url": "https://example/Apps.zip" }
        }
      ]
    }
    """;

    [Fact]
    public void Parses_marketplace_with_a_plugin()
    {
        var m = MarketplaceJson.Parse(Sample);
        Assert.Equal("Pane Official", m.Name);
        Assert.Equal("pane", m.Owner!.Name);
        var p = Assert.Single(m.Plugins);
        Assert.Equal("com.pane.apps", p.Id);
        Assert.Equal("1.0.0", p.Version);
        Assert.Equal("url", p.Source.Type);
        Assert.Equal("https://example/Apps.zip", p.Source.Url);
    }

    [Fact]
    public void Parses_when_optional_fields_absent()
    {
        var m = MarketplaceJson.Parse("""
        { "name": "Min", "plugins": [
          { "id": "x", "name": "X", "version": "1.0", "source": { "type": "url", "url": "u" } } ] }
        """);
        var p = Assert.Single(m.Plugins);
        Assert.Null(p.Description);
        Assert.Null(p.Category);
        Assert.Null(m.Owner);
    }

    [Fact]
    public void Throws_typed_on_malformed_json()
        => Assert.Throws<MarketplaceParseException>(() => MarketplaceJson.Parse("{ not json"));

    [Fact]
    public void Throws_typed_when_plugins_missing()
        => Assert.Throws<MarketplaceParseException>(() => MarketplaceJson.Parse("""{ "name": "x" }"""));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~MarketplaceModelsTests"`
Expected: compile failure — types not defined.

- [ ] **Step 3: Implement the models + parser**

Create `src/Pane.Core/Marketplace/MarketplaceModels.cs`:

```csharp
using System.Text.Json;

namespace Pane.Core.Marketplace;

public record MarketplaceOwner(string? Name, string? Url);

public record PluginSource(string Type, string? Url, string? Path);

public record MarketplacePlugin(
    string Id,
    string Name,
    string? Description,
    string? Icon,
    string Version,
    string? Category,
    string? Author,
    string? Homepage,
    PluginSource Source);

public record Marketplace(
    string Name,
    string? Description,
    MarketplaceOwner? Owner,
    IReadOnlyList<MarketplacePlugin> Plugins);

public sealed class MarketplaceParseException : Exception
{
    public MarketplaceParseException(string message, Exception? inner = null) : base(message, inner) { }
}

public static class MarketplaceJson
{
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    public static Marketplace Parse(string json)
    {
        Marketplace? m;
        try { m = JsonSerializer.Deserialize<Marketplace>(json, Opts); }
        catch (JsonException ex) { throw new MarketplaceParseException("Invalid marketplace JSON", ex); }

        if (m is null || m.Name is null || m.Plugins is null)
            throw new MarketplaceParseException("Marketplace JSON missing required 'name' or 'plugins'");
        return m;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~MarketplaceModelsTests"`
Expected: PASS (4).

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Marketplace/MarketplaceModels.cs tests/Pane.Core.Tests/MarketplaceModelsTests.cs
git commit -m "feat(marketplace): marketplace.json models and parser"
```

---

## Task 2: Marketplace source resolution (git repo link → marketplace.json)

**Files:**
- Create: `src/Pane.Core/Marketplace/MarketplaceSource.cs`
- Create: `tests/Pane.Core.Tests/MarketplaceSourceTests.cs`

**Interfaces:**
- Produces:
  - `record ResolvedSource(bool IsLocal, string Location)` — `Location` is a URL (remote) or a filesystem path (local).
  - `static ResolvedSource MarketplaceSource.Resolve(string source)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Pane.Core.Tests/MarketplaceSourceTests.cs`:

```csharp
using Pane.Core.Marketplace;
using Xunit;

public class MarketplaceSourceTests
{
    [Theory]
    [InlineData("https://github.com/dknathalage/pane",
                "https://raw.githubusercontent.com/dknathalage/pane/HEAD/marketplace.json")]
    [InlineData("https://github.com/dknathalage/pane.git",
                "https://raw.githubusercontent.com/dknathalage/pane/HEAD/marketplace.json")]
    [InlineData("https://github.com/dknathalage/pane/",
                "https://raw.githubusercontent.com/dknathalage/pane/HEAD/marketplace.json")]
    public void Github_repo_url_resolves_to_raw_marketplace_json(string src, string expected)
    {
        var r = MarketplaceSource.Resolve(src);
        Assert.False(r.IsLocal);
        Assert.Equal(expected, r.Location);
    }

    [Fact]
    public void Direct_json_url_is_used_verbatim()
    {
        var r = MarketplaceSource.Resolve("https://example.com/team/marketplace.json");
        Assert.False(r.IsLocal);
        Assert.Equal("https://example.com/team/marketplace.json", r.Location);
    }

    [Fact]
    public void Existing_local_path_is_local()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"mp-{Guid.NewGuid():N}.json");
        File.WriteAllText(tmp, "{}");
        try
        {
            var r = MarketplaceSource.Resolve(tmp);
            Assert.True(r.IsLocal);
            Assert.Equal(tmp, r.Location);
        }
        finally { File.Delete(tmp); }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~MarketplaceSourceTests"`
Expected: compile failure — `MarketplaceSource` not defined.

- [ ] **Step 3: Implement the resolver**

Create `src/Pane.Core/Marketplace/MarketplaceSource.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Pane.Core.Marketplace;

public record ResolvedSource(bool IsLocal, string Location);

public static class MarketplaceSource
{
    static readonly Regex GithubRepo = new(
        @"^https?://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ResolvedSource Resolve(string source)
    {
        source = source.Trim();

        // Existing local file (dev): read it directly.
        if (!source.StartsWith("http", StringComparison.OrdinalIgnoreCase) && File.Exists(source))
            return new ResolvedSource(true, source);

        // A GitHub repo URL → the repo's raw marketplace.json at HEAD.
        var m = GithubRepo.Match(source);
        if (m.Success)
            return new ResolvedSource(false,
                $"https://raw.githubusercontent.com/{m.Groups["owner"].Value}/{m.Groups["repo"].Value}/HEAD/marketplace.json");

        // Anything else (already a .json URL, or another raw URL) is used as-is.
        return new ResolvedSource(false, source);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~MarketplaceSourceTests"`
Expected: PASS (5).

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Marketplace/MarketplaceSource.cs tests/Pane.Core.Tests/MarketplaceSourceTests.cs
git commit -m "feat(marketplace): resolve git repo links to marketplace.json"
```

---

## Task 3: Marketplace config store (marketplaces.json)

**Files:**
- Create: `src/Pane.Core/Marketplace/MarketplaceConfigStore.cs`
- Create: `tests/Pane.Core.Tests/MarketplaceConfigStoreTests.cs`

**Interfaces:**
- Produces:
  - `record MarketplaceRef(string Name, string Source, bool BuiltIn)`
  - `class MarketplaceConfigStore(string path, string defaultName, string defaultSource)`:
    - `IReadOnlyList<MarketplaceRef> List()` — seeds the built-in default when the file is absent/empty.
    - `void Add(string name, string source)` — no-op if an entry with the same `source` already exists.
    - `void Remove(string source)` — throws `InvalidOperationException` if the target is built-in.

- [ ] **Step 1: Write the failing tests**

Create `tests/Pane.Core.Tests/MarketplaceConfigStoreTests.cs`:

```csharp
using Pane.Core.Marketplace;
using Xunit;

public class MarketplaceConfigStoreTests
{
    static MarketplaceConfigStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"mkts-{Guid.NewGuid():N}.json"),
            "Pane Official", "https://github.com/dknathalage/pane");

    [Fact]
    public void Seeds_builtin_default_when_absent()
    {
        var s = NewStore();
        var one = Assert.Single(s.List());
        Assert.Equal("Pane Official", one.Name);
        Assert.True(one.BuiltIn);
    }

    [Fact]
    public void Add_then_list_includes_user_source()
    {
        var s = NewStore();
        s.Add("Team", "https://github.com/acme/plugins");
        Assert.Contains(s.List(), r => r.Name == "Team" && !r.BuiltIn);
        Assert.Equal(2, s.List().Count);
    }

    [Fact]
    public void Add_is_idempotent_on_same_source()
    {
        var s = NewStore();
        s.Add("Team", "https://github.com/acme/plugins");
        s.Add("Team again", "https://github.com/acme/plugins");
        Assert.Equal(2, s.List().Count);   // default + one Team
    }

    [Fact]
    public void Remove_user_source_works_but_builtin_is_protected()
    {
        var s = NewStore();
        s.Add("Team", "https://github.com/acme/plugins");
        s.Remove("https://github.com/acme/plugins");
        Assert.Single(s.List());
        Assert.Throws<InvalidOperationException>(() => s.Remove("https://github.com/dknathalage/pane"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~MarketplaceConfigStoreTests"`
Expected: compile failure — `MarketplaceConfigStore` not defined.

- [ ] **Step 3: Implement the store**

Create `src/Pane.Core/Marketplace/MarketplaceConfigStore.cs`:

```csharp
using System.Text.Json;

namespace Pane.Core.Marketplace;

public record MarketplaceRef(string Name, string Source, bool BuiltIn);

public sealed class MarketplaceConfigStore
{
    sealed class File_ { public List<MarketplaceRef> Marketplaces { get; set; } = new(); }

    static readonly JsonSerializerOptions Opts =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    readonly string _path;
    readonly string _defaultName;
    readonly string _defaultSource;

    public MarketplaceConfigStore(string path, string defaultName, string defaultSource)
    {
        _path = path;
        _defaultName = defaultName;
        _defaultSource = defaultSource;
    }

    public IReadOnlyList<MarketplaceRef> List()
    {
        var file = Load();
        if (!file.Marketplaces.Any(m => m.Source == _defaultSource))
            file.Marketplaces.Insert(0, new MarketplaceRef(_defaultName, _defaultSource, BuiltIn: true));
        return file.Marketplaces;
    }

    public void Add(string name, string source)
    {
        var file = Load();
        if (file.Marketplaces.Any(m => m.Source == source)) return;
        file.Marketplaces.Add(new MarketplaceRef(name, source, BuiltIn: false));
        Save(file);
    }

    public void Remove(string source)
    {
        if (source == _defaultSource)
            throw new InvalidOperationException("The built-in marketplace cannot be removed.");
        var file = Load();
        file.Marketplaces.RemoveAll(m => m.Source == source);
        Save(file);
    }

    File_ Load()
    {
        if (!System.IO.File.Exists(_path)) return new File_();
        try { return JsonSerializer.Deserialize<File_>(System.IO.File.ReadAllText(_path), Opts) ?? new File_(); }
        catch { return new File_(); }
    }

    void Save(File_ file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        System.IO.File.WriteAllText(_path, JsonSerializer.Serialize(file, Opts));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~MarketplaceConfigStoreTests"`
Expected: PASS (4).

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Marketplace/MarketplaceConfigStore.cs tests/Pane.Core.Tests/MarketplaceConfigStoreTests.cs
git commit -m "feat(marketplace): marketplaces.json config store with protected default"
```

---

## Task 4: Installed-plugin provenance store (installed.json)

**Files:**
- Create: `src/Pane.Core/Marketplace/InstalledStore.cs`
- Create: `tests/Pane.Core.Tests/InstalledStoreTests.cs`

**Interfaces:**
- Produces:
  - `record InstalledInfo(string Marketplace, string SourceUrl, string Version)`
  - `class InstalledStore(string path)`:
    - `void Record(string id, InstalledInfo info)`
    - `InstalledInfo? Get(string id)`
    - `void Remove(string id)`
    - `IReadOnlyDictionary<string, InstalledInfo> All()`

- [ ] **Step 1: Write the failing tests**

Create `tests/Pane.Core.Tests/InstalledStoreTests.cs`:

```csharp
using Pane.Core.Marketplace;
using Xunit;

public class InstalledStoreTests
{
    static InstalledStore NewStore() =>
        new(Path.Combine(Path.GetTempPath(), $"inst-{Guid.NewGuid():N}.json"));

    [Fact]
    public void Record_then_get_roundtrips()
    {
        var s = NewStore();
        s.Record("com.pane.apps", new InstalledInfo("Pane Official", "https://x/Apps.zip", "1.0.0"));
        var info = s.Get("com.pane.apps");
        Assert.NotNull(info);
        Assert.Equal("Pane Official", info!.Marketplace);
        Assert.Equal("https://x/Apps.zip", info.SourceUrl);
        Assert.Equal("1.0.0", info.Version);
    }

    [Fact]
    public void Get_unknown_returns_null()
        => Assert.Null(NewStore().Get("nope"));

    [Fact]
    public void Remove_deletes_the_entry()
    {
        var s = NewStore();
        s.Record("x", new InstalledInfo("m", "u", "1.0"));
        s.Remove("x");
        Assert.Null(s.Get("x"));
    }

    [Fact]
    public void Record_overwrites_and_persists_across_instances()
    {
        var path = Path.Combine(Path.GetTempPath(), $"inst-{Guid.NewGuid():N}.json");
        new InstalledStore(path).Record("x", new InstalledInfo("m", "u", "1.0"));
        new InstalledStore(path).Record("x", new InstalledInfo("m", "u", "2.0"));
        Assert.Equal("2.0", new InstalledStore(path).Get("x")!.Version);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~InstalledStoreTests"`
Expected: compile failure — `InstalledStore` not defined.

- [ ] **Step 3: Implement the store**

Create `src/Pane.Core/Marketplace/InstalledStore.cs`:

```csharp
using System.Text.Json;

namespace Pane.Core.Marketplace;

public record InstalledInfo(string Marketplace, string SourceUrl, string Version);

public sealed class InstalledStore
{
    sealed class File_ { public Dictionary<string, InstalledInfo> Plugins { get; set; } = new(); }

    static readonly JsonSerializerOptions Opts =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    readonly string _path;
    public InstalledStore(string path) => _path = path;

    public void Record(string id, InstalledInfo info)
    {
        var file = Load();
        file.Plugins[id] = info;
        Save(file);
    }

    public InstalledInfo? Get(string id) => Load().Plugins.GetValueOrDefault(id);

    public void Remove(string id)
    {
        var file = Load();
        if (file.Plugins.Remove(id)) Save(file);
    }

    public IReadOnlyDictionary<string, InstalledInfo> All() => Load().Plugins;

    File_ Load()
    {
        if (!File.Exists(_path)) return new File_();
        try { return JsonSerializer.Deserialize<File_>(File.ReadAllText(_path), Opts) ?? new File_(); }
        catch { return new File_(); }
    }

    void Save(File_ file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(file, Opts));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~InstalledStoreTests"`
Expected: PASS (4).

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Marketplace/InstalledStore.cs tests/Pane.Core.Tests/InstalledStoreTests.cs
git commit -m "feat(marketplace): installed.json provenance store"
```

---

## Task 5: MarketplaceService — fetch, cache, aggregate, annotate

**Files:**
- Create: `src/Pane.Core/Marketplace/MarketplaceService.cs`
- Create: `tests/Pane.Core.Tests/MarketplaceServiceTests.cs`

**Interfaces:**
- Consumes: `MarketplaceConfigStore` (Task 3), `InstalledStore` (Task 4), `MarketplaceSource.Resolve` (Task 2), `MarketplaceJson.Parse` (Task 1), `PluginManager.List()` + `PluginVersion.IsUpdateAvailable` (Phase 1), `HttpClient`.
- Produces:
  - `enum MarketplaceItemState { Available, Installed, UpdateAvailable }`
  - `record MarketplaceEntry(MarketplacePlugin Plugin, string MarketplaceName, MarketplaceItemState State, string? InstalledVersion)`
  - `class MarketplaceService(HttpClient http, MarketplaceConfigStore config, InstalledStore installed, PluginManager plugins, string cacheDir)`:
    - `Task<IReadOnlyList<MarketplaceEntry>> GetCatalogAsync(CancellationToken ct = default)`

- [ ] **Step 1: Write the failing tests**

Create `tests/Pane.Core.Tests/MarketplaceServiceTests.cs`:

```csharp
using System.Net;
using Pane.Core.Marketplace;
using Pane.Core.Plugins;
using Xunit;

public class MarketplaceServiceTests
{
    sealed class StubHandler : HttpMessageHandler
    {
        readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
            => Task.FromResult(_fn(r));
    }

    const string Catalog = """
    { "name": "Pane Official", "plugins": [
      { "id": "com.pane.apps", "name": "Apps", "version": "2.0.0",
        "source": { "type": "url", "url": "https://x/Apps.zip" } } ] }
    """;

    static (MarketplaceService svc, string cache) NewService(
        Func<HttpRequestMessage, HttpResponseMessage> handler, string dataRoot)
    {
        var http = new HttpClient(new StubHandler(handler));
        var config = new MarketplaceConfigStore(
            Path.Combine(dataRoot, "marketplaces.json"), "Pane Official",
            "https://example.com/marketplace.json");   // direct-json default for the test
        var installed = new InstalledStore(Path.Combine(dataRoot, "installed.json"));
        var plugins = new PluginManager(dataRoot);     // empty — nothing installed
        var cache = Path.Combine(dataRoot, "marketplace-cache");
        return (new MarketplaceService(http, config, installed, plugins, cache), cache);
    }

    static string NewRoot()
    {
        var r = Path.Combine(Path.GetTempPath(), $"mksvc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(r);
        return r;
    }

    [Fact]
    public async Task Aggregates_catalog_and_marks_available_when_not_installed()
    {
        var root = NewRoot();
        var (svc, _) = NewService(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Catalog) }, root);

        var entries = await svc.GetCatalogAsync();

        var e = Assert.Single(entries);
        Assert.Equal("com.pane.apps", e.Plugin.Id);
        Assert.Equal(MarketplaceItemState.Available, e.State);
    }

    [Fact]
    public async Task Falls_back_to_cache_when_source_unreachable()
    {
        var root = NewRoot();
        // First call succeeds and caches.
        var (svc1, _) = NewService(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Catalog) }, root);
        await svc1.GetCatalogAsync();

        // Second service over the same dataRoot, network now failing.
        var (svc2, _) = NewService(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError), root);
        var entries = await svc2.GetCatalogAsync();

        Assert.Single(entries);   // served from cache, not empty
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~MarketplaceServiceTests"`
Expected: compile failure — `MarketplaceService` not defined.

- [ ] **Step 3: Implement the service (catalog aggregation + cache)**

Create `src/Pane.Core/Marketplace/MarketplaceService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Pane.Core.Plugins;

namespace Pane.Core.Marketplace;

public enum MarketplaceItemState { Available, Installed, UpdateAvailable }

public record MarketplaceEntry(
    MarketplacePlugin Plugin,
    string MarketplaceName,
    MarketplaceItemState State,
    string? InstalledVersion);

public sealed class MarketplaceService
{
    readonly HttpClient _http;
    readonly MarketplaceConfigStore _config;
    readonly InstalledStore _installed;
    readonly PluginManager _plugins;
    readonly string _cacheDir;

    public MarketplaceService(HttpClient http, MarketplaceConfigStore config,
        InstalledStore installed, PluginManager plugins, string cacheDir)
    {
        _http = http;
        _config = config;
        _installed = installed;
        _plugins = plugins;
        _cacheDir = cacheDir;
    }

    public async Task<IReadOnlyList<MarketplaceEntry>> GetCatalogAsync(CancellationToken ct = default)
    {
        // installed id -> live version from the running plugin manager
        var installedVersions = _plugins.List()
            .GroupBy(e => e.Metadata.Id)
            .ToDictionary(g => g.Key, g => g.First().Metadata.Version);

        var seen = new HashSet<string>();      // dedupe by plugin id, first marketplace wins
        var entries = new List<MarketplaceEntry>();

        foreach (var mref in _config.List())
        {
            var market = await LoadMarketplaceAsync(mref, ct);
            if (market is null) continue;

            foreach (var plugin in market.Plugins)
            {
                if (!seen.Add(plugin.Id)) continue;

                MarketplaceItemState state;
                string? installedVer = installedVersions.GetValueOrDefault(plugin.Id);
                if (installedVer is null)
                    state = MarketplaceItemState.Available;
                else if (PluginVersion.IsUpdateAvailable(installedVer, plugin.Version))
                    state = MarketplaceItemState.UpdateAvailable;
                else
                    state = MarketplaceItemState.Installed;

                entries.Add(new MarketplaceEntry(plugin, market.Name, state, installedVer));
            }
        }
        return entries;
    }

    async Task<Marketplace?> LoadMarketplaceAsync(MarketplaceRef mref, CancellationToken ct)
    {
        var resolved = MarketplaceSource.Resolve(mref.Source);
        var cachePath = CachePathFor(mref.Source);
        try
        {
            string json;
            if (resolved.IsLocal)
                json = await File.ReadAllTextAsync(resolved.Location, ct);
            else
            {
                var resp = await _http.GetAsync(resolved.Location, ct);
                resp.EnsureSuccessStatusCode();
                json = await resp.Content.ReadAsStringAsync(ct);
            }
            var market = MarketplaceJson.Parse(json);
            WriteCache(cachePath, json);
            return market;
        }
        catch
        {
            // Offline / malformed: fall back to the last good cached copy if any.
            if (File.Exists(cachePath))
            {
                try { return MarketplaceJson.Parse(await File.ReadAllTextAsync(cachePath, ct)); }
                catch { return null; }
            }
            return null;
        }
    }

    string CachePathFor(string source)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(source)))[..16];
        return Path.Combine(_cacheDir, hash + ".json");
    }

    void WriteCache(string cachePath, string json)
    {
        Directory.CreateDirectory(_cacheDir);
        File.WriteAllText(cachePath, json);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~MarketplaceServiceTests"`
Expected: PASS (2).

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Marketplace/MarketplaceService.cs tests/Pane.Core.Tests/MarketplaceServiceTests.cs
git commit -m "feat(marketplace): fetch/cache/aggregate catalog with install-state annotation"
```

---

## Task 6: Install / update / uninstall orchestration on the service

**Files:**
- Modify: `src/Pane.Core/Marketplace/MarketplaceService.cs`
- Test: `tests/Pane.Core.Tests/MarketplaceServiceTests.cs`

**Interfaces:**
- Consumes: `PluginManager.InstallFromUrlAsync`, `PluginManager.UpdateAsync`, `PluginManager.UninstallAsync` (Phase 1); `InstalledStore` (Task 4).
- Produces (added to `MarketplaceService`):
  - `Task InstallAsync(MarketplaceEntry entry, CancellationToken ct = default)`
  - `Task UpdateAsync(MarketplaceEntry entry, CancellationToken ct = default)`
  - `Task UninstallAsync(string id, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing test**

Add to `tests/Pane.Core.Tests/MarketplaceServiceTests.cs` (reuses `StubHandler`, `NewRoot`, and the zip helpers pattern — this test serves a real plugin zip built from the TestPlugin fixture):

```csharp
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
    using (var zip = new System.IO.Compression.ZipArchive(
        ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        foreach (var f in Directory.GetFiles(dir))
        {
            var e = zip.CreateEntry(Path.GetFileName(f));
            using var es = e.Open();
            using var fs = File.OpenRead(f);
            fs.CopyTo(es);
        }
    return ms.ToArray();
}

[Fact]
public async Task Install_installs_plugin_and_records_provenance()
{
    var root = NewRoot();
    var zip = ZipDir(FixtureDir("TestPlugin"));      // id "test", version "1.0"
    var http = new HttpClient(new StubHandler(_ =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) }));
    var config = new MarketplaceConfigStore(
        Path.Combine(root, "marketplaces.json"), "Pane Official", "https://example.com/marketplace.json");
    var installed = new InstalledStore(Path.Combine(root, "installed.json"));
    var fetcher = new PluginFetcher(http);
    var plugins = new PluginManager(root, store: null, fetcher: fetcher);
    var svc = new MarketplaceService(http, config, installed, plugins, Path.Combine(root, "cache"));

    var entry = new MarketplaceEntry(
        new MarketplacePlugin("test", "Test", null, "🧪", "1.0", null, null, null,
            new PluginSource("url", "https://x/test.zip", null)),
        "Pane Official", MarketplaceItemState.Available, null);

    await svc.InstallAsync(entry);

    Assert.Contains(plugins.List(), e => e.Metadata.Id == "test" && e.State == PluginState.Enabled);
    var prov = installed.Get("test");
    Assert.NotNull(prov);
    Assert.Equal("Pane Official", prov!.Marketplace);
    Assert.Equal("https://x/test.zip", prov.SourceUrl);
    Assert.Equal("1.0", prov.Version);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~Install_installs_plugin_and_records_provenance"`
Expected: compile failure — `MarketplaceService.InstallAsync` not defined.

- [ ] **Step 3: Add the orchestration methods**

Append to the `MarketplaceService` class in `src/Pane.Core/Marketplace/MarketplaceService.cs`:

```csharp
    public async Task InstallAsync(MarketplaceEntry entry, CancellationToken ct = default)
    {
        var src = entry.Plugin.Source;
        if (src.Type == "url" && src.Url is not null)
            await _plugins.InstallFromUrlAsync(src.Url, ct);
        else if (src.Type == "local" && src.Path is not null)
            await _plugins.InstallAsync(src.Path);
        else
            throw new InvalidOperationException($"Unsupported plugin source type '{src.Type}'");

        _installed.Record(entry.Plugin.Id,
            new InstalledInfo(entry.MarketplaceName, src.Url ?? src.Path ?? "", entry.Plugin.Version));
    }

    public async Task UpdateAsync(MarketplaceEntry entry, CancellationToken ct = default)
    {
        var src = entry.Plugin.Source;
        if (src.Type != "url" || src.Url is null)
            throw new InvalidOperationException("Only url-sourced plugins can be updated in place");

        await _plugins.UpdateAsync(entry.Plugin.Id, src.Url, ct);
        _installed.Record(entry.Plugin.Id,
            new InstalledInfo(entry.MarketplaceName, src.Url, entry.Plugin.Version));
    }

    public async Task UninstallAsync(string id, CancellationToken ct = default)
    {
        await _plugins.UninstallAsync(id);
        _installed.Remove(id);
    }
```

Add `using Pane.Core.Plugins;` is already present at the top of the file (from Task 5).

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Pane.Core.Tests -c Debug --filter "FullyQualifiedName~Install_installs_plugin_and_records_provenance"`
Expected: PASS. (Run `dotnet build Pane.slnx -c Debug` first so the TestPlugin fixture exists.)

- [ ] **Step 5: Commit**

```bash
git add src/Pane.Core/Marketplace/MarketplaceService.cs tests/Pane.Core.Tests/MarketplaceServiceTests.cs
git commit -m "feat(marketplace): install/update/uninstall orchestration + provenance"
```

---

## Task 7: DI wiring + default marketplace.json at repo root

**Files:**
- Create: `marketplace.json` (repo root)
- Modify: `src/Pane.App/Program.cs`

**Interfaces:**
- Consumes: all Task 3–5 constructors; the existing `HttpClient`, `PluginManager`, and `dataRoot` in `Program.cs`.
- Produces: a `MarketplaceService` singleton in DI, usable by the UI.

- [ ] **Step 1: Create the default marketplace at the repo root**

Create `marketplace.json` (official plugins point at GitHub **release** zip assets; a release must exist for installs to succeed — noted in the plan's Risks):

```json
{
  "name": "Pane Official",
  "description": "Official Pane plugins",
  "owner": { "name": "pane", "url": "https://github.com/dknathalage/pane" },
  "plugins": [
    {
      "id": "pane.apps",
      "name": "Applications",
      "description": "Launch installed applications",
      "icon": "🚀",
      "version": "1.0.0",
      "category": "system",
      "author": "pane",
      "homepage": "https://github.com/dknathalage/pane",
      "source": { "type": "url", "url": "https://github.com/dknathalage/pane/releases/download/plugins-v1.0.0/Pane.Plugins.Apps.zip" }
    },
    {
      "id": "pane.calculator",
      "name": "Calculator",
      "description": "Inline arithmetic",
      "icon": "🧮",
      "version": "1.0.0",
      "category": "utilities",
      "author": "pane",
      "homepage": "https://github.com/dknathalage/pane",
      "source": { "type": "url", "url": "https://github.com/dknathalage/pane/releases/download/plugins-v1.0.0/Pane.Plugins.Calculator.zip" }
    },
    {
      "id": "pane.scripts",
      "name": "Scripts",
      "description": "Run user scripts",
      "icon": "📜",
      "version": "1.0.0",
      "category": "utilities",
      "author": "pane",
      "homepage": "https://github.com/dknathalage/pane",
      "source": { "type": "url", "url": "https://github.com/dknathalage/pane/releases/download/plugins-v1.0.0/Pane.Plugins.Scripts.zip" }
    },
    {
      "id": "pane.vscode",
      "name": "VSCode Repos",
      "description": "Open repositories in VS Code",
      "icon": "📂",
      "version": "1.0.0",
      "category": "development",
      "author": "pane",
      "homepage": "https://github.com/dknathalage/pane",
      "source": { "type": "url", "url": "https://github.com/dknathalage/pane/releases/download/plugins-v1.0.0/Pane.Plugins.VSCode.zip" }
    }
  ]
}
```

- [ ] **Step 2: Register the stores + service in `Program.cs`**

In `src/Pane.App/Program.cs`, after the `PluginManager` registration (around line 38) and before the `QueryDispatcher` registration, add:

```csharp
// ── Marketplace ────────────────────────────────────────────────────────────
const string DefaultMarketplaceName = "Pane Official";
const string DefaultMarketplaceSource = "https://github.com/dknathalage/pane";

builder.Services.AddSingleton(new MarketplaceConfigStore(
    Path.Combine(dataRoot, "marketplaces.json"), DefaultMarketplaceName, DefaultMarketplaceSource));
builder.Services.AddSingleton(new InstalledStore(Path.Combine(dataRoot, "installed.json")));
builder.Services.AddSingleton(sp => new MarketplaceService(
    sp.GetRequiredService<HttpClient>(),
    sp.GetRequiredService<MarketplaceConfigStore>(),
    sp.GetRequiredService<InstalledStore>(),
    sp.GetRequiredService<PluginManager>(),
    Path.Combine(dataRoot, "marketplace-cache")));
```

Add the using at the top of `Program.cs` if not present:

```csharp
using Pane.Core.Marketplace;
```

- [ ] **Step 3: Verify build**

Run: `dotnet build Pane.slnx -c Debug`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add marketplace.json src/Pane.App/Program.cs
git commit -m "feat(marketplace): default marketplace.json + DI wiring"
```

---

## Task 8: Per-plugin release packaging

**Files:**
- Create: `build/package-plugins.sh`

**Interfaces:**
- Produces: `dist/plugins-release/Pane.Plugins.<name>.zip` for each plugin — the assets the default `marketplace.json` `source.url`s point at.

- [ ] **Step 1: Write the script**

Create `build/package-plugins.sh`:

```bash
#!/usr/bin/env bash
# Build each bundled plugin (Release) and zip it as an individual release asset,
# matching the URLs in the repo-root marketplace.json.
#   dist/plugins-release/Pane.Plugins.<name>.zip  (each zip holds the plugin dll)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/plugins-release"
mkdir -p "$OUT"

for p in Apps Calculator Scripts VSCode; do
  echo "==> Building Pane.Plugins.$p (Release)"
  dotnet build "$ROOT/src/Pane.Plugins.$p" -c Release --nologo -v q >/dev/null
  STAGE="$(mktemp -d)"
  cp "$ROOT/src/Pane.Plugins.$p/bin/Release/net10.0/Pane.Plugins.$p.dll" "$STAGE/"
  ZIP="$OUT/Pane.Plugins.$p.zip"
  rm -f "$ZIP"
  ( cd "$STAGE" && zip -qry "$ZIP" . )
  rm -rf "$STAGE"
  echo "==> $ZIP"
done

echo ""
echo "Publish with:"
echo "  gh release create plugins-v1.0.0 $OUT/*.zip -t 'Plugins v1.0.0' -n 'Bundled plugins'"
```

- [ ] **Step 2: Make it executable and smoke-test it**

Run:
```bash
chmod +x build/package-plugins.sh
./build/package-plugins.sh
ls dist/plugins-release
```
Expected: four zips `Pane.Plugins.Apps.zip`, `Pane.Plugins.Calculator.zip`, `Pane.Plugins.Scripts.zip`, `Pane.Plugins.VSCode.zip`; each contains one dll (`unzip -l dist/plugins-release/Pane.Plugins.Apps.zip` shows `Pane.Plugins.Apps.dll`).

- [ ] **Step 3: Commit**

```bash
git add build/package-plugins.sh
git commit -m "build: per-plugin release zips for the default marketplace"
```

---

## Task 9: Settings tabs + Marketplace pane UI

**Files:**
- Modify: `src/Pane.Ui/Settings/SettingsPage.razor`
- Create: `src/Pane.Ui/Settings/MarketplacePane.razor`
- Modify: `src/Pane.Ui/Settings/PluginsPane.razor`
- Modify: `src/Pane.Ui/wwwroot/pane.css`

**Interfaces:**
- Consumes: `MarketplaceService.GetCatalogAsync/InstallAsync/UpdateAsync/UninstallAsync`, `MarketplaceEntry`, `MarketplaceItemState` (Tasks 5–6); `MarketplaceConfigStore.List/Add/Remove` (Task 3).

- [ ] **Step 1: Make the settings nav multi-tab**

Replace `src/Pane.Ui/Settings/SettingsPage.razor` with:

```razor
<div class="settings">
    <nav>
        <button class="@(_tab == Tab.Plugins ? "active" : "")" @onclick="() => _tab = Tab.Plugins">Plugins</button>
        <button class="@(_tab == Tab.Marketplace ? "active" : "")" @onclick="() => _tab = Tab.Marketplace">Marketplace</button>
    </nav>
    <section>
        @if (_tab == Tab.Plugins) { <PluginsPane /> }
        else { <MarketplacePane /> }
    </section>
</div>

@code {
    enum Tab { Plugins, Marketplace }
    Tab _tab = Tab.Plugins;
}
```

- [ ] **Step 2: Create the Marketplace pane**

Create `src/Pane.Ui/Settings/MarketplacePane.razor`:

```razor
@using Pane.Core.Marketplace
@inject MarketplaceService Market
@inject MarketplaceConfigStore Config

<div class="marketplace-pane">
    <div class="mkt-manage">
        <input class="mkt-source-input" placeholder="Add a marketplace — paste a git repo link…"
               @bind="_newSource" @bind:event="oninput" />
        <button @onclick="AddSource" disabled="@string.IsNullOrWhiteSpace(_newSource)">Add</button>
    </div>

    <div class="mkt-sources">
        @foreach (var s in Config.List())
        {
            <span class="mkt-source-chip">
                @s.Name
                @if (!s.BuiltIn)
                {
                    <button class="mkt-source-remove" title="Remove" @onclick="() => RemoveSource(s.Source)">✕</button>
                }
            </span>
        }
        <button class="mkt-refresh" @onclick="Reload">Refresh</button>
    </div>

    @if (_loading)
    {
        <div class="mkt-status">Loading catalog…</div>
    }
    else if (_error is not null)
    {
        <div class="mkt-status mkt-error">@_error</div>
    }
    else if (_entries.Count == 0)
    {
        <div class="mkt-status">No plugins found. Add a marketplace above.</div>
    }
    else
    {
        <div class="mkt-grid">
            @foreach (var e in _entries)
            {
                <div class="mkt-card">
                    <div class="mkt-card-head">
                        <span class="mkt-icon">@(string.IsNullOrEmpty(e.Plugin.Icon) ? "🧩" : e.Plugin.Icon)</span>
                        <span class="mkt-name">@e.Plugin.Name</span>
                        <span class="mkt-ver">@e.Plugin.Version</span>
                    </div>
                    <div class="mkt-desc">@e.Plugin.Description</div>
                    <div class="mkt-meta">@e.MarketplaceName@(string.IsNullOrEmpty(e.Plugin.Category) ? "" : " · " + e.Plugin.Category)</div>
                    <div class="mkt-actions">
                        @switch (e.State)
                        {
                            case MarketplaceItemState.Installed:
                                <span class="mkt-installed">Installed</span>
                                break;
                            case MarketplaceItemState.UpdateAvailable:
                                <button class="mkt-update" disabled="@_busy" @onclick="() => Update(e)">Update → @e.Plugin.Version</button>
                                break;
                            default:
                                <button class="mkt-install" disabled="@_busy" @onclick="() => Install(e)">Install</button>
                                break;
                        }
                    </div>
                </div>
            }
        </div>
    }
</div>

@code {
    IReadOnlyList<MarketplaceEntry> _entries = Array.Empty<MarketplaceEntry>();
    string _newSource = "";
    bool _loading = true;
    bool _busy;
    string? _error;

    protected override async Task OnInitializedAsync() => await Reload();

    async Task Reload()
    {
        _loading = true; _error = null; StateHasChanged();
        try { _entries = await Market.GetCatalogAsync(); }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; StateHasChanged(); }
    }

    async Task AddSource()
    {
        var src = _newSource.Trim();
        if (src.Length == 0) return;
        Config.Add(src, src);   // name defaults to the source; the fetched marketplace's own name shows on cards
        _newSource = "";
        await Reload();
    }

    async Task RemoveSource(string source)
    {
        Config.Remove(source);
        await Reload();
    }

    async Task Install(MarketplaceEntry e)
    {
        _busy = true; StateHasChanged();
        try { await Market.InstallAsync(e); await Reload(); }
        catch (Exception ex) { _error = ex.Message; }
        finally { _busy = false; StateHasChanged(); }
    }

    async Task Update(MarketplaceEntry e)
    {
        _busy = true; StateHasChanged();
        try { await Market.UpdateAsync(e); await Reload(); }
        catch (Exception ex) { _error = ex.Message; }
        finally { _busy = false; StateHasChanged(); }
    }
}
```

- [ ] **Step 3: Remove folder-install from PluginsPane and record uninstall provenance**

In `src/Pane.Ui/Settings/PluginsPane.razor`:

Remove the folder-install button line:

```razor
    <button @onclick="Install">Install from folder…</button>
```

Remove the now-unused `Install()` method and the `@inject IFilePicker FilePicker` line and its `@using Pane.Core` if unused. Then inject the installed store and clear provenance on uninstall — change the top injects to:

```razor
@using Pane.Abstractions
@using Pane.Core.Plugins
@using Pane.Core.Marketplace
@inject PluginManager Manager
@inject InstalledStore Installed
```

And update `Uninstall` to also clear provenance:

```csharp
    async Task Uninstall(string id)
    {
        await Manager.UninstallAsync(id);
        Installed.Remove(id);
        StateHasChanged();
    }
```

(Leave the per-plugin settings form and enable/disable logic from Phase 1 untouched.)

- [ ] **Step 4: Add marketplace CSS**

Append to `src/Pane.Ui/wwwroot/pane.css` (reuse existing tokens):

```css
/* ── Marketplace pane ──────────────────────────────────────────────────── */
.marketplace-pane { display: flex; flex-direction: column; gap: 12px; padding: 4px 8px; }

.mkt-manage { display: flex; gap: 8px; }
.mkt-source-input {
    flex: 1; background: var(--bg-elev); color: var(--fg);
    border: 1px solid var(--border); border-radius: 8px; padding: 7px 10px; font-size: 13px;
}
.mkt-manage button {
    background: var(--accent); color: #fff; border: none; border-radius: 8px;
    padding: 7px 14px; font-size: 13px; font-weight: 500; cursor: pointer;
}
.mkt-manage button:disabled { opacity: 0.4; cursor: default; }

.mkt-sources { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; }
.mkt-source-chip {
    display: inline-flex; align-items: center; gap: 6px; font-size: 12px; color: var(--fg);
    background: var(--sel); border-radius: 999px; padding: 3px 10px;
}
.mkt-source-remove {
    background: none; border: none; color: var(--muted); cursor: pointer; font-size: 11px; padding: 0;
}
.mkt-source-remove:hover { color: var(--danger); }
.mkt-refresh {
    margin-left: auto; background: var(--bg-elev); color: var(--fg);
    border: 1px solid var(--border); border-radius: 8px; padding: 4px 12px; font-size: 12px; cursor: pointer;
}

.mkt-status { color: var(--muted); font-size: 13px; padding: 16px 4px; }
.mkt-error { color: var(--danger); }

.mkt-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 10px; }
.mkt-card {
    background: var(--bg-elev); border: 1px solid var(--border);
    border-radius: 12px; padding: 12px; display: flex; flex-direction: column; gap: 6px;
}
.mkt-card-head { display: flex; align-items: baseline; gap: 8px; }
.mkt-icon { font-size: 18px; }
.mkt-name { font-size: 14px; font-weight: 600; color: var(--fg); flex: 1; }
.mkt-ver { font-size: 11px; color: var(--muted); }
.mkt-desc { font-size: 12px; color: var(--fg); opacity: 0.85; min-height: 30px; }
.mkt-meta { font-size: 11px; color: var(--muted); }
.mkt-actions { margin-top: 4px; }
.mkt-actions button {
    width: 100%; border: none; border-radius: 8px; padding: 6px; font-size: 13px;
    font-weight: 500; cursor: pointer; color: #fff;
}
.mkt-actions button:disabled { opacity: 0.5; cursor: default; }
.mkt-install { background: var(--accent); }
.mkt-update { background: var(--mark); color: #1c1c1e !important; }
.mkt-installed { display: inline-block; font-size: 12px; font-weight: 600; color: var(--ok); padding: 6px 0; }
```

- [ ] **Step 5: Verify build**

Run: `dotnet build Pane.slnx -c Debug`
Expected: build succeeds (Razor compiles; no leftover references to the removed `Install()`/`FilePicker`).

- [ ] **Step 6: Run the full suite**

Run: `dotnet test Pane.slnx -c Debug`
Expected: all tests pass.

- [ ] **Step 7: Manual smoke (best-effort, may be headless)**

Run: `dotnet run --project src/Pane.App`
- Open settings (gear button), click the **Marketplace** tab.
- The default marketplace loads from `https://github.com/dknathalage/pane` (needs network + the repo's `marketplace.json`; if unreachable it shows a status message, which is acceptable).
- Paste a git repo link into the add box and confirm it appears as a source chip.
If headless (no window), state that in the report; the build + suite are the gating checks.

- [ ] **Step 8: Commit**

```bash
git add src/Pane.Ui/Settings/SettingsPage.razor src/Pane.Ui/Settings/MarketplacePane.razor src/Pane.Ui/Settings/PluginsPane.razor src/Pane.Ui/wwwroot/pane.css
git commit -m "feat(ui): marketplace tab — gallery, manage sources, git-repo-link add"
```

---

## Definition of Done (Phase 2)

- `dotnet test Pane.slnx -c Debug` green, including new tests for models/parse, source resolution, config store (seed/add/remove/protected default), installed store, and marketplace service (aggregate, cache fallback, install+provenance).
- `dotnet build Pane.slnx -c Debug` succeeds; the settings page has a **Plugins** and a **Marketplace** tab; the folder-install button is gone.
- Adding a marketplace by git repo link works; the gallery shows Install / Installed / Update → vX.Y states.
- `build/package-plugins.sh` produces the per-plugin zips the default `marketplace.json` references.

## Risks / Open Considerations

- **Security:** installing a plugin still runs unsandboxed code from a URL (same Phase-1 non-goal). The marketplace widens reach; a future signing/checksum story is worth adding (each `MarketplacePlugin` could gain an optional `sha256`).
- **Release dependency:** the default marketplace's `source.url`s point at `plugins-v1.0.0` release assets. Until that release exists (`build/package-plugins.sh` → `gh release create plugins-v1.0.0 …`), official installs fail with a fetch error; user-added marketplaces that host their own zips work regardless.
- **`raw.githubusercontent.com/.../HEAD/marketplace.json`** requires the repo's default branch to carry `marketplace.json` at its root (this plan adds it). Private repos would need auth — out of scope.
</content>
