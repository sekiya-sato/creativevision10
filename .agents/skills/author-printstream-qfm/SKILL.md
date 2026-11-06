---
name: author-printstream-qfm
description: Create or modify PrintStream report forms (.qfm) in the cv10 repo. Centers the mined-and-verified format spec at Doc/spec/PrintStream_qfmフォーマット仕様.md (element/attribute grammar from 2075 real qfm + CHM semantics), plus repo operational rules — Shift_JIS(cp932), CSV data.txt binding, copy-from-existing workflow, BaseReportViewModel report wiring, a DB-free local PDF render harness (tools/qfmprint), validation, and rollback discipline. Use when authoring or reviewing qfm forms under printform/.
---

# PrintStream qfm Authoring

`cv10` リポジトリで PrintStream 帳票 (`.qfm`) を**新規作成・修正**するためのスキル。

このスキルは 2 層構成です。

- **フォーマット仕様（何を書けるか）** → [`Doc/spec/PrintStream_qfmフォーマット仕様.md`](../../../Doc/spec/PrintStream_qfmフォーマット仕様.md)
  - 実 qfm **2075 本**（cv10 108 + 旧cv.net 1967）の機械マイニング + CHM 意味論。要素×属性×値の文法表、decode 書式チートシート、骨格テンプレを収録。
- **運用規律（どう安全に増やすか）** → この SKILL.md
  - Shift_JIS・CSV・コピー起点・validator・PDF 確認・ロールバック。

> フォーマットの疑問（属性・enum 値・書式文字列・要素の入れ子）は**必ず仕様 md を引く**。SKILL.md には書式詳細を重複させない。

## いつ使うか

- `printform/*.qfm` を新規作成する
- 既存 qfm のレイアウト・項目・書式を変更する
- qfm をレビューする

ViewModel 側の印刷配線（`FormFile` / `PrintBySqlParam` / `PrintByCsvParam` / F6）まで含むときは `add-print-process-master-mente` を併用する。

## まず守る基本ルール

1. **既存 qfm をコピーして最小差分で編集する。ゼロから独自構造を発明しない。**
2. 保存文字コードは **Shift_JIS(cp932)**。XML 宣言は `encoding="SHIFT_JIS"`。
3. ルートは `<printstream version="3.0">`。
4. 入力は **CSV + `data.txt`** のみ（`<path datatype="csv" target="data.txt"/>`）。固定長・XML 入力は扱わない。
5. `datarecord/item` の定義順・CSV 列順・各フィールドの `data @datasrc="itemN"` を必ず一致させる。
6. `recordtype` / `breaktype` / `groupcontrol` / `barcode type` / `font style` などの**数値属性は前例からコピー**する。CHM に数値対応表がないため推測で入れない（仕様 md §3〜§4 参照）。
7. `decode/format` は仕様 md §5 のチートシートと既存例から流用する。新書式を発明しない。
8. 中止・差し戻し時は既存のユーザー変更を保持し、今回の変更だけ扱う（→ ロールバック）。

## 雛形の選び方

| 種類 | まず選ぶ既存 qfm |
|---|---|
| 一覧帳票（region + record 繰り返し） | `printform/MasterShainMente.qfm`, `printform/MasterMeishoMente.qfm` |
| 単票帳票（ラベル + 値） | `printform/MasterSysKanriMente.qfm` |
| バーコード（縦配置） | `printform/MasterPrintBarcodeView_v_code39.qfm`, `..._v_nw7.qfm`, `..._v_jan.qfm` |
| バーコード（横配置） | `printform/MasterPrintBarcodeView_h_code39.qfm`, `..._h_nw7.qfm`, `..._h_jan.qfm` |

用紙は縦横どちらも実運用がある（仕様 md §3.1）。近い向きの前例を選ぶ。

## 標準ワークフロー

1. 最も近い既存 qfm を選ぶ（上表）。
2. `printform/` にコピーし、ファイル名を決める。
3. XML 宣言と `<printstream version="3.0">` を維持する。
4. `datarecord/item` を新しい CSV 列順に合わせる（仕様 md §2）。
5. 各フィールドの `data @datasrc="itemN"` を列順に合わせる（仕様 md §4.2）。
6. `decode/format` を仕様 md §5 と既存例から流用する。
7. `record` / `region` / `group` の種別・数値属性は前例からコピーする（仕様 md §3）。
8. 画像・バーコード・スクリプトは最後に追加する（仕様 md §4.8〜§6）。
9. **Shift_JIS(cp932)** で保存する。
10. validator を実行する（下記。Python が無ければ構造チェックで代替）。
11. **実 PDF をローカル描画して確認する**（下記ハーネス。DB・サーバ不要）。

### 旧 CRS とセットで移行するとき

