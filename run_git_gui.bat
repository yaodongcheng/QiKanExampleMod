@echo off
chcp 65001 >nul
cd /d "%~dp0"

set "PYEXE="
if exist "%LOCALAPPDATA%\Python\bin\python.exe" set "PYEXE=%LOCALAPPDATA%\Python\bin\python.exe"
if defined PYEXE goto :RUN

where /q py.exe
if not errorlevel 1 set "PYEXE=py"
if defined PYEXE goto :RUN

where /q python.exe
if not errorlevel 1 set "PYEXE=python"
if defined PYEXE goto :RUN

echo [ERROR] Python not found. Install Python 3.x and retry.
pause
exit /b 1

:RUN
"%PYEXE%" git_gui.py
if errorlevel 1 pause
