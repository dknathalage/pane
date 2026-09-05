# Startup, Self-Update, and Menu Bar Indication

**Date:** 2026-09-05
**Status:** Approved design (pending spec review)

## Summary

Four related capabilities that make an installed Pane feel like a
maintained desktop app rather than a one-shot install:

1. **Version identity** — the app knows its own version at runtime.
2. **Self-update from GitHub releases** — Pane auto-checks for a newer
   release, and a button in Settings downloads it, replaces the
   installed bundle, and relaunches.
3. **Start at login, controlled in-app** — a Settings toggle that owns
   the `com.pane.launcher` LaunchAgent.
4. **A real menu bar icon** — an SF Symbol template image in place of
   the current literal text "Pane".

## Motivation

Two of these partly exist, and both are stuck at install time:

- `install.sh` writes a LaunchAgent with `RunAtLoad`, so Pane already
  starts at login — but nothing in the app can see or change that. A
  user who wants to stop Pane launching at login has to know the
  LaunchAgent exists and hand-edit `~/Library/LaunchAgents`.
- `MacStatusBar` already puts an item in the menu bar, but its title
  is the literal string `"Pane"`, which eats menu bar width and looks
  unlike every other status item on the system.

Updating does not exist at all, and it cannot be built until the app
has a version:
`build/make-app.sh` hardcodes `CFBundleVersion 1.0` while
`.release-please-manifest.json` says `1.2.0`, and nothing stamps a
version into the managed assembly. There is no value in the process
to compare a GitHub release against.

A related fact discovered while designing this: **no release has ever
shipped a `Pane-osx-*.zip` asset.** The three existing releases
(`plugins-v1.2.0` and earlier) carry only `Pane.Plugins.*.zip` from
the pre-consolidation era. So `install.sh` without `--local` is
currently broken as well, and the updater will correctly find nothing
until the first `pane-v*` release is cut by the existing workflow.
The design must degrade gracefully in exactly that situation rather
than treat it as an error state.

## Decisions (locked)

1. **Full self-update** — check *and* one-click install, not
   check-and-notify and not a shell-out to `install.sh`.
2. **Auto-check, manual install** — Pane checks on launch and every
   24h; it never replaces itself without an explicit press. Auto-check
   is toggleable.
3. **Startup is a Settings toggle** — Pane owns the LaunchAgent plist
   and reflects its true on-disk state.
4. **Menu bar gets an icon only** — no update badge, no busy state, no
   version line in the menu. Just a proper template icon.
5. **`.release-please-manifest.json` is the single source of version
   truth** — no second place to bump.

## Architecture

### New/changed layout

```
Pane.Core/Updates/            # NEW - portable, no platform deps, fully tested
  AppVersion.cs               #   parse + compare, tag-name tolerance
  ReleaseInfo.cs              #   ReleaseInfo, ReleaseAsset records
  IReleaseSource.cs           #   check-for-release abstraction
  GitHubReleaseSource.cs      #   GitHub releases API client
  IUpdateInstaller.cs         #   platform install abstraction
  UpdateService.cs            #   orchestration + status state machine
  UpdateState.cs              #   last-check persistence

Pane.Core/Updates/BundleLayout.cs      # NEW - find enclosing .app, validate a bundle
Pane.Core/Updates/MacUpdateInstaller.cs # NEW - download, validate, detached swap
Pane.Core/Startup/MacLoginItem.cs       # NEW - LaunchAgent plist ownership
Pane.Core/Startup/LaunchAgentPlist.cs   # NEW - pure plist string generation

Pane.App/MacStatusBar.cs        # CHANGED - SF Symbol instead of text title
Pane.Core/Settings/             # CHANGED - AutoCheckUpdates app-level flag
Pane.Ui/Settings/GeneralPane.razor # CHANGED - version, startup, update controls
build/make-app.sh               # CHANGED - stamp real version
install.sh                      # CHANGED - launchctl bootstrap, not load
```

### 1. Version identity

`.release-please-manifest.json` (`{".": "1.2.0"}`) is the source of
truth.

`build/make-app.sh` resolves the version in this order:

1. `PANE_VERSION` environment variable, if set.
2. The `"."` key of `.release-please-manifest.json`.
3. `0.0.0` if neither is readable (a dev build).

It then passes `-p:Version=$VERSION` to `dotnet publish` and
substitutes the value into `CFBundleShortVersionString` and
`CFBundleVersion` in the generated `Info.plist`, replacing the
hardcoded `1.0`.

The release workflow already knows the tag it just cut
(`needs.release-please.outputs.tag_name`, e.g. `pane-v1.3.0`), so it
exports `PANE_VERSION` from that tag before calling `make-app.sh`.
This keeps CI builds stamped even though release-please updates the
manifest in the same commit.

