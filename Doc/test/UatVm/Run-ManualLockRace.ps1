# マニュアル排他制御の「真の同時TryBegin」を、別プロセスからの同時要求で検証する
# (テスト計画書 Doc/test/2026-09-07_マニュアル排他制御_テスト計画.md のE-04・E-02 実プロセス版)。
#
# CvServerは1本のみ動かす(README.md「並列実行はできない」)。排他はSysSequenceの行1本で効くため、
# 同一サーバへ2つの独立したUatVm.exe(manuallockrace)プロセスから同じ壁時計時刻(--fire-at)で
# 同時に要求を投げることで「真の同時TryBegin」の条件を満たす。
#
# 使い方:
#   powershell -NoProfile -File Doc\test\UatVm\Run-ManualLockRace.ps1
#   powershell -NoProfile -File Doc\test\UatVm\Run-ManualLockRace.ps1 -ManageServer:$false -Url http://127.0.0.1:5002
#
# 事前に `dotnet build Doc\test\UatVm\UatVm.csproj` (と -ManageServer 時は
# `dotnet build CvServer\CvServer.csproj`)が済んでいること。
[CmdletBinding()]
param(
	# 接続先CvServerのURL。
	[string]$Url = 'http://127.0.0.1:5002',
	# 発火時刻までの猶予秒数。WPF・gRPCの起動時間にばらつきがあるため、
	# 「起動してすぐ撃つ」ではなく、この秒数だけ先の時刻を両プロセスへ渡して待たせる。
	[int]$LeadSeconds = 40,
	# 自分でCvServerを起動・終了するか。既定はtrue。falseなら既に動作中のCvServerへ接続する
	# (その場合、呼び出し側がCV10_LOCK_SLEEP_BEGIN_MS(S1)を設定済みでCvServerを起動しておくこと)。
	[bool]$ManageServer = $true,
	# S1 (CV10_LOCK_SLEEP_BEGIN_MS)。CvServerの環境変数として渡す(ミリ秒)。既定60秒。
	# S1はManualLockDb.TryBeginが勝者確定直後に呼ぶThread.Sleepで、Vduを前進させずに占有する。
	# この間、後発は確実にTryBeginで弾かれるため、E-02(実行中エラー)の再現に必要。
	[int]$SleepBeginMs = 60000
)

$ErrorActionPreference = 'Stop'
$scriptStart = Get-Date

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$serverDir = Join-Path $repoRoot 'CvServer'
$serverDll = Join-Path $serverDir 'bin\Debug\net10.0\CvServer.dll'
$uatVmExe = Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows10.0.19041\UatVm.exe'
$outDir = Join-Path $PSScriptRoot 'out'
$stopServerScript = Join-Path $PSScriptRoot 'Stop-CvServer.ps1'

if (-not (Test-Path $uatVmExe)) {
	throw "UatVm がビルドされていません。dotnet build Doc\test\UatVm\UatVm.csproj を先に実行してください。 ($uatVmExe)"
}

# CvServerProcess.cs の IsListening と同じ考え方(短いタイムアウトでTCP接続を試みるだけ)。
function Test-PortListening {
	param([int]$Port)
	try {
		$client = New-Object System.Net.Sockets.TcpClient
		$iar = $client.BeginConnect('127.0.0.1', $Port, $null, $null)
		$ok = $iar.AsyncWaitHandle.WaitOne(500, $false)
		if ($ok -and $client.Connected) {
			$client.Close()
			return $true
		}
		$client.Close()
		return $false
	}
	catch {
		return $false
	}
}

# race:結果(VmSession.Note)を読み取る。証跡jsonlは1行1JSONオブジェクト(EvidenceWriter参照)。
function Get-RaceResult {
	param([System.IO.FileInfo]$EvidenceFile)
	if (-not $EvidenceFile) {
		return $null
	}
	$parsed = Get-Content -LiteralPath $EvidenceFile.FullName | ForEach-Object {
		try { $_ | ConvertFrom-Json } catch { $null }
	}
	$note = $parsed | Where-Object { $_ -and $_.kind -eq 'note' -and $_.name -eq 'race:結果' } | Select-Object -Last 1
	if (-not $note) {
		return $null
	}
	return $note.data
}

