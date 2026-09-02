# Summary残高の期間集計化と PreviousBalance 詳細設計（2026-09-02）

状態: **実装完了（2026-09-01、Step 1〜9すべて完了。詳細は12章末尾のステータスと10章の受入条件を参照）**。

## 1. 目的

`SummaryUriKake`（売掛）/ `SummaryUriSei`（請求）/ `SummaryKaiKake`（買掛）/ `SummaryKaiShi`（支払）の
全列を **「対象年月・対象締期間の集計値のみ」** に統一する。繰越（前期からの積み上げ）を
テーブルに持たせるのをやめ、帳票が必要とする前残は読み出し時に SQL で算出する。

あわせて区分99（その他売上／その他仕入）の `Sonota` 列を4テーブルに揃える。

### 1.1 現行の問題

1. **繰越がテーブルに焼き込まれている**
   - 売掛・買掛: 前月行の `Balance` 列を起点にウィンドウ関数で積み上げ（`SummaryDb.cs:951-960, 969-973`）
   - 請求・支払: 過去全行の `SUM(TotalIn - TotalSales)` を起点に加算（`SummaryDb.cs:1086-1091, 1134`）
   - このため過去月を1件訂正すると以降の全期間を再計算する必要があり、
     `ExtendToMonth`（`SummaryDb.cs:791-816`）が再計算範囲を伝票の最大月まで自動拡張していた。
2. **繰越の引き継ぎ方が2方式に分裂**しており、期首残高CSVは
   `Balance` 列と `TotalIn`/`TotalSales` の両方を矛盾なく埋める必要があった（`OpeningBalanceCsv.cs:262-279`）。
3. **帳票側の前残の作り方も2方式**に分裂している。
   - 逆算方式: `Balance + TotalSales - TotalIn`（請求一覧表・請求書印刷・支払残高明細書）
   - 前月行直読み方式: `SELECT Balance FROM SummaryUriKake WHERE DenMonth = 前月`（売掛金管理表・買掛金管理表）
4. **区分99の扱いが4テーブルで非対称**（`SummaryUriSei` のみ `Sonota` 分離、他は畳み込みまたは欠落）。

## 2. 決定事項

### 2.1 算式（4テーブル共通）

売掛・請求（債権側）:

```
TotalSales      = Uriage - Henpin - Nebiki + Sonota + Tax1 + Tax2 + Tax3
TotalIn         = Cash + Fee + Densai + Offset + Other
Balance         = TotalSales - TotalIn
```

買掛・支払（債務側）:

```
TotalShiire     = Shiire - Henpin - Nebiki + Sonota + Tax1 + Tax2 + Tax3
TotalOut        = Cash + Fee + Densai + Offset + Other
Balance         = TotalShiire - TotalOut
```

- 内訳（`Uriage`/`Shiire`/`Henpin`/`Nebiki`/`Sonota`）は**税抜**で積む（全体設計 3.8 を継承）。
- `TaxableAmount1/2/3` は税率別内訳の参考値であり `TotalSales`/`TotalShiire` には含めない（従来どおり）。
- **すべて対象期間内の値のみ**。前期の値は一切混ぜない。

### 2.2 符号規約（変更）

| | 旧 | 新 |
|---|---|---|
| `Balance`（売掛・請求） | `前残 + 入金 - 売上` → **負＝未回収** | `TotalSales - TotalIn` → **正＝未回収** |
| `Balance`（買掛・支払） | `前残 + 支払 - 仕入` → **負＝未払** | `TotalShiire - TotalOut` → **正＝未払** |

期首残高CSVの外部表現（正数＝未回収残）と内部表現の符号が**一致する**ようになる。

### 2.3 PreviousBalance（表示専用・DB非実体）

4テーブルに `[ResultColumn]` の表示専用プロパティを追加する。DDL生成器は
`ResultColumnAttribute` を持つプロパティをスキップする（`ExDatabase.cs:157-158`）ため物理列は作られない。
NPoco の `Insert`/`Update` からも除外されるので期首残高の投入経路（`OpeningBalanceDb.Replace`）に影響はない。

```csharp
	/// <summary>
	/// 表示専用項目
	/// </summary>
	[ObservableProperty]
	[ResultColumn]
	public partial long PreviousBalance { get; set; } = 0;
```

値は帳票・画面側の SQL で都度算出する。標準形は以下。

```sql
-- 請求（SummaryUriSei）: 締期間キー
previousBalance AS (
    SELECT Id_Tokui, SUM(TotalSales - TotalIn) AS PreviousBalance
    FROM SummaryUriSei WHERE DayTo < @dayFrom GROUP BY Id_Tokui
)
-- 売掛（SummaryUriKake）: 年月キー
previousBalance AS (
    SELECT Id_Tokui, SUM(TotalSales - TotalIn) AS PreviousBalance
    FROM SummaryUriKake WHERE DenMonth < @denMonth GROUP BY Id_Tokui
)
-- 支払（SummaryKaiShi）/ 買掛（SummaryKaiKake）
    SUM(TotalShiire - TotalOut) AS PreviousBalance
```

`SUM(TotalSales - TotalIn)` は `SUM(Balance)` と同値だが、`Balance` 列の再計算漏れに左右されないよう
**内訳合計から積む式を正とする**。

当月末残高が必要な帳票は `PreviousBalance + Balance` で求める。

## 3. データモデル変更

### 3.1 `CvBase/BaseDbKake.cs`

| テーブル | 追加列 | 備考 |
|---|---|---|
| `SummaryUriKake` | `Sonota`（物理）、`PreviousBalance`（ResultColumn） | Sonota は 26_09_02_01 で追加済み |
| `SummaryUriSei` | `PreviousBalance`（ResultColumn） | Sonota は既存 |
| `SummaryKaiKake` | `Sonota`（物理）、`PreviousBalance`（ResultColumn） | Sonota は 26_09_02_01 で追加済み |
| `SummaryKaiShi` | `Sonota`（物理）、`PreviousBalance`（ResultColumn） | Sonota は 26_09_02_01 で追加済み |

