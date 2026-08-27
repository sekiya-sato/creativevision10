# CvServer を Ctrl+C 相当（graceful shutdown）で終了する。
#
# taskkill による強制終了は、Kestrel の停止処理と SQLite の WAL 後始末を飛ばしてしまう。
# 人の操作と同じく CTRL_C_EVENT を送り、正規の終了経路を通す。
#
#   powershell -NoProfile -File Doc\test\UatVm\Stop-CvServer.ps1
#   powershell -NoProfile -File Doc\test\UatVm\Stop-CvServer.ps1 -ProcessId 12345
[CmdletBinding()]
param(
	# 対象プロセスID。省略時は CvServer.dll を実行している dotnet プロセスを探す。
	[int]$ProcessId = 0,
	# 終了を待つ秒数。
	[int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

if ($ProcessId -eq 0) {
	$found = Get-CimInstance Win32_Process -Filter "Name='dotnet.exe' OR Name='CvServer.exe'" |
		Where-Object { $_.CommandLine -like '*CvServer*' -and $_.CommandLine -notlike '*MSBuild*' }
	if (-not $found) {
		Write-Output 'CvServer は動作していません。'
		exit 0
	}
	if ($found.Count -gt 1) {
		Write-Output "CvServer が複数見つかりました: $($found.ProcessId -join ', ')"
	}
	$ProcessId = @($found)[0].ProcessId
}

Write-Output "CvServer (PID $ProcessId) へ Ctrl+C を送ります。"

# Windows PowerShell 5.1 では here-string の解釈に失敗することがあるため、配列を結合して渡す。
$signature = @(
	'[DllImport("kernel32.dll", SetLastError = true)] public static extern bool AttachConsole(uint dwProcessId);',
	'[DllImport("kernel32.dll", SetLastError = true)] public static extern bool FreeConsole();',
	'[DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);',
	'[DllImport("kernel32.dll", SetLastError = true)] public static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);'
) -join [Environment]::NewLine

Add-Type -Namespace Cv -Name ConsoleCtrl -MemberDefinition $signature

$CTRL_C_EVENT = 0

# 自分のコンソールを一旦切り離し、対象のコンソールへ入って Ctrl+C を送る。
[void][Cv.ConsoleCtrl]::FreeConsole()
$attached = [Cv.ConsoleCtrl]::AttachConsole([uint32]$ProcessId)
if (-not $attached) {
	Write-Output '対象のコンソールへ接続できませんでした。別セッションで起動している可能性があります。'
	exit 1
}
try {
	# 送った Ctrl+C で自分が落ちないように、自分側のハンドラを無効化しておく。
	[void][Cv.ConsoleCtrl]::SetConsoleCtrlHandler([IntPtr]::Zero, $true)
	$sent = [Cv.ConsoleCtrl]::GenerateConsoleCtrlEvent([uint32]$CTRL_C_EVENT, 0)
}
finally {
	[void][Cv.ConsoleCtrl]::FreeConsole()
	[void][Cv.ConsoleCtrl]::SetConsoleCtrlHandler([IntPtr]::Zero, $false)
}

if (-not $sent) {
	Write-Output 'Ctrl+C の送信に失敗しました。'
	exit 1
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
	if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
		Write-Output "CvServer (PID $ProcessId) は正常終了しました。"
		exit 0
	}
	Start-Sleep -Milliseconds 300
}

Write-Output "$TimeoutSeconds 秒待っても終了しませんでした (PID $ProcessId)。"
exit 1
