@echo off
setlocal
set TIMESTAMP=%date:~0,4%%date:~5,2%%date:~8,2%_%time:~0,2%%time:~3,2%
set TIMESTAMP=%TIMESTAMP: =0%
set TARGET_ZIP=bin/clickonce_%TIMESTAMP%.zip
echo Backing up to: %TARGET_ZIP%
zip -r -m -u %TARGET_ZIP%  "bin/publish/Application Files/*"
echo Backup completed.
endlocal
:: check error level
if %ERRORLEVEL% equ 12 (
    echo [INFO] Ignore: File empty/mismatch errors.
    exit /b 0
)
