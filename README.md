# Pane

A macOS spotlight-style launcher — press a hotkey, type, act. Ships as a single self-contained app with five built-in features.

## Features

| Feature | Activation | What it does |
|---------|-----------|--------------|
| **Applications** | (type anything) | Fuzzy-searches installed apps and launches them |
| **Files** | `/` keyword | Searches files and folders on disk; supports `*` / `?` wildcards |
| **Calculator** | `=` keyword | Evaluates math expressions inline |
| **Scripts** | `>` keyword | Lists and runs shell scripts from `~/.config/pane/scripts/` |
| **VSCode Repos** | (type anything) | Finds git repos under `~/repos/` and opens them in VS Code |

All five features are always present — no downloads, no plugins, no marketplace.

## Usage

1. Press **Alt+Space** (default hotkey) to open the launcher.
2. Type to search across Applications and VSCode Repos.
3. Prefix your query to activate a specific feature:
   - `/filename` — file search
   - `= 2 + 2` — calculator
   - `> scriptname` — run a script
4. Press **Enter** or click a result to act on it.
5. Press **Escape** to dismiss.

## Settings

Click the gear icon (or open Settings from the launcher) to:

- **Enable / disable** individual features.
- **Change the global hotkey** (e.g. `Ctrl+Space`).

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
