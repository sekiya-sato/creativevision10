# seikyushiharai_recalc — 請求・支払・売掛残・買掛残の全期間 一括再計算バッチ

`Doc/spec/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md` 9章「適用手順」のうち、
請求計算（`CalcSummaryUriSei`）・支払計算（`CalcSummaryKaiShi`）を「全締日・全請求/支払月」について、
売掛残（`CalcSummaryUriKake`）・買掛残（`CalcSummaryKaiKake`）を「全期間」について
自動で回すバッチツール。

- `creativevision10.slnx` には**含めない**（スタンドアロンのバッチツール）。
- 売掛残（`CalcSummaryUriKake`）・買掛残（`CalcSummaryKaiKake`）は日付範囲を1回の呼び出しで
  指定できるため締日×月のループを持たない。`kake-plan` / `kake-run` モードで全期間を1回ずつ実行する。
- 対象の締日・請求/支払月は**動的に求める**。得意先/仕入先マスタの締日ごとに、その締日を持つ
  得意先/仕入先の既存 `SummaryUriSei`/`SummaryKaiShi` 行の `DayTo` から対象月(YYYYMM)を逆算する
  （`ClosingMonthCalculator.GetPeriod` の実装上、`DayTo` は必ず対象月と同じ年月になるため）。
  ハードコードした締日・月レンジは持たない。

## 前提

- 実行前に対象DBを必ずバックアップすること（このツール自体はバックアップを取らない）。
- 既定DB: `CvServer/server-user163.db`。`server-*.db` は `.gitignore` 済み。

## 使い方

```bash
dotnet run --project "Doc/spec/tools/seikyushiharai_recalc" -- <mode> [dbPath]
```

または `dotnet build` 後に `seikyushiharai_recalc.exe <mode> [dbPath]`。

| mode | 内容 |
|---|---|
| `plan`（既定） | 請求計算・支払計算の実行対象（締日×月の一覧・件数）を表示するだけで、DBは一切更新しない |
| `run` | 対象すべてに `CalcSummaryUriSei` / `CalcSummaryKaiShi` を実行する |
| `kake-plan` | 売掛残・買掛残の対象期間（年月レンジ）を表示するだけで、DBは一切更新しない |
| `kake-run` | 全期間に対して `CalcSummaryUriKake` / `CalcSummaryKaiKake` を1回ずつ実行する |

`run` は各(締日, 月)ごとに個別に実行し、1件失敗しても残りは継続する。終了時に成功/失敗件数と
合計挿入行数を表示し、失敗が1件でもあれば終了コード1を返す。

`kake-run` の対象期間は動的に求める。下限は期首年月（`MasterSysman.FiscalStartDate`。
`CalcSummary*Kake` 自身が同じ値でクランプするため、これが全期間の下限になる）。
上限は集計元伝票の `KakeDay`（売掛: `Tran00Uriage`/`Tran06Nyukin`、
買掛: `Tran03Shiire`/`Tran02Material`/`Tran07Shiharai`）と既存 `Summary*Kake.DenMonth` の
最大月に、締日が月末でないときの翌月繰り上がりを見込んで1ヶ月足したもの。

`CalcSummary*Kake` の戻り値は DELETE+INSERT の影響行数のため、テーブル実行数の約2倍になる。

## 実行結果1: 請求計算・支払計算（2026-09-01、`CvServer/server-user163.db`）

```
請求計算(CalcSummaryUriSei) 対象: 178 件
  締日= 20  201905〜202609 (89ヶ月)
  締日= 99  201905〜202609 (89ヶ月)

支払計算(CalcSummaryKaiShi) 対象: 89 件
  締日= 99  201905〜202609 (89ヶ月)

成功: 267 件  失敗: 0 件  合計挿入行数: 1089395
```

実行後、`Balance = TotalSales - TotalIn`（買掛・支払は `TotalShiire - TotalOut`）が
全行で一致することと、`Tests/TestServer/bin/Debug/net10.0/TestServer.exe` 264件全成功を
併せて確認済み。

## 実行結果2: 売掛残・買掛残（2026-09-02、`CvServer/server-user163.db`）

本部売上ほか5画面の `Id_Tax` セット漏れ修正
（`Doc/spec/2026-09-02_本部売上入力_消費税セットと画面表示_詳細設計.md` 9.4）に伴い、
「伝票税額再更新」→ `run` → `kake-run` の順で実行した。

```
売掛残(CalcSummaryUriKake) / 買掛残(CalcSummaryKaiKake) 対象期間: 201304〜202609
  （期首年月=201304 / 伝票・既存Summaryの最大月=202608 +1ヶ月）
  OK  売掛残(CalcSummaryUriKake)  201304〜202609     37682 行
  OK  買掛残(CalcSummaryKaiKake)  201304〜202609      4498 行

成功: 2 件  失敗: 0 件  合計挿入行数: 42180
```

実行後の検証:

- 行数は実行前と同一（`SummaryUriKake` 18,841 行 / `SummaryKaiKake` 2,249 行、ともに 201905〜202608）。
  戻り値が行数の2倍なのは DELETE+INSERT の影響行数を返すためで、重複挿入ではない
- `Balance = TotalSales - TotalIn`（`SummaryUriKake`）不一致 0 件
- `Balance = TotalShiire - TotalOut`（`SummaryKaiKake`）不一致 0 件
- `TotalSales = Uriage - Henpin - Nebiki + Sonota + (Tax1+Tax2+Tax3)`（`SummaryUriKake`）不一致 0 件
- `TestServer.exe` 264 件全件成功
