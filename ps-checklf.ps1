param(
    [Parameter(Mandatory = $true)]
    [string]$Folder,

    [switch]$SummaryOnly
)

if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
    Write-Error "フォルダが存在しません: $Folder"
    exit 1
}

$files = Get-ChildItem -LiteralPath $Folder -Filter *.qfm -File -Recurse

$results = foreach ($file in $files) {

    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)

    $crlf = 0
    $lf   = 0
    $cr   = 0

    for ($i = 0; $i -lt $bytes.Length; $i++) {

        if ($bytes[$i] -eq 13) {
            if (($i + 1) -lt $bytes.Length -and $bytes[$i + 1] -eq 10) {
                $crlf++
                $i++
            }
            else {
                $cr++
            }
        }
        elseif ($bytes[$i] -eq 10) {
            $lf++
        }
    }

    $type =
        if ($crlf -gt 0 -and $lf -eq 0 -and $cr -eq 0) {
            "CRLF"
        }
        elseif ($lf -gt 0 -and $crlf -eq 0 -and $cr -eq 0) {
            "LF"
        }
        elseif ($cr -gt 0 -and $crlf -eq 0 -and $lf -eq 0) {
            "CR"
        }
        elseif ($crlf -eq 0 -and $lf -eq 0 -and $cr -eq 0) {
            "NoNewline"
        }
        else {
            "Mixed"
        }

    [PSCustomObject]@{
        File    = $file.FullName
        NewLine = $type
        CRLF    = $crlf
        LF      = $lf
        CR      = $cr
    }
}

if (-not $SummaryOnly) {
    $results | Format-Table -AutoSize
    Write-Host ""
}

Write-Host "===== Summary ====="
Write-Host ("Total     : {0}" -f $results.Count)
Write-Host ("CRLF      : {0}" -f @($results | Where-Object NewLine -eq "CRLF").Count)
Write-Host ("LF        : {0}" -f @($results | Where-Object NewLine -eq "LF").Count)
Write-Host ("CR        : {0}" -f @($results | Where-Object NewLine -eq "CR").Count)
Write-Host ("Mixed     : {0}" -f @($results | Where-Object NewLine -eq "Mixed").Count)
Write-Host ("NoNewline : {0}" -f @($results | Where-Object NewLine -eq "NoNewline").Count)
