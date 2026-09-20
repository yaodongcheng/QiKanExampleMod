@echo off
cd /d "%~dp0"
title Bannerlord Anim Viewer

set "PY="
where python >nul 2>nul && set "PY=python"
if not defined PY (
  where py >nul 2>nul && set "PY=py -3"
)
if not defined PY (
  echo.
  echo  [ERROR] Python 3 not found in PATH.
  echo  Install Python 3, or open this .bat and set PY to your python.exe full path.
  echo.
  pause
  exit /b 1
)

echo.
echo  ==============================================================
echo   Bannerlord Anim Viewer
echo  --------------------------------------------------------------
echo   * browser opens automatically - the PORT does not matter
echo   * keep THIS window open while using the viewer
echo   * close THIS window to stop the server
echo  ==============================================================
echo.

%PY% serve.py ue_exec_pair

echo.
echo  server stopped.
pause
