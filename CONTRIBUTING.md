# Contributing to BetterBar

Thanks for your interest in BetterBar! This document covers how to build, test, and submit
changes.

## Licensing of contributions

BetterBar is licensed under the **GNU General Public License v3.0** (see [LICENSE](LICENSE)).
By submitting a contribution (pull request, patch, etc.) you agree that your contribution is
licensed under the same GPLv3 terms and that you have the right to submit it.

If you add or update a third-party dependency, update
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and confirm the new dependency's license is
**GPLv3-compatible** (MIT, BSD, Apache-2.0, etc.). Do not add dependencies under licenses that
are incompatible with GPLv3.

## Prerequisites

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 (optional) or any editor + the `dotnet` CLI

## Build, run, test

```powershell
dotnet build BetterBar.sln                                   # build everything
dotnet run   --project BetterBarApp/BetterBarApp.csproj       # run the app
dotnet test  BetterBarApp.Tests/BetterBarApp.Tests.csproj     # run the unit tests
```

Please make sure the solution **builds with zero warnings** and the **test suite passes**
before opening a pull request. Add tests for new logic where practical.

## Project layout

```
BetterBar.sln
BetterBarApp/            # the WPF application (net8.0-windows)
  App.xaml(.cs)          # startup
  Models/                # item + bar data models (CommunityToolkit.Mvvm observables)
  Services/              # panel management, settings, theming, audio, tray host, search, ...
  Controls/              # bar item controls + the custom BarItemsPanel layout
  Pages/                 # settings app pages (WPF-UI Fluent)
  Windows/               # config window, start menu, flyouts
  Themes/                # swappable palette dictionaries (Dark.xaml, Light.xaml)
  Resources/             # app-wide styles
BetterBarApp.Tests/      # xUnit tests (net8.0-windows)
```

See the [Architecture](README.md#architecture-for-contributors) section of the README for notes
on the bar-definition vs. panel model, AppBar/DPI requirements, the `BarItemsPanel` sizing rules,
theming, and the threaded tray host.

## Coding guidelines

- Match the surrounding code's style, naming, and comment density.
- Prefer the existing patterns (e.g. `[ObservableProperty]` models, `DynamicResource` palette
  keys, the `BarItemsPanel.InvalidateForChild` helper when a bar item resizes itself).
- Use the custom `Controls/NumericBox` for numeric input (not WPF-UI's `NumberBox`).
- Keep UI work on the UI thread; marshal background events (ManagedShell can raise some off the
  UI thread) before touching WPF objects.
- Be DPI-aware: physical px = logical × `VisualTreeHelper.GetDpi(...).DpiScaleX`.

## Reporting issues

When filing a bug, please include your Windows version, monitor/DPI layout, the bar
configuration involved, and steps to reproduce. Screenshots of misbehaving bars are very
helpful.
