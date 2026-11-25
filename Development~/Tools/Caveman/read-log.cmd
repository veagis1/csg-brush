@echo off
setlocal
set "DO_NOT_TRACK=1"
set "CAVEMAN_HOME=%~dp0.state"
set "CAVEMAN_ENGINE_BIN=%CAVEMAN_HOME%\bin\caveman-engine.exe"
set "NODE=node.exe"
if exist "%~dp0.runtime\node.exe" set "NODE=%~dp0.runtime\node.exe"
"%NODE%" "%~dp0read-log.mjs" %*
exit /b %ERRORLEVEL%
