param(
    [string] $ExecutablePath = (Join-Path $PSScriptRoot "tiny-prompt-edit.exe"),
    [switch] $Remove
)

$ErrorActionPreference = "Stop"

$entries = @(
    [pscustomobject]@{
        SubKey = "Software\Classes\*\shell\TinyPromptEdit"
        Label = "Open with Tiny Prompt Edit"
        Arguments = '"%1"'
    },
    [pscustomobject]@{
        SubKey = "Software\Classes\Directory\shell\TinyPromptEditNew"
        Label = "New file with Tiny Prompt Edit..."
        Arguments = '--new "%1"'
    },
    [pscustomobject]@{
        SubKey = "Software\Classes\Directory\Background\shell\TinyPromptEditNew"
        Label = "New file with Tiny Prompt Edit..."
        Arguments = '--new "%V"'
    }
)

if ($Remove) {
    foreach ($entry in $entries) {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($entry.SubKey, $false)
    }

    Write-Host "Tiny Prompt Edit context-menu entries removed."
    return
}

$exe = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Executable not found: $exe"
}

foreach ($entry in $entries) {
    $menuKey = $null
    $commandKey = $null

    try {
        $menuKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($entry.SubKey, $true)
        $commandKey = $menuKey.CreateSubKey("command", $true)
        $menuKey.SetValue("", $entry.Label, [Microsoft.Win32.RegistryValueKind]::String)
        $menuKey.SetValue("Icon", $exe, [Microsoft.Win32.RegistryValueKind]::String)
        $commandKey.SetValue("", ('"' + $exe + '" ' + $entry.Arguments),
            [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally {
        if ($null -ne $commandKey) { $commandKey.Dispose() }
        if ($null -ne $menuKey) { $menuKey.Dispose() }
    }
}

Write-Host "Tiny Prompt Edit context-menu entries installed for the current user."