`PreviousBalance` は `Balance` の直前に置く。

### 3.2 `CvBase/UpdateDb.cs`

`26_09_02_01`（Sonota 3列追加）は適用済みとして残し、残高の符号・意味変更に追随する
データ移行を `26_09_02_02` として追加する。

```sql
UPDATE SummaryUriKake SET Balance = TotalSales - TotalIn;
UPDATE SummaryUriSei  SET Balance = TotalSales - TotalIn;
UPDATE SummaryKaiKake SET Balance = TotalShiire - TotalOut;
UPDATE SummaryKaiShi  SET Balance = TotalShiire - TotalOut;
```

- `TotalSales`/`TotalIn` は元々「当期間分のみ」なので、この UPDATE だけで
  既存行が新仕様（当期間ネット・正＝未回収）へ移行できる。
- ただし `Sonota` の分離は再計算しないと反映されないため、
  **適用手順で売掛・買掛・請求・支払の全期間再計算を必須とする**（第9章）。

## 4. 再計算ロジックの変更（`CvDomainLogic/SummaryDb.cs`）

### 4.1 共通（4メソッド）

| 削除するもの | 現行位置 |
|---|---|
| `previousBalance` CTE（売掛） | `SummaryDb.cs:951-960` |
| `previousBalance` CTE（請求） | `SummaryDb.cs:1086-1091` |
| `previousBalance` CTE（支払） | `SummaryDb.cs:1275-1280` |
| `previousBalance` CTE（買掛） | `SummaryDb.cs:1479-1488` |
| ウィンドウ関数による繰越積み上げ（売掛） | `SummaryDb.cs:969-973` |
| ウィンドウ関数による繰越積み上げ（買掛） | `SummaryDb.cs:1497-1501` |
| `ExtendToMonth` メソッドと呼び出し | `SummaryDb.cs:791-816` / `860` / `1366` |
| 上記に対応する `LEFT JOIN previousBalance` | `SummaryDb.cs:993, 1142, 1327, 1521` |

Balance の INSERT 式は次に置き換える。

```
-- 売掛・請求
c.Uriage - c.Henpin - c.Nebiki + c.Sonota + (c.Tax1 + c.Tax2 + c.Tax3)
  - (c.Cash + c.Fee + c.Densai + c.Offset + c.Other) AS Balance
-- 買掛・支払
c.Shiire - c.Henpin - c.Nebiki + c.Sonota + (c.Tax1 + c.Tax2 + c.Tax3)
  - (c.Cash + c.Fee + c.Densai + c.Offset + c.Other) AS Balance
```

`ExtendToMonth` の削除により、売掛・買掛の再計算は**指定した年月範囲のみ**を
DELETE→INSERT する。以降の月は影響を受けない。

### 4.2 期首日の凍結ガード

`GetFiscalStartDate()` によるガードは**残す**が、意味が変わる。

- 旧: 期首前の行は「繰越の起点」なので絶対に触らない
- 新: 期首前は伝票が移行されていないため集計しない、というだけ。
  期首残高行は「期首直前の1期間の実績行」として置かれ、`PreviousBalance` の SUM に自然に含まれる。

ガードの実装（範囲の切り上げ・早期 return）は現行のまま変更しない。

### 4.3 区分99（`Sonota`）の扱い

| メソッド | 伝票 | 現行 | 新 |
|---|---|---|---|
| `CalcSummaryUriKake` | `Tran00Uriage` | `Kubun=99` を `Uriage` へ畳み込み（`SummaryDb.cs:881`） | `Sonota` へ分離 |
| `CalcSummaryUriSei` | `Tran00Uriage` | `Sonota` へ分離（`SummaryDb.cs:1060`） | 変更なし |
| `CalcSummaryKaiKake` | `Tran03Shiire` | `Kubun=99` を `Shiire` へ畳み込み（`SummaryDb.cs:1388`） | `Sonota` へ分離 |
| `CalcSummaryKaiKake` | `Tran02Material` | `Sonota99` として丸めず `Tax1` へ加算（A-6、`SummaryDb.cs:1375`） | **A-6 を維持** |
| `CalcSummaryKaiShi` | `Tran03Shiire` | **どこにも入らず欠落**（`SummaryDb.cs:1235, 1239`） | `Sonota` へ分離（欠落を修正） |
| `CalcSummaryKaiShi` | `Tran02Material` | `Sonota99` として丸めず `Tax1` へ加算（A-6、`SummaryDb.cs:1214`） | **A-6 を維持** |

- `Tran02Material` の区分99 は「生地・付属の税調整目的の伝票」であり、
  金額を消費税へ全額振り替える旧CVnet由来の特殊処理（A-6）。`Sonota` とは別物として扱い、
  `Sonota` へは積まない（積むと二重計上になる）。
- `CalcSummaryKaiShi` で `Tran03Shiire` の区分99 が完全に欠落していたのは
  買掛（`CalcSummaryKaiKake`）との不整合であり、本改修で解消する。

## 5. 帳票・画面の改修

### 5.1 前残の算出方式を `PreviousBalance` へ統一

| file:line | 帳票 | 現行式 | 新 |
|---|---|---|---|
| `SeikyuListReportViewModel.cs:83` | 請求一覧表 | `s.Balance + s.TotalSales - s.TotalIn` | `previousBalance` CTE を JOIN |
| `SeikyuBalanceDetailViewModel.cs:96` | 請求書印刷 | 同上 | 同上 |
| `ShiharaiBalanceDetailViewModel.cs:78` | 支払残高明細書 | `k.Balance - k.TotalShiire + k.TotalOut` | 同上（`TotalShiire - TotalOut` 版） |
| `UrikakeBalanceReportViewModel.cs:64-65,70` | 売掛金管理表 | 前月行の `Balance` 直読み | `previousBalance` CTE（`DenMonth < 対象月`）へ差し替え |
| `KaikakeBalanceReportViewModel.cs:64-65,70` | 買掛金管理表 | 同上 | 同上 |

