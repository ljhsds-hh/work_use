@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

where node >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Node.js not found. Install it from https://nodejs.org first.
  pause
  exit /b 1
)

echo Starting Portmarks...
node src/server.js
echo.
echo Server stopped.
pause
