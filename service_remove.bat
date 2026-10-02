@echo off
setlocal
set "PAV_SOURCE=%~dp0"
set "PAV_TEMP_PS=%TEMP%\PAVVPN-uninstall-%RANDOM%-%RANDOM%.ps1"
copy /y "%PAV_SOURCE%uninstall.ps1" "%PAV_TEMP_PS%" >nul
if errorlevel 1 goto FAILED
cd /d "%TEMP%"
start "PAVVPN - Guvenli Kaldirma" powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PAV_TEMP_PS%" -Confirm
endlocal
exit /b 0

:FAILED
echo.
echo [HATA] Kaldirma tamamlanamadi. Yukaridaki mesaji kontrol edin.
echo Kapatmak icin ENTER tusuna basin.
set /p "="
endlocal
exit /b 1
