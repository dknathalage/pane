# Drop the Plugin System — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove Pane's plugin architecture (dynamic DLL loading, marketplace, per-plugin release/versioning) and fold its five features directly into `Pane.Core` so everything is built, versioned, and deployed as a single app.

**Architecture:** Each of the five plugins becomes a concrete `*Feature` class in `Pane.Core/Features/<Name>/` with a `Descriptor` property (a plain `FeatureDescriptor` record) and an `IAsyncEnumerable<PaneResult> QueryAsync(PaneQuery, CancellationToken)` method — no `IPlugin` interface, no reflection. `QueryDispatcher` holds the five features directly and routes by keyword prefix. `Pane.Abstractions`, `PluginManager`, `PluginLoader`, and the whole `Marketplace/` layer are deleted; shared contracts move into `Pane.Core/Contracts/`.

**Tech Stack:** C# / .NET 10, Photino.Blazor, xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-02-drop-plugin-system-design.md`

## Global Constraints

- Target framework `net10.0`; `ImplicitUsings` and `Nullable` enabled (match existing csproj).
- Test framework: xUnit 2.9.3.
- Feature ids MUST remain exactly: `apps`, `files`, `calc`, `scripts`, `vscode` (the UI in `ResultList.razor` switches on these strings for icons/action labels).
- Feature keywords MUST be preserved exactly: Files `/`, Calculator `=` (Priority 10), Scripts `>`; Apps and VSCode have no keyword.
- Settings persist to `~/.config/pane/settings.json`; the on-disk keys `disabledPlugins` and `hotkey` MUST keep working (backward compatible). A legacy `pluginSettings` key must be tolerated (ignored), never throw.
- The app must build after every task; every task ends with `dotnet build Pane.slnx` green and the relevant tests passing.
- Because `Pane.App` loads plugins by reflection from disk (it does NOT project-reference the plugin projects), deleting a plugin project does not break the app build — features are wired in explicitly at the cut-over task (Task 7).

---

### Task 1: Add `FeatureDescriptor` and rank against it

Introduce the descriptor type the collapsed features and dispatcher will use, and rewrite `ResultRanker` to consume it. This is isolated: `ResultRanker` currently takes `PluginMetadata`, but only reads `.Name`, `.Keywords`, `.Priority`, `.Id` — all present on the new record.

**Files:**
- Create: `src/Pane.Abstractions/FeatureDescriptor.cs`
- Modify: `src/Pane.Core/Query/ResultRanker.cs`
- Modify: `src/Pane.Core/Query/QueryDispatcher.cs` (signature only, see below)
- Test: `tests/Pane.Core.Tests/ResultRankerTests.cs` (create)

**Interfaces:**
- Produces: `public record FeatureDescriptor(string Id, string Name, string Icon, string? Keyword, int Priority, IReadOnlyList<string> Keywords)` in namespace `Pane.Abstractions`.
- Produces: `ResultRanker.Rank(PaneQuery q, FeatureDescriptor meta, IEnumerable<PaneResult> results)`.

- [ ] **Step 1: Write the descriptor record**

```csharp
namespace Pane.Abstractions;

public record FeatureDescriptor(
    string Id,
    string Name,
    string Icon,
    string? Keyword,
    int Priority,
    IReadOnlyList<string> Keywords);
```

- [ ] **Step 2: Write a failing ranker test**

Create `tests/Pane.Core.Tests/ResultRankerTests.cs`:

```csharp
using Pane.Abstractions;
using Pane.Core.Query;
using Xunit;

public class ResultRankerTests
{
    static readonly FeatureDescriptor Calc =
        new("calc", "Calculator", "🧮", "=", 10, new[] { "calc", "math", "=" });

    [Fact]
    public void EmptyQuery_KeepsResult_ScoredByBaseAndPriority()
    {
        var ranker = new ResultRanker(new FuzzyMatcher());
        var r = new PaneResult("= 4", "Copy", "🧮", 100, () => Task.CompletedTask, "2+2");
        var q = new PaneQuery("=2+2", "=", "2+2");

        var scored = ranker.Rank(q, Calc, new[] { r }).ToList();

        Assert.Single(scored);
        Assert.Equal("calc", scored[0].PluginId);
    }
}
```

- [ ] **Step 3: Run it — verify it FAILS to compile** (`Rank` still takes `PluginMetadata`)

Run: `dotnet test tests/Pane.Core.Tests -f net10.0 --filter ResultRankerTests`
Expected: build error — cannot convert `FeatureDescriptor` to `PluginMetadata`.

- [ ] **Step 4: Change `ResultRanker` to use `FeatureDescriptor`**

In `src/Pane.Core/Query/ResultRanker.cs`, replace every `PluginMetadata` with `FeatureDescriptor` (the method signature `Rank(...)` and the helper `PluginTokenScore(string token, FeatureDescriptor meta)`). No body logic changes — all accessed members (`Id`, `Name`, `Keywords`, `Priority`) exist on the new record.

