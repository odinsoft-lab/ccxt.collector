<#
.SYNOPSIS
    Builds and publishes the CCXT.Collector NuGet package to NuGet.org.

.DESCRIPTION
    This script automates the complete NuGet package publishing workflow:
    - Validates NuGet API key (from parameter or NUGET_API_KEY environment variable)
    - Verifies .NET SDK installation
    - Cleans previous package artifacts
    - Builds the project in Release configuration
    - Runs tests (optional)
    - Creates NuGet package (.nupkg) and symbol package (.snupkg)
    - Publishes to NuGet.org with user confirmation

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
    .\scripts\publish-nuget.ps1 -ApiKey "your-api-key"
    Builds, tests, and publishes the package using the provided API key.

.EXAMPLE
    .\scripts\publish-nuget.ps1 -DryRun
    Performs a dry run without publishing (requires NUGET_API_KEY env variable).

.EXAMPLE
    .\scripts\publish-nuget.ps1 -SkipTests -SkipBuild
    Publishes using existing build, skipping tests.

.NOTES
    File Name  : publish-nuget.ps1
    Author     : ODINSOFT
    Repository : https://github.com/odinsoft-lab/ccxt.collector
    Requires   : .NET SDK 8.0 or later

.LINK
    https://www.nuget.org/packages/CCXT.Collector
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ApiKey = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipBuild = $false,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipTests = $false,
    
    [Parameter(Mandatory=$false)]
    [switch]$DryRun = $false
)

# Configuration
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ProjectPath = Join-Path $RootDir "src\ccxt.collector.csproj"
$Configuration = "Release"
$OutputDirectory = Join-Path $RootDir "src\bin\Release"
$NuGetSource = "https://api.nuget.org/v3/index.json"

# Color output functions
function Write-Success { param($Message) Write-Host $Message -ForegroundColor Green }
function Write-Info { param($Message) Write-Host $Message -ForegroundColor Cyan }
function Write-Warning { param($Message) Write-Host $Message -ForegroundColor Yellow }
function Write-Error { param($Message) Write-Host $Message -ForegroundColor Red }

# Banner
Write-Host ""
Write-Info "========================================="
Write-Info "  CCXT.Collector NuGet Package Publisher"
Write-Info "========================================="
Write-Host ""

# Check if API key is provided or exists in environment
if ([string]::IsNullOrEmpty($ApiKey)) {
    $ApiKey = $env:NUGET_API_KEY
    if ([string]::IsNullOrEmpty($ApiKey)) {
        Write-Error "Error: NuGet API key not provided!"
        Write-Host ""
        Write-Host "Please provide API key using one of these methods:"
        Write-Host "  1. Pass as parameter: .\scripts\publish-nuget.ps1 -ApiKey YOUR_KEY"
        Write-Host "  2. Set environment variable: `$env:NUGET_API_KEY = 'YOUR_KEY'"
        Write-Host ""
        Write-Host "Get your API key from: https://www.nuget.org/account/apikeys"
        exit 1
    } else {
        Write-Info "Using API key from environment variable NUGET_API_KEY"
    }
}

# Check if dotnet CLI is available
try {
    $dotnetVersion = dotnet --version
    Write-Info "Found .NET SDK version: $dotnetVersion"
} catch {
    Write-Error "Error: .NET SDK not found! Please install from https://dotnet.microsoft.com/download"
    exit 1
}

# Clean previous packages
Write-Host ""
Write-Info "Cleaning previous packages..."
if (Test-Path $OutputDirectory) {
    Remove-Item "$OutputDirectory\*.nupkg" -Force -ErrorAction SilentlyContinue
    Remove-Item "$OutputDirectory\*.snupkg" -Force -ErrorAction SilentlyContinue
}

