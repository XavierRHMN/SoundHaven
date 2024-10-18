# SoundHaven PowerShell Menu Script

function Set-ExecutionPolicy-Bypass {
    try {
        Set-ExecutionPolicy Bypass -Scope Process -Force
        Write-Host "Execution policy set to Bypass for this session."
    }
    catch {
        Write-Host "Failed to set execution policy. You may need to run this script as an administrator."
        Write-Host "Error: $_"
        pause
        exit
    }
}

# Call the function to set execution policy at the start of the script
Set-ExecutionPolicy-Bypass

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$soundHavenPath = Join-Path $scriptPath "SoundHaven"
$clientPath = Join-Path $soundHavenPath "SoundHavenClient"
$apiKeysPath = Join-Path $clientPath "ApiKeys"
$apiKeyFile = Join-Path $apiKeysPath "LASTFM_API.txt"

function Show-Menu {
    Clear-Host
    Write-Host "================ SoundHaven Menu ================"
    Write-Host "1: Run SoundHaven Application"
    Write-Host "2: Re-enter Last.fm API Key"
    Write-Host "3: Re-enter Last.fm API Secret"
    Write-Host "4: Run Complete Setup"
    Write-Host "5: Delete SoundHaven"
    Write-Host "Q: Quit"
    Write-Host "================================================="
}

function Run-Application {
    Set-Location $clientPath
    dotnet run
}

function Get-ValidInput {
    param (
        [string]$prompt
    )
    do {
        $input = Read-Host $prompt
        if ($input.Length -ne 32) {
            Write-Host "Input must be exactly 32 characters long. Please try again."
        }
    } while ($input.Length -ne 32)
    return $input
}

function Set-ApiKey {
    $apiKey = Get-ValidInput "Enter your Last.fm API key (must be 32 characters)"
    Set-Content -Path $apiKeyFile -Value $apiKey -Force
    Write-Host "API Key updated successfully."
    pause
}

function Set-ApiSecret {
    $apiSecret = Get-ValidInput "Enter your Last.fm API secret (must be 32 characters)"
    Add-Content -Path $apiKeyFile -Value $apiSecret -Force
    Write-Host "API Secret updated successfully."
    pause
}

function Redo-Setup {
    # Clone the repository
    Set-Location $scriptPath
    if (Test-Path $soundHavenPath) {
        Remove-Item -Path $soundHavenPath -Recurse -Force
    }
    git clone https://github.com/XavierRHMN/SoundHaven.git

    # Build the project
    Set-Location $clientPath
    dotnet build

    # Create ApiKeys directory and set API key and secret
    New-Item -ItemType Directory -Force -Path $apiKeysPath
    Set-ApiKey
    Set-ApiSecret

    Write-Host "Setup completed successfully."
    pause
}

function Delete-SoundHaven {
    if (Test-Path $soundHavenPath) {
        $confirmation = Read-Host "Are you sure you want to delete SoundHaven? This action cannot be undone. (Y/N)"
        if ($confirmation -eq 'Y' -or $confirmation -eq 'y') {
            Remove-Item -Path $soundHavenPath -Recurse -Force
            Write-Host "SoundHaven has been deleted successfully."
        }
        else {
            Write-Host "Deletion cancelled."
        }
    }
    else {
        Write-Host "SoundHaven directory does not exist."
    }
    pause
}

# Main script logic
if (-not (Test-Path $soundHavenPath)) {
    Write-Host "SoundHaven is not set up. Running initial setup..."
    Redo-Setup
}

do {
    Show-Menu
    $selection = Read-Host "Please make a selection"
    switch ($selection) {
        '1' { Run-Application }
        '2' { Set-ApiKey }
        '3' { Set-ApiSecret }
        '4' { Redo-Setup }
        '5' { Delete-SoundHaven }
        'Q' { return }
    }
} until ($selection -eq 'Q')