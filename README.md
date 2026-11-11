# OpSecBar

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-5C2D91)
![Version](https://img.shields.io/badge/version-1.0.0-2ea44f)

**A minimal, fully customizable replacement taskbar for Windows 10 and 11, docked at the top by default.**
OpSecBar replaces the Windows taskbar visually and functionally **without ever killing Explorer**, and it restores the
original taskbar automatically in every situation: normal exit, crash, or being killed from Task Manager.

```
[ app icons ]                [ media player ]                [ clock | tray | quick settings | Start ]
```

## Table of contents

- [Features](#features)
- [Requirements](#requirements)
- [Quick start](#quick-start)
- [Commands and shortcuts](#commands-and-shortcuts)
- [What it does](#what-it-does)
- [Safety of the original taskbar](#safety-of-the-original-taskbar)
- [Data locations](#data-locations)
- [Notes and known limitations](#notes-and-known-limitations)
- [Development](#development)
- [Acknowledgements](#acknowledgements)

## Features

- **Never kills Explorer**: the Windows taskbar is hidden and reliably restored.
- **Three clear zones**: apps on the left, media player in the center, clock / tray / Start on the right.
- **Its own Start menu**, also opened by the Windows key.
- **Built-in media player** (Spotify, browsers, VLC…) through the Windows media controls.
- **Real system tray**, including a panel for hidden icons.
- **Full customization**: colors, opacity, blur / acrylic / glass, corners, margins, fonts, position, multiple monitors.
- **Themes**: built-in and custom, importable and exportable as `.json`.
- **Lightweight**: built to run all day with a small memory footprint.

## Requirements

| | |
| --- | --- |
| Operating system | Windows 10 (build 17763+) or Windows 11, x64 |
| To build | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| To run | .NET 8 Desktop Runtime (not needed if you build with `-SelfContained`) |

## Quick start

```powershell
.\scripts\publish.ps1        # builds into dist\OpSecBar
.\scripts\install.ps1        # copies to %LocalAppData%\Programs\OpSecBar, creates a shortcut and starts it
```

To produce a build that runs on any PC without installing the runtime:

```powershell
.\scripts\publish.ps1 -SelfContained
```

To try it without installing, run `dist\OpSecBar\OpSecBar.exe`.
The installation is per-user and **does not require administrator rights**.
Start with Windows can be enabled from *Settings → System*.

## Commands and shortcuts

| Command | Effect |
| --- | --- |
| `OpSecBar.exe` | Starts the bar. If it is already running, opens Settings. |
| `OpSecBar.exe --restore` | Closes OpSecBar and restores the Windows taskbar. |
| `scripts\Restore-Taskbar.cmd` | Same as above, with a double click. |
| `scripts\install.ps1 -Uninstall` | Uninstalls: restores the taskbar and removes files, shortcut and autostart entry. |

> **Emergency key: `Ctrl + Alt + Shift + Q`** closes OpSecBar and immediately restores the Windows taskbar.

## What it does

### Left: apps
Icons only, no labels: pinned apps plus running apps. Multiple windows of the same app are grouped into a single icon.

| Action | Effect |
| --- | --- |
| Click | Open / bring to front / minimize |
| Middle click | New window |
| Right click | Menu: New window, Run as administrator, Pin to taskbar, Minimize / Maximize / Close, **End task** (kills the process) |

### Center: media player
Optional, based on the Windows media controls: cover art, title, artist, previous / play-pause / next.
Click it to expand it with a progress bar.

### Right: system
- Clock with date.
- Real system tray: visible icons plus a panel for hidden icons; left and right clicks are forwarded to the apps.
- Windows quick settings and the Start button.
- Optional: CPU / RAM / battery.

### Start menu
Search, favorites, all installed apps (Win32 and Store), and lock / sign out / sleep / restart / shut down.
It opens from the Start button **and from the Windows key**; other shortcuts (Win+E, Win+D, Win+L…) stay with Windows.

### Settings
| Page | Options |
| --- | --- |
| Appearance | color, opacity, blur / acrylic / glass / transparent, corners, border, shadow, font |
| Themes | built-in and custom themes, save / import / export |
| Behavior | position (top / bottom / left / right), always visible or auto-hide, full-screen handling, multiple monitors, layout (height, margin for a floating bar, icon size and spacing) |
| Taskbar items | turn each part of the bar on or off individually |
| Pinned apps | manage the favorites shown in the bar |
| System tray | choose which tray icons are always visible |
| Start menu | Start menu options |
| System | start with Windows, about |

### Themes
Windows 11 Minimal, Glassmorphism, macOS Inspired, Transparent, Dark, Light, Terminal, Custom.
You can save your own themes, and import or export them as `.json`.

### Updates
Every time it starts, OpSec Bar checks the [latest GitHub release](https://github.com/Bebbesi/opsecbar/releases/latest).
If a newer version exists it downloads the package, verifies its SHA-256 digest, replaces its files and restarts.
Turn this off in *Settings → System → Install updates automatically*; the same page shows the status and a *Check now* button.

To publish a release, set `<Version>` in `src/OpSecBar/OpSecBar.csproj`, commit, then push a matching tag:

```powershell
git tag v1.2.0
git push origin v1.2.0      # the Release workflow builds and attaches OpSecBar-win-x64.zip
```

## Safety of the original taskbar

1. **Explorer is never closed**: the Windows taskbar is set to auto-hide and hidden; its original state is saved to disk.
2. **Restored on exit**: the menu, Settings, `--restore`, the emergency key and system shutdown all put everything back.
3. **Watchdog**: a second lightweight process watches OpSecBar. If it crashes or is killed from Task Manager,
   the taskbar is restored within half a second.
4. **Restored on startup**: if even the watchdog fails, the next launch of OpSecBar restores the saved state
   before doing anything else.

If anything goes wrong, run `scripts\Restore-Taskbar.cmd` or press `Ctrl + Alt + Shift + Q`.

## Data locations

| What | Where |
| --- | --- |
| Installed program | `%LocalAppData%\Programs\OpSecBar` |
| Settings (saved automatically) | `%AppData%\OpSecBar\settings.json` |
| Custom themes | `%AppData%\OpSecBar\themes` |
| Log | `%AppData%\OpSecBar\opsecbar.log` |

Uninstalling leaves settings and themes in `%AppData%\OpSecBar`; delete them manually if you don't need them.
On first launch, settings from the previous version (`%AppData%\NovaBar`) are imported automatically.

## Notes and known limitations

- **Blur / Acrylic / Glass** require Windows *Transparency effects* (Settings → Personalization → Colors).
  If they are off, OpSecBar falls back to plain transparency without blur and says so on the *Appearance* page.
- **Quick settings** and the **notification center** are the Windows ones and open in their usual place.
- The tray shows the icons Windows exposes as classic notification icons. If network or battery do not appear
  (it depends on your Windows 11 version), reach them from the quick settings button.
- Tray icons are drawn by the individual apps: on a light bar, white icons can be hard to see.

## Development

```powershell
dotnet build OpSecBar.sln        # debug build
dotnet run --project src\OpSecBar
```

### Code layout

```
scripts/
  publish.ps1              builds into dist\OpSecBar (-SelfContained to bundle the runtime)
  install.ps1              installs / uninstalls for the current user
  Restore-Taskbar.cmd      one-click taskbar restore
src/OpSecBar/
  Program.cs               entry point; lightweight --watchdog and --restore modes
  App.xaml(.cs)            composition: services, bars, lifecycle, error handling
  Models/AppSettings.cs    settings, theme (ThemeData), pinned apps
  Services/
    SettingsService        loading, automatic saving, change notifications
    ThemeService           built-in and user themes, dynamic UI resources
    TaskbarGuard           hide / restore the Windows taskbar, watchdog
    TaskListService        pinned apps + open windows -> bar icons
    MediaService           system media controls
    AppCatalogService      installed apps (Start menu), launching
    IconService            high-resolution icons from the shell
    BackdropHelper/Window  blur and acrylic, including rounded corners
    WinKeyHook             Windows key -> OpSecBar Start menu
    AutoStartService, SystemInfoService
  Windows/                 TaskbarWindow, StartMenuWindow, SettingsWindow
  Controls/                MediaWidget, TrayArea
  Themes/Controls.xaml     shared styles (buttons, menus, tooltips, sliders)
```

### Adding an option

1. Add a property to `AppSettings` (or `ThemeData`).
2. Add a row to the right page in `SettingsWindow.xaml.cs`.
3. Use the value in `TaskbarWindow.ApplyTheme` / `ApplyElements`.

## Acknowledgements

Open windows, the system tray and AppBar registration use the open-source
[ManagedShell](https://github.com/cairoshell/ManagedShell) library (Apache 2.0), the same one behind RetroBar and Cairo Shell.
