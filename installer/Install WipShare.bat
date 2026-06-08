@echo off
title WipShare installer
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-WipShare.ps1"
echo.
pause
