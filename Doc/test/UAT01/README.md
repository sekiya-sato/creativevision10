# UAT-01 実行ソース

## ファイル

- `UAT01Runner.csproj` / `Program.cs`：専用マスタ・発注・仕入・返品・支払の投入、WriteEffectRunner実行、SummaryDb再計算、期待値検証
- `ReportRunner.csproj` / `ReportRunner.cs`：localhostのCvServerへ接続し、買掛・支払台帳・月別支払予定表をPDF生成
- `Verify-UAT01.sql`：sqlite3の`-readonly`モードで実行するDB検算SQL

## 実行順

1. `UAT01Runner`を実行する。
2. `Verify-UAT01.sql`でDB値を照合する。
3. CvServerを`CvServer`フォルダ起点でlocalhost限定起動する。
4. `ReportRunner`を実行して帳票PDFを生成する。
5. PDFを目視確認する。

詳細は `Doc/test/UAT01_再テスト手順.md` を参照する。

## 注意

このソースは開発用DB専用である。同じ専用コードで再実行する場合は、テスト前バックアップへ戻す。派生SKUの補正処理は、`CvBase/BaseDbDerived.cs` の既存不具合を回避するテスト専用処理であり、製品コードの修正を代替しない。
