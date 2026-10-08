# UAT用に、CvServer/Services以下の *Service.cs の [UatChangeable] 直後だけを切り替える。
# 認証解除: .\Doc\spec\tools\change4uat_run.ps1
# 認証復元: .\Doc\spec\tools\change4uat_run.ps1 enable_auth
#            .\Doc\spec\tools\change4uat_run.ps1 -enable_auth
# 切り替え後はCvServerを再ビルド・再起動してからテストする。
[CmdletBinding()]
param(
	[Parameter(Position = 0)]
	[ValidateSet('enable_auth')]
	[string]$Mode,
	[switch]$enable_auth
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

# 配置階層・起動時のカレントディレクトリに依存せず、ソリューションのある親を探す。
$repoDirectory = [IO.DirectoryInfo]::new($PSScriptRoot)
while ($null -ne $repoDirectory -and -not (Test-Path -LiteralPath (Join-Path $repoDirectory.FullName 'creativevision10.slnx') -PathType Leaf)) {
	$repoDirectory = $repoDirectory.Parent
}
if ($null -eq $repoDirectory) {
	throw "リポジトリルートが見つかりません: $PSScriptRoot"
}
$repoRoot = $repoDirectory.FullName
$servicesRoot = Join-Path $repoRoot 'CvServer/Services'
if (-not (Test-Path -LiteralPath $servicesRoot -PathType Container)) {
	throw "CvServer/Servicesフォルダが見つかりません: $servicesRoot"
}

$authEnabled = $enable_auth.IsPresent -or $Mode -eq 'enable_auth'
$sourceAttribute = 'Authorize'
$targetAttribute = 'AllowAnonymous'
if ($authEnabled) {
	$sourceAttribute = 'AllowAnonymous'
	$targetAttribute = 'Authorize'
}

# 同じ行、または直後の行にある属性だけを対象にし、コメントや他の属性を挟む箇所は変更しない。
$pattern = '(?m)(^[\uFEFF \t]*\[UatChangeable\][ \t]*(?:\r?\n[ \t]*)?)\[' + $sourceAttribute + '\]'
$replacement = '${1}[' + $targetAttribute + ']'
$utf8 = [System.Text.UTF8Encoding]::new($false, $true)
$changedFiles = 0
$changedMethods = 0

$files = Get-ChildItem -LiteralPath $servicesRoot -Filter '*Service.cs' -File -Recurse |
	Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj|generated)[\\/]' }
foreach ($file in $files) {
	# BOMを文字として保持し、改行を変換せずUTF-8のバイト列へ戻す。
	$source = $utf8.GetString([IO.File]::ReadAllBytes($file.FullName))
	$count = [regex]::Matches($source, $pattern).Count
	if ($count -eq 0) {
		continue
	}

	$updated = [regex]::Replace($source, $pattern, $replacement)
	[IO.File]::WriteAllBytes($file.FullName, $utf8.GetBytes($updated))
	$changedFiles++
	$changedMethods += $count
	$relativePath = $file.FullName.Substring($repoRoot.Length + 1)
	Write-Output ("{0}: {1}箇所を[{2}]に変更" -f $relativePath, $count, $targetAttribute)
}

Write-Output ("完了: {0}ファイル、{1}箇所。対象属性: [{2}]" -f $changedFiles, $changedMethods, $targetAttribute)
