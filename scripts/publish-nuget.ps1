<#
.SYNOPSIS
    Builds and publishes the CCXT.Collector NuGet package to NuGet.org.

.DESCRIPTION
    This script automates the complete NuGet package publishing workflow:
    1. Validates NuGet API key (from parameter or NUGET_API_KEY environment variable)
    2. Verifies .NET SDK installation
    3. Cleans previous package artifacts
    4. Builds the project in Release configuration
    5. Runs tests (optional)
    6. Creates NuGet package (.nupkg) and symbol package (.snupkg)
    7. Publishes to NuGet.org with user confirmation

.PARAMETER ApiKey
    NuGet API key for authentication. If not provided, the script will attempt
    to use the NUGET_API_KEY environment variable.
    Get your API key from: https://www.nuget.org/account/apikeys

.PARAMETER SkipBuild
    Skip the build step and use existing build artifacts.

.PARAMETER SkipTests
    Skip running tests before publishing.

.PARAMETER DryRun
    Perform all steps except actual publishing. Useful for verification.

.EXAMPLE
    # Quick publish (API key from environment variable)
    .\publish-nuget.ps1

.EXAMPLE
    # Publish with API key
    .\publish-nuget.ps1 -ApiKey "your-api-key"

.EXAMPLE
    # Test package creation without publishing
    .\publish-nuget.ps1 -DryRun

.EXAMPLE
    # Quick publish, skip tests
    .\publish-nuget.ps1 -SkipTests

.EXAMPLE
    # Set API key as environment variable (recommended)
    $env:NUGET_API_KEY = "your-api-key-here"
    .\publish-nuget.ps1

.NOTES
    File Name  : publish-nuget.ps1
    Author     : ODINSOFT
    Repository : https://github.com/odinsoft-lab/ccxt.collector
    Requires   : .NET SDK 8.0 or later

    Before first use:
    1. Get your API key from https://www.nuget.org/account/apikeys
    2. Set environment variable: $env:NUGET_API_KEY = "your-key"
    3. Run: .\publish-nuget.ps1

.LINK
    https://www.nuget.org/packages/CCXT.Collector
#>

param(
    [Parameter(Mandatory=$false, HelpMessage="NuGet.org API key")]
    [string]$ApiKey = "",

    [Parameter(Mandatory=$false, HelpMessage="Skip build step")]
    [switch]$SkipBuild = $false,

    [Parameter(Mandatory=$false, HelpMessage="Skip running tests")]
    [switch]$SkipTests = $false,

    [Parameter(Mandatory=$false, HelpMessage="Test run without publishing")]
    [switch]$DryRun = $false
)

# Configuration
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ProjectPath = Join-Path $RootDir "src\ccxt.collector.csproj"
$TestPath = Join-Path $RootDir "tests\ccxt.tests.csproj"
$Configuration = "Release"
$OutputDirectory = Join-Path $RootDir "src\bin\Release"
$NuGetSource = "https://api.nuget.org/v3/index.json"

# Color output functions (using custom names to avoid conflicts with built-in cmdlets)
function Write-Success { param($Message) Write-Host $Message -ForegroundColor Green }
function Write-Info { param($Message) Write-Host $Message -ForegroundColor Cyan }
function Write-Warn { param($Message) Write-Host $Message -ForegroundColor Yellow }
function Write-Err { param($Message) Write-Host $Message -ForegroundColor Red }

# Banner
Write-Host ""
Write-Info "========================================="
Write-Info "  CCXT.Collector NuGet Publisher"
Write-Info "========================================="
Write-Host ""
Write-Host "  Usage: .\publish-nuget.ps1 [-ApiKey KEY] [-DryRun] [-SkipTests] [-SkipBuild]"
Write-Host "  Help:  Get-Help .\publish-nuget.ps1 -Detailed"
Write-Host ""

# Check if API key is provided or exists in environment (skip for DryRun)
if (-not $DryRun) {
    if ([string]::IsNullOrEmpty($ApiKey)) {
        $ApiKey = $env:NUGET_API_KEY
        if ([string]::IsNullOrEmpty($ApiKey)) {
            Write-Err "Error: NuGet API key not provided!"
            Write-Host ""
            Write-Host "Please provide API key using one of these methods:"
            Write-Host ""
            Write-Host "  Option 1 - Environment variable (recommended):"
            Write-Host "    `$env:NUGET_API_KEY = 'your-api-key'"
            Write-Host "    .\publish-nuget.ps1"
            Write-Host ""
            Write-Host "  Option 2 - Command parameter:"
            Write-Host "    .\publish-nuget.ps1 -ApiKey 'your-api-key'"
            Write-Host ""
            Write-Host "  Get your API key from: https://www.nuget.org/account/apikeys"
            exit 1
        } else {
            Write-Info "Using API key from environment variable NUGET_API_KEY"
        }
    } else {
        Write-Info "Using API key from command parameter"
    }
} else {
    Write-Warn "DRY RUN MODE - Skipping API key validation"
}

