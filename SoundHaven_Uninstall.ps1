[CmdletBinding()]
param(
    [switch]$PurgeUserData,
    [switch]$NoShortcuts,
    [string]$InstallDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Join-Path $env:LOCALAPPDATA "Programs\SoundHaven"
}

$destinationDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
$userDataDirectory = Join-Path $env:LOCALAPPDATA "SoundHaven"
$startMenuDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$shortcuts = @(
    (Join-Path $startMenuDirectory "SoundHaven.lnk"),
    (Join-Path $startMenuDirectory "Uninstall SoundHaven.lnk")
)

$runningApplication = Get-Process -Name "SoundHavenClient" -ErrorAction SilentlyContinue
if ($null -ne $runningApplication) {
    throw "Close SoundHaven before uninstalling it."
}

Set-Location ([System.IO.Path]::GetTempPath())

if (-not $NoShortcuts) {
    foreach ($shortcut in $shortcuts) {
        if (Test-Path -LiteralPath $shortcut) {
            Remove-Item -LiteralPath $shortcut -Force
        }
    }
}

if (Test-Path -LiteralPath $destinationDirectory) {
    Remove-Item -LiteralPath $destinationDirectory -Recurse -Force
}

if ($PurgeUserData -and (Test-Path -LiteralPath $userDataDirectory)) {
    Remove-Item -LiteralPath $userDataDirectory -Recurse -Force
    Write-Host "SoundHaven and its user data were removed."
}
else {
    Write-Host "SoundHaven was removed. User data was preserved at $userDataDirectory"
}