- [ ] **Step 5: Keep `QueryDispatcher` compiling**

`QueryDispatcher.CollectAsync` calls `_ranker.Rank(query, p.meta, raw)` where `p.meta` is `PluginMetadata`. To keep the build green this task, change the dispatcher's tuple type is deferred to Task 7 — instead, in this task only, add a temporary adapter at the call site: leave `QueryDispatcher` untouched by having `Rank` accept `PluginMetadata` too is NOT allowed (no overloads). Instead: this task modifies `QueryDispatcher` to build a `FeatureDescriptor` from `p.meta` inline before ranking:

```csharp
var desc = new FeatureDescriptor(p.meta.Id, p.meta.Name, p.meta.Icon, p.meta.Keyword, p.meta.Priority, p.meta.Keywords);
return _ranker.Rank(query, desc, raw).ToList();
```

(This inline conversion is discarded in Task 7 when the dispatcher holds `FeatureDescriptor` natively.)

- [ ] **Step 6: Run tests — verify PASS**

Run: `dotnet test tests/Pane.Core.Tests -f net10.0`
Expected: PASS (existing dispatcher/ranker tests still green; new ResultRankerTests green).

- [ ] **Step 7: Commit**

```bash
git add src/Pane.Abstractions/FeatureDescriptor.cs src/Pane.Core/Query/ResultRanker.cs src/Pane.Core/Query/QueryDispatcher.cs tests/Pane.Core.Tests/ResultRankerTests.cs
git commit -m "refactor(core): rank against FeatureDescriptor instead of PluginMetadata"
```

---

### Tasks 2–6: Convert each plugin into a `Pane.Core` feature

Each of these five tasks follows the **same shape**. The general recipe is written once here; each task section states only what differs.

**Recipe (apply per feature `<Name>`, id `<id>`, class `<X>Plugin` → `<X>Feature`):**

1. `git mv src/Pane.Plugins.<Name>/*.cs src/Pane.Core/Features/<Name>/` (move all source files; drop the `.csproj` and `obj/`). Delete the now-empty `src/Pane.Plugins.<Name>/` directory.
2. Remove the project from the solution: `dotnet sln Pane.slnx remove src/Pane.Plugins.<Name>/Pane.Plugins.<Name>.csproj`.
3. In the moved `<X>Plugin.cs`:
   - Rename the type `<X>Plugin` → `<X>Feature`.
   - Change namespace from `Pane.Plugins.<Name>` to `Pane.Core.Features.<Name>`.
   - Remove `: IPlugin` and delete the `DisposeAsync()` member.
   - Replace the `public PluginMetadata Metadata { get; } = new(...)` with `public FeatureDescriptor Descriptor { get; } = new(<id>, <name>, <icon>, <keyword-or-null>, <priority>, <keywords>);` — dropping the `Version` and `Description` positional args that `FeatureDescriptor` does not have.
   - Replace `public Task InitializeAsync(IPluginContext ctx)` with a parameterless `public Task InitializeAsync()` (or fold its work into the constructor where noted). Anything using `ctx.DataDirectory` takes that path via the constructor instead.
   - Keep the `QueryAsync(PaneQuery, CancellationToken)` body and all helper types unchanged (they reference `PaneQuery`/`PaneResult` from `Pane.Abstractions`, still present).
   - Update the moved helper files' namespaces from `Pane.Plugins.<Name>` to `Pane.Core.Features.<Name>`.
4. Move the feature's tests: `git mv tests/Pane.Plugins.<Name>.Tests/*.cs tests/Pane.Core.Tests/Features/<Name>/`, delete the old test csproj, and `dotnet sln Pane.slnx remove` it. Update the moved tests: namespace/usings to `Pane.Core.Features.<Name>`, and construction `new <X>Plugin(...)` → `new <X>Feature(...)`. Helper-only tests (e.g. `ExpressionTests`, `ScriptHeaderTests`, `RepoScannerTests`, `MacAppIndexerTests`) change only their `using`/namespace.
5. `dotnet build Pane.slnx` and `dotnet test tests/Pane.Core.Tests -f net10.0` green.
6. Commit: `git commit -m "refactor(features): fold <Name> plugin into Pane.Core"`.

> Note: after each of these tasks the app still builds but no longer loads `<id>` at runtime (the on-disk plugin is gone and the feature is not yet wired into the dispatcher). Runtime wiring happens in Task 7. This is expected.

---

### Task 2: Fold **Files** into `Pane.Core.Features.Files`

**Files:**
- Move: `src/Pane.Plugins.Files/{FilesPlugin,FileSearcherFactory,IFileSearcher,MacFileSearcher,LinuxFileSearcher,ProcessSearch}.cs` → `src/Pane.Core/Features/Files/`
- Test: move `tests/Pane.Plugins.Files.Tests/FilesPluginTests.cs` → `tests/Pane.Core.Tests/Features/Files/FilesFeatureTests.cs`

