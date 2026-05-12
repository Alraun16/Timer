@echo off
setlocal

set "SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Timer.lnk"
set "LOG=%TEMP%\Timer-toast.log"

reg delete "HKCU\Software\Classes\timer" /f >nul 2>nul
del "%SHORTCUT%" >nul 2>nul
del "%LOG%" >nul 2>nul

echo Timer notification registration has been removed.
echo You can now delete the Timer application files.