売掛金管理表・買掛金管理表は「前月行の `Balance`」→「前月までの累計」に変わる。
前月に行が無い得意先でも前々月以前の残が正しく出るようになる（現行は 0 になっていた）。

### 5.2 `Balance` を「当月残高」として表示している帳票

新仕様の `Balance` は当期間ネットなので、繰越込みの当月残高は `PreviousBalance + Balance` に直す。

| file:line | 帳票 | 対応 |
|---|---|---|
| `SeikyuListReportViewModel.cs:84` | 請求一覧表・当月残 | `pb.PreviousBalance + s.Balance AS balance` |
| `SeikyuListReportViewModel.cs:90` | 請求一覧表・繰越金額 | `pb.PreviousBalance - s.TotalIn AS carryOver` |
| `SeikyuBalanceDetailViewModel.cs:102` | 請求書印刷・当月残高 | `pb.PreviousBalance + s.Balance` |
| `SeikyuBalanceDetailViewModel.cs:187` | 請求書印刷 item39 | `h.prevBalance - h.totalIn`（式は不変、prevBalance の出所のみ変更） |
| `SeikyuLedgerReportViewModel.cs:70` | 請求台帳 | `pb.PreviousBalance + u.Balance AS balance` |
| `ShiharaiLedgerReportViewModel.cs:72` | 支払台帳 | `pb.PreviousBalance + k.Balance AS balance` |
| `ShiharaiListReportViewModel.cs:69` | 支払一覧表 | 同上 |
| `ShiharaiBalanceDetailViewModel.cs:82` | 支払残高明細書 | 同上 |
| `UrikakeBalanceReportViewModel.cs:78` | 売掛金管理表 | `pb.PreviousBalance + c.Balance AS balance` |
| `KaikakeBalanceReportViewModel.cs:78` | 買掛金管理表 | 同上 |
| `MonthlyNyukinYoteiTableViewModel.cs:92,116` | 月別入金予定表 | 予定金額は当月末残高なので `PreviousBalance + Balance` |
| `MonthlyShiharaiYoteiTableViewModel.cs:94,118` | 月別支払予定表 | 同上 |

`IsActiveOnly` の絞り込み条件（`Balance != 0` 等）も、当月残高ベースの判定へ合わせて見直す。

### 5.3 影響なし

- `TokuiLedgerViewModel`（得意先元帳）/ `ShiireLedgerViewModel`（仕入先元帳）: 伝票直読みで Summary を使わない
- `BillingCalculationViewModel` / `PaymentCalculationViewModel` / `StockKakeUpdateViewModel`: 再計算の起動のみ
- `BaseMatchingViewModel` および消込系: 残高列を参照しない
- `UriageCashTypeReportViewModel`: `SummaryUriKake` 型を運搬用DTOとして流用しているだけで実データは `Tran01Tenuri`
- `QueryMsgStreamService`: ディスパッチのみ
- `SummaryRebuildClosingCheck`: `DayTo`/`Shime1` のみ参照

## 6. 期首残高（`CvBase/OpeningBalanceCsv.cs` / `CvDomainLogic/OpeningBalanceDb.cs`）

- `CreateRecord` の `var balance = credit - debit;`（`OpeningBalanceCsv.cs:872`）を
  `var balance = debit - credit;` に変更（正＝未回収）。
- 「繰越の引き継ぎ方が2方式あるので Balance 列と合計列の双方を必ず埋める」という
  クラスコメント（`OpeningBalanceCsv.cs:262-279`）と `CreateRecord` の注記を、
  「4テーブル共通で `Balance = DebitTotal - CreditTotal` を満たす1期間分の実績行」という説明へ書き換える。
- `OpeningBalanceOwnerRow.Amount => DebitTotal - CreditTotal`（`OpeningBalanceCsv.cs:250`）は変更不要
  （新 `Balance` と同符号になる）。
- 4種別の `Sonota` を `CreateRecord` で全て設定する（現在 `SummaryUriSei` のみ）。
- `OpeningBalanceDb` は値を加工しないため変更不要。

## 7. テストの改修（`Tests/TestServer/`）

### 7.1 削除・書き換えが必要（繰越前提を固定しているもの）

| テスト | 対応 |
|---|---|
| `SummaryKakeDbTests.CalcSummaryUriKake_CarriesBalanceForwardAcrossMonths` | **繰越しない**ことを検証するテストへ書き換え（各月が独立） |
| `SummaryKakeDbTests.CalcSummaryUriKake_RecalculatesMonthsAfterTargetPeriod` | 対象月のみが再計算され後続月が**変化しない**ことの検証へ |
| `SummaryKakeDbTests.CalcSummaryUriKake_FreezesPreFiscalOpeningBalanceAndSeedsCarryForward` | 期首行が保持されることのみ検証（積み上げの検証を削除） |
| `SummaryKakeDbTests.CalcSummaryKaiKake_CarriesBalanceForwardAcrossMonths` | 同上 |
| `SummaryKakeDbTests.CalcSummaryKaiKake_RecalculatesMonthsAfterTargetPeriod` | 同上 |
| `SummaryKakeDbTests.CalcSummaryUriSei_PreviousBalanceIsRecoveredByAddingSalesAndSubtractingPayments` | `SUM(TotalSales - TotalIn)` 方式の `PreviousBalance` 検証へ全面書き換え |
| `OpeningBalanceCsvTests.Build_UriSei_SeedsCarryForwardThroughTotalDifference` | 符号反転と「1期間分の実績行」検証へ |
| `OpeningBalanceCsvTests.Build_KaiShi_SeedsCarryForwardThroughTotalDifference` | 同上 |
| `OpeningBalanceDbTests.Import_UriSei_SeedsCarryForwardOfNextClosingPeriod` | 期首行が `PreviousBalance` に載ることの検証へ |
| `OpeningBalanceDbTests.Import_UriKake_SeedsCarryForwardOfNextMonth` | 同上 |

