@echo off
setlocal
set "DO_NOT_TRACK=1"
set "XDG_STATE_HOME=%~dp0.state"
if exist "%~dp0.runtime\python\python.exe" set "PATH=%~dp0.runtime\python;%PATH%"
where.exe git.exe >nul 2>nul
if errorlevel 1 (
  for /d %%D in ("%LOCALAPPDATA%\GitHubDesktop\app-*") do if exist "%%D\resources\app\git\cmd\git.exe" set "DESKTOP_GIT=%%D\resources\app\git\cmd"
)
if defined DESKTOP_GIT set "PATH=%DESKTOP_GIT%;%PATH%"
set "BLOCKS=%~dp0.state\bin\caveman-blocks.exe"
if not exist "%BLOCKS%" (
  1>&2 echo Run Development~\Tools\Caveman\caveman.cmd on scripts --yes first.
  exit /b 1
)
"%BLOCKS%" %*
exit /b %ERRORLEVEL%
