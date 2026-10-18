@echo off
rem Double-click to build the release zip. Runs build-release.ps1 without having to change
rem the PowerShell execution policy, which blocks .ps1 files by default on Windows.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-release.ps1"
pause
