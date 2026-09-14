<#
.SYNOPSIS
実行中の Visual Studio に接続し、指定ファイルの指定行へブックマークを登録します。

.DESCRIPTION
Visual Studio 2026 の DTE (Development Tools Environment) に接続し、
指定されたファイルを開いて指定行へ移動した後、ブックマークを切り替えます。
ブックマーク名は Visual Studio が自動設定するデフォルト名を使用します。

.PARAMETER FileName
ブックマークを設定するファイル名。相対パス、絶対パスのどちらも指定できます。

.PARAMETER LineNumber
ブックマークを設定する行番号。1以上の整数を指定します。

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Register-VSBookmark.ps1 .\CvBase\UpdateDb.cs 16

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Register-VSBookmark.ps1 .\Doc\spec\2026-08-27_Mini-UAT自動化計画_VM駆動ハーネス.md 22

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Register-VSBookmark.ps1 .\CvBase\DefineDataTable.cs 160
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Register-VSBookmark.ps1 .\CvBase\Share\BaseEnumClass.cs 157

.NOTES
Visual Studio 2026 を起動した状態で実行してください。
同じ行を再度指定すると、Edit.ToggleBookmark の動作によりブックマークが解除されます。
PowerShell 7 から実行する場合も、Visual Studio の DTE 接続のため powershell.exe を使用してください。
#>

param(
	[Parameter(Mandatory = $true, Position = 0)]
	[string]$FileName,

	[Parameter(Mandatory = $true, Position = 1)]
	[ValidateRange(1, [int]::MaxValue)]
	[int]$LineNumber
)

$ErrorActionPreference = "Stop"

# Visual Studio 2026 の実行中インスタンスへ接続
$dte = [Runtime.InteropServices.Marshal]::GetActiveObject(
	"VisualStudio.DTE.18.0"
)

if ([IO.Path]::IsPathRooted($FileName)) {
	$fullPath = [IO.Path]::GetFullPath($FileName)
}
else {
	$fullPath = [IO.Path]::GetFullPath(
		(Join-Path (Get-Location) $FileName)
	)
}

if (-not (Test-Path $fullPath)) {
	throw "ファイルが存在しません: $fullPath"
}

# ファイルを開く
$null = $dte.ItemOperations.OpenFile($fullPath)
Start-Sleep -Milliseconds 300

# DTE上のドキュメントを取得
$document = $null

foreach ($candidate in $dte.Documents) {
	if ([StringComparer]::OrdinalIgnoreCase.Equals(
		$candidate.FullName,
		$fullPath
	)) {
		$document = $candidate
		break
	}
}

if ($null -eq $document) {
	throw "Visual Studio でドキュメントを取得できません: $fullPath"
}

$document.Windows.Item(1).Activate()

$textDocument = $document.Object("TextDocument")

if ($LineNumber -gt $textDocument.EndPoint.Line) {
	throw "行番号が範囲外です。最大行: $($textDocument.EndPoint.Line)"
}

# 指定行へ移動してブックマークを切り替え
$textDocument.Selection.GotoLine($LineNumber)
$dte.ExecuteCommand("Edit.ToggleBookmark")

Write-Host "ブックマークを登録しました: $fullPath : $LineNumber"
