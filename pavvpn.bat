@echo off
setlocal
title PAVVPN Native v5 - Kurulum
cd /d "%~dp0"
echo ================================================================
echo PAVVPN NATIVE v5 - Discord'a Ozel Surucusuz Yerel Motor
echo ================================================================
echo Harici proxy motoru, kernel surucusu, WinDivert veya servis kurulmaz.
echo Yonetici olarak calistirmayin.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 goto FAILED
echo.
echo [OK] PAVVPN Native kuruldu ve dogrulandi.
exit /b 0
:FAILED
echo.
echo [HATA] Kurulum tamamlanamadi. Yukaridaki mesaji kontrol edin.
echo Kapatmak icin ENTER tusuna basin.
set /p "="
exit /b 1
