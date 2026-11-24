@echo off
setlocal
title Building SwiftGet
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo  The free .NET SDK is not installed yet.
  echo  Install it with ONE of these, then run build.bat again:
  echo.
  echo    winget install Microsoft.DotNet.SDK.8
  echo    or download it from https://dotnet.microsoft.com/download
  echo.
  pause
  exit /b 1
)

echo Building SwiftGet...
dotnet build SwiftGet.csproj -c Release -o dist --nologo -v minimal
if errorlevel 1 (
  echo.
  echo  Build failed - copy the red error text and send it to me.
  pause
  exit /b 1
)

echo.
echo  Done!  Your app is:  %~dp0dist\SwiftGet.exe
echo  You can copy SwiftGet.exe anywhere and run it.
echo.
start "" "%~dp0dist"
pause
