param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.9.0'
)
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $releaseDir = Join-Path $PSScriptRoot "Releases\$Version"
    $names = @("CvServer-$Version-win-x64.zip", "CvServer-$Version-linux-x64.zip", "CreativeVision10-$Version-win-x64-Portable.zip")
    $urls = @()
    foreach ($name in $names) {
        $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $releaseDir $name))
        try {
            $private = @($zip.Entries | Where-Object {
                $_.FullName -match '(?i)printstream|ikvm|\.(db|sqlite|sqlite3|pfx|p12|pem|key)$'
            })
            if ($private.Count -gt 0) { throw "Private data or printing dependency found: $name" }
            $configs = @($zip.Entries | Where-Object { $_.FullName -match '(^|/)appsettings[^/]*\.json$' })
            if ($configs.Count -ne 1 -or $configs[0].Name -ne 'appsettings.json') { throw "Unexpected settings files: $name" }
            $reader = [IO.StreamReader]::new($configs[0].Open())
            try { $content = $reader.ReadToEnd() }
            finally { $reader.Dispose() }
            foreach ($match in [regex]::Matches($content, '"([^"\r\n]*(?:Password|Pass|Secret|ApiKey|ClientId|LoginId|Token|UserId)[^"\r\n]*)"\s*:\s*"([^"]*)"', [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                if ($match.Groups[2].Value -ne '') { throw "Credential-like value found: $name" }
            }
            if ($name.StartsWith('CvServer-')) {
                $settings = $content | ConvertFrom-Json
                if ($settings.ServerVersion -ne $Version -or $settings.WebAuthJwt.SecretKey -ne '' -or $settings.ConnectionStrings.sqlite -ne 'server.db') { throw "Server settings mismatch: $name" }
                $urls += [string]$settings.Kestrel.Endpoints.Http.Url
            }
            else {
                $actualVersion = [regex]::Match($content, '"Application"\s*:\s*\{[^}]*?"Version"\s*:\s*"([^"]*)"').Groups[1].Value
                if ($actualVersion -ne $Version) { throw 'Client version mismatch.' }
                $urls += [regex]::Match($content, '"ConnectionStrings"\s*:\s*\{[^}]*?"Url"\s*:\s*"([^"]*)"').Groups[1].Value
                if ($content -match '"FeedUrl"\s*:\s*"[^"]+"') { throw 'Update feed must be unset.' }
            }
            if ($content -match '(?i)(password|pwd|user id)\s*=') { throw "Credential-like connection string found: $name" }
            if ($null -eq $zip.GetEntry('LICENSE') -and $null -eq $zip.GetEntry('current/LICENSE')) { throw "License missing: $name" }
            Write-Host "[INFO] Verified: $name"
        }
        finally { $zip.Dispose() }
    }
    if (-not $urls[0] -or $urls[0] -ne $urls[1] -or $urls[0] -ne $urls[2]) { throw 'Server/client endpoints do not match.' }
    $lines = foreach ($name in $names) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath (Join-Path $releaseDir $name) -Algorithm SHA256).Hash.ToLowerInvariant(), $name
    }
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), [string[]]$lines, $utf8)
    $notes = Join-Path $PSScriptRoot "Doc\spec\リリースノート_$Version.md"
    if (Test-Path -LiteralPath $notes) { Copy-Item -LiteralPath $notes -Destination (Join-Path $releaseDir 'release-notes.md') }
    Write-Host "[INFO] Verified endpoint: $($urls[0])"
    Write-Host "[INFO] Created SHA256SUMS.txt"
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
