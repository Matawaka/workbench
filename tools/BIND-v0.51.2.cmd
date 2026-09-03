@echo off
setlocal
powershell.exe -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0BindWorkbenchV0512LocalPredecessor.ps1"
if errorlevel 1 (
  echo.
  echo v0.51.2 local binding did not complete.
  pause
)
