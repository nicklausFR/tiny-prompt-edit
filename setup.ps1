[CmdletBinding()]
param(
    [string] $InstallDirectory = (Join-Path $env:LOCALAPPDATA "Programs\TinyPromptEdit"),
    [switch] $SkipPowerShellIntegration,
    [switch] $Uninstall
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "tiny-prompt-edit.csproj"
$menuScriptPath = Join-Path $PSScriptRoot "install-context-menu.ps1"
$installedExePath = Join-Path $InstallDirectory "tiny-prompt-edit.exe"
$installedConfigPath = Join-Path $InstallDirectory "tiny-prompt-edit.ini"
$profileStartMarker = "# >>> Tiny Prompt Edit Ctrl+G >>>"
$profileEndMarker = "# <<< Tiny Prompt Edit Ctrl+G <<<"

function Get-PowerShellProfilePaths {
    $documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    @(
        (Join-Path $documents "WindowsPowerShell\Microsoft.PowerShell_profile.ps1"),
        (Join-Path $documents "PowerShell\Microsoft.PowerShell_profile.ps1")
    )
}

function Remove-ManagedProfileHandler {
    param([string] $ProfilePath)

    if (-not (Test-Path -LiteralPath $ProfilePath -PathType Leaf)) {
        return
    }

    $content = [System.IO.File]::ReadAllText($ProfilePath)
    $pattern = "(?ms)\r?\n?" + [regex]::Escape($profileStartMarker) +
        ".*?" + [regex]::Escape($profileEndMarker) + "\r?\n?"
    $updated = [regex]::Replace($content, $pattern, [Environment]::NewLine)

    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText(
            $ProfilePath,
            $updated.TrimEnd() + [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($true))
        Write-Host "Removed Ctrl+G integration from $ProfilePath"
    }
}

function Install-ProfileHandler {
    param([string] $ProfilePath)

    $profileDirectory = Split-Path -Parent $ProfilePath
    New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null

    $content = if (Test-Path -LiteralPath $ProfilePath -PathType Leaf) {
        [System.IO.File]::ReadAllText($ProfilePath)
    } else {
        ""
    }

    # Migrate an existing Tiny Prompt Edit handler without replacing the user's block.
    $existingPathPattern = '(?im)(-FilePath\s+)(["''])[^"''\r\n]*tiny-prompt-edit\.exe\2'
    if ([regex]::IsMatch($content, $existingPathPattern)) {
        $updated = [regex]::Replace(
            $content,
            $existingPathPattern,
            { param($match) $match.Groups[1].Value + $match.Groups[2].Value +
                $installedExePath + $match.Groups[2].Value })

        [System.IO.File]::WriteAllText(
            $ProfilePath,
            $updated,
            [System.Text.UTF8Encoding]::new($true))
        Write-Host "Updated existing Ctrl+G integration in $ProfilePath"
        return
    }

    if ($content.Contains($profileStartMarker)) {
        Remove-ManagedProfileHandler -ProfilePath $ProfilePath
        $content = [System.IO.File]::ReadAllText($ProfilePath)
    }

    $escapedExePath = $installedExePath.Replace("'", "''")
    $block = @'
__START__
Set-PSReadLineKeyHandler -Chord Ctrl+g -ScriptBlock {
    $line = ""
    $cursor = 0
    [Microsoft.PowerShell.PSConsoleReadLine]::GetBufferState([ref]$line, [ref]$cursor)

    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($tmp, $line)
        Start-Process -FilePath '__EXE__' -ArgumentList "`"$tmp`"" -Wait
        $newLine = [System.IO.File]::ReadAllText($tmp)
        [Microsoft.PowerShell.PSConsoleReadLine]::Replace(0, $line.Length, $newLine)
    }
    finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}
__END__
'@
    $block = $block.Replace("__START__", $profileStartMarker).
        Replace("__END__", $profileEndMarker).
        Replace("__EXE__", $escapedExePath)

    $separator = if ([string]::IsNullOrWhiteSpace($content)) { "" } else { [Environment]::NewLine }
    [System.IO.File]::WriteAllText(
        $ProfilePath,
        $content.TrimEnd() + $separator + $block,
        [System.Text.UTF8Encoding]::new($true))
    Write-Host "Installed Ctrl+G integration in $ProfilePath"
}

if ($Uninstall) {
    & $menuScriptPath -Remove

    if (-not $SkipPowerShellIntegration) {
        foreach ($profilePath in Get-PowerShellProfilePaths) {
            Remove-ManagedProfileHandler -ProfilePath $profilePath
        }
    }

    if (Test-Path -LiteralPath $InstallDirectory) {
        $resolvedInstallDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
        $allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs"))

        if (-not $resolvedInstallDirectory.StartsWith(
            $allowedRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a directory outside $allowedRoot"
        }

        Remove-Item -LiteralPath $resolvedInstallDirectory -Recurse -Force
        Write-Host "Application removed from $resolvedInstallDirectory"
    }

    exit 0
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found. Install the .NET SDK, then run this script again."
}

$publishDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (
    "tiny-prompt-edit-publish-" + [Guid]::NewGuid().ToString("N"))

try {
    Write-Host "Publishing Tiny Prompt Edit..."
    dotnet publish $projectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        --output $publishDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }

    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null

    foreach ($item in Get-ChildItem -LiteralPath $publishDirectory) {
        if ($item.Name -eq "tiny-prompt-edit.ini" -and
            (Test-Path -LiteralPath $installedConfigPath -PathType Leaf)) {
            Write-Host "Keeping existing configuration: $installedConfigPath"
            continue
        }

        Copy-Item -LiteralPath $item.FullName -Destination $InstallDirectory -Recurse -Force
    }

    if (-not (Test-Path -LiteralPath $installedExePath -PathType Leaf)) {
        throw "Published executable was not found at $installedExePath"
    }

    if (-not (Test-Path -LiteralPath $installedConfigPath -PathType Leaf)) {
        throw "Configuration file was not found at $installedConfigPath"
    }

    $configContent = [System.IO.File]::ReadAllText($installedConfigPath)
    if ($configContent -notmatch '(?im)^\s*language\s*=') {
        if ($configContent -match '(?im)^\s*\[general\]\s*$') {
            $configContent = [regex]::Replace(
                $configContent,
                '(?im)^\s*\[general\]\s*$',
                '$0' + [Environment]::NewLine + 'language = en',
                1)
        }
        else {
            $configContent = '[general]' + [Environment]::NewLine +
                'language = en' + [Environment]::NewLine + [Environment]::NewLine +
                $configContent
        }

        [System.IO.File]::WriteAllText(
            $installedConfigPath,
            $configContent,
            [System.Text.UTF8Encoding]::new($true))
        Write-Host "Added the default language setting to $installedConfigPath"
    }

    if ($configContent -notmatch '(?im)^\s*show_scrollbars\s*=') {
        if ($configContent -match '(?im)^\s*\[editor\]\s*$') {
            $configContent = [regex]::Replace(
                $configContent,
                '(?im)^\s*\[editor\]\s*$',
                '$0' + [Environment]::NewLine + 'show_scrollbars = true',
                1)
        }
        else {
            $configContent += [Environment]::NewLine + '[editor]' +
                [Environment]::NewLine + 'show_scrollbars = true' + [Environment]::NewLine
        }

        [System.IO.File]::WriteAllText(
            $installedConfigPath,
            $configContent,
            [System.Text.UTF8Encoding]::new($true))
        Write-Host "Added the default scrollbar setting to $installedConfigPath"
    }

    $editorDefaults = @(
        [pscustomobject]@{ Name = 'word_wrap'; Value = 'true' },
        [pscustomobject]@{ Name = 'large_file_threshold_mb'; Value = '2' }
    )
    foreach ($setting in $editorDefaults) {
        if ($configContent -match ('(?im)^\s*' + [regex]::Escape($setting.Name) + '\s*=')) {
            continue
        }

        $configContent = [regex]::Replace(
            $configContent,
            '(?im)^\s*\[editor\]\s*$',
            '$0' + [Environment]::NewLine + $setting.Name + ' = ' + $setting.Value,
            1)
        [System.IO.File]::WriteAllText(
            $installedConfigPath,
            $configContent,
            [System.Text.UTF8Encoding]::new($true))
        Write-Host "Added $($setting.Name) to $installedConfigPath"
    }

    & $menuScriptPath -ExecutablePath $installedExePath

    if (-not $SkipPowerShellIntegration) {
        foreach ($profilePath in Get-PowerShellProfilePaths) {
            Install-ProfileHandler -ProfilePath $profilePath
        }
    }

    Write-Host ""
    Write-Host "Tiny Prompt Edit is ready."
    Write-Host "Executable:  $installedExePath"
    Write-Host "Configuration: $installedConfigPath"
}
finally {
    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }
}