**Interfaces:**
- Produces: `Pane.Core.Features.Files.FilesFeature` with `FeatureDescriptor Descriptor`, ctors `FilesFeature()`, `FilesFeature(IFileSearcher)`, `FilesFeature(IFileSearcher, TimeSpan)`, `Task InitializeAsync()`, `IAsyncEnumerable<PaneResult> QueryAsync(PaneQuery, CancellationToken)`.

- [ ] **Step 1: Move sources + tests** per the recipe.
- [ ] **Step 2: Rewrite `FilesPlugin` → `FilesFeature`.** Descriptor:

```csharp
public FeatureDescriptor Descriptor { get; } = new(
    "files", "Files", "📁", "/", 0, new[] { "file", "folder", "find" });
```

Keep constructors, `_searcher`, `_debounce`, `LongestLiteralRun`, `Abbreviate`, `Open`, and the full `QueryAsync` body verbatim. Change `InitializeAsync(IPluginContext ctx)` to `InitializeAsync()`; body stays `{ _searcher ??= FileSearcherFactory.Create(); return Task.CompletedTask; }`. Remove `: IPlugin` and `DisposeAsync`.
- [ ] **Step 3: Update the test** — namespace to `Pane.Core.Features.Files`, `new FilesPlugin(fake)` → `new FilesFeature(fake)`. `FakeSearcher : IFileSearcher` stays. The Files tests already pass `PaneQuery` with Keyword `"/"`; unchanged.
- [ ] **Step 4: Build + test.** Run: `dotnet build Pane.slnx && dotnet test tests/Pane.Core.Tests -f net10.0`. Expected: PASS.
- [ ] **Step 5: Commit** `refactor(features): fold Files plugin into Pane.Core`.

---

### Task 3: Fold **Calculator** into `Pane.Core.Features.Calculator`

**Files:**
- Move: `src/Pane.Plugins.Calculator/{CalculatorPlugin,Expression}.cs` → `src/Pane.Core/Features/Calculator/`
- Test: move `tests/Pane.Plugins.Calculator.Tests/ExpressionTests.cs` → `tests/Pane.Core.Tests/Features/Calculator/ExpressionTests.cs`

**Interfaces:**
- Produces: `Pane.Core.Features.Calculator.CalculatorFeature` with `FeatureDescriptor Descriptor`, `Task InitializeAsync()`, `QueryAsync(...)`.

- [ ] **Step 1: Move sources + tests** per recipe.
- [ ] **Step 2: Rewrite `CalculatorPlugin` → `CalculatorFeature`.** Descriptor:

```csharp
public FeatureDescriptor Descriptor { get; } = new(
    "calc", "Calculator", "🧮", "=", 10, new[] { "calc", "math", "=" });
```

Keep `QueryAsync` (uses `Expression.TryEval`, BaseScore 100) verbatim. `InitializeAsync()` returns `Task.CompletedTask`.
- [ ] **Step 3: Update `ExpressionTests`** — namespace/usings only (`Expression` is a plain static evaluator).
- [ ] **Step 4: Build + test.** Expected: PASS (2+2, unary, parens, garbage-rejection cases).
- [ ] **Step 5: Commit** `refactor(features): fold Calculator plugin into Pane.Core`.

---

### Task 4: Fold **Scripts** into `Pane.Core.Features.Scripts`

**Files:**
- Move: `src/Pane.Plugins.Scripts/{ScriptsPlugin,ScriptHeader}.cs` → `src/Pane.Core/Features/Scripts/`
- Test: move `tests/Pane.Plugins.Scripts.Tests/ScriptHeaderTests.cs` → `tests/Pane.Core.Tests/Features/Scripts/ScriptHeaderTests.cs`

**Interfaces:**
- Produces: `Pane.Core.Features.Scripts.ScriptsFeature` with `FeatureDescriptor Descriptor`, `Task InitializeAsync()`, `QueryAsync(...)`.

- [ ] **Step 1: Move sources + tests** per recipe.
- [ ] **Step 2: Rewrite `ScriptsPlugin` → `ScriptsFeature`.** Descriptor:

```csharp
public FeatureDescriptor Descriptor { get; } = new(
    "scripts", "Scripts", "📜", ">", 0, new[] { "script", "run", "sh" });
```

Keep `Dirs`, the `QueryAsync` scan/`ScriptHeader.Parse`/`RunScript` body verbatim. `InitializeAsync()` returns `Task.CompletedTask`.
- [ ] **Step 3: Update `ScriptHeaderTests`** — namespace/usings only.
- [ ] **Step 4: Build + test.** Expected: PASS.
- [ ] **Step 5: Commit** `refactor(features): fold Scripts plugin into Pane.Core`.

---

### Task 5: Fold **VSCode** into `Pane.Core.Features.VSCode`

**Files:**
- Move: `src/Pane.Plugins.VSCode/{VSCodePlugin,RepoScanner}.cs` → `src/Pane.Core/Features/VSCode/`
- Test: move `tests/Pane.Plugins.VSCode.Tests/RepoScannerTests.cs` → `tests/Pane.Core.Tests/Features/VSCode/RepoScannerTests.cs`

