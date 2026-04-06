# Smart Test Runner for MAUI Multi-Platform Testing
# This script intelligently tests available platforms and provides guidance

param(
    [switch]$AllPlatforms,
    [switch]$WindowsOnly,
    [string]$Logger,
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

# Define target frameworks with their runtime requirements
$frameworks = @{
    "net10.0-windows10.0.19041.0" = @{
        Name = "Windows"
        Available = $true
        RuntimeRequirement = "Windows 10+"
        TestCommand = "dotnet test ipdfreely.Tests/ipdfreely.Tests.csproj --framework net10.0-windows10.0.19041.0"
    }
    "net10.0-android36.0" = @{
        Name = "Android"
        Available = $false
        RuntimeRequirement = "Android SDK/Emulator"
        TestCommand = "dotnet test ipdfreely.Tests/ipdfreely.Tests.csproj --framework net10.0-android36.0"
        Note = "Requires Android development environment"
    }
    "net10.0-ios26.1" = @{
        Name = "iOS"
        Available = $false
        RuntimeRequirement = "Xcode/macOS with iOS Simulator"
        TestCommand = "dotnet test ipdfreely.Tests/ipdfreely.Tests.csproj --framework net10.0-ios26.1"
        Note = "Requires macOS with Xcode"
    }
    "net10.0-maccatalyst26.1" = @{
        Name = "MacCatalyst"
        Available = $false
        RuntimeRequirement = "macOS"
        TestCommand = "dotnet test ipdfreely.Tests/ipdfreely.Tests.csproj --framework net10.0-maccatalyst26.1"
        Note = "Requires macOS"
    }
}

Write-Host "=== MAUI Multi-Platform Test Runner ===" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host ""

# Detect current platform
$currentOS = $PSVersionTable.Platform
if (-not $currentOS) {
    $currentOS = if ($IsWindows) { "Windows" } elseif ($IsLinux) { "Linux" } elseif ($IsMacOS) { "macOS" } else { "Unknown" }
}

Write-Host "Current Platform: $currentOS" -ForegroundColor Cyan
Write-Host ""

# Update availability based on current platform
if ($currentOS -eq "Windows") {
    $frameworks["net10.0-windows10.0.19041.0"].Available = $true
} elseif ($currentOS -eq "macOS") {
    $frameworks["net10.0-maccatalyst26.1"].Available = $true
    $frameworks["net10.0-ios26.1"].Available = $true
}

# Filter frameworks based on parameters
$frameworksToTest = @()
if ($WindowsOnly) {
    $frameworksToTest += "net10.0-windows10.0.19041.0"
} elseif ($AllPlatforms) {
    $frameworksToTest = $frameworks.Keys
} else {
    # Default: test only available platforms
    $frameworksToTest = $frameworks.Keys | Where-Object { $frameworks[$_].Available }
}

if (-not $frameworksToTest) {
    Write-Host "❌ No platforms available for testing on this system." -ForegroundColor Red
    Write-Host ""
    Write-Host "Platform Requirements:" -ForegroundColor Yellow
    foreach ($fw in $frameworks.Keys) {
        $info = $frameworks[$fw]
        Write-Host "  • $($info.Name): $($info.RuntimeRequirement)" -ForegroundColor White
        if ($info.Note) {
            Write-Host "    Note: $($info.Note)" -ForegroundColor Gray
        }
    }
    Write-Host ""
    Write-Host "Use -AllPlatforms to attempt testing all platforms (may fail)." -ForegroundColor Cyan
    Write-Host "Use -WindowsOnly to force Windows testing only." -ForegroundColor Cyan
    exit 1
}

Write-Host "Platforms to test:" -ForegroundColor Yellow
foreach ($fw in $frameworksToTest) {
    $info = $frameworks[$fw]
    $status = if ($info.Available) { "✅ Available" } else { "⚠️ May fail" }
    Write-Host "  • $($info.Name): $status" -ForegroundColor $(if ($info.Available) { "Green" } else { "Yellow" })
}
Write-Host ""

# Test results
$results = @()
$overallSuccess = $true

foreach ($framework in $frameworksToTest) {
    $info = $frameworks[$framework]
    Write-Host "Testing framework: $($info.Name) ($framework)" -ForegroundColor Cyan
    Write-Host "----------------------------------------"
    
    try {
        # Build arguments
        $buildArgs = @(
            "test",
            "ipdfreely.Tests/ipdfreely.Tests.csproj",
            "--framework", $framework,
            "--configuration", $Configuration,
            "--verbosity", "normal"
        )
        
        if ($Logger) {
            $buildArgs += @("--logger", $Logger)
        }
        
        # Run the test
        $testResult = & dotnet $buildArgs 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Tests passed for $($info.Name)" -ForegroundColor Green
            $results += @{ Framework = $framework; Name = $info.Name; Status = "Passed"; Output = $testResult }
        } else {
            Write-Host "❌ Tests failed for $($info.Name)" -ForegroundColor Red
            if (-not $info.Available) {
                Write-Host "   (Expected - platform not available on this system)" -ForegroundColor Yellow
            }
            $results += @{ Framework = $framework; Name = $info.Name; Status = "Failed"; Output = $testResult }
            if ($info.Available) {
                $overallSuccess = $false
            }
        }
    }
    catch {
        Write-Host "❌ Error running tests for $($info.Name): $_" -ForegroundColor Red
        $results += @{ Framework = $framework; Name = $info.Name; Status = "Error"; Output = $_ }
        if ($info.Available) {
            $overallSuccess = $false
        }
    }
    
    Write-Host ""
}

# Summary
Write-Host "=== Test Results Summary ===" -ForegroundColor Green
foreach ($result in $results) {
    $statusColor = switch ($result.Status) {
        "Passed" { "Green" }
        "Failed" { if ($frameworks[$result.Framework].Available) { "Red" } else { "Yellow" } }
        "Error" { "Red" }
        default { "Yellow" }
    }
    Write-Host "$($result.Name): $($result.Status)" -ForegroundColor $statusColor
}

Write-Host ""
if ($overallSuccess) {
    Write-Host "🎉 All available platform tests completed successfully!" -ForegroundColor Green
} else {
    Write-Host "💥 Some available platform tests failed. Check the output above for details." -ForegroundColor Red
}

# Provide guidance for unavailable platforms
$unavailablePlatforms = $frameworks.Keys | Where-Object { 
    -not $frameworks[$_].Available -and 
    $_ -notin $frameworksToTest 
}
if ($unavailablePlatforms) {
    Write-Host ""
    Write-Host "Platforms not tested (setup required):" -ForegroundColor Yellow
    foreach ($fw in $unavailablePlatforms) {
        $info = $frameworks[$fw]
        Write-Host "  • $($info.Name): $($info.RuntimeRequirement)" -ForegroundColor White
        if ($info.Note) {
            Write-Host "    Note: $($info.Note)" -ForegroundColor Gray
        }
    }
    Write-Host ""
    Write-Host "Use -AllPlatforms to attempt testing these platforms." -ForegroundColor Cyan
}

exit $(if ($overallSuccess) { 0 } else { 1 })
