@echo off
title ASX Worker - Multi-map Low
cd /d "%~dp0"
echo.
echo  AutoScenarioX worker - all installed scenario maps, Low quality.
echo  Start the API and a campaign in the frontend; the worker waits for queued runs.
echo  Inventory only: Start-ASX-Worker.bat -ListMaps
echo  Short run: Start-ASX-Worker.bat -Quick
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\run_worker.ps1" %*
set EXITCODE=%ERRORLEVEL%
echo.
if %EXITCODE% NEQ 0 (
  echo Worker exited with error %EXITCODE%.
) else (
  echo Worker stopped.
)
echo Press any key to close...
pause >nul