**Interfaces:**
- Produces: `Pane.Core.Features.VSCode.VSCodeFeature` with `FeatureDescriptor Descriptor`, `Task InitializeAsync()`, `QueryAsync(...)`.

- [ ] **Step 1: Move sources + tests** per recipe.
- [ ] **Step 2: Rewrite `VSCodePlugin` → `VSCodeFeature`.** Descriptor (no keyword):

```csharp
public FeatureDescriptor Descriptor { get; } = new(
    "vscode", "VSCode Repos", "📂", null, 0, new[] { "code", "repo", "vscode" });
```

Keep `ReposDir`, `RepoScanner.Scan`, `Open` body verbatim. `InitializeAsync()` returns `Task.CompletedTask`.
- [ ] **Step 3: Update `RepoScannerTests`** — namespace/usings only.
- [ ] **Step 4: Build + test.** Expected: PASS.
- [ ] **Step 5: Commit** `refactor(features): fold VSCode plugin into Pane.Core`.

---

### Task 6: Fold **Apps** into `Pane.Core.Features.Apps`

Apps is the only feature that used `ctx.DataDirectory` (for the macOS icon cache). It takes that directory via the constructor now.

**Files:**
- Move: `src/Pane.Plugins.Apps/{AppsPlugin,AppIndexerFactory,IAppIndexer,MacAppIndexer,WindowsAppIndexer,LinuxAppIndexer,MacAppIcons}.cs` → `src/Pane.Core/Features/Apps/`
- Test: move `tests/Pane.Plugins.Apps.Tests/MacAppIndexerTests.cs` → `tests/Pane.Core.Tests/Features/Apps/MacAppIndexerTests.cs`

**Interfaces:**
- Produces: `Pane.Core.Features.Apps.AppsFeature` with `FeatureDescriptor Descriptor`, ctor `AppsFeature(string dataDirectory)`, `Task InitializeAsync()`, `QueryAsync(...)`.

- [ ] **Step 1: Move sources + tests** per recipe.
- [ ] **Step 2: Rewrite `AppsPlugin` → `AppsFeature`.** Descriptor (no keyword):

```csharp
public FeatureDescriptor Descriptor { get; } = new(
    "apps", "Applications", "🚀", null, 0, new[] { "app", "open", "launch" });
```

Add a constructor capturing the data dir, and move `InitializeAsync`'s work to use it:

```csharp
readonly string _dataDir;
public AppsFeature(string dataDirectory) => _dataDir = dataDirectory;

public Task InitializeAsync()
{
    _apps = AppIndexerFactory.Create().Index().ToList();
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        Directory.CreateDirectory(_dataDir);
        _icons = new MacAppIcons(_dataDir);
        var targets = _apps.Select(a => a.LaunchTarget).ToList();
        _icons.LoadCached(targets);
        _ = _icons.GenerateMissingAsync(targets);
    }
    return Task.CompletedTask;
}
```

Keep `QueryAsync`, `Launch`, and helper types verbatim.
- [ ] **Step 3: Update `MacAppIndexerTests`** — namespace/usings only (it tests `MacAppIndexer`, not the feature).
- [ ] **Step 4: Build + test.** Expected: PASS.
- [ ] **Step 5: Commit** `refactor(features): fold Apps plugin into Pane.Core`.

---

### Task 7: Cut over the dispatcher and app to the five features

Rewire `QueryDispatcher` to hold the five features directly, wire them in `Program.cs` DI, and update `Launcher.razor` to stop using `PluginManager`. This is the milestone where the app runs on the new model. `PluginManager`/marketplace code still exists but is now unused by the query path (deleted in Task 9).

**Files:**
- Modify: `src/Pane.Core/Query/QueryDispatcher.cs`
- Modify: `src/Pane.App/Program.cs:26-101`
- Modify: `src/Pane.Ui/Launcher.razor:1-146`
- Test: `tests/Pane.Core.Tests/QueryDispatcherTests.cs` (update construction)

**Interfaces:**
- Produces: `QueryDispatcher(IFuzzyMatcher matcher, SettingsStore settings, AppsFeature apps, FilesFeature files, CalculatorFeature calc, ScriptsFeature scripts, VSCodeFeature vscode)`.
- Produces: `Task<IReadOnlyList<ScoredResult>> DispatchAsync(string rawText, CancellationToken ct)` (no `plugins` parameter).
- Produces: `IReadOnlyList<FeatureDescriptor> Features { get; }` (for the settings UI).
- Produces: `void ReloadSettings()` (re-reads the disabled set after a settings save).

- [ ] **Step 1: Rewrite `QueryDispatcher`.**

