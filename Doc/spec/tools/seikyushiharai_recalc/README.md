# seikyushiharai_recalc — 請求計算・支払計算の全締日・全期間 一括再計算バッチ

`Doc/spec/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md` 9章「適用手順」のうち、
請求計算（`CalcSummaryUriSei`）・支払計算（`CalcSummaryKaiShi`）を「全締日・全請求/支払月」について
自動で回すバッチツール。

- `creativevision10.slnx` には**含めない**（スタンドアロンのバッチツール）。
- 売掛残（`CalcSummaryUriKake`）・買掛残（`CalcSummaryKaiKake`）はこのツールの対象外。
  日付範囲を1回の呼び出しで指定できるため、画面またはSQL直接実行で別途再計算しておくこと。
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
| `plan`（既定） | 実行対象（締日×月の一覧・件数）を表示するだけで、DBは一切更新しない |
| `run` | 実際に対象すべてに `CalcSummaryUriSei` / `CalcSummaryKaiShi` を実行する |

`run` は各(締日, 月)ごとに個別に実行し、1件失敗しても残りは継続する。終了時に成功/失敗件数と
合計挿入行数を表示し、失敗が1件でもあれば終了コード1を返す。

## 実行結果（2026-09-01、`CvServer/server-user163.db`）

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
