@echo off
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "TEST_RESULTS_DIR=%SCRIPT_DIR%..\Nexum.Tests\TestResults"
set "REPORT_OUTPUT_DIR=%SCRIPT_DIR%..\coveragereport"

where reportgenerator >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ReportGenerator not found. Installing...
    dotnet tool install -g dotnet-reportgenerator-globaltool
)

if not exist "%TEST_RESULTS_DIR%" (
    echo Error: TestResults directory not found at %TEST_RESULTS_DIR%
    echo Please run 'Run All Tests ^(Coverage^)' task first.
    exit /b 1
)

set "LATEST_COVERAGE="
for /f "delims=" %%f in ('powershell -NoProfile -Command "Get-ChildItem -Path '%TEST_RESULTS_DIR%' -Recurse -Filter 'coverage.cobertura.xml' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName"') do (
    set "LATEST_COVERAGE=%%f"
)

if not defined LATEST_COVERAGE (
    echo Error: No coverage files found in %TEST_RESULTS_DIR%
    echo Please run 'Run All Tests ^(Coverage^)' task first.
    exit /b 1
)

echo Using coverage file: %LATEST_COVERAGE%

echo Generating coverage report...
reportgenerator "-reports:%LATEST_COVERAGE%" "-targetdir:%REPORT_OUTPUT_DIR%" "-reporttypes:Html"

if %ERRORLEVEL% equ 0 (
    echo Coverage report generated successfully!
    echo Report location: %REPORT_OUTPUT_DIR%\index.html
    
    echo Opening report in browser...
    start "" "%REPORT_OUTPUT_DIR%\index.html"
) else (
    echo Error: Failed to generate coverage report
    exit /b 1
)

endlocal