At runtime, `AppVersion.Current` reads
`AssemblyInformationalVersionAttribute` from the entry assembly and
trims anything from the first `+` (SourceLink appends `+<sha>`).

The entry assembly specifically: `Pane.Core.csproj` sets
`<GenerateAssemblyInfo>false</GenerateAssemblyInfo>`, so `Pane.Core`
carries no version attribute of its own and reading
`typeof(AppVersion).Assembly` would always yield nothing.
`Pane.App.csproj` leaves assembly-info generation on, so
`-p:Version=` reaches the attribute there. When the attribute is
absent or unparseable — a unit test host, for instance —
`AppVersion.Current` is `0.0.0`, which makes every real release look
newer and is therefore reported honestly rather than hidden.

### 2. `AppVersion`

A comparable value type over `major.minor.patch`.

- `AppVersion.TryParse("1.2.0")` — bare triple.
- `AppVersion.TryParseTag(tag)` — tolerates the prefixes that appear
  in this repo's history and future: `pane-v1.2.0`, `plugins-v1.2.0`,
  `v1.2.0`, `1.2.0`. It strips any leading non-digit prefix ending in
  `v` or `-v`, then parses the remainder.
- Ordering is numeric per component, so `1.10.0 > 1.9.0`.
- Pre-release suffixes (`-rc1`) are **out of scope**: a tag carrying
  one fails to parse and is skipped, rather than being mis-ordered.
  release-please does not produce them for this project.

### 3. `IReleaseSource` / `GitHubReleaseSource`

```csharp
public sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);
public sealed record ReleaseInfo(
    AppVersion Version, string Tag, string HtmlUrl,
    IReadOnlyList<ReleaseAsset> Assets);

public interface IReleaseSource
{
    Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct);
}
```

`GitHubReleaseSource` GETs
`https://api.github.com/repos/{repo}/releases/latest` with an explicit
`User-Agent` (GitHub rejects requests without one) and
`Accept: application/vnd.github+json`. The repo defaults to
`dknathalage/pane` and is overridable so a fork can point elsewhere.
The repo is public, so no token handling exists.

It takes an `HttpMessageHandler` in its constructor so tests supply a
canned handler and never touch the network. `FetchLatestAsync` returns
`null` — not an exception — when the tag fails to parse, so a stray
non-version tag is a no-op rather than a failure the user sees.
Transport and non-success HTTP status do throw, and `UpdateService`
turns them into `Failed`.

### 4. `UpdateService`

The one place that decides anything. Holds current version, an
`IReleaseSource`, an `IUpdateInstaller`, and an `UpdateState` store.

```csharp
public abstract record UpdateStatus
{
    public sealed record Idle : UpdateStatus;
    public sealed record Checking : UpdateStatus;
    public sealed record UpToDate(DateTimeOffset CheckedAt) : UpdateStatus;
    public sealed record Available(ReleaseInfo Release, ReleaseAsset Asset) : UpdateStatus;
    public sealed record Downloading(int Percent) : UpdateStatus;
    public sealed record Installing : UpdateStatus;
    public sealed record Failed(string Message) : UpdateStatus;
}
```

- `Status` is the current value; `StatusChanged` is an event the
  Settings pane subscribes to.
- `CheckAsync(force)` — fetches, compares against
  `AppVersion.Current`, and resolves the asset for this machine's RID
  (below). Newer release **with** a matching asset → `Available`.
  Newer release **without** one → `UpToDate` with a logged note, since
  a release we cannot install is not actionable for the user. Same or
  older → `UpToDate`.
- `InstallAsync()` — only valid from `Available`; delegates to
  `IUpdateInstaller`, forwarding progress as `Downloading(pct)`.
- `MaybeAutoCheckAsync()` — no-ops when auto-check is off or when
  `UpdateState.LastCheckUtc` is under 24h old. Called once at startup;
  a timer re-invokes it every 6h so a long-running instance eventually
  crosses the 24h boundary.

RID resolution mirrors what `install.sh` does:
`RuntimeInformation.ProcessArchitecture` → `osx-arm64` / `osx-x64` →
asset name `Pane-{rid}.zip`, matched case-insensitively.

`UpdateState` persists only `LastCheckUtc` to
`~/.config/pane/update-state.json`. It is deliberately **not** in
`settings.json`: it is machine state, not a user preference, and
writing it on every check would churn a file the user edits by hand.

### 5. `MacUpdateInstaller` — self-replacement

`MacUpdateInstaller`, `BundleLayout`, and `MacLoginItem` live in
`Pane.Core`, not `Pane.App`. That matches the existing convention —
`MacAppIndexer` and `MacFileSearcher` are already macOS-specific types
inside `Pane.Core`, covered by `Pane.Core.Tests` — and it matters
here: there is no `Pane.App.Tests` project and the solution has only
two test projects, so logic placed in `Pane.App` would be untestable
without adding one. Only ObjC interop (`MacStatusBar`) stays in
`Pane.App`.

