@echo off
setlocal

set "PROJECT_DIR=%~dp0"
set "PUBLISH_DIR=%PROJECT_DIR%bin\publish-velopack"
set "VELOPACK_VERSION=0.0.1298"

for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-Content '%PROJECT_DIR%appsettings.json' -Raw | ConvertFrom-Json).Application.Version"`) do set "APP_VERSION=%%i"

if "%APP_VERSION%"=="" (
	echo [ERROR] appsettings.json から Application.Version を取得できませんでした。
	exit /b 1
)

where vpk >nul 2>nul
if errorlevel 1 (
	echo [ERROR] vpk が見つかりません。dotnet tool install -g vpk --version %VELOPACK_VERSION% を実行してください。
	exit /b 1
)

if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

dotnet publish "%PROJECT_DIR%Cvnet10Wpfclient.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_DIR%" /p:Version=%APP_VERSION% /p:FileVersion=%APP_VERSION% /p:InformationalVersion=%APP_VERSION%
if errorlevel 1 exit /b 1

pushd "%PROJECT_DIR%"
vpk pack --packId creativevision10 --packVersion %APP_VERSION% --packDir "%PUBLISH_DIR%" --mainExe creativevision10.exe
if errorlevel 1 (
	popd
	exit /b 1
)
popd

REM TODO: ここに scp コピー処理を追記する

echo [INFO] Velopack パッケージ作成が完了しました。Version=%APP_VERSION%
endlocal
