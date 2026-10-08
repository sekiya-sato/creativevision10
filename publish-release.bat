@echo off
setlocal
set "RELEASE_VERSION=%~1"
if "%RELEASE_VERSION%"=="" set "RELEASE_VERSION=0.9.0"
REM サーバ設定を作成後、クライアントへ反映し、公開物を検証する。
call "%~dp0publish-server.bat" "%RELEASE_VERSION%"
if errorlevel 1 exit /b 1
call "%~dp0publish-velopack.bat" "%RELEASE_VERSION%" --local
if errorlevel 1 exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0prepare-release-assets.ps1" -Version "%RELEASE_VERSION%"
exit /b %errorlevel%
