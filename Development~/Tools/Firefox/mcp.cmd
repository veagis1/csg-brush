@echo off
setlocal
set "NODE=node.exe"
if exist "%~dp0..\Caveman\.runtime\node.exe" set "NODE=%~dp0..\Caveman\.runtime\node.exe"
set "MCP=%~dp0node_modules\@mozilla\firefox-mcp\dist\index.js"
if not exist "%MCP%" (
  1>&2 echo Install Development~\Tools\Firefox dependencies with npm ci first.
  exit /b 1
)
if not defined FIREFOX_MCP_EXECUTABLE set "FIREFOX_MCP_EXECUTABLE=%ProgramFiles%\Mozilla Firefox\firefox.exe"
if not exist "%FIREFOX_MCP_EXECUTABLE%" (
  1>&2 echo Firefox was not found. Set FIREFOX_MCP_EXECUTABLE to the path of firefox.exe.
  exit /b 1
)
if not defined FIREFOX_MCP_SESSION_DIR set "FIREFOX_MCP_SESSION_DIR=%~dp0.state\session"
if not exist "%FIREFOX_MCP_SESSION_DIR%\." mkdir "%FIREFOX_MCP_SESSION_DIR%"
if errorlevel 1 exit /b 1
set "GECKODRIVER_CACHE_DIR=%~dp0.state\bin"
if exist "%GECKODRIVER_CACHE_DIR%\geckodriver.exe" set "PATH=%GECKODRIVER_CACHE_DIR%;%PATH%"
"%NODE%" "%MCP%" --headless --firefox-path "%FIREFOX_MCP_EXECUTABLE%" --profile-path "%FIREFOX_MCP_SESSION_DIR%\profile" --output-file "%FIREFOX_MCP_SESSION_DIR%\firefox.log" --tool-preset slim --start-url about:blank
exit /b %ERRORLEVEL%
