# tiny-prompt-edit

Minimal external prompt editor for terminal tools such as Codex CLI.

## Features

- Fast native Windows app
- Borderless movable window
- Dark mode support
- Transparency
- Configurable font and zoom
- Configurable close shortcuts
- Opens empty without arguments
- Reads/writes a file when a path is passed as argument

## Config

Place `tiny-prompt-edit.ini` next to `tiny-prompt-edit.exe`.

Example:

```ini
[window]
width = 800
height = 400
x = center
y = center
border = 5
borderless = true
always_on_top = true
alpha = 0.92

[editor]
font = Consolas
font_size = 11
zoom_step = 1
zoom_modifier = Control
min_font_size = 6
max_font_size = 40
close_shortcuts = Control+X, Escape
```

## Codex CLI

```powershell
setx VISUAL "C:\path\to\tiny-prompt-edit.exe"
setx EDITOR "C:\path\to\tiny-prompt-edit.exe"
```

Then restart the terminal and use `Ctrl+G` in Codex CLI.