### 7.2 符号のみ反転

`Balance` の期待値を持つ全アサーション（`SummaryKakeDbTests.cs:82, 178, 193-194, 221, 229-230, 267, 272, 287, 317, 473, 547-548, 575, 582-583, 631` ほか、
`OpeningBalanceCsvTests.cs:125, 144, 180, 201`、`OpeningBalanceDbTests` の各 `Balance` 期待値）。

### 7.3 追加

- `PreviousBalance` を4テーブル分算出する標準SQLの検証（期首行を含む累計になること）
- `Sonota` が4テーブルで分離集計されること、`TotalSales`/`TotalShiire` に加算されること
- `CalcSummaryKaiShi` で `Tran03Shiire` の区分99 が `Sonota` に入ること（欠落の回帰防止）
- `Tran02Material` の区分99 が `Sonota` ではなく `Tax1` に入り続けること（A-6 の回帰防止）

### 7.4 UAT

- `Doc/test/UatVmSeed/ShimeBoundarySeeder.cs:185-201` の `cumulative -= totalSales` による累積期待値を
  各期間の単独値へ変更し、繰越検証は `PreviousBalance` 相当の別アサーションへ移す。
- `Doc/test/UatVm/Scenarios/ShimeBoundaryScenario.cs:107` の表示名「残高(繰越込み)」を「当期間残高」へ。
- `Doc/spec/tools/summaryreconcile/Program.cs` は単月クリーンルームのため表示ラベルのみ調整。

## 8. ドキュメントの改修

| ファイル | 対応 | 優先 |
|---|---|---|
| `Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md` 2.3 / 3.8 | 新算式・新符号へ更新し、本設計書を `10.5 関連ドキュメント` へ追加 | 高 |
| `Doc/spec/archive/2026-09-01_請求一覧表_旧cvnet帳票移植_詳細設計.md` 4.2 | 前月残・当月残・繰越金額を `PreviousBalance` ベースへ | 高 |
| `Doc/spec/archive/2026-09-01_請求書印刷_旧cvnet帳票移植_詳細設計.md:22, 66` | 前回残高の復元式を `PreviousBalance` へ | 高 |
| 本設計書 | 新規作成（`Balance` の一次定義をここへ集約） | 高 |
| `Doc/spec/archive/2026-08-18_請求計算・支払計算_詳細設計.md` | 冒頭に「本設計書により全面的に置換された」旨の追記 | 中 |
| `Doc/spec/archive/2026-08-21_残高登録処理_詳細設計.md` | 同上（符号規約が反転した旨を明記） | 中 |
| `.omo/2026-08-20_E11_その他売上_詳細設計.md` | 4テーブルへ `Sonota` を展開した旨を追記 | 低 |

## 9. 適用手順

1. プログラムを更新して起動し、`UpdateDb` の `26_09_02_01` / `26_09_02_02` を適用する。**適用済み**（2026-09-01、`CvServer/server-user163.db`）。
2. 期首日（`MasterSysman.FiscalStartDate`）以降の全期間について、次を再計算する。
   - 売掛残更新（`CalcSummaryUriKake`）— **適用済み**（画面操作、2026-09-01）
   - 買掛残更新（`CalcSummaryKaiKake`）— **適用済み**（画面操作、2026-09-01）
   - 請求計算（`CalcSummaryUriSei`）を全締日・全請求月について — **適用済み**
     （`Doc/spec/tools/seikyushiharai_recalc` バッチ、2026-09-01、178件全成功）
   - 支払計算（`CalcSummaryKaiShi`）を全締日・全支払月について — **適用済み**
     （同上、89件全成功）

   請求・支払の対象締日・月は `Doc/spec/tools/seikyushiharai_recalc`（README参照）が
   既存 `SummaryUriSei`/`SummaryKaiShi` から動的に特定し、1件ずつ実行した
   （締日20/99 × `201905`〜`202609` の89ヶ月、計267件、合計1,089,395行挿入、失敗0件）。
3. 再計算しないと `Sonota` が 0 のままになる（`Balance` 自体は `26_09_02_02` の UPDATE で整合する）。
   実行後、`Balance = TotalSales - TotalIn`（買掛・支払は `TotalShiire - TotalOut`）が
   全行で一致することをSQLで確認済み。`Tests/TestServer/bin/Debug/net10.0/TestServer.exe` 264件全成功も確認済み。
4. 売掛金管理表・買掛金管理表・請求一覧表・支払一覧表で、
   前月残＋当月増減＝当月残 が成立することを確認する。**完了**（2026-09-01）。
   各帳票のViewModel実装SQLを`cv-sqlite`で対象月(202608等)に対して直接実行し、
   `前月残 + 当月売上(仕入) - 当月入金(支払) - 当月残` の差分をSQLで検算。
   4帳票すべて・全該当行で差分0（売掛金管理表2,357件・買掛金管理表502件・
   請求一覧表(締日99)1,604件・支払一覧表502件）。画面からのPDF出力（qfmprint等）は
   実施していないが、帳票SQLの結果（CSV相当）が正しいことを確認したため、
   数値検証としてはこれをもって完了とする。

## 10. 受入条件

- 4テーブルすべてで `Balance = TotalSales - TotalIn`（債務側は `TotalShiire - TotalOut`）が成立する。
  **確認済み**（全行で差分0、2026-09-01）。
- 過去の1期間だけを再計算しても、それ以外の期間の行が1件も変化しない。
  **確認済み**（`Tests/TestServer/SummaryKakeDbTests.cs`の
  `CalcSummaryUriKake_RecalculatesOnlyTargetMonthLeavingLaterMonthsUntouched`等で回帰検証、
  Step3のDELETE→INSERTが指定範囲のみに限定される実装で担保）。
