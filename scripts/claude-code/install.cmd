@echo off
setlocal EnableDelayedExpansion

echo === AI-Plugins-And-Skills Plugin Installer ===
echo.

:: Dynamically resolve repo root from script location (works from any clone path)
:: %~dp0 = drive+path of this batch file (absolute)
set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%..\.."
set "REPO_ROOT=%CD%"

:: Stay in repo root and use relative path (claude expects ./path format)
echo Adding AI-Plugins-And-Skills marketplace from: %REPO_ROOT%
call claude plugin marketplace add "./"
if errorlevel 1 goto :error

echo.
echo Installing ai-plugins-and-skills plugin...
call claude plugin install ai-plugins-and-skills@ai-plugins-and-skills-ai-standards
if errorlevel 1 goto :error

echo.
echo Installing ai-plugins-and-skills-config-sync plugin...
call claude plugin install ai-plugins-and-skills-config-sync@ai-plugins-and-skills-ai-standards
if errorlevel 1 goto :error

popd

echo.
echo === Installation Complete ===
echo Restart Claude Code to activate plugins.
goto :end

:error
popd
echo.
echo Installation failed. Check errors above.
exit /b 1

:end
endlocal