```csharp
using Pane.Abstractions;
using Pane.Core.Features.Apps;
using Pane.Core.Features.Calculator;
using Pane.Core.Features.Files;
using Pane.Core.Features.Scripts;
using Pane.Core.Features.VSCode;
using Pane.Core.Settings;

namespace Pane.Core.Query;

public sealed class QueryDispatcher
{
    static readonly TimeSpan PerFeatureTimeout = TimeSpan.FromSeconds(2);
    readonly ResultRanker _ranker;
    readonly SettingsStore _settings;
    readonly (FeatureDescriptor desc,
              Func<PaneQuery, CancellationToken, IAsyncEnumerable<PaneResult>> query)[] _features;
    HashSet<string> _disabled;

    public QueryDispatcher(
        IFuzzyMatcher matcher, SettingsStore settings,
        AppsFeature apps, FilesFeature files, CalculatorFeature calc,
        ScriptsFeature scripts, VSCodeFeature vscode)
    {
        _ranker = new ResultRanker(matcher);
        _settings = settings;
        _disabled = settings.Load().DisabledPlugins;
        _features = new (FeatureDescriptor, Func<PaneQuery, CancellationToken, IAsyncEnumerable<PaneResult>>)[]
        {
            (apps.Descriptor,   apps.QueryAsync),
            (files.Descriptor,  files.QueryAsync),
            (calc.Descriptor,   calc.QueryAsync),
            (scripts.Descriptor, scripts.QueryAsync),
            (vscode.Descriptor, vscode.QueryAsync),
        };
    }

    public IReadOnlyList<FeatureDescriptor> Features => _features.Select(f => f.desc).ToList();

    public void ReloadSettings() => _disabled = _settings.Load().DisabledPlugins;

    public async Task<IReadOnlyList<ScoredResult>> DispatchAsync(string rawText, CancellationToken ct)
    {
        var active = _features.Where(f => !_disabled.Contains(f.desc.Id)).ToList();
        var (keyword, terms) = ParsePrefix(rawText, active);
        var targets = keyword is null
            ? active
            : active.Where(f => f.desc.Keyword == keyword).ToList();

        var query = new PaneQuery(rawText, keyword, terms);
        var perFeature = await Task.WhenAll(targets.Select(f => CollectAsync(f, query, ct)));
        return perFeature.SelectMany(x => x).OrderByDescending(s => s.Score).ToList();
    }

    async Task<IReadOnlyList<ScoredResult>> CollectAsync(
        (FeatureDescriptor desc, Func<PaneQuery, CancellationToken, IAsyncEnumerable<PaneResult>> query) f,
        PaneQuery query, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PerFeatureTimeout);
        var raw = new List<PaneResult>();
        try
        {
            await foreach (var r in f.query(query, cts.Token).WithCancellation(cts.Token))
                raw.Add(r);
        }
        catch { return Array.Empty<ScoredResult>(); }
        return _ranker.Rank(query, f.desc, raw).ToList();
    }

    static (string? keyword, string terms) ParsePrefix(
        string rawText,
        IReadOnlyList<(FeatureDescriptor desc, Func<PaneQuery, CancellationToken, IAsyncEnumerable<PaneResult>> query)> features)
    {
        var text = rawText.TrimStart();
        foreach (var f in features)
        {
            var kw = f.desc.Keyword;
            if (!string.IsNullOrEmpty(kw) && text.StartsWith(kw))
                return (kw, text[kw.Length..].TrimStart());
        }
        return (null, rawText.Trim());
    }
}
```

- [ ] **Step 2: Update `QueryDispatcherTests`.** Construct with real features + a temp `SettingsStore`. Example wiring for a keyword-routing test:

```csharp
var store = new SettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
var d = new QueryDispatcher(new FuzzyMatcher(), store,
    new AppsFeature(Path.GetTempPath()), new FilesFeature(new FakeSearcher()),
    new CalculatorFeature(), new ScriptsFeature(), new VSCodeFeature());
var results = await d.DispatchAsync("=2+2", CancellationToken.None);
```

Adapt existing assertions (they previously built a `(PluginMetadata, IPlugin)` list — replace with the no-arg `DispatchAsync`). Keep the behavioural assertions (calc routing, empty-query, etc.).

- [ ] **Step 3: Wire DI in `Program.cs`.** Replace lines 26–56 (the `HttpClient`/`PluginFetcher`/`PluginManager`/Marketplace/`QueryDispatcher` block) with:

```csharp
builder.Services.AddSingleton<IFuzzyMatcher, FuzzyMatcher>();

var settingsStorePath = Path.Combine(dataRoot, "settings.json");
var settingsStore = new SettingsStore(settingsStorePath);
builder.Services.AddSingleton(settingsStore);

// Features (built-in; no dynamic loading).
builder.Services.AddSingleton(new AppsFeature(Path.Combine(dataRoot, "data", "apps")));
builder.Services.AddSingleton<FilesFeature>();
builder.Services.AddSingleton<CalculatorFeature>();
builder.Services.AddSingleton<ScriptsFeature>();
builder.Services.AddSingleton<VSCodeFeature>();

builder.Services.AddSingleton(sp => new QueryDispatcher(
    sp.GetRequiredService<IFuzzyMatcher>(),
    sp.GetRequiredService<SettingsStore>(),
    sp.GetRequiredService<AppsFeature>(),
    sp.GetRequiredService<FilesFeature>(),
    sp.GetRequiredService<CalculatorFeature>(),
    sp.GetRequiredService<ScriptsFeature>(),
    sp.GetRequiredService<VSCodeFeature>()));
```