function Get-LatestEvidence {
	param([string]$Label)
	Get-ChildItem -Path $outDir -Filter "manuallockrace-$Label-*.jsonl" -ErrorAction SilentlyContinue |
		Where-Object { $_.LastWriteTime -ge $scriptStart } |
		Sort-Object LastWriteTime -Descending |
		Select-Object -First 1
}

$port = ([Uri]$Url).Port
$serverProc = $null
$overallOk = $false

try {
	if ($ManageServer) {
		if (-not (Test-Path $serverDll)) {
			throw "CvServer がビルドされていません。dotnet build CvServer\CvServer.csproj を先に実行してください。 ($serverDll)"
		}
		if (Test-PortListening -Port $port) {
			throw "ポート $port は既に使用されています。動作中のCvServerを停止してから実行してください。"
		}

		# S1をCvServerの環境へ確実に渡す。CvServerは静的初期化時に一度だけ環境変数を読むため、
		# 起動より前に、この(親)プロセスの環境変数へ設定しておく必要がある。
		# Start-Processの子プロセスは既定でこのプロセスの環境変数を継承するため、
		# Start-Process側にオプションは無いが、これだけで確実にCvServer側へ渡る。
		$env:CV10_LOCK_SLEEP_BEGIN_MS = "$SleepBeginMs"
		$env:DOTNET_ENVIRONMENT = 'Development'
		$env:ASPNETCORE_ENVIRONMENT = 'Development'
		$env:Kestrel__Endpoints__Http__Url = $Url

		Write-Output "CvServer を起動します。 dll=$serverDll url=$Url S1(CV10_LOCK_SLEEP_BEGIN_MS)=$SleepBeginMs ms"
		$serverProc = Start-Process -FilePath 'dotnet' -ArgumentList "`"$serverDll`"" -WorkingDirectory $serverDir -PassThru

		$deadline = (Get-Date).AddSeconds(120)
		$listening = $false
		while ((Get-Date) -lt $deadline) {
			if ($serverProc.HasExited) {
				throw "CvServer が待ち受け前に終了しました(ExitCode=$($serverProc.ExitCode))。"
			}
			if (Test-PortListening -Port $port) {
				$listening = $true
				break
			}
			Start-Sleep -Milliseconds 300
		}
		if (-not $listening) {
			throw 'CvServer が120秒以内に待ち受けを開始しませんでした。'
		}
		Write-Output "CvServer 待ち受け開始 (PID=$($serverProc.Id))。"
	}

	$fireAt = (Get-Date).AddSeconds($LeadSeconds)
	$fireAtText = $fireAt.ToString('HH:mm:ss')
	Write-Output "発火時刻: $fireAtText (現在時刻 + $LeadSeconds 秒。両プロセスへ同じ値を渡す)"

	# --manage-server は付けない(CvServerは上でこのスクリプトが1本だけ起動済み)。
	$argsA = @('manuallockrace', '--url', $Url, '--fire-at', $fireAtText, '--race-label', 'A', '--hide-views')
	$argsB = @('manuallockrace', '--url', $Url, '--fire-at', $fireAtText, '--race-label', 'B', '--hide-views')

	Write-Output 'プロセスA(請求計算役)とプロセスB(支払計算役)を、別プロセスとして起動します。'
	$procA = Start-Process -FilePath $uatVmExe -ArgumentList $argsA -WorkingDirectory $PSScriptRoot -PassThru
	$procB = Start-Process -FilePath $uatVmExe -ArgumentList $argsB -WorkingDirectory $PSScriptRoot -PassThru

	$procA.WaitForExit()
	$procB.WaitForExit()
	Write-Output "プロセスA終了 ExitCode=$($procA.ExitCode)"
	Write-Output "プロセスB終了 ExitCode=$($procB.ExitCode)"

	$fileA = Get-LatestEvidence -Label 'A'
	$fileB = Get-LatestEvidence -Label 'B'

	$overallOk = $true
	$reasons = @()

	if (-not $fileA) {
		$overallOk = $false
		$reasons += 'プロセスAの証跡jsonlが見つかりません。'
	}
	if (-not $fileB) {
		$overallOk = $false
		$reasons += 'プロセスBの証跡jsonlが見つかりません。'
	}

	$resultA = Get-RaceResult -EvidenceFile $fileA
	$resultB = Get-RaceResult -EvidenceFile $fileB

	if ($fileA -and -not $resultA) {
		$overallOk = $false
		$reasons += "プロセスAの証跡($($fileA.Name))から race:結果 を読み取れません。"
	}
	if ($fileB -and -not $resultB) {
		$overallOk = $false
		$reasons += "プロセスBの証跡($($fileB.Name))から race:結果 を読み取れません。"
	}

	if ($resultA -and $resultB) {
		Write-Output "A: outcome=$($resultA.outcome) afterRows=$($resultA.afterRows) (証跡: $($fileA.Name))"
		Write-Output "B: outcome=$($resultB.outcome) afterRows=$($resultB.afterRows) (証跡: $($fileB.Name))"

		# 収束後の判定その1: 1勝1敗になっていること(ambiguousや両勝ち・両負けは矛盾)。
		$sortedOutcomes = @($resultA.outcome, $resultB.outcome) | Sort-Object
		$isOneWinOneLose = ($sortedOutcomes[0] -eq 'lose') -and ($sortedOutcomes[1] -eq 'win')
		if (-not $isOneWinOneLose) {
			$overallOk = $false
			$reasons += "1勝1敗になっていません(A=$($resultA.outcome), B=$($resultB.outcome))。"
		}

		# 収束後の判定その2: SysSeqType=1の行が最終的に0行または1行であること。
		foreach ($pair in @(@{ Label = 'A'; Result = $resultA }, @{ Label = 'B'; Result = $resultB })) {
			$afterRows = $pair.Result.afterRows
			if (($null -ne $afterRows) -and ([int]$afterRows -gt 1)) {
				$overallOk = $false
				$reasons += "$($pair.Label) の直後SysSeqType=1行数が1行を超えています(afterRows=$afterRows)。"
			}
		}
	}

	# 各プロセス内の矛盾チェック(勝ったのに行が無い/負けたのに行が残っている等)はExitCodeへ反映される
	# (VmSession.Checkの失敗でUatVm.exeがFAIL終了する。README.md「終了コードは0が全PASS、1がFAIL」)。
	if ($procA.ExitCode -ne 0) {
		$overallOk = $false
		$reasons += "プロセスAの終了コードが0ではありません(ExitCode=$($procA.ExitCode))。証跡jsonlのcheck/failを確認してください。"
	}
	if ($procB.ExitCode -ne 0) {
		$overallOk = $false
		$reasons += "プロセスBの終了コードが0ではありません(ExitCode=$($procB.ExitCode))。証跡jsonlのcheck/failを確認してください。"
	}

	if ($overallOk) {
		Write-Output 'VERDICT: PASS'
	}
	else {
		Write-Output 'VERDICT: FAIL'
		foreach ($reason in $reasons) {
			Write-Output "  - $reason"
		}
	}
}
catch {
	Write-Output "ERROR: $($_.Exception.Message)"
	$overallOk = $false
}
finally {
	if ($ManageServer -and $serverProc -and (-not $serverProc.HasExited)) {
		Write-Output 'CvServer を正規終了(Ctrl+C相当)させます。'
		& $stopServerScript -ProcessId $serverProc.Id
	}
}

if ($overallOk) {
	exit 0
}
else {
	exit 1
}
