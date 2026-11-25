@echo off
setlocal
set "DO_NOT_TRACK=1"
set "CAVEMAN_HOME=%~dp0.state"
set "CAVEMAN_ENGINE_BIN=%CAVEMAN_HOME%\bin\caveman-engine.exe"
if /i "%~1"=="browse" goto :firefox
if /i "%~1"=="tools" if /i "%~2"=="browse" goto :firefox
set "CLI=%~dp0node_modules\@caveman-ai\cli\dist\index.js"
set "NODE=node.exe"
if exist "%~dp0.runtime\node.exe" set "NODE=%~dp0.runtime\node.exe"
if not exist "%CLI%" (
  1>&2 echo Caveman is not installed. See README.md.
  exit /b 1
)
"%NODE%" "%CLI%" %*
exit /b %ERRORLEVEL%
:firefox
1>&2 echo Caveman Browse requires Chromium. This project uses Firefox MCP. See Development~/Knowledge/Инструкции/Caveman.md.
exit /b 2
