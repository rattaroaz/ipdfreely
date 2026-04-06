# Run All Platform Tests Script
# This script runs tests for all MAUI target frameworks

param(
    [switch]$SkipBuild,
    [string]$Logger,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

# Define target frameworks
$frameworks = @(
    "net10.0-android36.0",
    "net10.0-ios26.1", 
    "net10.0-maccatalyst26.1",
    "net10.0-windows10.0.19041.0"
)

Write-Host "=== Running MAUI Multi-Platform Tests ===" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Frameworks: $($frameworks -join ', ')" -ForegroundColor Yellow
Write-Host ""

# Test results
$results = @()
$overallSuccess = $true

foreach ($framework in $frameworks) {
    Write-Host "Testing framework: $framework" -ForegroundColor Cyan
    Write-Host "----------------------------------------"
    
    try {
        # Build arguments
        $buildArgs = @(
            "test",
            "ipdfreely.Tests/ipdfreely.Tests.csproj",
            "--framework", $framework,
            "--configuration", $Configuration,
            "--verbosity", "normal",
            "--no-build"
        )
        
        if (-not $SkipBuild) {
            $buildArgs = $buildArgs[0..($buildArgs.Length - 4)]  # Remove --no-build
        }
        
        if ($Logger) {
            $buildArgs += @("--logger", $Logger)
        }
        
        # Run the test
        $testResult = & dotnet $buildArgs 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Tests passed for $framework" -ForegroundColor Green
            $results += @{ Framework = $framework; Status = "Passed"; Output = $testResult }
        } else {
            Write-Host "❌ Tests failed for $framework" -ForegroundColor Red
            Write-Host $testResult -ForegroundColor Red
            $results += @{ Framework = $framework; Status = "Failed"; Output = $testResult }
            $overallSuccess = $false
        }
    }
    catch {
        Write-Host "❌ Error running tests for $framework`: $_" -ForegroundColor Red
        $results += @{ Framework = $framework; Status = "Error"; Output = $_ }
        $overallSuccess = $false
    }
    
    Write-Host ""
}

# Summary
Write-Host "=== Test Results Summary ===" -ForegroundColor Green
foreach ($result in $results) {
    $statusColor = if ($result.Status -eq "Passed") { "Green" } elseif ($result.Status -eq "Failed") { "Red" } else { "Yellow" }
    Write-Host "$($result.Framework): $($result.Status)" -ForegroundColor $statusColor
}

Write-Host ""
if ($overallSuccess) {
    Write-Host "🎉 All tests completed successfully!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "💥 Some tests failed. Check the output above for details." -ForegroundColor Red
    exit 1
}