# Build the project
if (-not $SkipBuild) {
    Write-Host ""
    Write-Info "Building project in $Configuration mode..."
    
    $buildCommand = "dotnet build `"$ProjectPath`" --configuration $Configuration"
    Write-Host "  Command: $buildCommand"
    
    $buildResult = Invoke-Expression $buildCommand
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed! Please fix build errors and try again."
        exit 1
    }
    Write-Success "Build completed successfully!"
} else {
    Write-Warning "Skipping build step (using existing build)"
}

# Run tests (optional)
if (-not $SkipTests) {
    Write-Host ""
    Write-Info "Running tests..."
    $TestPath = Join-Path $RootDir "tests\CCXT.Collector.Tests.csproj"
    
    # Build test project first
    $testBuildCommand = "dotnet build `"$TestPath`" --configuration $Configuration"
    Write-Host "  Building tests: $testBuildCommand"
    $testBuildResult = Invoke-Expression $testBuildCommand
    
    if ($LASTEXITCODE -eq 0) {
        # Run tests
        $testCommand = "dotnet test `"$TestPath`" --configuration $Configuration --no-build"
        Write-Host "  Running tests: $testCommand"
        $testResult = Invoke-Expression $testCommand
        
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Tests failed! Consider fixing tests before publishing."
            $confirmation = Read-Host "Do you want to continue anyway? (y/N)"
            if ($confirmation -ne 'y' -and $confirmation -ne 'Y') {
                Write-Warning "Publication cancelled"
                exit 1
            }
        } else {
            Write-Success "All tests passed!"
        }
    } else {
        Write-Warning "Test build failed. Skipping tests."
    }
} else {
    Write-Warning "Skipping tests (use -SkipTests flag to suppress this warning)"
}

# Create NuGet package
Write-Host ""
Write-Info "Creating NuGet package..."

$packCommand = "dotnet pack `"$ProjectPath`" --configuration $Configuration --no-build"
Write-Host "  Command: $packCommand"

$packResult = Invoke-Expression $packCommand
if ($LASTEXITCODE -ne 0) {
    Write-Error "Package creation failed!"
    exit 1
}
Write-Success "Package created successfully!"

# Find the created package
$packageFile = Get-ChildItem -Path $OutputDirectory -Filter "*.nupkg" | 
                Where-Object { $_.Name -notlike "*.symbols.nupkg" } | 
                Select-Object -First 1

if ($null -eq $packageFile) {
    Write-Error "No package file found in $OutputDirectory"
    exit 1
}

$packagePath = $packageFile.FullName
$packageName = $packageFile.Name

Write-Host ""
Write-Success "Package created: $packageName"
Write-Info "Package location: $packagePath"

# Get package info
Write-Host ""
Write-Info "Package Information:"
$packageInfo = dotnet nuget locals all --list
Write-Host "  Package: $packageName"
Write-Host "  Size: $([math]::Round($packageFile.Length / 1MB, 2)) MB"

# Publish to NuGet
if ($DryRun) {
    Write-Host ""
    Write-Warning "DRY RUN MODE - Package will NOT be published"
    Write-Info "To publish, run without -DryRun flag"
} else {
    Write-Host ""
    Write-Warning "About to publish package to NuGet.org"
    Write-Host "Package: $packageName"
    Write-Host ""
    
    $confirmation = Read-Host "Do you want to continue? (y/N)"
    if ($confirmation -ne 'y' -and $confirmation -ne 'Y') {
        Write-Warning "Publication cancelled by user"
        exit 0
    }
    
    Write-Host ""
    Write-Info "Publishing package to NuGet.org..."
    
    $pushCommand = "dotnet nuget push `"$packagePath`" --api-key `"$ApiKey`" --source `"$NuGetSource`" --skip-duplicate"
    Write-Host "  Command: dotnet nuget push [package] --api-key [HIDDEN] --source $NuGetSource --skip-duplicate"
    
    $pushResult = Invoke-Expression $pushCommand
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Package publication failed!"
        Write-Host ""
        Write-Host "Common issues:"
        Write-Host "  - Invalid API key"
        Write-Host "  - Package version already exists"
        Write-Host "  - Network connection issues"
        exit 1
    }
    
    Write-Host ""
    Write-Success "========================================="
    Write-Success "  Package published successfully!"
    Write-Success "========================================="
    Write-Host ""
    Write-Info "Package URL: https://www.nuget.org/packages/ccxt.collector/"
    Write-Info "It may take a few minutes for the package to appear on NuGet.org"
}

Write-Host ""
Write-Success "Process completed!"