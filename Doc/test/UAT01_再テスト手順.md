# UAT-01 再テスト手順

## 1. 目的

開発用DB `CvServer/server-user163.db` に専用データを投入し、発注から仕入、仕入返品、買掛、支払、集計、帳票までを再実行する。初回結果の詳細は `UAT01_20260821_テスト結果レポート.md` を参照する。

実行プログラムは `Doc/test/UAT01/` に保存している。

## 2. 前提・注意

- 実行対象は開発用DBだけに限定する。
- UAT専用コードは `UAT01-SI`、`UAT01-SK`、`UAT01-P01` である。
- 同じDBへそのまま再実行すると重複するため、バックアップへ戻すかコード定数を変更する。
- DBを書き換える前に、CvServer、CvWpfclient、SQLite接続ツールを停止する。
- CvWpfclientを起動する場合は、必ず `CvWpfclient` フォルダをカレントディレクトリにする。プロジェクトフォルダ以外から起動しない。
- 税率は `MasterSysman` の税率1にある `TaxNewRate` を使用する。2026/09の期待値は10%である。

## 3. 初回実行または再実行前のバックアップ

PowerShellでリポジトリルートから実行する。

```powershell
$repo = 'C:\gitroot\documents\new2022\cv10'
$db = Join-Path $repo 'CvServer\server-user163.db'
$backup = Join-Path $repo 'refer\back\UAT-01_20260821_1c873e92_server-user163.db'
```

初回実行前は、対象DBを別名でも退避してからテスト前バックアップを配置する。

```powershell
$beforeRerun = Join-Path $repo 'refer\back\UAT-01_before_rerun_server-user163.db'
Copy-Item -LiteralPath $db -Destination $beforeRerun
```

既存のUATデータを消して初回状態へ戻す場合は、CvServer停止を確認したうえで、WAL/SHMを退避してからDB本体を復元する。

```powershell
$wal = "$db-wal"
$shm = "$db-shm"
$sidecarBackup = Join-Path $repo 'refer\back\UAT-01_before_rerun_sidecars'
New-Item -ItemType Directory -Force -Path $sidecarBackup | Out-Null
if (Test-Path -LiteralPath $wal) { Move-Item -LiteralPath $wal -Destination (Join-Path $sidecarBackup 'server-user163.db-wal') }
if (Test-Path -LiteralPath $shm) { Move-Item -LiteralPath $shm -Destination (Join-Path $sidecarBackup 'server-user163.db-shm') }
Copy-Item -LiteralPath $backup -Destination $db -Force
```

復元はDBを初期化する操作である。対象パスが `CvServer/server-user163.db` であることを確認してから実行する。

## 4. DB投入・業務計算の実行

リポジトリルートから、保存したプロジェクトをビルドする。

```powershell
Set-Location $repo
C:\gitroot\UT\vscmd.bat dotnet build Doc\test\UAT01\UAT01Runner.csproj
```

次に、対象DBの絶対パスを引数にして実行する。

```powershell
dotnet run --project Doc\test\UAT01\UAT01Runner.csproj --no-build -- $db
```

標準出力で次を確認する。

- `MasterSysman tax1 current new rate is 10 percent`
- 発注Aの仕入4で発注残6、仕入6で自動完了
- 発注Bの部分仕入、手動完了、手動完了解除、残り仕入
- 返品後の在庫14
- 買掛・支払の集計値
- 最後の `accounts payable and payment calculation are idempotent`

派生SKUの列順不整合が残っている場合、プログラムは`WARN repaired dedicated derived SKU after production SQL column-order mismatch`を出して、テスト専用SKUだけを明示列INSERTで補正する。この補正は製品ソースの修正ではない。

## 5. SQLite読み取り検算

DB投入後、書き込みプログラムを終了してから読み取り専用SQLを実行する。

```powershell
& C:\gitroot\UT\sqlite3.exe -readonly -header -column -- $db ".read Doc/test/UAT01/Verify-UAT01.sql"
```

`Verify-UAT01.sql` の期待値は次のとおり。

|項目|期待値|
|---|---:|
|税率1 `TaxNewRate`|10|
|発注A/B `EndFlag`|1 / 1|
|仕入合計|15,000|
|返品|1,000、`CalcFlag=-1`|
|在庫|14|
|買掛 `TotalShiire`|15,400|
|買掛 `TotalOut`|5,000|
|買掛 `Tax`|1,400|
|買掛 `Balance`|-10,400|
|支払予定日|20260930|

## 6. 帳票PDFの再生成

### 6.1 CvServerのビルドと起動

```powershell
Set-Location $repo
C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj
Set-Location (Join-Path $repo 'CvServer')
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:Kestrel__Endpoints__Http__Url = 'http://127.0.0.1:5002'
$env:Kestrel__Endpoints__HttpsDefaultCert__Url = 'http://127.0.0.1:5012'
dotnet .\bin\Debug\net10.0\CvServer.dll
```

CvServerは必ず `CvServer` フォルダをカレントディレクトリとして起動する。これにより、対象DBは `CvServer/server-user163.db` になる。`0.0.0.0` ではなくlocalhost限定で起動する。

### 6.2 帳票クライアントの実行

別のPowerShellでリポジトリルートから実行する。

```powershell
Set-Location $repo
C:\gitroot\UT\vscmd.bat dotnet build Doc\test\UAT01\ReportRunner.csproj
dotnet run --project Doc\test\UAT01\ReportRunner.csproj --no-build
```

3帳票の完了メッセージと、CvServerが出力した`CvServer/wrk/<timestamp>/outfile*.pdf`を確認する。

### 6.3 PDF目視確認

Popplerの`pdftoppm.exe`でPDFをPNG化し、次を確認する。

- 買掛金管理表：仕入、税、返品、支払、残高
- 支払台帳：支払日、支払額、残高、支払予定日
- 月別支払予定表：予定年月、予定日、仕入先、予定額

確認後は、今回生成した`CvServer/wrk/<timestamp>`だけを削除してよい。既存の他の作業ディレクトリは削除しない。

## 7. CvWpfclientで画面確認する場合

画面確認を追加で行う場合は、CvServerを起動した状態で、別PowerShellを次のように操作する。

```powershell
Set-Location (Join-Path $repo 'CvWpfclient')
C:\gitroot\UT\vscmd.bat dotnet run --project .\CvWpfclient.csproj
```

起動元がリポジトリルートや`CvServer`の場合、相対パスの解決に失敗する可能性がある。既存のCreativeVision10.exeが動作中なら、競合を避けて停止・上書きせず、画面確認を見送る。

## 8. 終了・復旧

- UATデータを残す場合：CvServerを停止し、結果レポートとDBバックアップの場所を記録する。
- 初期状態へ戻す場合：3章の復元手順を使用し、WAL/SHMが残っていないことを確認する。
- 製品ソースの派生SKU不具合を修正した後は、テスト専用補正が不要になることを確認して、商品登録からUAT-01を再実施する。
