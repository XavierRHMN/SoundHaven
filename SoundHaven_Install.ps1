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

function Show-Menu {
    Clear-Host
    Write-Host "================ SoundHaven Menu ================"
    Write-Host "1: Run SoundHaven Application"
    Write-Host "2: Set Last.fm API Key"
    Write-Host "3: Set Last.fm API Secret"
    Write-Host "4: Run Complete Setup"
    Write-Host "5: Delete SoundHaven"
    Write-Host "6: View Current API Settings"
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

    # Set for current session
    $env:LASTFM_API_KEY = $apiKey

    # Set permanently for user
    [System.Environment]::SetEnvironmentVariable('LASTFM_API_KEY', $apiKey, 'User')

    Write-Host "API Key has been set in environment variables."
    Write-Host "Note: You may need to restart Rider to see the updated environment variables."
    pause
}

function Set-ApiSecret {
    $apiSecret = Get-ValidInput "Enter your Last.fm API secret (must be 32 characters)"

    # Set for current session
    $env:LASTFM_API_SECRET = $apiSecret

    # Set permanently for user
    [System.Environment]::SetEnvironmentVariable('LASTFM_API_SECRET', $apiSecret, 'User')

    Write-Host "API Secret has been set in environment variables."
    Write-Host "Note: You may need to restart Rider to see the updated environment variables."
    pause
}

function View-ApiSettings {
    Write-Host "`nCurrent API Settings:"
    Write-Host "LASTFM_API_KEY: $env:LASTFM_API_KEY"
    Write-Host "LASTFM_API_SECRET: $env:LASTFM_API_SECRET"
    Write-Host "`nNote: If empty, try restarting your terminal or IDE."
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

    # Set API key and secret
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

            # Option to remove environment variables
            $removeEnv = Read-Host "Do you want to remove the API environment variables as well? (Y/N)"
            if ($removeEnv -eq 'Y' -or $removeEnv -eq 'y') {
                [System.Environment]::SetEnvironmentVariable('LASTFM_API_KEY', $null, 'User')
                [System.Environment]::SetEnvironmentVariable('LASTFM_API_SECRET', $null, 'User')
                $env:LASTFM_API_KEY = $null
                $env:LASTFM_API_SECRET = $null
                Write-Host "Environment variables have been removed."
            }

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
        '6' { View-ApiSettings }
        'Q' { return }
    }
} until ($selection -eq 'Q')