@echo off
setlocal
title Publish SwiftGet to GitHub
cd /d "%~dp0"

echo.
echo  This will put the SwiftGet folder on your GitHub account as a new public repository.
echo.

where git >nul 2>nul
if errorlevel 1 goto nogit
where gh >nul 2>nul
if errorlevel 1 goto nogh

gh auth status >nul 2>nul
if errorlevel 1 (
  echo  First, log in to GitHub. A browser window will open - follow the steps there.
  gh auth login --web --git-protocol https
  if errorlevel 1 goto failed
)

git config user.name >nul 2>nul
if errorlevel 1 (
  set /p GITNAME= Your name for the commit history: 
  git config --global user.name "%GITNAME%"
)
git config user.email >nul 2>nul
if errorlevel 1 (
  set /p GITMAIL= Your email (the one on your GitHub account): 
  git config --global user.email "%GITMAIL%"
)

set REPO=swiftget
set /p REPO= Repository name (press Enter for swiftget): 
if "%REPO%"=="" set REPO=swiftget

if not exist ".git" git init -b main
git add -A
git commit -m "Initial commit: SwiftGet download manager"

gh repo create %REPO% --public --source=. --remote=origin --push --description "Free multi-connection download manager for Windows with a Chrome/Edge extension"
if errorlevel 1 goto failed

echo.
echo  Done! Your project is online.
echo  To publish a downloadable SwiftGet.exe on the Releases page, run these two lines:
echo.
echo      git tag v1.0.0
echo      git push origin v1.0.0
echo.
gh repo view --web
pause
exit /b 0

:nogit
echo  Git is not installed. Install it with:   winget install Git.Git
echo  Then close this window and run this file again.
pause
exit /b 1

:nogh
echo  The GitHub tool is not installed. Install it with:   winget install GitHub.cli
echo  Then close this window and run this file again.
pause
exit /b 1

:failed
echo.
echo  Something went wrong - copy the text above and send it to your AI agent or to me.
pause
exit /b 1
