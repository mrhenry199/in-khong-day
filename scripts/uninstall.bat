@echo off
setlocal
echo ========================================================
echo   PrintBridge - Cleanup ^& Uninstallation
echo ========================================================
echo.

:: Check for administrative permissions
net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [REQUEST] Requesting Administrator privileges...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo [1/4] Closing running PrintBridge instances...
taskkill /F /IM PrintBridge.exe >nul 2>&1

echo [2/4] Removing Windows Firewall rules and approved device list...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-firewall.ps1" -Remove

echo [3/4] Removing Windows Startup entry...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "PrintBridge" /f >nul 2>&1

echo [4/4] Removing settings and unpacked runtime files...
if exist "%ProgramData%\PrintBridge" rmdir /s /q "%ProgramData%\PrintBridge"
if exist "%TEMP%\.net\PrintBridge" rmdir /s /q "%TEMP%\.net\PrintBridge"

echo.
echo ========================================================
echo   PrintBridge has been cleaned up!
echo   Firewall rules, approved devices, startup entry and
echo   settings were removed. Printers and drivers are untouched.
echo   You can now delete PrintBridge.exe.
echo ========================================================
echo.
pause
