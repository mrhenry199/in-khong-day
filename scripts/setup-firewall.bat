@echo off
powershell.exe -NoProfile -File "%~dp0setup-firewall.ps1" %*
exit /b %errorlevel%
