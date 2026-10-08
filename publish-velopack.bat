@echo off
setlocal
set "DOTNET_ENVIRONMENT=Development"
set "ASPNETCORE_ENVIRONMENT=Development"
set "APP_VERSION="
set "RELEASE_VERSION=%~1"
set "LOCAL_ONLY=%~2"
if not "%LOCAL_ONLY%"=="" if not "%LOCAL_ONLY%"=="--local" exit /b 1
if not "%RELEASE_VERSION%"=="" (
	powershell -NoProfile -Command "if ($env:RELEASE_VERSION -notmatch '^\d+\.\d+\.\d+$') { exit 1 }"
	if errorlevel 1 exit /b 1
)
REM bat-file on cv10-folder 
REM set "PROJECT_DIR=%~dp0"

REM 配布対象の WPF クライアントと一時 publish 出力先を設定する。
set "PROJECT_DIR=%~dp0CvWpfclient\"
set "PUBLISH_DIR=%PROJECT_DIR%bin\publish-velopack"
set "VELOPACK_VERSION=1.2.161"

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

REM 明示版数は出力だけへ設定し、ソース設定の自動増分・同期を行わない。
if not "%RELEASE_VERSION%"=="" (
	set "APP_VERSION=%RELEASE_VERSION%"
	goto version_ready
)
REM appsettings.json の Application.Version をパッチ増分し、今回の配布バージョンとして受け取る。
for /f "usebackq delims=" %%i in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_DIR%publish-velopack.version.ps1" -AppSettingsPath "%PROJECT_DIR%appsettings.json" -Increment`) do set "APP_VERSION=%%i"

REM バージョン更新に失敗した場合は publish せずに停止する。
if "%APP_VERSION%"=="" (
	echo [ERROR] Failed to update Application.Version in appsettings.json.
	exit /b 1
)

REM CvServer/appsettings.json の ServerVersion も同じ配布バージョンに合わせる。
powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_DIR%publish-velopack.version.ps1" -AppSettingsPath "%~dp0CvServer\appsettings.json" -TopLevel -Key "ServerVersion" -SetVersion "%APP_VERSION%"
if errorlevel 1 (
	echo [ERROR] Failed to update ServerVersion in CvServer/appsettings.json.
	exit /b 1
)

:version_ready
REM 明示版数は専用フォルダで作成し、既存の配布成果物は上書きしない。
if not "%RELEASE_VERSION%"=="" (
	set "RELEASE_DIR=%~dp0Releases\%RELEASE_VERSION%"
	if exist "%~dp0Releases\%RELEASE_VERSION%\CreativeVision10-%RELEASE_VERSION%-win-x64-Portable.zip" (
		echo [ERROR] Release portable ZIP already exists.
		exit /b 1
	)
)
REM 前回の publish 出力を削除し、古いファイルが package に混ざらないようにする。
REM 削除対象をリポジトリ配下の専用出力に限定する。
powershell -NoProfile -Command "$expected = [IO.Path]::GetFullPath((Join-Path $env:PROJECT_DIR 'bin\publish-velopack')); if ([IO.Path]::GetFullPath($env:PUBLISH_DIR) -ne $expected) { exit 1 }; if (Test-Path -LiteralPath $expected) { Remove-Item -LiteralPath $expected -Recurse -Force }"
if errorlevel 1 exit /b 1

rem Do not use the /p:Version option. It modifies the AssemblyVersion, which triggers JSON conversion errors.
rem Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
rem dotnet publish "%PROJECT_DIR%CvWpfclient.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_DIR%" /p:FileVersion=%APP_VERSION% /p:InformationalVersion=%APP_VERSION%
REM AssemblyVersion は変更せず、FileVersion / InformationalVersion だけ配布版数へ合わせる。
dotnet publish "%PROJECT_DIR%CvWpfclient.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_DIR%" /p:FileVersion=%APP_VERSION% /p:InformationalVersion=%APP_VERSION%
if errorlevel 1 exit /b 1

REM GitHub向けの明示版数ではローカル環境設定を除外する。
if not "%RELEASE_VERSION%"=="" (
	powershell -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_DIR%publish-velopack.version.ps1" -AppSettingsPath "%PUBLISH_DIR%\appsettings.json" -SetVersion "%APP_VERSION%"
	if errorlevel 1 exit /b 1
	powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0prepare-release-client.ps1" -Version "%APP_VERSION%" -PublishDir "%PUBLISH_DIR%"
	if errorlevel 1 exit /b 1
)
REM 公開用は毎回別のpack作業先を使用する。更新用nupkg等はbin配下へ残す。
if not "%RELEASE_VERSION%"=="" (
	for /f %%i in ('powershell -NoProfile -Command "[guid]::NewGuid().ToString('N')"') do set "PACK_ID=%%i"
)
set "PACKAGE_DIR=%PROJECT_DIR%bin\velopack-package\%PACK_ID%"
REM Velopack package を CvWpfclient フォルダ基準で作成する。
pushd "%PROJECT_DIR%"
if "%RELEASE_VERSION%"=="" (
	vpk pack --packId CreativeVision10 --packVersion %APP_VERSION% --packDir "%PUBLISH_DIR%" --mainExe CreativeVision10.exe
) else (
	vpk pack --packId CreativeVision10 --packVersion %APP_VERSION% --packDir "%PUBLISH_DIR%" --mainExe CreativeVision10.exe --outputDir "%PACKAGE_DIR%" --channel win --runtime win-x64 --noInst --delta None
)
if errorlevel 1 (
	popd
	exit /b 1
)
popd

REM GitHub添付先には版数入りPortable ZIPだけを配置する。
if not "%RELEASE_VERSION%"=="" (
	powershell -NoProfile -Command "$ErrorActionPreference = 'Stop'; New-Item -ItemType Directory -Force -Path $env:RELEASE_DIR | Out-Null; Copy-Item -LiteralPath (Join-Path $env:PACKAGE_DIR 'CreativeVision10-win-Portable.zip') -Destination (Join-Path $env:RELEASE_DIR ('CreativeVision10-' + $env:APP_VERSION + '-win-x64-Portable.zip'))"
	if errorlevel 1 exit /b 1
)
REM TODO: Add scp copy process here.
REM 作成した Velopack 生成物を公開先へ転送する。
if "%LOCAL_ONLY%"=="--local" goto finished
REM 明示版数での公開は手順書に従い、別サーバへ自動転送しない。
if not "%RELEASE_VERSION%"=="" goto finished
bash ~/bin/publish.sh
if errorlevel 1 exit /b 1
:finished

REM 完了時に実際に使用した Application.Version を表示する。
echo [INFO] Velopack finished task for creating package. Version=%APP_VERSION%
endlocal