- 同一条件での再計算が冪等である（`summaryreconcile idempotent`）。
  **確認済み**（`seikyushiharai_recalc`を実データに対して2回連続実行し、
  `SummaryUriSei`/`SummaryKaiShi`の件数・`Balance`/`TotalSales`/`TotalIn`/`TotalShiire`/
  `TotalOut`/`Sonota`の合計値が1回目と2回目で完全一致することをSQLで確認、2026-09-01）。
- 各帳票の「前月残 + 当月売上 - 当月入金 = 当月残」が成立する。
  **確認済み**（適用手順4を参照）。
- 区分99 が4テーブルすべてで `Sonota` に分離され、`Tran02Material` の A-6 が維持される。
  **確認済み**（`Tests/TestServer/SummaryKakeDbTests.cs`の
  `CalcSummaryKaiShi_SeparatesTran03ShiireKubun99AsSonota`・
  `CalcSummaryKaiKake_AddsTran02MaterialKubun99FullyIntoTax1WithoutSonota`・
  `CalcSummaryKaiShi_AddsTran02MaterialKubun99FullyIntoTax1WithoutSonota`で回帰検証）。

第10章の受入条件はすべて確認済み。Step 9・本設計書の適用作業は完了した。

## 11. 変更ファイル一覧

```
CvBase/BaseDbKake.cs                 PreviousBalance 4件追加（Sonota 3件は適用済み）
CvBase/UpdateDb.cs                   26_09_02_02 追加
CvBase/OpeningBalanceCsv.cs          符号反転・コメント整理・Sonota 展開
CvDomainLogic/SummaryDb.cs           4メソッドの Balance 式変更、previousBalance CTE と ExtendToMonth 削除、区分99 分離
CvWpfclient/ViewModels/06Uriage/SeikyuListReportViewModel.cs
CvWpfclient/ViewModels/06Uriage/SeikyuLedgerReportViewModel.cs
CvWpfclient/ViewModels/06Uriage/SeikyuBalanceDetailViewModel.cs
CvWpfclient/ViewModels/06Uriage/UrikakeBalanceReportViewModel.cs
CvWpfclient/ViewModels/06Uriage/MonthlyNyukinYoteiTableViewModel.cs
CvWpfclient/ViewModels/05Shiire/ShiharaiListReportViewModel.cs
CvWpfclient/ViewModels/05Shiire/ShiharaiLedgerReportViewModel.cs
CvWpfclient/ViewModels/05Shiire/ShiharaiBalanceDetailViewModel.cs
CvWpfclient/ViewModels/05Shiire/KaikakeBalanceReportViewModel.cs
CvWpfclient/ViewModels/05Shiire/MonthlyShiharaiYoteiTableViewModel.cs
Tests/TestServer/SummaryKakeDbTests.cs
Tests/TestServer/OpeningBalanceCsvTests.cs
Tests/TestServer/OpeningBalanceDbTests.cs
Doc/test/UatVmSeed/ShimeBoundarySeeder.cs
Doc/test/UatVm/Scenarios/ShimeBoundaryScenario.cs
Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md
Doc/spec/archive/2026-09-01_請求一覧表_旧cvnet帳票移植_詳細設計.md
Doc/spec/archive/2026-09-01_請求書印刷_旧cvnet帳票移植_詳細設計.md
```

## 12. 承認確認事項

1. **買掛・支払の A-6（`Tran02Material` 区分99 → 丸めず `Tax1` へ全額）を維持**し、
   `Sonota` には `Tran03Shiire` の区分99 のみを入れる（4.3節）。
2. **`CalcSummaryKaiShi` で `Tran03Shiire` の区分99 が欠落していた不具合を本改修で修正**する（4.3節）。
   支払金額が区分99の分だけ増える。
3. **帳票の「当月残高」欄は `PreviousBalance + Balance`（繰越込み）** とし、
   帳票上の見た目の値は現行から変えない（5.2節）。
4. **月別入金予定表・月別支払予定表の予定金額も `PreviousBalance + Balance`** とする（5.2節）。
5. 売掛金管理表・買掛金管理表の前月残が「前月行の値」から「前月までの累計」に変わり、
   前月に行が無い取引先の前月残が 0 でなくなる（5.1節）。

**ステータス: 完了（2026-09-01）。Step 1〜9すべて完了。第10章の受入条件もすべて確認済み。**

---

## 13. 実装手順（サブエージェント引き継ぎ用）

各 Step は独立したサブエージェント（Sonnet 5 想定）へそのまま渡せる粒度で書いてある。
**Step 番号順に実行し、前の Step の完了条件を満たしてから次へ進むこと。**
Step 4 と Step 5 のみ並列実行してよい（触るファイルが重ならない）。

### 共通の前提（全 Step 共通・プロンプト先頭に必ず貼ること）

