param (
    [Parameter(Mandatory=$false, Position=0)]
    [ValidateSet("debug", "release")]
    [string]$Target = "debug"
)

$ErrorActionPreference = "Stop"

# Map lowercase input to proper .NET configuration casing
$Configuration = if ($Target -eq "release") { "Release" } else { "Debug" }

Write-Host "Building in $Configuration mode..." -ForegroundColor Cyan
dotnet build -c $Configuration

$ExecutablePath = "src\TrafficAnalyzer.Client\bin\$Configuration\net10.0\TrafficAnalyzer.Client.exe"

# Check for Administrator privileges (Windows equivalent of needing setcap)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "ERROR: Packet capture on Windows requires Administrator privileges." -ForegroundColor Red
    Write-Host "Please close this terminal, right-click PowerShell, select 'Run as Administrator', and try again." -ForegroundColor Yellow
    exit 1
}

Write-Host "Permissions verified." -ForegroundColor Green
Write-Host "Running the traffic analyzer..." -ForegroundColor Cyan
Write-Host "Path of executable is: $ExecutablePath"

# Execute the app
& $ExecutablePath