Update the `using` block (remove `Pane.Core.Marketplace`, `Pane.Core.Plugins`; add `Pane.Core.Features.Apps/Files/Calculator/Scripts/VSCode`). Remove `pluginsRoot` (lines 15, 17) and the plugin-load block (lines 99–101). Replace it with feature initialization before `app.Run()`:

```csharp
await app.Services.GetRequiredService<AppsFeature>().InitializeAsync();
await app.Services.GetRequiredService<FilesFeature>().InitializeAsync();
// calc/scripts/vscode InitializeAsync are no-ops but call for symmetry:
await app.Services.GetRequiredService<CalculatorFeature>().InitializeAsync();
await app.Services.GetRequiredService<ScriptsFeature>().InitializeAsync();
await app.Services.GetRequiredService<VSCodeFeature>().InitializeAsync();
```

- [ ] **Step 4: Update `Launcher.razor`.** Remove `@inject PluginManager Manager` (line 1). In `RunQuery()` (lines 132–139), delete the `Manager.List()...` block and change the dispatch call to:

```csharp
var results = (await Dispatcher.DispatchAsync(_text, token)).ToList();
```

Remove the now-unused `@using`/`PluginState` reference. Leave the built-in Settings command handling (`AddSettingsResult`) unchanged.

- [ ] **Step 5: Build + test.** Run: `dotnet build Pane.slnx && dotnet test tests/Pane.Core.Tests -f net10.0`. Expected: PASS. (Marketplace/PluginManager tests still exist and still pass — untouched.)

- [ ] **Step 6: Commit** `refactor(app): dispatch through built-in features; drop PluginManager from query path`.

---

### Task 8: Replace the settings UI (features + hotkey; delete marketplace)

Rework the settings pane into a plain feature list with enable/disable toggles, and remove the marketplace tab.

**Files:**
- Modify: `src/Pane.Ui/Settings/SettingsPage.razor`
- Rewrite: `src/Pane.Ui/Settings/PluginsPane.razor` → feature settings (rename to `FeaturesPane.razor`)
- Delete: `src/Pane.Ui/Settings/MarketplacePane.razor`

**Interfaces:**
- Consumes: `QueryDispatcher.Features`, `QueryDispatcher.ReloadSettings()`, `SettingsStore.Load()/Save(...)`.

- [ ] **Step 1: Rewrite the pane as `FeaturesPane.razor`.**

```razor
@using Pane.Abstractions
@using Pane.Core.Query
@using Pane.Core.Settings
@inject QueryDispatcher Dispatcher
@inject SettingsStore Settings

<div class="pane-settings-list">
    @foreach (var f in Dispatcher.Features)
    {
        <label class="pane-settings-row">
            <span class="pane-icon">@f.Icon</span>
            <span class="pane-text">
                <span class="pane-title">@f.Name</span>
                @if (!string.IsNullOrEmpty(f.Keyword))
                {
                    <span class="pane-subtitle">keyword: <code>@f.Keyword</code></span>
                }
            </span>
            <input type="checkbox" checked="@(!_disabled.Contains(f.Id))"
                   @onchange="e => Toggle(f.Id, (bool)e.Value!)" />
        </label>
    }
</div>

@code {
    HashSet<string> _disabled = new();

    protected override void OnInitialized() => _disabled = Settings.Load().DisabledPlugins;

    void Toggle(string id, bool enabled)
    {
        if (enabled) _disabled.Remove(id); else _disabled.Add(id);
        var s = Settings.Load() with { DisabledPlugins = _disabled };
        Settings.Save(s);
        Dispatcher.ReloadSettings();
    }
}
```

- [ ] **Step 2: Update `SettingsPage.razor`.** Remove the tab strip and the `<MarketplacePane />` (line 14); render only `<FeaturesPane />`. Keep the `OnBack` wiring / header.
- [ ] **Step 3: Delete `MarketplacePane.razor`.**
- [ ] **Step 4: Build.** Run: `dotnet build Pane.slnx`. Expected: PASS. (`InstalledStore`/`MarketplaceService` are no longer referenced by UI.)
- [ ] **Step 5: Commit** `feat(ui): feature enable/disable settings; remove marketplace tab`.

---

### Task 9: Delete the plugin runtime, marketplace, and their tests

Remove the now-unreferenced infrastructure and simplify `PaneSettings`.

**Files:**
- Delete dirs: `src/Pane.Core/Loading/`, `src/Pane.Core/Plugins/`, `src/Pane.Core/Marketplace/`, `src/Pane.Core/Context/`
- Modify: `src/Pane.Core/Settings/SettingsStore.cs` (drop `PluginSettings`)
- Delete tests: `tests/Pane.Core.Tests/{PluginManagerTests,PluginLoaderTests,PluginFetcherTests,PluginInstallUpdateTests,MarketplaceServiceTests,MarketplaceConfigStoreTests,InstalledStoreTests,MarketplaceModelsTests,MarketplaceSourceTests,MarketplaceVersionConsistencyTests,PluginSettingsMergeTests}.cs`
- Delete fixtures: `tests/Pane.Core.Tests/Fixtures/{TestPlugin,TestPluginV2,ThrowingPlugin}/` and remove their project references from the test csproj