```
作業ディレクトリ: C:\gitroot\new2022\cv10
設計書: Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md
        （着手前に必ず全文を読むこと）

【この改修の中核】
Summary 4テーブル（SummaryUriKake=売掛 / SummaryUriSei=請求 /
SummaryKaiKake=買掛 / SummaryKaiShi=支払）の全列を「対象期間の集計値のみ」にする。
繰越はテーブルに持たせない。

  TotalSales  = Uriage - Henpin - Nebiki + Sonota + Tax1 + Tax2 + Tax3
  TotalIn     = Cash + Fee + Densai + Offset + Other
  Balance     = TotalSales - TotalIn          ← 正＝未回収（旧仕様から符号が反転）

  買掛・支払は TotalShiire / TotalOut に読み替え、Balance = TotalShiire - TotalOut（正＝未払）。

前残が要る帳票は読み出し時に SQL で算出する（テーブルには持たない）:
  SUM(TotalSales - TotalIn)  WHERE キー < 対象期間の開始
表示専用プロパティ PreviousBalance（[ResultColumn]、DB非実体）で受ける。

【ビルドとテスト】
  dotnet build creativevision10.slnx -v q --nologo
  Tests/TestServer/bin/Debug/net10.0/TestServer.exe
  ※ この環境では dotnet test は 0 件になる。TestServer.exe を直接実行すること。
  ※ Doc/test/** と Doc/spec/tools/** は solution 外。個別ビルドが要る。

【踏み抜きやすい点】
- SQL は文字列なのでコンパイラが検出しない。列名・算式を変えたら
  Doc/test/** と Doc/spec/tools/** まで grep すること。
- 符号が反転する。既存の期待値・表示式をそのまま流用しないこと。
- Tran02Material の区分99 は「丸めず Tax1 へ全額」という特殊処理（A-6）であり、
  Sonota とは別物。Sonota へ積むと二重計上になる。
```

### Step 0: 承認取得（人間が実施・サブエージェント不可）

第12章の承認確認事項1〜5について合意を得る。特に次の2点は**現行の帳票出力値が変わる**。

- #2 `CalcSummaryKaiShi` の `Tran03Shiire` 区分99 欠落の修正 → 支払額が増える
- #5 売掛金管理表・買掛金管理表の前月残が「前月行の値」→「前月までの累計」

分割コミットにするか一括にするかもここで決める。

### Step 1: テーブル定義（`CvBase/BaseDbKake.cs`）

```
Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md の 3.1 を実装せよ。

SummaryUriKake / SummaryUriSei / SummaryKaiKake / SummaryKaiShi の4クラスすべてに、
Balance プロパティの直前へ次を追加する（4クラスとも同一の文面）:

	/// <summary>
	/// 表示専用項目
	/// </summary>
	[ObservableProperty]
	[ResultColumn]
	public partial long PreviousBalance { get; set; } = 0;

XMLコメントには「前期間までの累計残高。DB上には作らず帳票SQLが SUM(TotalSales - TotalIn)
（買掛・支払は TotalShiire - TotalOut）で埋める」ことを書く。

あわせて Balance の [Comment] を「当月残高」から「当期間残高 TotalSales - TotalIn（正=未回収）」
相当へ更新する（買掛・支払は TotalShiire - TotalOut、正=未払）。

Sonota 列（3テーブル分）は既に追加済みなので触らないこと。
ファイルは BOM なし UTF-8。BOM を付けないこと。

完了条件: dotnet build CvBase/CvBase.csproj -v q --nologo が 0 エラー 0 警告。
確認: [ResultColumn] は ExDatabase.cs:157-158 で DDL 生成からスキップされる。物理列は増えない。
```

### Step 2: マイグレーション（`CvBase/UpdateDb.cs`）

```
Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md の 3.2 を実装せよ。

versions 配列の末尾（26_09_02_01 の次）へ 26_09_02_02 を1行追加する。SQL は次の4文を
セミコロン連結した1つの文字列にする:

  UPDATE SummaryUriKake SET Balance = TotalSales - TotalIn;
  UPDATE SummaryUriSei  SET Balance = TotalSales - TotalIn;
  UPDATE SummaryKaiKake SET Balance = TotalShiire - TotalOut;
  UPDATE SummaryKaiShi  SET Balance = TotalShiire - TotalOut;

Memo には「残高を当期間ネット(正=未回収)へ変更 繰越はテーブルに持たず帳票側で算出する
Sonotaの分離には別途全期間の再計算が必要」旨を書く。

既存行の書式（インデントはタブ、new (バージョン,"SQL","メモ"),）に合わせること。
ファイルは BOM なし UTF-8、改行 CRLF。

完了条件: dotnet build CvBase/CvBase.csproj -v q --nologo が 0 エラー 0 警告。
```

### Step 3: 再計算ロジック（`CvDomainLogic/SummaryDb.cs`）— 本改修の中核

```
Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md の 4章を実装せよ。
このファイルの改修が本改修の中核であり、最も慎重さを要する。

(a) 繰越の除去（4メソッド共通）
    - previousBalance CTE を4つとも削除（売掛 951-960 / 請求 1086-1091 /
      支払 1275-1280 / 買掛 1479-1488 付近）
    - 対応する LEFT JOIN previousBalance を削除（993 / 1142 / 1327 / 1521 付近）
    - 売掛・買掛のウィンドウ関数による積み上げを削除（969-973 / 1497-1501 付近）
    - Balance の INSERT 式を次に置き換える:
        売掛・請求: Uriage - Henpin - Nebiki + Sonota + (Tax1+Tax2+Tax3)
                    - (Cash + Fee + Densai + Offset + Other)
        買掛・支払: Shiire - Henpin - Nebiki + Sonota + (Tax1+Tax2+Tax3)
                    - (Cash + Fee + Densai + Offset + Other)
    - ExtendToMonth メソッド（791-816 付近）と、その呼び出し2箇所（860 / 1366 付近）を削除。
      これに伴い売掛・買掛の再計算は指定年月範囲のみを DELETE→INSERT する形になる。
      DELETE の範囲指定が ExtendToMonth 由来の変数を使っていないか必ず確認すること。

(b) 区分99（Sonota）の4テーブル統一 — 設計書 4.3 の表のとおり
    - CalcSummaryUriKake: Uriage の CASE から「OR t.Kubun = 99」を外し、
      Sonota 列（Kubun=99 のみ）を新設して TotalSales へ加算（881 付近）
    - CalcSummaryKaiKake: Shiire の CASE から「OR t.Kubun = 99」を外し、
      Tran03Shiire 側に Sonota を新設して TotalShiire へ加算（1388 付近）
    - CalcSummaryKaiShi: Tran03Shiire 側の区分99 が現在どこにも入らず欠落している
      （1235 で Shiire から除外、1239 で Sonota99 = 0 固定）。Sonota として集計するよう修正
    - CalcSummaryUriSei: 既に Sonota 分離済み。変更しない
    - 【重要】Tran02Material の区分99（Sonota99 → 丸めず Tax1 へ全額加算、A-6）は
      CalcSummaryKaiKake:1375 / CalcSummaryKaiShi:1214 の FinalTaxExprSql 第4引数として
      現状どおり維持する。Sonota へは積まないこと（積むと二重計上）。
      Sonota99（A-6用）と Sonota（区分99の分離集計）は別物であり、名前が紛らわしいので
      コメントで両者の違いを明記すること。

(c) 期首日の凍結ガード（GetFiscalStartDate による早期 return と範囲切り上げ）は
    実装を変更しない。ただし意味が変わるのでコメントを設計書 4.2 のとおり書き換える。

(d) 既存コメントのうち「繰越は前月から積み上がる」「過去月だけ再作成すると以降の月が
    古い繰越のまま残る」といった記述（771-790, 818-826 付近）を新仕様に合わせて全面的に
    書き直す。古いコメントを残さないこと。

完了条件:
- dotnet build creativevision10.slnx -v q --nologo が 0 エラー 0 警告
- SummaryDb.cs 内に previousBalance / ExtendToMonth の残骸が grep で出ないこと
- 4メソッドすべてで Balance が自期間の内訳のみから算出されていること
テストは Step 6 で直すため、この時点で TestServer.exe が失敗するのは想定内。
どのテストが失敗したかを一覧で報告すること。
```

