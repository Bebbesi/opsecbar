# Open TaskBar

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-5C2D91)
![Version](https://img.shields.io/badge/version-1.1.7-2ea44f)

**A minimal, fully customizable replacement taskbar for Windows 10 and 11, docked at the top by default.**
Open TaskBar replaces the Windows taskbar visually and functionally **without ever killing Explorer**, and it restores the
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
- **Built-in media player** (Spotify, browsers, VLC…) through the Windows media controls, with seek, shuffle, repeat and per-app volume.
- **Real system tray**, including a panel for hidden icons.
- **Full customization**: colors (with an eyedropper and recent colors), gradients, opacity, blur / acrylic / glass, corners, margins, borders, neon glow, fonts, position, multiple monitors.
- **Styled widgets**: clock, date and volume with their own colors, fonts and formats.
- **More widgets, as many as you like**: world clocks, network speed, CPU, memory, battery, disk space, uptime, timers,
  shortcuts, text, separators and spacers, anywhere on the bar (even several of the same kind).
- **Animated GIFs** on the bar: drop them in Settings or straight onto the bar.
- **Themes**: 42 built-in (Cyberpunk, Lofi, Gaming, terminal palettes like Gruvbox, Tokyo Night, Catppuccin, Solarized, Phosphor CRT…) and custom, importable and exportable as `.json`.
- **Icon colors**: one color for all bar glyphs, and app icons in original colors, tinted or as monochrome silhouettes.
- **Animated borders**: minimal (pulse, comet, marching ants, scanner, rainbow) or fun (a duck, a duck family, a snake, fireflies, a UFO) walking around the bar.
- **Lightweight**: built to run all day with a small memory footprint.

## Requirements

| | |
| --- | --- |
| Operating system | Windows 10 (build 17763+) or Windows 11, x64 |
| To build | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| To run | .NET 8 Desktop Runtime (not needed if you build with `-SelfContained`) |

## Quick start

**Just want to use it?** Download `OpSecBar-Setup.exe` from the
[latest release](https://github.com/Bebbesi/opsecbar/releases/latest) and run it. It installs for the current user,
needs no administrator rights and includes the .NET runtime. Uninstall from *Windows Settings → Apps*.

To build from source:

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
| `OpSecBar.exe --restore` | Closes Open TaskBar and restores the Windows taskbar. |
| `scripts\Restore-Taskbar.cmd` | Same as above, with a double click. |
| `scripts\install.ps1 -Uninstall` | Uninstalls: restores the taskbar and removes files, shortcut and autostart entry. |

> **Emergency key: `Ctrl + Alt + Shift + Q`** closes Open TaskBar and immediately restores the Windows taskbar.

## What it does

### Left: apps
Icons only, no labels: pinned apps plus running apps. Multiple windows of the same app are grouped into a single icon.

| Action | Effect |
| --- | --- |
| Click | Open / bring to front / minimize |
| Middle click | New window |
| Right click | Menu: New window, Run as administrator, Pin to taskbar, Minimize / Maximize / Close, **End task** (kills the process) |
| Drag a file over an icon and wait | The app's window comes to the front, so you can drop inside it (with several windows, pick one from the previews) |
| Drop files on an icon | Opens them with that app, running or not, when Windows lists it under "Open with" for those file types |

### Center: media player
Optional, based on the Windows media controls: cover art, title, artist, previous / play-pause / next.
Click it to expand it with a progress bar.

### Right: system
- Clock with date (custom formats, colors and fonts) and a volume widget: click for a slider, scroll to change it, right-click to mute.
- Real system tray: visible icons plus a panel for hidden icons; left and right clicks are forwarded to the apps. Drag an icon out of the hidden panel onto the bar to keep it visible, along the bar to reorder it, or off the bar to hide it again.
- Quick settings and the Start button.
- Optional: CPU / RAM / battery.

### More widgets
**Settings → Layout → Add widgets** (or right-click the bar → *Add widgets…*): click a widget to put it on the bar, or
drag it to the exact spot in a zone. You can add as many as you like, including several of the same kind (a clock for
each time zone, a shortcut for each app or site). Each one has its own options under **Your widgets**, and you can
right-click it on the bar for quick actions or to remove it.

| Widget | Shows | Click |
| --- | --- | --- |
| World clock | the time in any time zone, named after its city or your own label | |
| Network speed | live download and upload speed | Task Manager |
| CPU / Memory | processor and RAM usage | Task Manager |
| Battery | charge level and charging state (hidden on PCs without a battery) | power settings |
| Disk space | free space on a drive | opens the drive |
| Uptime | time since Windows started | |
| Timer | a countdown (plays a sound at zero) or a stopwatch | start / pause, right-click to reset |
| Shortcut | the icon of an app, file, folder, website or command | opens it |
| Text | any text, e.g. `$ whoami` for a terminal look | |
| Separator / Spacer | a thin line or empty space between items | |

Added widgets use the bar's font and colors (or their own color), and the widget background pill from **Widgets**.
They need the advanced layout: adding the first one turns it on, starting from the classic arrangement.

### Start menu
Search, favorites, your apps, and lock / sign out / sleep / restart / shut down. The app list shows the shortcuts in
your Start Menu `Programs` folder (including Steam games); **Settings → Start menu → App list** switches it to all
installed apps (Win32 and Store).
It opens from the Start button **and from the Windows key**; other shortcuts (Win+E, Win+D, Win+L…) stay with Windows.

### Calendar and quick settings
Both are Open TaskBar's own panels, themed like the Start menu.
- **Calendar** (click the clock): the time and date, a month view you can browse with the arrows, the mouse wheel or the
  arrow keys, and a button for the Windows notification panel.
- **Quick settings**: Wi-Fi and Bluetooth on/off, airplane mode, accessibility tools, energy saver, live captions,
  the volume and the sound output device. The arrow next to Wi-Fi lists the networks around you: connect (typing the
  password when needed) or disconnect right there. The arrow next to Bluetooth lists your devices and the ones nearby:
  pair new devices (including PIN confirmation), connect or disconnect headphones and speakers, or remove a device. The arrow next to the volume slider opens the volume mixer: a slider and mute button for every app playing
  sound, plus the list of output devices (right-click the arrow for a quick device menu).

Right-click the clock or the quick settings button for the Windows panels. To always use the Windows ones, turn off
**Open TaskBar calendar** / **Open TaskBar quick settings** in **Settings → Taskbar items**.

### Settings
| Page | Options |
| --- | --- |
| Appearance | color and gradient, opacity, blur / acrylic / glass / transparent, corners, border (sides, gradient), shadow or neon glow, font. Every color picker has an eyedropper, a palette and recent colors |
| Themes | built-in and custom themes, save / import / export |
| Behavior | position (top / bottom / left / right), always visible or auto-hide, full-screen handling, multiple monitors, layout (height, margin for a floating bar, icon size and spacing) |
| Taskbar items | turn each part of the bar on or off individually |
| Layout | advanced layout: the zone and order of every item, presets, and extra widgets with their options |
| Pinned apps | manage the favorites shown in the bar |
| System tray | choose which tray icons are always visible |
| Start menu | Start menu options |
| Widgets | clock, date and volume: formats, colors, fonts, background pill |
| GIFs | upload area for animated GIFs, position and size on the bar |
| System | start with Windows, about |

### Themes
Windows 11 Minimal, Glassmorphism, macOS Inspired, Transparent, Dark, Light, Terminal, Cyberpunk, Lofi, Soft, Gaming,
Synthwave, Vaporwave, Matrix, Nord, Dracula, Sakura, Ocean, Forest, Sunset, Retro Amber, Coffee, Monochrome, Pastel Pop,
Neon Arcade, Custom.
You can save your own themes, and import or export them as `.json`.

### Updates
Every time it starts, Open TaskBar checks the [latest GitHub release](https://github.com/Bebbesi/opsecbar/releases/latest).
If a newer version exists it downloads the package, verifies its SHA-256 digest, replaces its files and restarts.
Turn this off in *Settings → System → Install updates automatically*; the same page shows the status and a *Check now* button.

*Get pre-release updates* (same page) follows the most recently published release, test builds included. Turning it
off goes back to the latest release, even if that is older than the test build installed.

To publish a release, set `<Version>` in `src/OpSecBar/OpSecBar.csproj`, commit, then push a matching tag:

```powershell
git tag v1.2.0
git push origin v1.2.0      # the Release workflow builds OpSecBar-Setup.exe and OpSecBar-win-x64.zip
```

To publish a test build as a GitHub pre-release, push a build tag instead: `b` + year, month, day, with `-2`, `-3`...
for further builds on the same day. The tag is stamped into the app's version (e.g. `1.1.4-b20261112`).

```powershell
git tag b20261112
git push origin b20261112   # second build of the day: b20261112-2
```

## Safety of the original taskbar

1. **Explorer is never closed**: the Windows taskbar is set to auto-hide and hidden; its original state is saved to disk.
2. **Restored on exit**: the menu, Settings, `--restore`, the emergency key and system shutdown all put everything back.
3. **Watchdog**: a second lightweight process watches OpSecBar. If it crashes or is killed from Task Manager,
   the taskbar is restored within half a second.
4. **Restored on startup**: if even the watchdog fails, the next launch of Open TaskBar restores the saved state
   before doing anything else.

If anything goes wrong, run `scripts\Restore-Taskbar.cmd` or press `Ctrl + Alt + Shift + Q`.

## Data locations

| What | Where |
| --- | --- |
| Installed program | `%LocalAppData%\Programs\OpSecBar` |
| Settings (saved automatically) | `%AppData%\OpSecBar\settings.json` |
| Custom themes | `%AppData%\OpSecBar\themes` |
| Uploaded GIFs | `%AppData%\OpSecBar\gifs` |
| Log | `%AppData%\OpSecBar\opsecbar.log` |

Uninstalling leaves settings and themes in `%AppData%\OpSecBar`; delete them manually if you don't need them.
On first launch, settings from the previous version (`%AppData%\NovaBar`) are imported automatically.

## Notes and known limitations

- **Blur / Acrylic / Glass** require Windows *Transparency effects* (Settings → Personalization → Colors).
  If they are off, Open TaskBar falls back to plain transparency without blur and says so on the *Appearance* page.
- The **notification center** is the Windows one: open it from the calendar or by right-clicking the clock.
- Quick settings has no brightness, night light or cast controls: those are in the Windows quick settings
  (right-click the quick settings button). Energy saver opens its Windows settings page instead of switching directly.
- Listing Wi-Fi networks needs Windows *location access* for apps; if it's off, the Wi-Fi page says so and links to
  the setting. Work or school (802.1X) networks are connected from the Windows panel.
- Bluetooth devices that aren't headphones or speakers (mice, keyboards, controllers) can't be connected on demand:
  they connect by themselves when switched on and in range.
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
    WinKeyHook             Windows key -> Open TaskBar Start menu
    AudioService           volume, mute, sound output device
    RadioService           Wi-Fi, Bluetooth, airplane mode on/off
    WifiService            Wi-Fi networks: scan, connect, disconnect
    BluetoothService       Bluetooth devices: list, pair, remove (audio connect lives in AudioService)
    AutoStartService, SystemInfoService
  Windows/                 TaskbarWindow, SettingsWindow, and the panels built on FlyoutWindow:
                           StartMenuWindow, CalendarWindow, QuickSettingsWindow
  Controls/                MediaWidget, TrayArea, ExtraWidgetView (the widgets added from Layout)
  Themes/Controls.xaml     shared styles (buttons, menus, tooltips, sliders)
```

### Adding an option

1. Add a property to `AppSettings` (or `ThemeData`).
2. Add a row to the right page in `SettingsWindow.xaml.cs`.
3. Use the value in `TaskbarWindow.ApplyTheme` / `ApplyElements`.

## Acknowledgements

Open windows, the system tray and AppBar registration use the open-source
[ManagedShell](https://github.com/cairoshell/ManagedShell) library (Apache 2.0), the same one behind RetroBar and Cairo Shell.
