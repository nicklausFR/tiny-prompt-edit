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

## PowerShell

Add this to your PowerShell profile (`notepad $PROFILE`) to use `Ctrl+G` on the current command line:

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

## License

Copyright (C) 2026 nicklausFR

GPL-3.0-or-later. See [LICENSE](LICENSE).
