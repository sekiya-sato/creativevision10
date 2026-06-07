@echo off
setlocal
REM bat-file on cv10-folder 
REM set "PROJECT_DIR=%~dp0"

REM 配布対象の WPF クライアントと一時 publish 出力先を設定する。
set "PROJECT_DIR=%~dp0CvWpfclient\"
set "PUBLISH_DIR=%PROJECT_DIR%bin\publish-velopack"
set "VELOPACK_VERSION=1.2.0"

REM vpk コマンドが PATH から実行できることを確認する。
where vpk >nul 2>nul
if errorlevel 1 (
	echo [ERROR] vpk was not found. Run: dotnet tool install -g vpk --version %VELOPACK_VERSION%
	exit /b 1
)

REM vpk --version は使えないため、vpk -h の先頭行から CLI バージョンを取得する。
set "INSTALLED_VELOPACK_VERSION="
for /f "tokens=3" %%i in ('vpk -h 2^>nul ^| findstr /c:"Velopack CLI"') do set "INSTALLED_VELOPACK_VERSION=%%i"
set "INSTALLED_VELOPACK_VERSION=%INSTALLED_VELOPACK_VERSION:,=%"

REM 取得できない場合は、インストール済み vpk が想定外の状態として停止する。
if "%INSTALLED_VELOPACK_VERSION%"=="" (
	echo [ERROR] Failed to check vpk version from vpk -h. Run: dotnet tool update -g vpk --version %VELOPACK_VERSION%
	exit /b 1
)

REM publish / pack の再現性を守るため、vpk は固定バージョンだけ許可する。
if not "%INSTALLED_VELOPACK_VERSION%"=="%VELOPACK_VERSION%" (
	echo [ERROR] vpk version must be %VELOPACK_VERSION%. Current=%INSTALLED_VELOPACK_VERSION%
	echo [ERROR] Run: dotnet tool update -g vpk --version %VELOPACK_VERSION%
	exit /b 1
)

REM appsettings.json の Application.Version をパッチ増分し、今回の配布バージョンとして受け取る。
for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_DIR%publish-velopack.version.ps1" -AppSettingsPath "%PROJECT_DIR%appsettings.json" -Increment`) do set "APP_VERSION=%%i"

REM バージョン更新に失敗した場合は publish せずに停止する。
if "%APP_VERSION%"=="" (
	echo [ERROR] Failed to update Application.Version in appsettings.json.
	exit /b 1
)

REM 前回の publish 出力を削除し、古いファイルが package に混ざらないようにする。
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"

rem Do not use the /p:Version option. It modifies the AssemblyVersion, which triggers JSON conversion errors.
rem Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
rem dotnet publish "%PROJECT_DIR%CvWpfclient.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_DIR%" /p:FileVersion=%APP_VERSION% /p:InformationalVersion=%APP_VERSION%
REM AssemblyVersion は変更せず、FileVersion / InformationalVersion だけ配布版数へ合わせる。
dotnet publish "%PROJECT_DIR%CvWpfclient.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_DIR%" /p:FileVersion=%APP_VERSION% /p:InformationalVersion=%APP_VERSION%
if errorlevel 1 exit /b 1

REM Velopack package を CvWpfclient フォルダ基準で作成する。
pushd "%PROJECT_DIR%"
vpk pack --packId CreativeVision10 --packVersion %APP_VERSION% --packDir "%PUBLISH_DIR%" --mainExe CreativeVision10.exe
if errorlevel 1 (
	popd
	exit /b 1
)
popd

REM TODO: Add scp copy process here.
REM 作成した Velopack 生成物を公開先へ転送する。
bash ~/bin/publish.sh

REM 完了時に実際に使用した Application.Version を表示する。
echo [INFO] Velopack finished task for creating package. Version=%APP_VERSION%
endlocal
