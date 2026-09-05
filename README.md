# Pane

A macOS spotlight-style launcher — press a hotkey, type, act. Ships as a single self-contained app with two built-in features.

## Features

| Feature | What it does | Optional keyword |
|---------|--------------|------------------|
| **Applications** | Fuzzy-searches installed apps and launches them | — |
| **Files** | Searches files and folders on disk; supports `*` / `?` wildcards | `/` |

Both features are always present — no downloads, no plugins, no marketplace.

Every feature answers a plain query out of the box; keywords are optional
accelerators that narrow the results to one feature. A feature that can't run on
this machine — no search index, no folder to scan — is skipped automatically and
says why in Settings.

## Usage

1. Press **Alt+Space** (default hotkey) to open the launcher.
2. Type to search across every available feature — an app name or a filename
   both work with no prefix.
3. Optionally prefix your query to narrow it to one feature:
   - `/filename` — files only
4. Press **Enter** or click a result to act on it.
5. Press **Escape** to dismiss.

Pane runs as a background agent: no Dock icon and no entry in the ⌘-Tab switcher,
and the launcher floats over full-screen apps and follows you across Spaces. It
lives in the menu bar — left-click the **Pane** item to open the launcher,
right-click it for **Open Pane** and **Quit Pane**. Quitting from that menu is the
way out, since there is no Dock icon to right-click.

## Settings

Search for "settings" in the launcher to open it. The page is a sidebar of tabs —
**General**, then one tab per feature:

- **General** — the global hotkey (e.g. `Ctrl+Space`). A combo needs a modifier and
  a key; anything else is refused rather than saved. Applies when Pane restarts.
- **A feature tab** — enable or disable it, change its keyword or ranking priority,
  and edit its own settings: the folders Applications scans, how many results Files
  returns and how long it waits before searching.

A feature that can't run here is labelled in the sidebar and says why on its tab.

Settings are saved to `~/.config/pane/settings.json`.

## Install

### Download the latest release (recommended)

```bash
curl -fsSL https://raw.githubusercontent.com/dknathalage/pane/main/install.sh | bash
```

Or clone the repo and run:

```bash
./install.sh
```

The script downloads the prebuilt `Pane.app` for your architecture (arm64 / x64), copies it to `~/Applications/`, and registers a LaunchAgent so the hotkey is active at login.

### Build from source

Requires .NET 10 SDK.

```bash
./install.sh --local
```

This builds the app from the local checkout (`dotnet publish` → `make-app.sh`) and then performs the same install steps.

To build without installing:

```bash
dotnet build Pane.slnx
dotnet test -f net10.0
./build/make-app.sh osx-arm64 dist/   # or osx-x64
```

## Project layout

```
src/
  Pane.App/       macOS entry point, hotkey, status-bar icon
  Pane.Core/      Features, query dispatch, settings
  Pane.Platform/  Platform abstractions (hotkey, file picker)
  Pane.Ui/        Blazor Hybrid UI (launcher window, settings page)
tests/
  Pane.Core.Tests/
```
