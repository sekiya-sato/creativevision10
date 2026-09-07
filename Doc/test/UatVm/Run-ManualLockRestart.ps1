# マニュアル排他制御「テストケースE-13: CvServer再起動で監視の前回状態が消える」の自動化版。
# (テスト計画書 Doc/test/2026-09-07_マニュアル排他制御_テスト計画.md §4.2 E-13、
#  手動観測手順書 Doc/test/2026-09-07_マニュアル排他制御_手動観測手順.md §6 のシナリオ読み替え)。
#
# UatVm.exe --manage-server はCvServerを子プロセスとして1回起動し、シナリオ終了時に正規終了させる
# 作りで、シナリオの途中で再起動する手段が無い(Doc/test/UatVm/CvServerProcess.cs参照)。
# そこで本スクリプトが manuallockrestart1 → manuallockrestart2 の順に、それぞれ
# --manage-server を付けて2回別プロセスとして起動する。各段でCvServerが起動・正規終了されること
# 自体が「再起動」の実体であり、CvServerProcess.cs は無改修のまま正規の起動・終了経路を2回使うだけ。
#
# 環境変数スイッチS1〜S5は使わない(閾値を触らない。手動観測手順書§6.2のとおりS1/S2/S3/S5は
# 未設定のままにし、閾値15分以上を維持することで、手順の途中で誤って解放されないようにする)。
#
# 使い方:
#   powershell -NoProfile -File Doc\test\UatVm\Run-ManualLockRestart.ps1
#   powershell -NoProfile -File Doc\test\UatVm\Run-ManualLockRestart.ps1 -Url http://127.0.0.1:5002
#
# 事前に `dotnet build Doc\test\UatVm\UatVm.csproj` と `dotnet build CvServer\CvServer.csproj` が
# 済んでいること。また、手動観測手順書§6.2のとおりMasterConfigのcron(S4)を`*/1 * * * *`へ、
# 実行フラグ(S4)を`1`へ変更し、CvServerを一度再起動して反映させておくこと(反映されていないと
# 1段目が「観測できず」でスキップになる)。
[CmdletBinding()]
param(
	# 接続先CvServerのURL。
	[string]$Url = 'http://127.0.0.1:5002',
	# --hide-views を各段に付けるか。既定はtrue(画面を表示しない)。
	[bool]$HideViews = $true
)

$ErrorActionPreference = 'Stop'
$scriptStart = Get-Date

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$serverDir = Join-Path $repoRoot 'CvServer'
$serverDll = Join-Path $serverDir 'bin\Debug\net10.0\CvServer.dll'
$uatVmExe = Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows10.0.19041\UatVm.exe'
$outDir = Join-Path $PSScriptRoot 'out'

