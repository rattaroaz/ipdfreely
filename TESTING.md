# MAUI Multi-Platform Testing Guide

This document explains how to run tests for the ipdfreely MAUI application across all target platforms.

## Quick Start

### Run Tests for Available Platforms (Recommended)
```powershell
.\run-tests-smart.ps1
```

### Run Windows Tests Only
```powershell
.\run-tests-smart.ps1 -WindowsOnly
```

### Run All Platform Tests (May Fail on Unavailable Platforms)
```powershell
.\run-tests-smart.ps1 -AllPlatforms
```

## Manual Test Execution

### Using Solution File
```bash
# Test Windows platform
dotnet test ipdfreely.sln --framework net10.0-windows10.0.19041.0

# Test specific project
dotnet test ipdfreely.Tests/ipdfreely.Tests.csproj --framework net10.0-windows10.0.19041.0
```

### Using Individual Framework Commands
```bash
# Windows
dotnet test ipdfreely.Tests/ipdfreely.Tests.csproj --framework net10.0-windows10.0.19041.0

# Android (requires Android SDK)
dotnet test ipdfreely.Tests/ipdfreely.Tests.csproj --framework net10.0-android36.0

# iOS (requires macOS with Xcode)
dotnet test ipdfreely.Tests/ipdfreely.Tests.csproj --framework net10.0-ios26.1

# MacCatalyst (requires macOS)
dotnet test ipdfreely.Tests/ipdfreely.Tests.csproj --framework net10.0-maccatalyst26.1
```

## Platform Requirements

| Platform | Runtime Requirement | Notes |
|----------|-------------------|-------|
| **Windows** | Windows 10+ | ✅ Available on this system |
| **Android** | Android SDK/Emulator | Requires Android development environment |
| **iOS** | Xcode/macOS with iOS Simulator | Requires macOS with Xcode |
| **MacCatalyst** | macOS | Requires macOS |

## Test Structure

The test project (`ipdfreely.Tests`) includes:

- **Unit Tests**: Service layer testing with xUnit, FluentAssertions, and Moq
- **Integration Tests**: End-to-end testing of PDF operations
- **Multi-Targeting**: Configured for all MAUI target frameworks

### Test Categories
- `LoggingServiceTests`: Logging functionality
- `PdfServicesTests`: PDF content detection and export services
- `IntegrationTests`: Cross-service integration scenarios

## Configuration Notes

### Multi-Target Framework Support
The test project targets all MAUI frameworks:
```xml
<TargetFrameworks>net10.0-android36.0;net10.0-ios26.1;net10.0-maccatalyst26.1;net10.0-windows10.0.19041.0</TargetFrameworks>
```

### MAUI-Specific Considerations
- **Resizetizer Disabled**: Prevents duplicate asset errors in test environment
- **Source File Inclusion**: Service files included directly instead of project reference
- **FileSystem Fallback**: LoggingService handles MAUI FileSystem unavailability in tests

### Package References
- xUnit 2.9.3 (test framework)
- FluentAssertions 6.12.0 (assertions)
- Moq 4.20.69 (mocking)
- UglyToad.PdfPig 1.7.0-custom-5 (PDF processing)
- PdfSharpCore 1.3.65 (PDF generation)

## CI/CD Integration

For automated testing in CI/CD pipelines:

```yaml
# Example GitHub Actions workflow
- name: Run Tests
  run: |
    dotnet test ipdfreely.sln --framework net10.0-windows10.0.19041.0 --logger trx --results-directory TestResults
```

## Troubleshooting

### Common Issues

1. **"Specify which project or solution file to use" Error**
   - Use: `dotnet test ipdfreely.sln` or `.\run-tests-smart.ps1`

2. **MAUI FileSystem Errors in Tests**
   - Expected behavior - tests use fallback directory logic

3. **Platform-Specific Build Failures**
   - Use smart script to test only available platforms
   - Check platform requirements table above

4. **Test Project Not Found**
   - Ensure `ipdfreely.Tests.csproj` exists in the `ipdfreely.Tests` directory

### Getting Help

- Use `.\run-tests-smart.ps1` for intelligent platform detection
- Check the platform requirements table for missing dependencies
- Review test output for specific error messages

## Scripts Reference

- `run-tests-smart.ps1`: Intelligent multi-platform test runner (recommended)
- `run-tests.ps1`: Full multi-platform test runner
- `run-tests.bat`: Batch file version for Windows CMD

Choose the script that best fits your testing needs.
