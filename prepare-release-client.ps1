param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
try {
    $expected = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'CvWpfclient\bin\publish-velopack'))
    if ([IO.Path]::GetFullPath($PublishDir) -ne $expected) { throw 'Unexpected client publish directory.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $serverUrls = @()
    foreach ($rid in @('win-x64', 'linux-x64')) {
        $archive = Join-Path $PSScriptRoot "Releases\$Version\CvServer-$Version-$rid.zip"
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $entry = $zip.GetEntry('appsettings.json')
            if ($null -eq $entry) { throw "Server settings missing: $rid" }
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $settings = $reader.ReadToEnd() | ConvertFrom-Json }
            finally { $reader.Dispose() }
            if ($settings.ServerVersion -ne $Version) { throw "Server version mismatch: $rid" }
            $url = [string]$settings.Kestrel.Endpoints.Http.Url
            $uri = $null
            if (-not [uri]::TryCreate($url, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -notin @('http', 'https') -or $uri.UserInfo) {
                throw "Invalid server endpoint: $rid"
            }
            $serverUrls += $url
        }
        finally { $zip.Dispose() }
    }
    if ($serverUrls[0] -ne $serverUrls[1]) { throw 'Windows/Linux server endpoints do not match.' }

    # ソース設定を変えず、パッケージ化前の出力設定をサーバへ合わせる。
    $path = Join-Path $PublishDir 'appsettings.json'
    $content = [IO.File]::ReadAllText($path, $utf8)
    $regex = [regex]::new('("ConnectionStrings"\s*:\s*\{[^}]*?"Url"\s*:\s*")([^"]*)(")')
    if ($regex.Matches($content).Count -ne 1) { throw 'Expected exactly one ConnectionStrings.Url.' }
    $url = $serverUrls[0]
    $content = $regex.Replace($content, [Text.RegularExpressions.MatchEvaluator] {
        param($match)
        $match.Groups[1].Value + $url + $match.Groups[3].Value
    }, 1)
    # ローカル設定に認証情報が追加されていた場合は値を表示せず停止する。
    foreach ($match in [regex]::Matches($content, '"([^"\r\n]*(?:Password|Pass|Secret|ApiKey|ClientId|LoginId|Token|UserId)[^"\r\n]*)"\s*:\s*"([^"]*)"', [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        if ($match.Groups[2].Value -ne '') { throw 'Credential-like value found in client settings.' }
    }
    if ($content -match '(?i)(password|pwd|user id)\s*=') { throw 'Credential-like connection string found.' }
    [IO.File]::WriteAllText($path, $content, $utf8)
    Get-ChildItem -LiteralPath $PublishDir -Filter 'appsettings.*.json' | Remove-Item -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $PublishDir
    $excluded = @(Get-ChildItem -LiteralPath $PublishDir -File -Recurse | Where-Object {
        $_.Name -match '(?i)printstream|ikvm' -or $_.Extension -in @('.db', '.sqlite', '.sqlite3', '.pfx', '.p12', '.pem', '.key')
    })
    if ($excluded.Count -gt 0) { throw 'Private data or printing dependency found in client output.' }
    Write-Host "[INFO] Client endpoint: $url"
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
