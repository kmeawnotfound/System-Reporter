@echo off
rem ===========================================================================
rem  Build.bat - bootstrap build for SystemReport.
rem
rem  Uses the C# compiler that ships with the .NET Framework, so no SDK,
rem  NuGet, or Visual Studio installation is required.
rem
rem  Usage:
rem    Build.bat          build and offer to run the result
rem    Build.bat -q       build only (quiet; safe for scripts and CI)
rem
rem  For clean/test/package/release workflows use build.ps1 instead.
rem ===========================================================================
setlocal
title Build SystemReport

set "QUIET="
if /i "%~1"=="-q" set "QUIET=1"
if /i "%~1"=="--quiet" set "QUIET=1"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo.
    echo   Could not find the .NET Framework C# compiler on this PC.
    echo   Install .NET Framework 4.x, then run this script again.
    echo.
    if not defined QUIET pause
    exit /b 1
)

if not exist "%~dp0SystemReport.cs" (
    echo.
    echo   SystemReport.cs must be in the same folder as Build.bat.
    echo.
    if not defined QUIET pause
    exit /b 1
)

rem Do not leave an old exe behind if the new build fails.
if exist "%~dp0SystemReport.exe" del "%~dp0SystemReport.exe"

echo.
echo   Compiling SystemReport.cs ...
"%CSC%" /nologo /target:exe /platform:anycpu /langversion:5 /optimize+ /warn:4 /out:"%~dp0SystemReport.exe" /reference:System.Management.dll "%~dp0SystemReport.cs"
if errorlevel 1 (
    echo.
    echo   Build failed. See the errors above.
    echo.
    if not defined QUIET pause
    exit /b 1
)

echo.
echo   Built: %~dp0SystemReport.exe
echo.

rem "Build.bat -q" is useful for scripts: build only, do not prompt or run.
if defined QUIET exit /b 0

choice /m "  Run it now"
if errorlevel 2 exit /b 0
start "" "%~dp0SystemReport.exe"