- [ ] **Step 1: Delete infra source dirs** (`git rm -r` the four directories above).
- [ ] **Step 2: Delete the marketplace/plugin tests and the three fixtures.** Remove any `<ProjectReference>` to the fixtures from `tests/Pane.Core.Tests/Pane.Core.Tests.csproj` and any fixture build steps.
- [ ] **Step 3: Simplify `PaneSettings`.** In `SettingsStore.cs`:

```csharp
public record PaneSettings(HashSet<string> DisabledPlugins, string Hotkey);

// Load(): drop the PluginSettings arms; unknown JSON keys (legacy "pluginSettings") are ignored by System.Text.Json.
public PaneSettings Load()
{
    if (!File.Exists(_path)) return new PaneSettings(new(), DefaultHotkey);
    try
    {
        var s = JsonSerializer.Deserialize<PaneSettings>(File.ReadAllText(_path));
        if (s is null) return new PaneSettings(new(), DefaultHotkey);
        return s with
        {
            DisabledPlugins = s.DisabledPlugins ?? new(),
            Hotkey = string.IsNullOrEmpty(s.Hotkey) ? DefaultHotkey : s.Hotkey,
        };
    }
    catch { return new PaneSettings(new(), DefaultHotkey); }
}
```

- [ ] **Step 4: Add a backward-compat load test.** `tests/Pane.Core.Tests/SettingsStoreTests.cs`:

```csharp
[Fact]
public void Load_IgnoresLegacyPluginSettingsKey()
{
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
    File.WriteAllText(path, """
        { "disabledPlugins": ["vscode"], "hotkey": "Alt+Space",
          "pluginSettings": { "files": { "hidden": "true" } } }
        """);
    var s = new SettingsStore(path).Load();
    Assert.Contains("vscode", s.DisabledPlugins);
    Assert.Equal("Alt+Space", s.Hotkey);
    File.Delete(path);
}
```

- [ ] **Step 5: Build + test.** Run: `dotnet build Pane.slnx && dotnet test tests/Pane.Core.Tests -f net10.0`. Expected: PASS.
- [ ] **Step 6: Commit** `refactor(core): delete plugin runtime, marketplace, and their tests`.

---

### Task 10: Collapse `Pane.Abstractions` into `Pane.Core`

Move the surviving contracts into `Pane.Core` and delete the abstractions project — completing the single-app collapse.

**Files:**
- Move: `src/Pane.Abstractions/{FeatureDescriptor.cs, FuzzyMatcher.cs}` and the `PaneQuery`/`PaneResult` records → `src/Pane.Core/Contracts/`
- Delete: `src/Pane.Abstractions/{IPlugin.cs, PluginMetadata.cs}` and the project `src/Pane.Abstractions/Pane.Abstractions.csproj`

- [ ] **Step 1: Create `src/Pane.Core/Contracts/`** and move `FeatureDescriptor.cs`, `FuzzyMatcher.cs`, and a new `Contracts.cs` holding `PaneQuery` and `PaneResult` (lifted from `PluginMetadata.cs`). Change their namespace from `Pane.Abstractions` to `Pane.Core.Contracts`.
- [ ] **Step 2: Delete** `IPlugin.cs`, `PluginMetadata.cs` (its `IPlugin`/`IPluginContext`/`PluginMetadata`/`PluginSettingSpec`/`PluginSettingType` types are all now unused).
- [ ] **Step 3: Remove the project.** `dotnet sln Pane.slnx remove src/Pane.Abstractions/Pane.Abstractions.csproj`; `git rm -r src/Pane.Abstractions`. Remove the `<ProjectReference Include="..\Pane.Abstractions\...">` from `Pane.Core.csproj`, `Pane.Ui.csproj`, `Pane.App.csproj` (wherever present).
- [ ] **Step 4: Fix usings.** Replace `using Pane.Abstractions;` with `using Pane.Core.Contracts;` across `Pane.Core`, `Pane.Ui`, `Pane.App`, and the tests. (`grep -rl "Pane.Abstractions" src tests` to find them.)
- [ ] **Step 5: Build + test.** Run: `dotnet build Pane.slnx && dotnet test tests/Pane.Core.Tests -f net10.0`. Expected: PASS.
- [ ] **Step 6: Commit** `refactor(core): move contracts into Pane.Core; delete Pane.Abstractions`.

---

### Task 11: Collapse build & release to a single app

Remove the plugin packaging/marketplace-sync pipeline and single-version the app.