A running app cannot safely overwrite its own bundle: a self-contained
.NET app demand-loads dylibs out of `Contents/MacOS` for its whole
lifetime, so replacing those files underneath it risks a fault. An
in-process swap-then-re-exec was considered and rejected for that
reason.

Instead, the swap is performed by a detached helper after the app is
gone — the approach Sparkle and most macOS updaters take:

1. **Download** the asset to a fresh temp directory, reporting
   progress from `Content-Length` when the server provides it.
2. **Expand** with `/usr/bin/ditto -x -k <zip> <dir>` (ditto, not
   `unzip`, preserves the symlinks and extended attributes inside a
   `.app`).
3. **Validate before committing to anything.** All must hold:
   - exactly one `*.app` at the archive root, named `Pane.app`;
   - `Contents/MacOS/Pane.App` exists and is executable;
   - `Contents/Info.plist` parses and its
     `CFBundleShortVersionString` is greater than the running version.

   This is `BundleLayout.Validate`, a pure function over a directory
   path, so every branch is unit-tested against fixture directories.

   Any failure aborts with `Failed(msg)` and deletes the temp
   directory. Nothing has touched the installed app at this point.
4. **Hand off.** Write a helper script to the temp directory, spawn it
   fully detached, then terminate the app. The script:
   - waits for our PID to exit (bounded — ~30s, then proceeds);
   - `rm -rf`s the installed bundle and `cp -R`s the new one in;
   - `xattr -dr com.apple.quarantine` on the result;
   - `open`s the app;
   - deletes its own temp directory.

   If the copy fails the script restores the bundle it moved aside, so
   a failed update leaves a working app rather than none.

The install target is the bundle the process is actually running from
— `BundleLayout.FindEnclosingBundle(AppContext.BaseDirectory)`, which
walks up to the enclosing `.app` — not a hardcoded
`~/Applications/Pane.app`. When the
process is *not* running from a bundle (a `dotnet run` dev session),
`CanInstall` is false and the Settings pane says so instead of
offering a button that would corrupt a checkout.

**Trust model.** This downloads and executes unsigned code, trusting
TLS and GitHub's control of the release assets. That is precisely the
trust `install.sh` already requires, so it introduces no new exposure
— but it is explicitly *not* code-signature or checksum verification,
and the UI will not imply that it is. Signing and notarization are a
separate, larger piece of work and are out of scope here.

### 6. `MacLoginItem` — start at login

Owns `~/Library/LaunchAgents/com.pane.launcher.plist`.

- `IsEnabled` — true when the plist exists *and* its first
  `ProgramArguments` entry points at the bundle we are running from.
  Comparing the path matters: a plist left behind by an older install
  at a different location would otherwise report "on" while silently
  launching something else at login.
- `Enable()` — write the plist, then
  `launchctl bootstrap gui/<uid> <plist>` (preceded by a tolerated
  `bootout`, so re-enabling over a stale registration works).
- `Disable()` — `launchctl bootout gui/<uid>/com.pane.launcher`, then
  delete the plist.
- `CanManage` — false when not running from a bundle, same as the
  installer.

Plist generation is split out as a pure
`LaunchAgentPlist.Build(string execPath)`, so its content is
unit-tested without touching the filesystem or `launchctl`.

`install.sh` is corrected at the same time: its `launchctl load` is
the deprecated form, and `build/install-startup.sh` already uses
`bootout`/`bootstrap`. The two are brought in line.

### 7. Menu bar icon

`MacStatusBar.Setup` currently does `setTitle:` with an `NSString`. It
gains an image path first:

```
NSImage imageWithSystemSymbolName:@"magnifyingglass" accessibilityDescription:@"Pane"
```

then `setTemplate:YES` (so macOS inverts it correctly for light and
dark menu bars) and `setImage:` on the button. `LSMinimumSystemVersion`
is already `11.0` and SF Symbols arrived in 11.0, so no OS check is
needed — but if the symbol lookup returns nil the code falls back to
the existing text title rather than leaving an invisible, unclickable
status item.

### 8. Settings — General pane

`GeneralPane.razor` grows, below the existing hotkey field:

- **Version** — `Pane v1.2.0`, read-only.
- **Start at login** — checkbox bound to `MacLoginItem.IsEnabled`,
  disabled with an explanation when `CanManage` is false.
- **Check for updates automatically** — checkbox bound to the new
  setting.
- **Check now** — button; disabled while `Checking`.
- **Status line** — renders `UpdateStatus`: "Up to date (checked 2
  hours ago)", "Pane v1.3.0 is available" plus an **Install and
  restart** button, a progress percentage while downloading, or the
  failure message.

The pane subscribes to `StatusChanged` and calls `StateHasChanged`,
unsubscribing on dispose.