旧 CRS は帳票要件の根拠として読む。`SatooDialog.Title`、範囲指定・選択肢、`OnTouch` の条件、切替先 qfm を対応表にしてから、WPF 画面側の実装へ渡す。`OnTouch` の SELECT 列順は qfm の `item1..itemN` と CSV 列順に対応させ、同名列は必ず一意の別名にする。サーバの CSV 化は重複列名を扱えないためである。

旧 qfm を採用する指定では内容を保ち、`printform/*.qfm` は cp932・LF で保存する。カード型など意図的に標準 A4 位置と異なる帳票は、validator の位置警告だけで変更せず、XML 構造・`itemN` 対応・PDF 結果を分けて確認する。

## 検証

repo 共通 validator:

```powershell
python .agents\skills\add-print-process-master-mente\scripts\validate_qfm.py printform\MasterXxxMente.qfm
```

主なチェック: Shift_JIS で読めるか / XML 宣言 encoding / ルート `printstream` / `path datatype="csv"` `target="data.txt"` / `page orientation` / A4 縦基本 position / `datarecord/item` 存在。

#### Python が無い環境での代替（実績）

この環境には Python 実体が無い（`python`/`python3` は WindowsApps のスタブで実行不可）ことがある。その場合は validator の検査項目を手動で代替する。

```bash
# cp932 で読めるか & 主要不変条件（bash + iconv）
U=$(iconv -f CP932 -t UTF-8 printform/Xxx.qfm)
echo "$U" | grep -q 'encoding="SHIFT_JIS"'         && echo OK-encoding
echo "$U" | grep -q '<printstream version="3.0">'  && echo OK-root
echo "$U" | grep -q '<path datatype="csv" target="data.txt"/>' && echo OK-path
echo "$U" | grep -q '<page orientation='           && echo OK-page
echo "$U" | grep -oE '<item id="item[0-9]+"' | wc -l   # item 数
echo "$U" | grep -oE 'datasrc="item[0-9]+"' | wc -l    # datasrc 数（item と対応）
```

```powershell
# XML 整形式チェック（.NET / cp932 読み）
$enc=[Text.Encoding]::GetEncoding(932)
$t=[IO.File]::ReadAllText("printform\Xxx.qfm",$enc)
$x=[xml]$t; "root="+$x.DocumentElement.Name+" items="+$x.SelectNodes('//item').Count
```

これは **repo 運用ルール**の確認であり、ベンダー仕様の完全検証ではない。legacy qfm の position warning は単独で失敗扱いしない。次の 3 点を分けて確認する。

- Shift_JIS(cp932) で読めること
- `itemN` / `datasrc` 対応が壊れていないこと
- PDF 出力結果が要求形状に合うこと

`CvPrints/PrintAdapter.cs` は現在 `FormWriter.PDF` を使うため、**最終確認は PDF 実出力ベース**で行う。PDF は標準印刷と完全一致しない（太字・自動改行・網掛けで差が出やすい／仕様 md §4.4）。

### 実 PDF をローカル描画して確認する（DB・サーバ不要）— 実績手順

本番の PDF 生成はサーバ側（gRPC `PrintPdfService` が SQL→CSV→`FormWriter`）だが、**qfm 単体の描画確認は DB もサーバも要らない**。`CvPrints.PrintAdapter` を直接呼ぶ小ハーネスで実 PDF を出せる（`PRINT_ENABLE` は `CvPrints.csproj` で既定 true、IKVM が `printstream.jar` を取り込む）。

ハーネスは同梱: [`tools/qfmprint/`](tools/qfmprint/)（`Program.cs` は `CvServer/Services/PrintPdfService.cs:94-100` の `PrintContext` 構築を最小再現）。本番の `OutputFileName` は `outfile{yyyyMMddHHmm}.pdf`（`PrintPdfService.cs:118-119`）だが、本ハーネスは確認しやすいよう固定名 `outfile.pdf` を使う。

手順:

```powershell
# 1) ハーネスをビルド
dotnet build .agents\skills\author-printstream-qfm\tools\qfmprint\qfmprint.csproj

# 2) ライセンスを実行フォルダへ（未登録だと FormWriter.submit() が失敗する）
$bin = ".agents\skills\author-printstream-qfm\tools\qfmprint\bin\Debug\net10.0"
Copy-Item refer\printdll\printstream.license $bin -Force

# 3) 検証データ data.txt を用意（★ Shift_JIS/cp932・列順は qfm の item1..itemN と一致）
#    正常行・負値行・空欄行など境界を混ぜる。bash なら:
#    iconv -f UTF-8 -t CP932 data.utf8.txt > <workdir>\data.txt

# 4) 実行（form の絶対パス, data.txt を置いた workdir）
& "$bin\qfmprint.exe" (Resolve-Path 'printform\Xxx.qfm').Path '<workdirの絶対パス>'
```

