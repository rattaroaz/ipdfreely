@echo off
setlocal enabledelayedexpansion

echo === Running MAUI Multi-Platform Tests ===
echo.

REM Define target frameworks
set frameworks[0]=net10.0-android36.0
set frameworks[1]=net10.0-ios26.1
set frameworks[2]=net10.0-maccatalyst26.1
set frameworks[3]=net10.0-windows10.0.19041.0

set overallSuccess=true

for /l %%i in (0,1,3) do (
    set framework=!frameworks[%%i]!
    echo Testing framework: !framework!
    echo ----------------------------------------
    
    dotnet test ipdfreely.Tests\ipdfreely.Tests.csproj --framework !framework! --configuration Debug --verbosity normal
    
    if !errorlevel! equ 0 (
        echo ✅ Tests passed for !framework!
    ) else (
        echo ❌ Tests failed for !framework!
        set overallSuccess=false
    )
    echo.
)

echo === Test Results Summary ===
if "%overallSuccess%"=="true" (
    echo 🎉 All tests completed successfully!
    exit /b 0
) else (
    echo 💥 Some tests failed. Check the output above for details.
    exit /b 1
)