### Step 4: 期首残高（`CvBase/OpeningBalanceCsv.cs`）

```
Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md の 6章を実装せよ。

- CreateRecord の `var balance = credit - debit;`（872 付近）を `debit - credit` に変更。
  直前のコメント「内部の Balance は「負=未回収」。繰越は売掛・買掛が Balance 列、
  請求・支払が合計の差で読むため双方を埋める。」を
  「Balance は当期間ネット(正=未回収)。4テーブル共通で Balance = DebitTotal - CreditTotal。」
  相当へ書き換える。
- クラス OpeningBalanceCsv の XMLコメント（262-279 付近）から
  「繰越の引き継ぎ方が売掛・買掛（Balance列）と請求・支払（TotalIn-TotalSales）で異なる」
  という2方式の説明を削除し、「期首行は期首直前の1期間分の実績行であり、
  帳票側の PreviousBalance の SUM に自然に含まれる」という説明へ差し替える。
- CreateRecord の4分岐のうち SummaryUriKake / SummaryKaiKake / SummaryKaiShi にも
  Sonota = breakdown.Sonota を追加する（現在 SummaryUriSei のみ設定されている）。
- OpeningBalanceOwnerRow.Amount => DebitTotal - CreditTotal（250 付近）は変更しない
  （新 Balance と同符号になる）。
- DebitTotal / CreditTotal / NetAmount の定義（90-94 付近）は変更しない。

CvDomainLogic/OpeningBalanceDb.cs は値を加工しないため変更不要。確認だけして触らないこと。

完了条件: dotnet build creativevision10.slnx -v q --nologo が 0 エラー 0 警告。
テストは Step 6 で直す。失敗したテスト名を一覧で報告すること。
```

### Step 5: 帳票・画面 10本（Step 4 と並列可）

```
Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md の 5章を実装せよ。

対象10ファイル（5.1 / 5.2 の表に file:line と対応が書いてある）:
  CvWpfclient/ViewModels/06Uriage/SeikyuListReportViewModel.cs        請求一覧表
  CvWpfclient/ViewModels/06Uriage/SeikyuLedgerReportViewModel.cs      請求台帳
  CvWpfclient/ViewModels/06Uriage/SeikyuBalanceDetailViewModel.cs     請求書印刷
  CvWpfclient/ViewModels/06Uriage/UrikakeBalanceReportViewModel.cs    売掛金管理表
  CvWpfclient/ViewModels/06Uriage/MonthlyNyukinYoteiTableViewModel.cs 月別入金予定表
  CvWpfclient/ViewModels/05Shiire/ShiharaiListReportViewModel.cs      支払一覧表
  CvWpfclient/ViewModels/05Shiire/ShiharaiLedgerReportViewModel.cs    支払台帳
  CvWpfclient/ViewModels/05Shiire/ShiharaiBalanceDetailViewModel.cs   支払残高明細書
  CvWpfclient/ViewModels/05Shiire/KaikakeBalanceReportViewModel.cs    買掛金管理表
  CvWpfclient/ViewModels/05Shiire/MonthlyShiharaiYoteiTableViewModel.cs 月別支払予定表

各ファイルで:
1. previousBalance CTE を追加する。標準形は設計書 2.3 のとおり:
     締日キー（請求・支払）: WHERE DayTo   < 対象期間の開始日
     年月キー（売掛・買掛）: WHERE DenMonth < 対象年月
   集計は SUM(TotalSales - TotalIn)（買掛・支払は SUM(TotalShiire - TotalOut)）。
   取引先で GROUP BY し、本体へ LEFT JOIN して ifnull(pb.PreviousBalance, 0) で受ける。
2. 既存の前残の逆算式（Balance + TotalSales - TotalIn など）と
   前月行直読み CTE（prev AS (SELECT ... WHERE DenMonth = 前月)）を削除し、
   1 の値へ差し替える。
3. 「当月残高」として Balance をそのまま出していた列は
   PreviousBalance + Balance へ変更する（帳票の見た目の値を現行と同じに保つため）。
4. IsActiveOnly の絞り込み条件（Balance != 0 等）を当月残高ベースへ見直す。
5. 各クラス冒頭の XMLコメントにある前残の説明を新方式へ書き換える。
   古い逆算式の説明を残さないこと。

【重要】帳票 qfm 側の項目名・並びは変更しない。SQL の別名（prevBalance / balance /
carryOver / item39 など）もそのまま維持し、中身の式だけを差し替えること。
CvWpfclient/Views 配下の XAML も変更不要のはず。変更が必要になったら報告して止まること。

対象外（触らないこと）:
  TokuiLedgerViewModel / ShiireLedgerViewModel（伝票直読み）
  UriageCashTypeReportViewModel（SummaryUriKake 型を DTO 流用しているだけ）
  BillingCalculationViewModel / PaymentCalculationViewModel / StockKakeUpdateViewModel
  BaseMatchingViewModel / QueryMsgStreamService / SummaryRebuildClosingCheck

完了条件: dotnet build creativevision10.slnx -v q --nologo が 0 エラー 0 警告。
各ファイルについて「どの式をどう変えたか」を before/after で一覧報告すること。
```