if (-not (Test-Path $uatVmExe)) {
	throw "UatVm がビルドされていません。dotnet build Doc\test\UatVm\UatVm.csproj を先に実行してください。 ($uatVmExe)"
}
if (-not (Test-Path $serverDll)) {
	throw "CvServer がビルドされていません。dotnet build CvServer\CvServer.csproj を先に実行してください。 ($serverDll)"
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

# 証跡jsonlは1行1JSONオブジェクト(EvidenceWriter参照)。kind='note'かつ指定nameの最後の1件のdataを返す。
function Get-NoteData {
	param([System.IO.FileInfo]$EvidenceFile, [string]$Name)
	if (-not $EvidenceFile) {
		return $null
	}
	$parsed = Get-Content -LiteralPath $EvidenceFile.FullName -Encoding utf8 | ForEach-Object {
		try { $_ | ConvertFrom-Json } catch { $null }
	}
	$note = $parsed | Where-Object { $_ -and $_.kind -eq 'note' -and $_.name -eq $Name } | Select-Object -Last 1
	if (-not $note) {
		return $null
	}
	return $note.data
}

# kind='check'でnameが指定文字列で始まる行のうち、result='PASS'のものが1件でもあるか。
function Test-CheckPassed {
	param([System.IO.FileInfo]$EvidenceFile, [string]$NamePrefix)
	if (-not $EvidenceFile) {
		return $false
	}
	$parsed = Get-Content -LiteralPath $EvidenceFile.FullName -Encoding utf8 | ForEach-Object {
		try { $_ | ConvertFrom-Json } catch { $null }
	}
	$checks = $parsed | Where-Object { $_ -and $_.kind -eq 'check' -and $_.name -like "$NamePrefix*" }
	# @()で必ず配列にする。Windows PowerShell 5.1 では該当が1件のときパイプラインの結果が
	# スカラーになり .Count が $null になるため、$null -gt 0 で常に偽になってしまう。
	return @($checks | Where-Object { $_.data.result -eq 'PASS' }).Count -gt 0
}

function Get-LatestEvidence {
	param([string]$Label)
	Get-ChildItem -Path $outDir -Filter "$Label-*.jsonl" -ErrorAction SilentlyContinue |
		Where-Object { $_.LastWriteTime -ge $scriptStart } |
		Sort-Object LastWriteTime -Descending |
		Select-Object -First 1
}

$port = ([Uri]$Url).Port
$overallOk = $false
$reasons = @()

$hideViewsArg = @()
if ($HideViews) {
	$hideViewsArg = @('--hide-views')
}

try {
	# --- 1段目: manuallockrestart1 (排他行を作り、2b検知を待つ。CvServerはこの段で起動・正規終了する) ---
	if (Test-PortListening -Port $port) {
		throw "ポート $port は既に使用されています。動作中のCvServerを停止してから実行してください。"
	}

	Write-Output '1段目(manuallockrestart1)を起動します。CvServerはこの段のUatVm.exeが起動・正規終了させます。'
	$args1 = @('manuallockrestart1', '--url', $Url, '--manage-server') + $hideViewsArg
	$proc1 = Start-Process -FilePath $uatVmExe -ArgumentList $args1 -WorkingDirectory $PSScriptRoot -PassThru
	$proc1.WaitForExit()
	Write-Output "1段目(manuallockrestart1) 終了 ExitCode=$($proc1.ExitCode)"

	# 段の間で数秒待ち、ポートが解放されたことを確認してから2段目を起動する
	# (Run-ManualLockRace.ps1と同じ考え方。CvServerの正規終了処理が完了するまでの猶予)。
	$portReleased = $false
	for ($i = 0; $i -lt 20; $i++) {
		if (-not (Test-PortListening -Port $port)) {
			$portReleased = $true
			break
		}
		Start-Sleep -Seconds 1
	}
	if (-not $portReleased) {
		throw "1段目の終了後もポート $port が解放されませんでした。CvServerが正規終了しなかった可能性があります。"
	}
	Start-Sleep -Seconds 2

	# --- 2段目: manuallockrestart2 (新たな2bが出るかを観測する。CvServerはこの段で再度起動・正規終了する) ---
	Write-Output '2段目(manuallockrestart2)を起動します。CvServerを再度起動します(これが「再起動」の実体)。'
	$args2 = @('manuallockrestart2', '--url', $Url, '--manage-server') + $hideViewsArg
	$proc2 = Start-Process -FilePath $uatVmExe -ArgumentList $args2 -WorkingDirectory $PSScriptRoot -PassThru
	$proc2.WaitForExit()
	Write-Output "2段目(manuallockrestart2) 終了 ExitCode=$($proc2.ExitCode)"

	$file1 = Get-LatestEvidence -Label 'manuallockrestart1'
	$file2 = Get-LatestEvidence -Label 'manuallockrestart2'

	$overallOk = $true

	if (-not $file1) {
		$overallOk = $false
		$reasons += '1段目(manuallockrestart1)の証跡jsonlが見つかりません。'
	}
	if (-not $file2) {
		$overallOk = $false
		$reasons += '2段目(manuallockrestart2)の証跡jsonlが見つかりません。'
	}

	# 各段内の矛盾(Check失敗)はExitCodeへ反映される(VmSession.Checkの失敗でUatVm.exeがFAIL終了する)。
	if ($proc1.ExitCode -ne 0) {
		$overallOk = $false
		$reasons += "1段目(manuallockrestart1)の終了コードが0ではありません(ExitCode=$($proc1.ExitCode))。証跡jsonlのcheck/failを確認してください。"
	}
	if ($proc2.ExitCode -ne 0) {
		$overallOk = $false
		$reasons += "2段目(manuallockrestart2)の終了コードが0ではありません(ExitCode=$($proc2.ExitCode))。証跡jsonlのcheck/failを確認してください。"
	}

	if ($file1) {
		$handoff = Get-NoteData -EvidenceFile $file1 -Name 'manuallockrestart1 引き継ぎ情報(2段目用)'
		if ($handoff) {
			Write-Output "1段目: 排他行Id=$($handoff.LockId) 基準点Id=$($handoff.BaselineId) (証跡: $($file1.Name))"
		}
		else {
			Write-Output "1段目: 2b(検知)を観測できずスキップした可能性があります(証跡: $($file1.Name))。"
		}
	}

	# 判定その1: 2段目が「新たな2b(検知)が記録される」をPASSで確認できていること
	# (これが本ケースの想定される結果。設計§3.7の対の規則が再起動を跨ぐと崩れることの確認)。
	$newDetectedPassed = $false
	if ($file2) {
		$newDetectedPassed = Test-CheckPassed -EvidenceFile $file2 -NamePrefix 'manuallockrestart2 CvServer再起動後に新たな2b'
		if ($newDetectedPassed) {
			Write-Output "2段目: 新たな2b(検知)を確認しました(設計の穴を観測。証跡: $($file2.Name))。"
		}
		else {
			Write-Output "2段目: 新たな2b(検知)を観測できませんでした(要追加調査。証跡: $($file2.Name))。"
		}
	}
	if (-not $newDetectedPassed) {
		$overallOk = $false
		$reasons += '2段目で新たな2b(検知)を確認できませんでした。手動観測手順書§6.1の実装事実と食い違うため、'
		$reasons += 'CvServerが正しく再起動されたか(ポート解放待ち・正規終了)、S4(cron・実行フラグ)が反映されていたかを確認してください。'
	}

	if ($overallOk) {
		Write-Output 'VERDICT: PASS'
		Write-Output '  (E-13: 設計書§3.7の対の規則がCvServer再起動を跨ぐと崩れる[2b→2b]ことを観測した。設計の穴として設計書への追記を提案する。)'
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

if ($overallOk) {
	exit 0
}
else {
	exit 1
}
