<#
.SYNOPSIS
    Unlists (hides) a specific version of the CCXT.Collector package from NuGet.org.

.DESCRIPTION
    This script unlists a specific version of the package from NuGet.org.
    Unlisting hides the package from search results but does NOT delete it.
    Users with direct links or existing projects can still access it.

.PARAMETER Version
    The version number to unlist (e.g., "2.1.5", "2.1.6").
    This parameter is required.

.PARAMETER ApiKey
    NuGet API key for authentication. If not provided, the script will attempt
    to use the NUGET_API_KEY environment variable.
    Get your API key from: https://www.nuget.org/account/apikeys

.PARAMETER Force
    Skip confirmation prompt and unlist immediately.
    Use with caution.

.EXAMPLE
    # Unlist version 2.1.5 (API key from environment variable)
    .\unlist-nuget.ps1 -Version 2.1.5

.EXAMPLE
    # Unlist with API key
    .\unlist-nuget.ps1 -Version 2.1.5 -ApiKey "your-api-key"

.EXAMPLE
    # Unlist without confirmation prompt
    .\unlist-nuget.ps1 -Version 2.1.5 -Force

.NOTES
    File Name  : unlist-nuget.ps1
    Author     : ODINSOFT
    Repository : https://github.com/odinsoft-lab/ccxt.collector

    IMPORTANT: Unlisting is NOT the same as deleting!
    - Unlisted packages are hidden from search results
    - Direct URLs still work
    - Existing projects can still restore the package
    - To completely remove a package, contact NuGet support

.LINK
    https://www.nuget.org/packages/CCXT.Collector
#>

param(
    [Parameter(Mandatory=$true, HelpMessage="Version number to unlist")]
    [string]$Version,

    [Parameter(Mandatory=$false, HelpMessage="NuGet.org API key")]
    [string]$ApiKey = "",

    [Parameter(Mandatory=$false, HelpMessage="Skip confirmation prompt")]
    [switch]$Force = $false
)

# Configuration
$PackageId = "CCXT.Collector"
$NuGetSource = "https://api.nuget.org/v3/index.json"

# Color output functions (using custom names to avoid conflicts with built-in cmdlets)
function Write-Success { param($Message) Write-Host $Message -ForegroundColor Green }
function Write-Info { param($Message) Write-Host $Message -ForegroundColor Cyan }
function Write-Warn { param($Message) Write-Host $Message -ForegroundColor Yellow }
function Write-Err { param($Message) Write-Host $Message -ForegroundColor Red }

# Banner
Write-Host ""
Write-Warn "========================================="
Write-Warn "  CCXT.Collector NuGet Unlist Tool"
Write-Warn "========================================="
Write-Host ""
Write-Host "  Usage: .\unlist-nuget.ps1 -Version VERSION [-ApiKey KEY] [-Force]"
Write-Host "  Help:  Get-Help .\unlist-nuget.ps1 -Detailed"
Write-Host ""

# Check if API key is provided or exists in environment
if ([string]::IsNullOrEmpty($ApiKey)) {
    $ApiKey = $env:NUGET_API_KEY
    if ([string]::IsNullOrEmpty($ApiKey)) {
        Write-Err "Error: NuGet API key not provided!"
        Write-Host ""
        Write-Host "Please provide API key using one of these methods:"
        Write-Host ""
        Write-Host "  Option 1 - Environment variable (recommended):"
        Write-Host "    `$env:NUGET_API_KEY = 'your-api-key'"
        Write-Host "    .\unlist-nuget.ps1 -Version $Version"
        Write-Host ""
        Write-Host "  Option 2 - Command parameter:"
        Write-Host "    .\unlist-nuget.ps1 -Version $Version -ApiKey 'your-api-key'"
        Write-Host ""
        Write-Host "  Get your API key from: https://www.nuget.org/account/apikeys"
        exit 1
    } else {
        Write-Info "Using API key from environment variable NUGET_API_KEY"
    }
} else {
    Write-Info "Using API key from command parameter"
}

# Check if dotnet CLI is available
try {
    $dotnetVersion = & dotnet --version 2>&1
    Write-Info "Found .NET SDK version: $dotnetVersion"
} catch {
    Write-Err "Error: .NET SDK not found! Please install from https://dotnet.microsoft.com/download"
    exit 1
}

# Display package information
Write-Host ""
Write-Warn "========================================="
Write-Warn "  Package to Unlist"
Write-Warn "========================================="
Write-Host "  Package ID: $PackageId"
Write-Host "  Version:    $Version"
Write-Host ""
Write-Warn "NOTE: This will hide the package from search results but NOT delete it."
Write-Warn "Users with direct links or existing projects can still access it."
Write-Host ""

# Confirmation
if (-not $Force) {
    $confirmation = Read-Host "Are you sure you want to unlist this package? (y/N)"
    if ($confirmation -ne 'y' -and $confirmation -ne 'Y') {
        Write-Warn "Operation cancelled by user"
        exit 0
    }
}

# Unlist the package
Write-Host ""
Write-Info "Unlisting package from NuGet.org..."

# Capture both stdout and stderr
$deleteOutput = & dotnet nuget delete "$PackageId" "$Version" --source "$NuGetSource" --api-key "$ApiKey" --non-interactive 2>&1
$exitCode = $LASTEXITCODE

# Check for success
$outputString = $deleteOutput | Out-String
$isSuccess = $false

if ($exitCode -eq 0) {
    $isSuccess = $true
} elseif ($outputString -match "was deleted" -or
          $outputString -match "unlisted") {
    $isSuccess = $true
}

if (-not $isSuccess) {
    Write-Err "Failed to unlist package!"
    Write-Host ""
    Write-Host $outputString
    Write-Host ""
    Write-Host "Common issues:"
    Write-Host "  - Invalid or expired API key"
    Write-Host "  - Package version doesn't exist"
    Write-Host "  - Insufficient permissions"
    Write-Host "  - Package was already unlisted"
    exit 1
}

Write-Host ""
Write-Success "========================================="
Write-Success "  Package unlisted successfully!"
Write-Success "========================================="
Write-Host ""
Write-Info "Package: $PackageId version $Version"
Write-Info "Status:  Hidden from NuGet.org search results"
Write-Host ""
Write-Host "Direct URL (still accessible):"
Write-Host "  https://www.nuget.org/packages/$PackageId/$Version"
Write-Host ""
Write-Warn "To completely remove a package, you must contact NuGet support."

Write-Host ""
Write-Success "Done!"
