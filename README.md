# AlightenInk

Elegant on-screen annotation for Windows 10/11 — draw over charts while you trade or record, then
click straight through to your platform. A modern, dark, glassy replacement for Epic Pen.

## Features

- Transparent overlay on every monitor (mixed DPI aware), **Draw** and **Pass-through** modes.
  Starts in pass-through; picking any tool starts drawing; going back returns focus to your app.
- Pen, highlighter, eraser (ink, shapes and text), line, arrow, rectangle, ellipse,
  full-width horizontal "price" line, and on-screen text. Hold **Shift** to snap lines/arrows to 45°,
  rectangles to squares and ellipses to circles.
- Seven brand swatches plus custom colours (HSV picker; right-click a custom swatch to remove it),
  three widths.
- Fading ink (2/3/5/8 s hold, then a 0.8 s fade) — right-click the timer button to change the hold.
- Whiteboard / blackboard, global undo/redo (one eraser drag = one step), clear all.
- Snagit-style screenshots: the camera button / Ctrl+Shift+S dims the screen, you drag a dashed
  pumpkin box (live W×H readout), and the region — drawings included, toolbar hidden — is saved to
  `Pictures\AlightenInk` **and** copied. A click without dragging takes the whole monitor, Esc cancels,
  right-click the camera for an instant full-screen shot, and the ⋯ menu switches the default
  between *Region* and *Full screen*. The copy button uses the same selector but only copies.
- Drawing cursors with a dark/light dual outline that stay visible on whiteboard, blackboard and
  charts; pen, highlighter and eraser cursors are sized to the brush.
- Floating toolbar: horizontal or vertical, collapses to a small round button, remembers its place.
- Tray icon, single instance, automatic updates (Velopack + GitHub Releases).

## Shortcuts

| Global (work anywhere) | | While drawing | |
|---|---|---|---|
| Ctrl+Shift+D | Draw / pass-through | P H E T | Pen, highlighter, eraser, text |
| Ctrl+Shift+F | Fading ink on/off | L A R O I | Line, arrow, rectangle, ellipse, h-line |
| Ctrl+Shift+C | Copy region | 1 2 3 | Width |
| Ctrl+Shift+S | Screenshot (region by default) | Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+Shift+Z / Y | Undo / redo | Esc | Clear all (in an empty text box: leave text mode) |
| Ctrl+Shift+X | Clear all | | |
| Ctrl+Shift+W | Whiteboard → blackboard → off | | |

If another program already owns a global shortcut (Epic Pen, for example, uses some of these),
AlightenInk still starts; the button tooltip says "(in use by another app)".

Text: click to place, type, **Enter** commits, **Shift+Enter** adds a line, **Esc** cancels.

Settings live in `%AppData%\AlightenInk\settings.json` (delete it to reset); a `log.txt` next to it
records errors.

## Building

```powershell
dotnet build AlightenInk.sln
dotnet test  AlightenInk.sln
```

Projects:

- `src/AlightenInk` — the WPF app (net8.0-windows; WinForms only for the tray icon, monitors and clipboard).
- `src/AlightenInk.Core` — UI-free logic: settings, undo/redo, shape geometry, fade timing, hotkeys, colours.
- `tests/AlightenInk.Tests` — xUnit tests for the core.

## Making a release (installer + auto-update)

One-time setup:

```powershell
dotnet tool install -g vpk
```

Build the installer locally:

```powershell
.\build-release.ps1 -Version 1.0.0
```

(Velopack refuses to pack a version that is not newer than what is already in `Releases\`; bump the
version, or delete `Releases\` for a throw-away local rebuild.)

This publishes a self-contained win-x64 build and runs `vpk pack`, producing
`Releases\AlightenInk-win-Setup.exe` (what you give to friends — it installs per-user to
`%LocalAppData%\AlightenInk`, no admin rights) plus the `.nupkg`/`releases.win.json` files the
updater reads.

### Publishing updates through GitHub (not done yet — your call)

The app checks `https://github.com/alighten-dev/AlightenInk` for updates (12 s after start, and from
the ⋯ menu / tray). Until that repository exists and has a release, checks fail quietly.

1. Create the **public** repository `alighten-dev/AlightenInk` on GitHub and push this code.
2. Create a token with `contents: write` on that repo (fine-grained PAT).
3. For each version:

   ```powershell
   .\build-release.ps1 -Version 1.0.1
   vpk upload github -o Releases --repoUrl https://github.com/alighten-dev/AlightenInk `
       --publish --releaseName "AlightenInk 1.0.1" --tag v1.0.1 --token <YOUR_TOKEN>
   ```

   Always bump the version; before packing a new version you can run
   `vpk download github -o Releases --repoUrl https://github.com/alighten-dev/AlightenInk` so Velopack can build a
   small delta package from the previous release.

Installed copies download the update in the background and apply it the next time AlightenInk exits
(or immediately when the user clicks **Check for updates**).

### Code signing

The installer is unsigned, so Windows SmartScreen will show "Windows protected your PC → More info →
Run anyway" the first time. To avoid that, sign with a code-signing certificate (or Azure Trusted
Signing) by passing `--signParams` / `--azureTrustedSignFile` to `vpk pack`.