# Check if dotnet CLI is available
try {
    $dotnetVersion = & dotnet --version 2>&1
    Write-Info "Found .NET SDK version: $dotnetVersion"
} catch {
    Write-Err "Error: .NET SDK not found! Please install from https://dotnet.microsoft.com/download"
    exit 1
}

# Step 1: Clean previous packages
Write-Host ""
Write-Info "Step 1/4: Cleaning previous packages..."
if (Test-Path $OutputDirectory) {
    Remove-Item "$OutputDirectory\*.nupkg" -Force -ErrorAction SilentlyContinue
    Remove-Item "$OutputDirectory\*.snupkg" -Force -ErrorAction SilentlyContinue
}
Write-Success "  Done."

# Step 2: Build the project
Write-Host ""
if (-not $SkipBuild) {
    Write-Info "Step 2/4: Building project in $Configuration mode..."

    $buildResult = & dotnet build "$ProjectPath" --configuration $Configuration 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Build failed! Please fix build errors and try again."
        Write-Host $buildResult
        exit 1
    }
    Write-Success "  Build completed successfully!"
} else {
    Write-Warn "Step 2/4: Skipping build (using existing build)"
}

# Step 3: Run tests
Write-Host ""
if (-not $SkipTests) {
    Write-Info "Step 3/4: Running tests..."

    if (Test-Path $TestPath) {
        $testResult = & dotnet test "$TestPath" --configuration $Configuration --verbosity quiet 2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Warn "  Tests failed! Consider fixing tests before publishing."
            $confirmation = Read-Host "  Do you want to continue anyway? (y/N)"
            if ($confirmation -ne 'y' -and $confirmation -ne 'Y') {
                Write-Warn "Publication cancelled"
                exit 1
            }
        } else {
            Write-Success "  All tests passed!"
        }
    } else {
        Write-Warn "  Test project not found at: $TestPath"
        Write-Warn "  Skipping tests."
    }
} else {
    Write-Warn "Step 3/4: Skipping tests"
}

# Step 4: Create NuGet package
Write-Host ""
Write-Info "Step 4/4: Creating NuGet package..."

$packResult = & dotnet pack "$ProjectPath" --configuration $Configuration --no-build 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Err "Package creation failed!"
    Write-Host $packResult
    exit 1
}

# Find the created package
$packageFile = Get-ChildItem -Path $OutputDirectory -Filter "*.nupkg" |
                Where-Object { $_.Name -notlike "*.symbols.nupkg" } |
                Select-Object -First 1

if ($null -eq $packageFile) {
    Write-Err "No package file found in $OutputDirectory"
    exit 1
}

$packagePath = $packageFile.FullName
$packageName = $packageFile.Name

Write-Success "  Package created: $packageName"

# Package summary
Write-Host ""
Write-Info "========================================="
Write-Info "  Package Summary"
Write-Info "========================================="
Write-Host "  Name: $packageName"
Write-Host "  Size: $([math]::Round($packageFile.Length / 1KB, 2)) KB"
Write-Host "  Path: $packagePath"
Write-Host ""

# Publish to NuGet
if ($DryRun) {
    Write-Warn "DRY RUN COMPLETE - Package was NOT published"
    Write-Host ""
    Write-Host "To publish for real, run:"
    Write-Host "  .\publish-nuget.ps1"
    Write-Host ""
} else {
    Write-Warn "Ready to publish to NuGet.org"
    Write-Host ""
    $confirmation = Read-Host "Publish now? (y/N)"
    if ($confirmation -ne 'y' -and $confirmation -ne 'Y') {
        Write-Warn "Publication cancelled by user"
        exit 0
    }

    Write-Host ""
    Write-Info "Publishing to NuGet.org..."

    # Capture both stdout and stderr
    $pushOutput = & dotnet nuget push "$packagePath" --api-key "$ApiKey" --source "$NuGetSource" --skip-duplicate 2>&1
    $exitCode = $LASTEXITCODE

    # Check for success
    $outputString = $pushOutput | Out-String
    $isSuccess = $false

    if ($exitCode -eq 0) {
        $isSuccess = $true
    } elseif ($outputString -match "Your package was pushed" -or
              $outputString -match "already exists" -or
              $outputString -match "conflict.*409") {
        $isSuccess = $true
        Write-Warn "Package version already exists (skipped)"
    }

    if (-not $isSuccess) {
        Write-Err "Publication failed!"
        Write-Host ""
        Write-Host $outputString
        Write-Host ""
        Write-Host "Common issues:"
        Write-Host "  - Invalid or expired API key"
        Write-Host "  - Network connection issues"
        Write-Host "  - NuGet.org service temporarily unavailable"
        exit 1
    }

    Write-Host ""
    Write-Success "========================================="
    Write-Success "  Published successfully!"
    Write-Success "========================================="
    Write-Host ""
    Write-Info "View package: https://www.nuget.org/packages/CCXT.Collector/"
    Write-Host ""
    Write-Host "Note: It may take a few minutes for the package to appear on NuGet.org"
}

Write-Host ""
Write-Success "Done!"
