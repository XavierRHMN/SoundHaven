[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$Repository = "XavierRHMN/SoundHaven",
    [switch]$NoShortcuts,
    [string]$PackagePath,
    [string]$ChecksumPath,
    [string]$InstallDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
    throw "Repository must use the 'owner/name' format."
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Join-Path $env:LOCALAPPDATA "Programs\SoundHaven"
}

$destinationDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
$programsRoot = Split-Path -Parent $destinationDirectory
if ([string]::IsNullOrWhiteSpace($programsRoot)) {
    throw "InstallDirectory must have a parent directory."
}

$useLocalPackage = (-not [string]::IsNullOrWhiteSpace($PackagePath)) -or (-not [string]::IsNullOrWhiteSpace($ChecksumPath))
if ($useLocalPackage -and (
        [string]::IsNullOrWhiteSpace($PackagePath) -or
        [string]::IsNullOrWhiteSpace($ChecksumPath))) {
    throw "PackagePath and ChecksumPath must be supplied together."
}

$startMenuDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$applicationShortcut = Join-Path $startMenuDirectory "SoundHaven.lnk"
$uninstallShortcut = Join-Path $startMenuDirectory "Uninstall SoundHaven.lnk"
$downloadDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("SoundHaven-Install-" + [Guid]::NewGuid().ToString("N"))
$stagingDirectory = Join-Path $programsRoot (".SoundHaven-staging-" + [Guid]::NewGuid().ToString("N"))
$backupDirectory = Join-Path $programsRoot (".SoundHaven-backup-" + [Guid]::NewGuid().ToString("N"))
$backupCreated = $false
$newInstallCreated = $false

function New-Shortcut {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [string]$Arguments = "",
        [string]$WorkingDirectory = "",
        [string]$IconLocation = ""
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $TargetPath
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $IconLocation
    $shortcut.Save()
}

try {
    New-Item -ItemType Directory -Path $programsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

    if ($useLocalPackage) {
        $resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
        $resolvedChecksumPath = (Resolve-Path -LiteralPath $ChecksumPath).Path
    }
    else {
        $headers = @{
            Accept = "application/vnd.github+json"
            "User-Agent" = "SoundHaven-Installer"
            "X-GitHub-Api-Version" = "2022-11-28"
        }

        if ($Version -eq "latest") {
            $releaseUri = "https://api.github.com/repos/$Repository/releases/latest"
        }
        else {
            $releaseTag = if ($Version.StartsWith("v")) { $Version } else { "v$Version" }
            $releaseUri = "https://api.github.com/repos/$Repository/releases/tags/$releaseTag"
        }

        Write-Host "Resolving SoundHaven release..."
        $release = Invoke-RestMethod -Uri $releaseUri -Headers $headers
        $zipAsset = $release.assets |
            Where-Object { $_.name -match '^SoundHaven-.+-win-x64\.zip$' } |
            Select-Object -First 1

        if ($null -eq $zipAsset) {
            throw "The selected release does not contain a win-x64 SoundHaven ZIP."
        }

        $checksumAssetName = "$($zipAsset.name).sha256"
        $checksumAsset = $release.assets |
            Where-Object { $_.name -eq $checksumAssetName } |
            Select-Object -First 1

        if ($null -eq $checksumAsset) {
            throw "The selected release is missing $checksumAssetName."
        }

        $resolvedPackagePath = Join-Path $downloadDirectory $zipAsset.name
        $resolvedChecksumPath = Join-Path $downloadDirectory $checksumAsset.name

        Write-Host "Downloading $($zipAsset.name)..."
        Invoke-WebRequest -Uri $zipAsset.browser_download_url -Headers $headers -OutFile $resolvedPackagePath -UseBasicParsing
        Invoke-WebRequest -Uri $checksumAsset.browser_download_url -Headers $headers -OutFile $resolvedChecksumPath -UseBasicParsing
    }

    $checksumText = (Get-Content -LiteralPath $resolvedChecksumPath -Raw).Trim()
    if ($checksumText -notmatch '^(?<hash>[A-Fa-f0-9]{64})(?:\s+\*?.+)?$') {
        throw "The release checksum file has an invalid format."
    }

    $expectedHash = $Matches.hash.ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $resolvedPackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Checksum verification failed. The downloaded release was not installed."
    }

    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    Expand-Archive -LiteralPath $resolvedPackagePath -DestinationPath $stagingDirectory

    $stagedApplication = Join-Path $stagingDirectory "SoundHaven"
    $stagedExecutable = Join-Path $stagedApplication "SoundHavenClient.exe"
    if (-not (Test-Path -LiteralPath $stagedExecutable -PathType Leaf)) {
        throw "The release archive does not contain SoundHavenClient.exe."
    }

    if (Test-Path -LiteralPath $destinationDirectory) {
        [System.IO.Directory]::Move($destinationDirectory, $backupDirectory)
        $backupCreated = $true
    }

    [System.IO.Directory]::Move($stagedApplication, $destinationDirectory)
    $newInstallCreated = $true

    if (-not $NoShortcuts) {
        New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
        $installedExecutable = Join-Path $destinationDirectory "SoundHavenClient.exe"
        New-Shortcut `
            -Path $applicationShortcut `
            -TargetPath $installedExecutable `
            -WorkingDirectory $destinationDirectory `
            -IconLocation "$installedExecutable,0"

        $uninstaller = Join-Path $destinationDirectory "SoundHaven_Uninstall.ps1"
        if (Test-Path -LiteralPath $uninstaller) {
            $powerShellPath = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
            New-Shortcut `
                -Path $uninstallShortcut `
                -TargetPath $powerShellPath `
                -Arguments "-NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`"" `
                -WorkingDirectory $programsRoot
        }
    }

    if ($backupCreated -and (Test-Path -LiteralPath $backupDirectory)) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
        $backupCreated = $false
    }

    Write-Host "SoundHaven is installed at $destinationDirectory"
}
catch {
    if ($newInstallCreated -and (Test-Path -LiteralPath $destinationDirectory)) {
        Remove-Item -LiteralPath $destinationDirectory -Recurse -Force
    }

    if ($backupCreated -and (Test-Path -LiteralPath $backupDirectory)) {
        [System.IO.Directory]::Move($backupDirectory, $destinationDirectory)
    }

    throw
}
finally {
    if (Test-Path -LiteralPath $downloadDirectory) {
        Remove-Item -LiteralPath $downloadDirectory -Recurse -Force
    }

    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
