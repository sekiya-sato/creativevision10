@echo off
setlocal
set "DOTNET_ENVIRONMENT=Development"
set "ASPNETCORE_ENVIRONMENT=Development"
REM Windows/Linux x64 の公開ZIPを作成する。省略時は 0.9.0。
set "RELEASE_VERSION=%~1"
if "%RELEASE_VERSION%"=="" set "RELEASE_VERSION=0.9.0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-server.ps1" -Version "%RELEASE_VERSION%"
exit /b %errorlevel%
