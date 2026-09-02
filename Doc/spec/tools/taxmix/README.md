# taxmix — 明細別消費税（軽減税率混在）の実DB検証ハーネス

[2026-08-25_明細別消費税計算_詳細設計.md](../../archive/2026-08-25_明細別消費税計算_詳細設計.md) の 7章「テスト観点」のうち、
**実DBの税率定義・商品の税区分に依存する部分**を確認する開発ツール。
`creativevision10.slnx` には含めない。

計算ロジック自体の回帰テストは `Tests/TestServer/TranTaxRebuildTests.cs`（`TestServer.exe`）にある。
本ツールは「開発DBの実データが期待どおりの税区分になっているか」を見るために使う。

## 使い方

```bash
dotnet run --project taxmix.csproj -- <command> [dbPath]
```

| command | 内容 |
|---|---|
| `inspect` | `MasterSysTax` の定義、税Idごとに解決される税率（サーバ式とクライアント式の一致確認）、検証用商品の `Id_Tax`、`Id_Tax` の分布を表示 |
| `mixed` | 軽減税率(8%)・標準税率(10%)・非課税を混在させた明細で税額計算を突合。切替日前後と返品(金額が負)も確認 |
| `all` | `inspect` → `mixed` |

`dbPath` 省略時は `C:\gitroot\new2022\cv10\CvServer\server-user163.db`。

伝票はDBへ投入せず、実DBの `MasterSysman.Jsub` と `MasterShohin.Id_Tax` を読んでメモリ上で検証する
（`TranTaxRebuildDb.ApplyMeisaiTax` は伝票税額再更新の本体と同じコード）。DBは読み取りのみで変更しない。

## 前提

- `dbPath` は開発用DB。実運用DBには使わない。
- 軽減税率の検証用商品（開発DB `server-user163.db` の実値、いずれも `Id_Tax=2`）

  | Id | Code | 名称 |
  |---:|---|---|
  | 37522 | 20617565001 | 紅茶入り缶 |
  | 37524 | 20617565003 | スターキャンディー入り缶 |
  | 37715 | 20618365001 | スターキャンディ |
  | 37835 | 20619299001 | ａ．オリジナルコーヒー(TO) |
  | 37845 | 20619599001 | ｃａｆｅ（TO) |

## 2026-08-25 実行結果

`inspect`:

| 税Id | TaxRate | DateFrom | TaxNewRate | 解決される税率 |
|---:|---:|---|---:|---:|
| 1 | 8 | 20191001 | 10 | 10%（標準） |
| 2 | 8 | 20191001 | 8 | 8%（軽減） |
| 3 | 15 | 19010101 | 0 | 15%（未使用） |

サーバ側 `TaxRateResolver` とクライアント側 `LogicGetTax` の式は全税Idで一致。
`MasterShohin` の `Id_Tax` 分布は 1:78,749件 / 2:183件（0 と 3 は該当なし）。

`mixed`: 全チェックPASS。混在伝票（軽減8%×2行・標準10%×2行）でヘッダTax=1,720円となり、
全件10%一括計算の1,950円と230円の差が出ることを確認（＝軽減税率が明細ごとに効いている）。