- 成功すると `IsSuccess=True` と `workdir\outfile.pdf` が出る。`CheckLicense` の各 product が `status=True` であること。
- 生成 PDF のテキスト層を対応ツールで確認し、列見出し・各行の値・書式（負値、日付、空欄）を突合できる。
- 注意: このローカルハーネスはフォント埋め込みが本番サーバ環境と異なり、ラスタ画像で一部 CJK グリフが欠けて見えることがある。テキスト層だけで見切れ・配置・改ページの妥当性を判断せず、画像と実サーバでの表示も確認する。
- `data.txt` の列順は SQL の SELECT 列順であり、それが qfm の `item1..itemN` に対応する。ズレると全列が横にずれる。

### 文法の再抽出（仕様 md を更新するとき）

```powershell
powershell -File Doc\spec\tools\extract_qfm_grammar.ps1 printform C:\gitroot\cv\cvnet_pkg\cvnetpss
```

出力（要素／属性／enum 値の分布）を仕様 md §1〜§4 の表へ反映する。

## 帳票（レポート）画面の配線パターン

qfm は単体では動かない。「パラメータ入力→SQL→qfm で PDF」型の帳票画面は `BaseReportViewModel` を継承すると最短で配線できる（マスターメンテ画面の F6 差し替えは `add-print-process-master-mente` を参照）。

派生 ViewModel が実装するのは 3 点だけ:

```csharp
public partial class XxxReportViewModel : Helpers.BaseReportViewModel {
    protected override string ReportTitle => "○○帳票";
    protected override string FormFileName => "Xxx.qfm";   // printform 配下
    protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) {
        // SELECT の列順 = qfm の item1..itemN。AddSqlParameter でプレースホルダ採番。
        // 日付列は TranMeisaiSql.DateLabel("u.DenDay") で yyyy/MM/dd 表示にできる。
        ...
        return Task.FromResult<QueryListSqlParam?>(new QueryListSqlParam(typeof(object), sql, [.. parameters]));
    }
}
```

- View は `BaseWindow` を継承した XAML（`DoOutputPdfCommand` を F6/ボタンに割当）。既存帳票 View をコピーする。
- メニュー登録は `CvWpfclient/Models/MenuData.cs` に 1 行追加。
- 返品を含む伝票の合計・残高・繰越は SQL で `h.CalcFlag *` を掛ける。伝票行の表示金額は正値のまま残す（`AGENTS.md` 7.6）。
- `DoOutputPdf`（基底）→ `RunPrintPdfAsync`（`PrintPdfHelper`）が gRPC でサーバへ投げる。**実行時はサーバ＋DB が要る**が、qfm 自体の描画確認は上記ローカルハーネスで先行できる。

この配線は作業時点の `CvWpfclient/Helpers/ViewModels/BaseReportViewModel.cs` と `PrintPdfHelper.cs` で確認する。設計書・過去の CRS だけで基底 API や出力経路を決めない。

### 実装済みの worked example

`SummaryUriSei` の保存済み請求書番号・再発行世代・入金予定日を出力する **請求台帳（発行控え）** が一式の実例:

- qfm: `printform/SeikyuLedgerReport.qfm`（`SeikyuListReport.qfm` をコピーし列を差替えた実例）
- VM: `CvWpfclient/ViewModels/06Uriage/SeikyuLedgerReportViewModel.cs`
- View: `CvWpfclient/Views/06Uriage/SeikyuLedgerReportView.xaml(.cs)`
- メニュー: `MenuData.cs`「請求台帳（発行控え）」
- SQL・列対応: [SeikyuLedgerReportViewModel.cs](../../../CvWpfclient/ViewModels/06Uriage/SeikyuLedgerReportViewModel.cs)

## ロールバック

- ユーザーが中止・差し戻しを指示した場合、今回の変更のみ戻す。事前からあった変更は保持し、不明な場合は確認する。
- 検証で自分が作成した一時成果物を片付ける。

## 参照優先度

1. `printform/*.qfm` — 実運用帳票本体。最優先で雛形にする。
2. [`Doc/spec/PrintStream_qfmフォーマット仕様.md`](../../../Doc/spec/PrintStream_qfmフォーマット仕様.md) — フォーマット文法・書式・意味論。
3. `CvPrints/PrintAdapter.cs` — `FormWriter` に何を渡すか、実行時要件。
4. `add-print-process-master-mente/scripts/validate_qfm.py` — repo 共通 validator。
5. `tools/qfmprint/` — DB 不要の実 PDF 描画ハーネス（qfm 単体の描画確認）。
6. `refer/printdll/PrintStream_decompiled/FormEditor/*.html` — CHM 展開版（概念・高度機能の一次資料）。

## このスキルがカバーしないこと

- ベンダーが raw qfm XML の完全仕様を保証している、という主張（CHM に XML 文法書はない）。
- 数値属性（recordtype 等）の完全対応表を前例なしに推測すること。
- CSV 以外の入力（固定長・XML）。
- FormEditor が不要である、という断定。

## 関連スキル

- `add-print-process-master-mente` — ViewModel 側の印刷配線まで含めるとき。
- `wpf-project-guide` — WPF 画面修正も同時に行うとき。
