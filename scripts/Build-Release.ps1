[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipTests,
    [switch]$SkipSmokeTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repositoryRoot "SoundHaven.sln"
$projectPath = Join-Path $repositoryRoot "SoundHavenClient\SoundHavenClient.csproj"
$releaseRoot = Join-Path $repositoryRoot "artifacts\release"
$stagingRoot = Join-Path $releaseRoot "staging"
$publishDirectory = Join-Path $stagingRoot "SoundHaven"

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Remove-DirectoryWithRetry {
    param([Parameter(Mandatory = $true)][string]$Path)

    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            if (Test-Path -LiteralPath $Path) {
                Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            }

            if (Test-Path -LiteralPath $Path) {
                throw "Directory cleanup did not remove '$Path'."
            }

            return
        }
        catch {
            if ($attempt -eq 10) {
                throw
            }

            Start-Sleep -Milliseconds 500
        }
    }
}

function New-ZipArchiveWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,

        [int]$MaximumAttempts = 20,

        [int]$DelayMilliseconds = 500
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try {
            if (Test-Path -LiteralPath $DestinationPath) {
                Remove-Item -LiteralPath $DestinationPath -Force -ErrorAction Stop
            }

            [System.IO.Compression.ZipFile]::CreateFromDirectory(
                $SourceDirectory,
                $DestinationPath,
                [System.IO.Compression.CompressionLevel]::Optimal,
                $true)
            return
        }
        catch {
            if ($attempt -eq $MaximumAttempts) {
                throw
            }

            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath $projectPath
    $Version = [string]$project.Project.PropertyGroup.VersionPrefix
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Version '$Version' is not a valid semantic version."
}

Write-Host "Building SoundHaven $Version for win-x64..."

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-DirectoryWithRetry -Path $releaseRoot
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    Invoke-DotNet @("restore", $solutionPath, "--locked-mode")
    Invoke-DotNet @("restore", $projectPath, "-r", "win-x64", "--locked-mode")
    Invoke-DotNet @("format", $solutionPath, "--verify-no-changes", "--no-restore")

    if (-not $SkipTests) {
        Invoke-DotNet @(
            "test",
            $solutionPath,
            "-c", "Release",
            "--no-restore",
            "--filter", "Category!=Live"
        )
    }

    Invoke-DotNet @(
        "publish",
        $projectPath,
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "--no-restore",
        "-o", $publishDirectory,
        "-p:Version=$Version",
        "-p:PublishSingleFile=false",
        "-p:PublishTrimmed=false",
        "-p:DebugType=None",
        "-p:DebugSymbols=false"
    )
}
finally {
    Pop-Location
}

$executablePath = Join-Path $publishDirectory "SoundHavenClient.exe"
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Publish output is missing SoundHavenClient.exe."
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD_PARTY_NOTICES.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "README.md") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "SoundHaven_Uninstall.ps1") -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot "licenses") -Destination $publishDirectory -Recurse
[System.IO.File]::WriteAllText(
    (Join-Path $publishDirectory "VERSION"),
    "$Version`r`n",
    [System.Text.UTF8Encoding]::new($false))

$archiveName = "SoundHaven-$Version-win-x64.zip"
$archivePath = Join-Path $releaseRoot $archiveName
New-ZipArchiveWithRetry -SourceDirectory $publishDirectory -DestinationPath $archivePath

$validationDirectory = Join-Path $releaseRoot "validation"
Expand-Archive -LiteralPath $archivePath -DestinationPath $validationDirectory
$validatedExecutable = Join-Path $validationDirectory "SoundHaven\SoundHavenClient.exe"
if (-not (Test-Path -LiteralPath $validatedExecutable -PathType Leaf)) {
    throw "The release archive did not preserve the expected portable layout."
}

if (-not $SkipSmokeTest) {
    $process = Start-Process -FilePath $validatedExecutable -PassThru
    try {
        Start-Sleep -Seconds 3
        if ($process.HasExited -and $process.ExitCode -ne 0) {
            throw "The clean release exited during startup with code $($process.ExitCode)."
        }
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }

        $process.Dispose()
        Start-Sleep -Seconds 1
    }
}

Remove-DirectoryWithRetry -Path $stagingRoot
Remove-DirectoryWithRetry -Path $validationDirectory

$hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
$checksumPath = "$archivePath.sha256"
$checksumLine = "$($hash.Hash.ToLowerInvariant())  $archiveName`r`n"
[System.IO.File]::WriteAllText(
    $checksumPath,
    $checksumLine,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Created $archivePath"
Write-Host "Created $checksumPath"