### Step 6: テスト（`Tests/TestServer/`）

```
Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md の 7.1〜7.3 を実装せよ。
Step 1〜5 の実装が済んでいる前提。

(a) 7.1 の表にある10本のテストを書き換える。特に:
    - CarriesBalanceForwardAcrossMonths（売掛・買掛）は
      「繰越しない＝各期間が独立している」ことを検証するテストへ意味ごと書き換える
    - RecalculatesMonthsAfterTargetPeriod（売掛・買掛）は
      「対象月のみ再計算され、後続月の行が1件も変化しない」ことの検証へ
    - CalcSummaryUriSei_PreviousBalanceIsRecoveredByAddingSalesAndSubtractingPayments は
      SUM(TotalSales - TotalIn) 方式の PreviousBalance 検証へ全面書き換え
    テストメソッド名も新しい意味に合わせて改名すること（旧名を残さない）。

(b) 7.2 の符号反転。Balance の期待値を持つ全アサーションを新符号（正=未回収）へ。
    値を機械的に -1 倍するのではなく、各テストのセットアップから期待値を計算し直すこと。

(c) 7.3 の追加テストを新規に書く:
    - PreviousBalance 標準SQLが期首行を含めて正しく累計すること（4テーブル分）
    - Sonota が4テーブルで分離集計され TotalSales / TotalShiire に加算されること
    - CalcSummaryKaiShi で Tran03Shiire の区分99 が Sonota に入ること（欠落の回帰防止）
    - Tran02Material の区分99 が Sonota ではなく Tax1 に入り続けること（A-6 の回帰防止）

完了条件: Tests/TestServer/bin/Debug/net10.0/TestServer.exe が全件成功。
失敗が残る場合は、実装バグかテスト期待値の誤りかを切り分けて報告すること。
テストを通すためだけに実装を歪めないこと。
```

### Step 7: UAT シードと突合ツール（solution 外）

```
Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md の 7.4 を実装せよ。
これらは solution 外なので dotnet build creativevision10.slnx では検出されない。個別にビルドすること。

- Doc/test/UatVmSeed/ShimeBoundarySeeder.cs:185-201
  BuildExpectations() の cumulative -= totalSales による累積期待値を各期間の単独値へ変更。
  コメント「残高は TotalIn - TotalSales の累積である」も新仕様へ書き換える。
  繰越の検証が必要なら PreviousBalance 相当の別フィールド・別アサーションとして分離する。
- Doc/test/UatVm/Scenarios/ShimeBoundaryScenario.cs:107
  CheckEqual の表示名「残高(繰越込み)」→「当期間残高」。
- Doc/spec/tools/summaryreconcile/Program.cs
  Show() の表示ラベルを当期間ベースへ調整。Snapshot()/Idempotent() は
  列をそのまま比較しているだけなのでロジック変更は不要。

完了条件: 対象3プロジェクトが個別ビルドで 0 エラー。
```

### Step 8: ドキュメント更新

```
Doc/spec/archive/2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md の 8章を実施せよ。

高優先:
- Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md
    2.3 節の Summary 算式を新仕様（Sonota 込み・4テーブル共通）へ更新
    3.8 節の「Balance 自体の式は変更していない」という記述（284-285 行付近）を
    「本設計により Balance は当期間ネットへ変更された」旨へ差し替え
    10.5 関連ドキュメントへ本設計書を追加
- Doc/spec/archive/2026-09-01_請求一覧表_旧cvnet帳票移植_詳細設計.md 4.2 金額算式（76-92 行付近）
    前月残 / 当月残 / 繰越金額を PreviousBalance ベースの式へ
- Doc/spec/archive/2026-09-01_請求書印刷_旧cvnet帳票移植_詳細設計.md:22, 66
    「前回残高は Balance + TotalSales - TotalIn で復元する」を PreviousBalance ベースへ

中優先（冒頭に注記を追記するのみ。本文は歴史的記録として残す）:
- Doc/spec/archive/2026-08-18_請求計算・支払計算_詳細設計.md
    「本文の累計残の定義は 2026-09-02_Summary残高_期間集計化とPreviousBalance_詳細設計.md
      により全面的に置換された」
- Doc/spec/archive/2026-08-21_残高登録処理_詳細設計.md
    上記に加え「符号規約が反転した（負=未回収 → 正=未回収）」ことと
    「繰越の2方式は廃止された」ことを明記

低優先:
- .omo/2026-08-20_E11_その他売上_詳細設計.md
    Sonota を4テーブルへ展開した旨と、Tran02Material の A-6 とは別物である旨を追記

完了条件: 上記ドキュメントに旧算式・旧符号の記述が残っていないこと（grep で確認）。
```

### Step 9: 最終確認（人間が実施）

- `dotnet build creativevision10.slnx -v q --nologo` → 0 エラー 0 警告
- `Tests/TestServer/bin/Debug/net10.0/TestServer.exe` → 全件成功
- solution 外プロジェクトの個別ビルド → 0 エラー
- 第10章の受入条件を1件ずつ確認
- 第9章の適用手順を本番データへ実行し、旧値との差額を確認
