param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.9.0'
)

$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$root = $PSScriptRoot
$releaseDir = Join-Path $root "Releases\$Version"
$workDir = Join-Path $root ('CvServer\bin\publish-server\' + [guid]::NewGuid().ToString('N'))
$runtimes = @('win-x64', 'linux-x64')
$printProject = Join-Path $root 'CvPrints\CvPrints.csproj'
$originalPrintProject = $null

try {
    # 既存の配布ZIPを上書きしない。ビルド出力は毎回新しい作業フォルダへ作る。
    foreach ($rid in $runtimes) {
        if (Test-Path -LiteralPath (Join-Path $releaseDir "CvServer-$Version-$rid.zip")) {
            throw "Release archive already exists: CvServer-$Version-$rid.zip"
        }
    }
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    # 印刷設定は発行中だけfalseにし、元のバイト列をfinallyで復元する。
    $originalPrintProject = [IO.File]::ReadAllBytes($printProject)
    $printContent = [IO.File]::ReadAllText($printProject)
    if ([regex]::Matches($printContent, '<PrintEnable>[^<]*</PrintEnable>').Count -ne 1) {
        throw 'Expected exactly one PrintEnable property in CvPrints.csproj.'
    }
    [IO.File]::WriteAllText($printProject, [regex]::Replace($printContent, '<PrintEnable>[^<]*</PrintEnable>', '<PrintEnable>false</PrintEnable>'), $utf8)
    Push-Location $root
    try {
        foreach ($rid in $runtimes) {
            $publishDir = Join-Path $workDir $rid
            # AssemblyVersion を変更しない。反射・JSONを使用するためtrim/single-fileも無効とする。
            & dotnet publish (Join-Path $root 'CvServer\CvServer.csproj') -c Release -r $rid --self-contained true -o $publishDir "-p:FileVersion=$Version" "-p:InformationalVersion=$Version" -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PrintEnable=false
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $rid" }

            # ローカルの認証情報を配布しない。設定は公開用雛形のみを同梱する。
            Get-ChildItem -LiteralPath $publishDir -Filter 'appsettings*.json' | Remove-Item -Force
            $settings = @{
                ServerVersion = $Version
                Database = @{ Provider = 'Sqlite' }
                ConnectionStrings = @{ sqlite = 'server.db' }
                WebAuthJwt = @{
                    SecretKey = ''
                    Issuer = 'CreativeVision10'
                    Audience = 'CreativeVision10'
                    Lifetime = '60'
                    Refreshtime = '1440'
                }
                Kestrel = @{
                    EndpointDefaults = @{ Protocols = 'Http2' }
                    Endpoints = @{ Http = @{ Url = 'http://localhost:5002' } }
                }
                PrintServer = @{
                    UsePrint = $false
                    PrintBaseDir = '.'
                    PrintFormDir = 'printform'
                    PrintOutputDir = 'wrk'
                }
                Diagnostics = @{ EnableDetailedRequestLogging = $false }
                NLog = @{
                    targets = @{ console = @{ type = 'Console' } }
                    rules = @(@{ logger = '*'; minLevel = 'Info'; writeTo = 'console' })
                }
            }
            [IO.File]::WriteAllText((Join-Path $publishDir 'appsettings.json'), (($settings | ConvertTo-Json -Depth 8) -replace '\r?\n', "`r`n") + "`r`n", $utf8)
            Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $publishDir
            # DB、証明書、秘密鍵が意図せず出力された場合は公開ZIPを作らず停止する。
            $privateFiles = @(Get-ChildItem -LiteralPath $publishDir -File -Recurse | Where-Object { $_.Extension -in @('.db', '.sqlite', '.sqlite3', '.pfx', '.p12', '.pem', '.key') })
            if ($privateFiles.Count -gt 0) { throw 'Private data found in publish output. Review the staging directory.' }
            $archive = Join-Path $releaseDir "CvServer-$Version-$rid.zip"
            Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $archive -CompressionLevel Optimal
            Write-Host "[INFO] Created: $archive"
        }
    }
    finally { Pop-Location }
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
finally {
    if ($null -ne $originalPrintProject) {
        [IO.File]::WriteAllBytes($printProject, $originalPrintProject)
    }
}
