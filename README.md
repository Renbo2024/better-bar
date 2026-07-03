# BetterBar

A configurable **Windows 10/11 taskbar replacement** for the desktop, built on WPF (.NET 8).
BetterBar docks one or more bars to the top or bottom edge of any monitor and fills them
with the widgets you choose: start button, task buttons, system tray, clock, launchers,
audio controls and more, all themeable and laid out exactly how you like.  Some inspiration
taken from XFCE panels in the design of this system.

Why did I build this?  Mostly because the native task bars have done 2 things which have been
very frustrating over the years: 

1. Remove useful features (like quicklaunch)
2. Create features that can be annoying to some (like me), but cannot be disabled (Like window 
   previews on task button hover).

There have been hacks through tools like ExplorerPatcher which brought back disabled but still
present features, but those are beginning to disappear and become unsupported.

> **License:** GNU General Public License v3.0 - see [LICENSE](LICENSE).
> Third-party components and their licenses are listed in
> [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## Table of contents

- [What it is](#what-it-is)
- [Project status](#project-status)
- [Requirements](#requirements)
- [Install & run](#install--run)
- [Core concepts](#core-concepts)
  - [Bar definitions vs. panels](#bar-definitions-vs-panels)
  - [The settings app](#the-settings-app)
- [Bar items](#bar-items)
  - [Start Button](#start-button)
  - [Task Buttons](#task-buttons)
  - [Launcher](#launcher)
  - [System Tray](#system-tray)
  - [Clock](#clock)
  - [Audio Control](#audio-control)
  - [System Monitor](#system-monitor)
  - [Power](#power)
  - [Weather](#weather)
  - [Separator](#separator)
- [Themes](#themes)
- [Keyboard](#keyboard)
- [Advanced](#advanced)
  - [Start with Windows](#start-with-windows)
  - [Hide the Windows taskbar](#hide-the-windows-taskbar)
  - [Import / export configuration](#import--export-configuration)
  - [Software updates](#software-updates)
  - [Re-run the setup wizard (testing aid)](#re-run-the-setup-wizard-testing-aid)
- [Where your settings live](#where-your-settings-live)
- [Architecture (for contributors)](#architecture-for-contributors)
- [Building from source](#building-from-source)
- [License & attribution](#license--attribution)
- [Support the project](#support-the-project)

---

## What it is

BetterBar replaces (or supplements) the Windows taskbar with a fully configurable bar of your
own design. It uses the Win32 **AppBar** API so the bar reserves screen space like a real
taskbar, and it is **per-monitor DPI aware**, so bars position and scale correctly across
mixed-DPI multi-monitor setups.

You decide:

- how many bars there are and which monitor/edge each one docks to,
- how tall each bar is,
- which widgets ("items") appear, in what order, and how each one looks and behaves,
- the colour theme, down to individual palette entries.

## Project status

BetterBar is in **active development**, in its **early public releases**. Most items are fully
functional; a few actions are still being finished. Expect rough edges and configuration changes
between releases.

## Requirements

- **Windows 10/11** (designed for and tested on Windows 11).
- **No separate runtime for the installer** — the released build is self-contained and bundles
  everything it needs. Building or running **from source** needs the **.NET 8 SDK**.

## Install & run

### Install (recommended)

Download the latest **`BetterBar-win-Setup.exe`** from the
[Releases](https://github.com/Renbo2024/better-bar/releases) page and run it. BetterBar installs
per-user and afterwards updates itself from GitHub Releases — use **Settings → Advanced → Check for
updates**, or let the automatic background check stage the next version for you. Builds are currently
**unsigned**, so Windows SmartScreen may warn on first run; choose *More info → Run anyway*.

### Run from source

```powershell
# Build
dotnet build BetterBar.sln

# Run
dotnet run --project BetterBarApp/BetterBarApp.csproj
```

Or open `BetterBar.sln` in Visual Studio 2022 and press F5.

On first launch BetterBar opens a short **setup wizard** that asks a few quick questions and then
places a bar along the bottom of your primary monitor; its settings are written to
`%APPDATA%\BetterBar`. Right-click a bar (or use the tray/start entry point) to open **Settings**,
where everything is configured. The wizard can also be re-run as a testing aid — see
[Re-run the setup wizard](#re-run-the-setup-wizard-testing-aid).

To have BetterBar launch automatically every time you sign in, turn on
**Settings → Advanced → "Start BetterBar when I sign in"** (see [below](#start-with-windows)).

---

## Core concepts

### Bar definitions vs. panels

BetterBar separates *what a bar contains* from *where it is shown*:

- A **Bar Definition** is a reusable layout: a **name**, a **height** (in pixels), and an
  ordered list of **items**. Edit a definition once and every place it is shown updates.
- A **Panel** is one *placement* of a definition: a definition + a **monitor** + an **edge**
  (top or bottom) + an enabled flag.

This lets you, for example, design one "Primary" bar and show it on the bottom of your main
monitor, while a stripped-down "Secondary" definition appears on the top of another screen.

### The settings app

Settings is a single **Fluent (Windows 11-style)** window with a navigation pane. From it you
can manage bar definitions and their panels, edit each item, pick or customize a theme, and
reach the Advanced tools. Changes apply **live** — editing a definition immediately refreshes
every bar showing it.

---

## Bar items

Add items to a bar definition and order them left-to-right. Each item type has its own
settings page. Items are sized to their content; one item per bar may be set to **grow to
fill** the remaining width, and **Task Buttons** additionally shrink to fit when space is tight.

### Start Button

A clickable Start icon that opens BetterBar's **start menu**: a search box over a scrollable
list of pinned/recent entries.

- **Search sources** (toggle each on/off): Quick Launch, installed Apps, Windows Settings
  pages, Documents, and any number of **custom folders** you add. Each source can optionally
  contribute to **recency/frecency** ranking so things you use often float to the top.
- **Custom folders** can cascade into subfolders and use include/exclude regex filters.
- **Appearance:** icon size, margins, label text size, max label width, and a minimum menu
  height so the menu never collapses when search results are sparse.
- The menu opens **flush against the bar**: its edge meets the bar, and it aligns to the left
  of the start button's area (the monitor edge when the start button is the first item).
- Each start button has its **own** independent search configuration.

See also: [Keyboard](#keyboard) — the Windows key opens the leftmost start button on the
primary monitor.

### Task Buttons

One persistent button per open window, like the classic taskbar.

- **Scope:** show windows from *this monitor only* or *all monitors*.
- **Layout:** number of **rows**, **max button width** (buttons shrink below this to fit when
  the bar is full, then wrap into the configured rows), **horizontal spacing** between button
  columns, and **text margin** around each label.
- **Order:** an optional **priority order** (comma-separated terms matched against title/app)
  pulls matching windows to the front.
- **Accent bar:** a Windows 11-style underline under each button, with configurable
  **thickness**, **selected** and **unselected** colours, and selected/unselected **widths**
  (as a percentage of the button). By default the focused window shows a full-width blue bar
  and background windows a full-width grey bar.
- **Behaviour:** click to focus / restore, or minimize if already focused (matching the
  Windows behaviour); right-click for the standard window menu (move/size/minimize/maximize/
  close) plus BetterBar extras.
- **Grow to fill** the bar's leftover space if desired.

### Launcher

A grid of shortcuts sourced from a folder you choose.

- **Layout:** rows, icon size, icon spacing, and margin.
- **Reorder** icons by dragging; the order persists.
- **Drag in** files from Explorer to add them as shortcuts.
- **Right-click** gives the real Explorer shell context menu (Open, Copy, Properties, …) plus
  a BetterBar **Hide** command to drop an item from the launcher without deleting the file.

### System Tray

Hosts the Windows **notification area** (the tray icons that normally live by the clock).

- BetterBar becomes the tray host and renders the icons, forwarding clicks/menus to their
  owning apps exactly like Explorer's tray.
- **Layout:** rows, icon spacing, and margin (it grows in width and balances across rows as
  icons come and go).
- **Exclusions:** the clock is never shown here; the **sound** and **microphone** icons are
  excluded by default (since the [Audio Control](#audio-control) item covers them).
- The tray runs on its own background thread, so a burst of apps registering their icons at
  startup never freezes the bar.

### Clock

A configurable date/time readout. Clicking it opens BetterBar's own themed **calendar flyout**:
a month grid that follows the bar palette (weekends in an accent colour, today highlighted),
with arrows to page by month or year and a **Today** button to jump back.

### Audio Control

Speaker and/or microphone controls with live level meters.

- **Icons:** a speaker and a microphone button (show either or both), each reflecting the
  current level/mute state.
- **Flyout:** click an icon for a device picker (switch the default output/input device) and a
  level slider. A soft confirmation tone plays when you release the speaker slider.
- **Level meters:** a left/right meter under each icon. Response is tunable:
  - **Smoothing** — temporal glide that damps jitter.
  - **Auto-scale** — the meter range adapts to recent peaks so both quiet and loud activity
    stay visible; left and right share one scale so differences between them show.
  - **Auto-scale speed** — how quickly the range re-adapts after a peak.
- Appearance: icon size, spacing, meter thickness, and meter colour.

> The microphone meter opens a lightweight capture stream while it is visible, so Windows will
> show the "microphone in use" indicator when a mic meter is on a bar.

### System Monitor

One or more compact live graphs laid out left-to-right for an at-a-glance read on the bar.
Each widget has its own width, an optional **title** (over the top) and **subtitle** (over the
bottom), and shares the CPU/network look-and-feel for scroll mode, sample rate, and time span.

- **CPU monitor** — per-logical-processor usage drawn as overlapping translucent fills, with an
  optional static grid. Title/subtitle placeholder: `%value%` (overall CPU %).
- **Network monitor** — throughput of a single chosen interface: **Total** (send + receive) as a
  fill from the bottom, with **Receive** and **Send** lines on top. The vertical scale tracks
  110% of the largest total over the last 5 minutes, so a slow link never shows a 10 Gbps line. An
  optional grid draws fixed bandwidth lines (**10 Mbps / 100 Mbps / 1 Gbps / 10 Gbps**, each
  individually toggleable) plus an optional reference line at the **average** total over the
  visible span. Title/subtitle placeholders: `%receive%`, `%send%`, `%total%` (in bits/sec).

### Power

Quick power actions: **shut down**, **restart**, **sleep**, **hibernate** (shown only when
your system allows it), and **sign out**. Each button performs the action immediately, with an
optional confirmation prompt you can enable in the item's settings.

### Weather

Current conditions and forecast for any location, powered by [Open-Meteo](https://open-meteo.com).
Each instance is configured independently (its own place, units, and sections).

- **Location:** search a city in settings and pick a result; its coordinates are stored and used
  for the forecast.
- **Title:** the location name by default, or a custom override.
- **Sections** (laid out left-to-right, each with a label beneath):
  - **Current** (always shown) — an icon for the present conditions, with temperature and
    humidity beside it.
  - **Forecast** (optional) — looks ahead by a number of **hours** or **days** (1 day =
    *Tomorrow*); the label reflects the range (e.g. `2H Forecast`, `Tomorrow Forecast`,
    `3-Day Forecast`) and the icon shows the **worst** condition expected in that window.
- **Units:** Metric (°C, km/h) or Imperial (°F, mph).
- **Tooltip:** hovering a section reveals extra detail (feels-like, wind, precipitation chance)
  after a configurable delay; it can be turned off entirely.
- **Flyout:** clicking opens a flyout above the bar with **Hourly** and **Daily** tabs (the last
  viewed re-opens by default). Each tab shows a detail list plus line charts (temperature,
  humidity, wind, precipitation), paged with the ‹ › arrows — 6 hours / 7 days at a time — through
  the full range (48 hours / 14 days); the charts follow the current page.
- Appearance: icon and font sizes, section spacing, text colour, and refresh interval.

> Weather data by Open-Meteo.com, licensed CC BY 4.0.

### Separator

A spacer/divider between items.

- **Margin** on each side, an optional visible **divider line**, and a **grow to fill** option
  to push later items to the far edge.

---

## Themes

BetterBar themes the **bar and start menu** (the Fluent settings window keeps the system
Fluent look). Two built-in themes ship:

- **Dark** — a dark bar with light text (the default).
- **Light** — the inverse: soft light surfaces with dark text. Blue is kept for accents,
  selection, and the focused task button.

You can also create **custom themes** in the **theme editor** (Settings → Appearance → open
editor):

- Edit every palette entry — panel background, task-button text/hover/active, the default
  unselected accent, separators, start-menu colours, accent colours, dialog colours — plus the
  task-button corner radius. Colours support alpha where it makes sense.
- **Clone** any theme (including the built-ins) into an editable copy, **rename**, **import**,
  **export**, and **delete** your own themes. Edits preview live.

Built-in themes are read-only; clone one to start customizing.

---

## Keyboard

- **Windows key** — opens (or toggles) the leftmost Start Button on the primary monitor.
  BetterBar captures the lone Win keypress and re-injects Win-key *combinations* (Win+E,
  Win+R, …) so your shortcuts keep working.
- Inside the **start menu**: type to search, arrow keys to move through results, Enter to
  launch, Esc to close.

---

## Advanced

Found under **Settings → Advanced**.

### Start with Windows

Toggle **"Start BetterBar when I sign in"** to launch BetterBar automatically at sign-in for
the **current user**. This is implemented with the standard per-user *Run* registry entry
(`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) pointing at the current executable, so
it keeps working wherever the app is located — whether installed or run from a build.

### Hide the Windows taskbar

Toggle **"Hide the Windows taskbar"** to control whether BetterBar hides the native Explorer
taskbar(s) while it runs (on by default). Turn it off to run BetterBar *alongside* the Windows
taskbar. Either way, the original taskbar — including its auto-hide preference — is restored when
BetterBar exits.

### Import / export configuration

Save any combination of **themes** and **bar definitions** to a `.bbconfig` file and import
them back on another machine or after a reset. Imports warn before overwriting anything you
already have, so you can merge selectively.

### Software updates

When BetterBar is **installed** (from the [Releases](https://github.com/Renbo2024/better-bar/releases)
`Setup.exe`), it updates itself from GitHub Releases. The **Updates** card shows your **installed
version**. On startup it checks in the background and quietly downloads any newer version; the card then
shows **"Restart to update"**, and **Check for updates** looks right away. Updates apply on the next
restart. When you're running **from source** (not installed), there's nothing to update, so the button is
disabled and the card shows that no version is installed.

### Re-run the setup wizard (testing aid)

> **Not needed for normal use.** In everyday operation you never need this — configure your bars in
> **Settings**, which is more capable than the wizard and edits your setup in place. The `--setup`
> switch exists mainly to test/preview the first-run wizard, and because it **overwrites** your
> bottom-primary bar it's best kept to that purpose.

The same short wizard shown on first launch can be re-run by starting BetterBar with the `--setup`
command-line switch:

```powershell
BetterBarApp.exe --setup
```

Completing the wizard **replaces any bar(s) on the bottom of your primary monitor** with the new one
(bars on other edges/monitors are left untouched); cancelling changes nothing. BetterBar runs as a
**single instance**, so **close it first** — a second copy (including one launched with `--setup`) won't
start while one is already running.

---

## Where your settings live

Everything BetterBar stores lives under `%APPDATA%\BetterBar`:

- `settings.json` — bar definitions and panels.
- `app.json` — app-wide preferences (active theme, etc.).
- `Themes\*.json` — your custom themes.

Deleting that folder resets BetterBar to defaults.

---

## Architecture (for contributors)

BetterBar is a single-project WPF app (`BetterBarApp`) targeting `net8.0-windows`, with an
xUnit test project (`BetterBarApp.Tests`).

- **AppBar / positioning** is delegated to **ManagedShell**'s `AppBarWindow` (the engine
  RetroBar uses). Per-Monitor-V2 DPI awareness is required (declared in `app.manifest`).
- **Bar layout** uses a custom `BarItemsPanel` (content-size → proportional shrink of
  shrinkable items → grow-to-fill).
- **Window tasks** and the **notification area** come from ManagedShell
  (`WindowsTasks` / `WindowsTray`).
- **Theming** swaps a palette `ResourceDictionary` at runtime; styles reference it via
  `DynamicResource`.
- **Settings** persist as `System.Text.Json`.

For deeper contributor notes see [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Building from source

```powershell
dotnet build BetterBar.sln            # build app + tests
dotnet test  BetterBarApp.Tests/BetterBarApp.Tests.csproj   # run the test suite
dotnet run   --project BetterBarApp/BetterBarApp.csproj      # launch
```

---

## License & attribution

BetterBar is free software, released under the **GNU General Public License, version 3**.
You may redistribute and/or modify it under the terms of the GPL as published by the Free
Software Foundation. See [LICENSE](LICENSE) for the full text. BetterBar is distributed in the
hope that it will be useful, but **without any warranty**.

BetterBar builds on several open-source libraries; their copyrights and licenses (all
GPLv3-compatible) are reproduced in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## Support the project

BetterBar is free and open-source, built in my own time. If it's useful to you and you'd like
to help support its continued development, donations are welcome and entirely optional.

**Bitcoin (BTC)**

```
bc1qykz8q6m4yrctfazra2yfclcta2f9298pe28uac
```

**Monero (XMR)**

```
8BHA64nccm2GT3S7NbtEaLCP47FBhPfcyJd8BybXYdwHEWUwgzyFDErZjdLAh7VSf2GMThgdreToGXNDcCAyDdPSNc3PhHb
```
