@echo off
cd /d "%~dp0"
if not exist ".venv\Scripts\python.exe" (
  py -3.12 -m venv .venv
  if errorlevel 1 goto failed
)
".venv\Scripts\python.exe" -m pip install -r requirements.txt
if errorlevel 1 goto failed
".venv\Scripts\python.exe" setup_window.py
if errorlevel 1 goto failed
exit /b 0
:failed
echo Skontroluj instalaciu Python 3.12 a pripojenie.
pause
exit /b 1
