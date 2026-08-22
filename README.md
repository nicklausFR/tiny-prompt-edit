# tiny-prompt-edit

Small, fast, general-purpose Windows text editor. It is useful for composing long terminal
commands, editing prompts, and making quick changes to Markdown, plain-text, or configuration
files. It integrates with tools such as Codex CLI, but is not limited to them.

## Use cases

- Compose or revise a long command without fighting the terminal's single-line editor
- Use an external editor for Codex CLI and other terminal tools
- Quickly edit a small Markdown note or README
- Inspect or modify a plain-text configuration file
- Open an arbitrary file directly from the Windows context menu

## Features

- Fast native Windows app
- Borderless movable window
- Live Windows light/dark theme support, including menus and title bar
- Transparency
- Configurable font and zoom
- Live configuration reload after saving `tiny-prompt-edit.ini`
- Optional line numbers
- Optional vertical scrollbar display without disabling mouse-wheel or keyboard scrolling
- Lightweight plain-text engine for large files
- Configurable close shortcuts
- Editor context menu: search, save, open with, settings and close
- Opens empty without arguments
- Reads/writes a file when a path is passed as argument
- Works with extensionless files as well as Markdown, text, and configuration files

## Config

Place `tiny-prompt-edit.ini` next to `tiny-prompt-edit.exe`.

Example:

```ini
[general]
language = en

[window]
width = 800
height = 400
x = center
y = center
border = 5
borderless = false
always_on_top = true
alpha = 0.92

[editor]
font = Consolas
font_size = 11
zoom_step = 1
zoom_modifier = Control
min_font_size = 6
max_font_size = 40
show_line_numbers = false
show_scrollbars = true
word_wrap = true
large_file_threshold_mb = 2
close_shortcuts = Control+X, Escape
```

English is the source and default interface language. Set `language = fr` to load the
French gettext catalog from `locales/fr.po`. Language changes are applied when the INI is saved,
just like the other live settings. Additional translations can be added as PO files using the
same English `msgid` values.

Setting `show_scrollbars = false` hides only the scrollbar itself. Scrolling remains available
with the mouse wheel, arrow keys, and `Page Up` / `Page Down`.

When a file reaches `large_file_threshold_mb`, Tiny Prompt Edit switches from `RichTextBox` to the
lighter native multiline text control. Word wrapping and the line-number gutter are disabled in
this mode, while editing, search, save, zoom, mouse-wheel, and keyboard scrolling remain available.
Their configured values still apply to smaller files. Set the threshold to `0` to disable this
protection.

## Codex CLI

```powershell
setx VISUAL "C:\path\to\tiny-prompt-edit.exe"
setx EDITOR "C:\path\to\tiny-prompt-edit.exe"
```

Then restart the terminal and use `Ctrl+G` in Codex CLI.

## PowerShell

The setup script installs a `Ctrl+G` handler that opens the current command line in Tiny Prompt
Edit. This is particularly useful for long or multi-part commands. The equivalent manual profile
configuration is:

```powershell
Set-PSReadLineKeyHandler -Chord Ctrl+g -ScriptBlock {
    $line = ""
    $cursor = 0

    [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState(
        [ref]$line,
        [ref]$cursor
    )

    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $line)

    Start-Process `
        -FilePath "C:\path\to\tiny-prompt-edit.exe" `
        -ArgumentList "`"$tmp`"" `
        -Wait

    $newLine = [System.IO.File]::ReadAllText($tmp)
    Remove-Item $tmp -Force

    [Microsoft.PowerShell.PSConsoleReadLine]::Replace(
        0,
        $line.Length,
        $newLine
    )
}
```

## Windows context menu

Tiny Prompt Edit can be added to the Windows file context menu so any file can be opened directly with it.

Create a file such as `tiny-prompt-edit-context-menu.reg`, replace the executable path, then double-click the file to import it into the Registry:

```reg
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\*\shell\TinyPromptEdit]
@="Open with Tiny Prompt Edit"
"Icon"="C:\\PATH\\TO\\tiny-prompt-edit.exe"

[HKEY_CURRENT_USER\Software\Classes\*\shell\TinyPromptEdit\command]
@="\"C:\\PATH\\TO\\tiny-prompt-edit.exe\" \"%1\""
```

This adds **Open with Tiny Prompt Edit** to the context menu for all files. No administrator rights are required because the entry is stored under `HKEY_CURRENT_USER`.

The included script installs that entry and also adds **New file with Tiny Prompt Edit...**
when right-clicking a folder or a folder background. The command asks for the file name
before opening the editor:

```powershell
.\install-context-menu.ps1 -ExecutablePath "C:\path\to\tiny-prompt-edit.exe"
```

Remove both entries with:

```powershell
.\install-context-menu.ps1 -Remove
```

Windows' built-in **New** submenu only supports file templates (`ShellNew`) and does not
launch an application to ask for a name. The added command therefore appears alongside
that submenu and provides the requested name prompt.

## Build and setup

Run the all-in-one setup script from the repository:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
```

It publishes the Release build to `%LOCALAPPDATA%\Programs\TinyPromptEdit`, preserves an
existing `tiny-prompt-edit.ini` during upgrades, and installs both Windows context-menu
commands with the stable installed executable path. It also installs or updates the
`Ctrl+G` handler for Windows PowerShell and PowerShell 7. Restart open terminals after setup.

Use `-SkipPowerShellIntegration` if the PowerShell profiles must not be changed.

To remove the context-menu commands and installed application:

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -Uninstall
```

## License

Copyright (C) 2026 nicklausFR

GPL-3.0-or-later. See [LICENSE](LICENSE).