**Files:**
- Modify: `build/make-app.sh` (drop plugin staging, lines ~55–62)
- Delete: `build/package-plugins.sh`, `build/sync-marketplace.sh`, `marketplace.json`
- Modify: `install.sh` (drop plugin deploy, lines ~70–71)
- Modify: `release-please-config.json` (single app component, remove plugin `extra-files` + `marketplace.json` jsonpath)
- Modify: `.github/workflows/release-please.yml` (remove the plugin `build-and-upload` job; build one `Pane-<rid>.zip`)

- [ ] **Step 1: `make-app.sh`** — delete the `Building + staging plugins` loop and the `$PLUGINS_OUT` handling; the script publishes `Pane.App` and assembles the `.app` only.
- [ ] **Step 2: Delete** `build/package-plugins.sh`, `build/sync-marketplace.sh`, `marketplace.json` (`git rm`).
- [ ] **Step 3: `install.sh`** — delete the `Deploying plugins → $PLUGINS_DEST` block and `$SRC_PLUGINS`/`$PLUGINS_DEST` vars. Keep app install, LaunchAgent registration, quarantine clearing.
- [ ] **Step 4: `release-please-config.json`** — set the `.` package to `release-type: simple`, `component: "pane"`, `include-component-in-tag: true`, `package-name: "pane"`, `changelog-path: "CHANGELOG.md"`. Remove the `marketplace.json` `extra-files` entry and the five `*Plugin.cs` entries. (Optionally add the app csproj/version file as an `extra-files` bump target if one exists.)
- [ ] **Step 5: `release-please.yml`** — remove the `build-and-upload` job that runs `package-plugins.sh`/`sync-marketplace.sh`. Replace with a job that, on release, runs `build/make-app.sh` for each RID and `gh release upload` the resulting `Pane-<rid>.zip`.
- [ ] **Step 6: Verify the app bundle.** Run: `bash build/make-app.sh osx-arm64 /tmp/pane-dist` (or the script's expected args) and confirm a launchable `Pane.app` with no `plugins/` dir. Note in the commit if the packaging arg shape differs.
- [ ] **Step 7: Commit** `build: single-app packaging and release; drop plugin pipeline`.

---

### Task 12: Docs + final verification

**Files:**
- Modify: `README.md` and any docs referencing plugins/marketplace/`install.sh --local` plugin behavior
- Delete: `CHANGELOG-plugins.md` if present (superseded by single changelog)

- [ ] **Step 1: Update `README.md`** — remove plugin/marketplace install/authoring sections; describe the five built-in features and single-app install.
- [ ] **Step 2: Full build + test sweep.** Run: `dotnet build Pane.slnx && dotnet test -f net10.0`. Expected: PASS, no `Pane.Abstractions`/`Pane.Plugins.*` projects in the solution.
- [ ] **Step 3: Manual smoke (optional).** Launch the built `Pane.app`; verify apps launch, `/` file search, `=` calc, `>` scripts, VSCode repos, and the settings enable/disable toggles work.
- [ ] **Step 4: Commit** `docs: describe single-app build with built-in features`.

---

## Self-Review

**Spec coverage:**
- Delete `Pane.Abstractions` → Task 10. ✓
- Delete `Loading/`, `PluginManager`, `Marketplace/` → Task 9. ✓
- Relocate `PaneQuery`/`PaneResult`/`IFuzzyMatcher` into `Pane.Core` → Task 10. ✓
- 5 features as concrete services, no interface → Tasks 2–6 (classes), Task 7 (dispatch). ✓
- Keyword routing (`/`, `=`, `>`) + 2s timeout + error isolation + enable/disable → Task 7. ✓
- Simplified typed settings + backward-compatible load → Tasks 8–9. ✓
- Settings page hand-written, no schema; marketplace tab removed → Task 8. ✓
- Build/deploy single app; delete `package-plugins.sh`/`sync-marketplace.sh`/`marketplace.json`; simplify `install.sh`, `make-app.sh`; collapse release-please → Task 11. ✓
- Delete plugin/marketplace tests + fixtures; migrate feature tests → Tasks 2–6 (migrate), Task 9 (delete). ✓

**Placeholder scan:** No TBD/TODO. Feature `QueryAsync`/helper bodies are "moved verbatim" (exact current code, already read) rather than re-pasted — acceptable since the move is a `git mv`, not a rewrite.

**Type consistency:** `FeatureDescriptor(Id, Name, Icon, Keyword, Priority, Keywords)` used identically in Tasks 1–8. `DispatchAsync(string, CancellationToken)`, `Features`, `ReloadSettings()` consistent between Tasks 7 and 8. `PaneSettings(DisabledPlugins, Hotkey)` consistent between Tasks 8 and 9 (Task 8 uses `Load() with { DisabledPlugins = ... }`, valid before and after the `PluginSettings` field is dropped in Task 9).

**Note on Task 1 ↔ Task 7:** Task 1 adds a throwaway inline `FeatureDescriptor` construction inside `QueryDispatcher` to keep the build green; Task 7 replaces the whole dispatcher, discarding it. Intentional and called out in both tasks.