`PaneSettings` becomes
`(string Hotkey, bool AutoCheckUpdates, Dictionary<string, JsonObject> Features)`
with `AutoCheckUpdates` defaulting to **true** and read
back-compatibly — an existing `settings.json` without the key loads as
true rather than resetting the rest of the document.

### DI wiring

`Program.cs` registers `IReleaseSource` → `GitHubReleaseSource`,
`IUpdateInstaller` → `MacUpdateInstaller`, `UpdateService`, and
`MacLoginItem`, then fires `MaybeAutoCheckAsync()` on a background
task after the window is up so a slow or offline network never delays
startup.

## Error handling

Every failure is a status the user can read, never a crash and never a
silent no-op:

| Condition | Behaviour |
|---|---|
| Offline / DNS failure / timeout | `Failed("Could not reach GitHub.")`; last-check time is not advanced. |
| GitHub 403 rate limit | `Failed` naming rate limiting, so it is not mistaken for "no update". |
| Latest tag unparseable | Treated as no release; `UpToDate`. |
| Newer release, no matching asset | `UpToDate`, note logged. Covers today's `plugins-v*` releases exactly. |
| Download truncated / bad zip | `Failed`; temp cleaned; installed app untouched. |
| Downloaded bundle fails validation | `Failed`; temp cleaned; installed app untouched. |
| Copy fails mid-swap | Helper restores the moved-aside bundle and reopens it. |
| Not running from a `.app` | Update and login-item controls disabled with an explanation. |
| `launchctl` non-zero exit | Toggle reverts to real on-disk state; message surfaced. |

## Testing

Test-driven, with the platform boundary drawn so that the logic is
portable and the untestable part is thin.

**Unit — `Pane.Core.Tests/Updates/`**

- `AppVersion`: parse, reject junk, numeric ordering (`1.10.0 > 1.9.0`),
  equality; `TryParseTag` across `pane-v`, `plugins-v`, `v`, bare, and
  a pre-release tag that is correctly rejected.
- `GitHubReleaseSource` against a stub `HttpMessageHandler`: parses a
  real captured payload, selects assets, sends a `User-Agent`, returns
  null on an unparseable tag, throws on 500 and on 403.
- `UpdateService` with a fake source and fake installer: every
  transition in the table above; asset selection per architecture;
  `MaybeAutoCheckAsync` no-ops when off or within 24h and runs when
  stale; `InstallAsync` rejected from a non-`Available` status.
- `UpdateState`: round-trip, and a corrupt file loading as "never
  checked" rather than throwing.

**Unit — `Pane.Core.Tests/SettingsStoreTests.cs`**

- `AutoCheckUpdates` round-trips; a document without the key loads
  `true` and preserves the rest.

**Unit — `Pane.Core.Tests/Startup/` and `Pane.Core.Tests/Updates/`**

- `LaunchAgentPlist.Build` output: label, `ProgramArguments` including
  `--startup`, `RunAtLoad`.
- `BundleLayout.FindEnclosingBundle`: resolves a path inside
  `Contents/MacOS`, returns null for a plain directory.
- `BundleLayout.Validate`: accepts a well-formed fixture directory,
  rejects a missing executable, a missing `Info.plist`, and a
  same-or-older version.

No new test project is needed: all of the above sit in `Pane.Core`,
which `Pane.Core.Tests` already references.

**Component — `Pane.Ui.Tests`**

- General pane renders version, both checkboxes, and Check now;
  toggling auto-check persists; an `Available` status renders the
  install button; a `Failed` status renders the message.

**Manual verification** (the parts no test can honestly cover): a real
end-to-end update against a genuine `pane-v*` release, the login-item
toggle surviving an actual logout/login, and the icon's appearance in
both light and dark menu bars.

## Out of scope

- Code signing, notarization, and checksum/signature verification of
  downloaded assets.
- Delta updates, rollback to a previous version, update channels, and
  pre-release tags.
- Windows and Linux updating or login items — this is macOS-only, as
  `install.sh` and `MacStatusBar` already are.
- Update badges or busy indication in the menu bar (explicitly
  declined; the icon is the whole menu bar change).

## Implementation order

1. **Version identity** — `make-app.sh` stamping, workflow
   `PANE_VERSION`, `AppVersion.Current`. Hard prerequisite: nothing
   downstream can compare versions without it.
2. **`AppVersion`** parsing and comparison.
3. **`GitHubReleaseSource`** + `ReleaseInfo`.
4. **`UpdateService`** + `UpdateState`.
5. **`MacUpdateInstaller`** — download, validate, detached swap.
6. **`MacLoginItem`** + the `install.sh` `bootstrap` fix.
7. **Settings General pane** — wires 1-6 into UI.
8. **Menu bar icon** — independent of everything else; can land any
   time.
