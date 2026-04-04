@echo off
setlocal

set "PROJECT_DIR=%~dp0"
set "PUBLISH_DIR=%PROJECT_DIR%bin\publish-velopack"
set "VELOPACK_VERSION=0.0.1298"

for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_DIR%publish-velopack.version.ps1" -AppSettingsPath "%PROJECT_DIR%appsettings.json"`) do set "APP_VERSION=%%i"

if "%APP_VERSION%"=="" (
	echo [ERROR] Failed to read Application.Version from appsettings.json.
	exit /b 1
)

where vpk >nul 2>nul
if errorlevel 1 (
	echo [ERROR] vpk was not found. Run: dotnet tool install -g vpk --version %VELOPACK_VERSION%
	exit /b 1
)

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish "%PROJECT_DIR%Cvnet10Wpfclient.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_DIR%" /p:Version=%APP_VERSION% /p:FileVersion=%APP_VERSION% /p:InformationalVersion=%APP_VERSION%
if errorlevel 1 exit /b 1

pushd "%PROJECT_DIR%"
vpk pack --packId CreativeVision10 --packVersion %APP_VERSION% --packDir "%PUBLISH_DIR%" --mainExe CreativeVision10.exe
if errorlevel 1 (
	popd
	exit /b 1
)
popd

REM TODO: Add scp copy process here.
bash ~/bin/publish.sh


echo [INFO] Velopack finished task for creating package. Version=%APP_VERSION%
endlocal
