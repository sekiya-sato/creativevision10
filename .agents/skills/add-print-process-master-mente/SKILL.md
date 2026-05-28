---
name: add-print-process-master-mente
description: Adds PDF print support to CvWpfclient master maintenance screens by replacing JSON output with DoOutputPdfCommand, adding FormFile and PrintBySqlParam or PrintByCsvParam in the ViewModel, and creating Shift_JIS A4 portrait qfm forms under printform. Use for Master* Mente View/ViewModel print additions based on MasterShainMente, MasterMeishoMente, or MasterSysKanriMente patterns.
---

# Add Print Process to Master Mente

このスキルは、`CvWpfclient` のマスターメンテ画面で `JSON出力 (F6)` を `印刷 (F6)` に置き換え、既存の `BaseMenteViewModel.DoOutputPdfCommand` を使う印刷処理へ横展開するための手順です。

## 前提

- 先に `wpf-project-guide` を読み、対象の View / ViewModel / 既存 ResourceDictionary を確認する。
- `BaseMenteViewModel<T>` には `FormFile`、`PrintByCsvParam`、`PrintBySqlParam`、`DoOutputPdfCommand` があるため、通常は基底クラスを変更しない。
- qfm は `printform/` 配下に置き、A4縦を基本に Shift_JIS(cp932) で保存する。

## 参照パターン

- 一覧SQL印刷: `MasterShainMenteViewModel`
  - `FormFile => "MasterShainMente.qfm"`
  - `PrintBySqlParam` で `CreateListQueryParam()` と `query.AddWhereOrder()` を使い、画面の検索条件・並び順を印刷にも反映する。
- UI選択条件付き一覧SQL印刷: `MasterMeishoMenteViewModel`
  - `SelectedKubun` が未選択なら `PrintBySqlParam` を `null` にして、基底側の「印刷データが設定されていません」を使う。
  - 選択値を `QueryListSqlParam` のパラメータへ渡し、SQL文字列へ直結しない。
- 単票CSV印刷: `MasterSysKanriMenteViewModel`
  - 画面上の単一レコードや加工済み値を出す場合は `PrintByCsvParam` を使う。
  - `EscapeCsvField`、`CultureInfo.InvariantCulture`、`\r\n` 終端を守る。

## 実装手順

1. 対象 ViewModel に `FormFile` を追加する。

```csharp
protected override string? FormFile => "MasterXxxMente.qfm";
```

2. 印刷データの供給方法を選ぶ。

- 一覧をそのまま印刷する画面は `PrintBySqlParam` を優先する。
- 現在レコードだけ、または画面表示用に加工した値を印刷する画面は `PrintByCsvParam` を使う。
- `PrintByCsvParam` と `PrintBySqlParam` は同時に返さない。基底側でエラーになる。

3. `PrintBySqlParam` では SELECT 項目順を qfm の `item1`、`item2`... と一致させる。

```csharp
protected override QueryListSqlParam? PrintBySqlParam {
    get {
        var query = CreateListQueryParam();
        var sql = @$"
select Id, __serverdate__(Vdc) Vdcdate, __serverdate__(Vdu) Vdudate,
Code, Name, Ryaku
from MasterXxx {query.AddWhereOrder()}
";
        return new QueryListSqlParam(typeof(MasterXxx), sql, query.Parameters);
    }
}
```

4. View の F6 とツールバーボタンを JSON から印刷へ差し替える。

- `Command="{Binding DoOutputJsonCommand}"` を `Command="{Binding DoOutputPdfCommand}"` にする。
- `ToolTip="JSON出力 (F6)"` を `ToolTip="印刷 (F6)"` にする。
- アイコンは `materialDesign:PackIcon Kind="Printer"` にする。
- 既存の印刷対応画面に合わせ、ボタン内に `TextBlock Text="印刷"` を置ける場合は置く。

5. qfm を作成する。

- ファイル名は `FormFile` と完全一致させる。
- XML 宣言は `encoding="SHIFT_JIS"` または `encoding="shift_jis"` にする。
- データ入力は `<path datatype="csv" target="data.txt" />` にする。
- A4縦の基本ページは以下を使う。

```xml
<page orientation="portrait" cpi="10" lpi="6" compatibility="3.0.0" id="Form1" tree="1">
    <position x="8" y="8" width="156" height="272" />
</page>
```

- 明細一覧は `MasterShainMente.qfm` / `MasterMeishoMente.qfm` のように `region` + `record` を使う。
- 単票は `MasterSysKanriMente.qfm` のように固定ラベル + `datasrc="itemN"` の配置を使う。
- `datarecord` の `itemN` 定義順と SELECT/CSV の列順を必ず一致させる。

## qfm の保存と検証

qfm は UTF-8 ではなく Shift_JIS(cp932) で保存する。変更後は付属スクリプトで文字コードとA4縦の基本設定を確認する。

```powershell
python .agents\skills\add-print-process-master-mente\scripts\validate_qfm.py printform\MasterXxxMente.qfm
```

`validate_qfm.py` は Python 3 で実行できる。WSL / Linux / macOS など `python` が未設定の環境では `python3` を使う。

```bash
python3 .agents/skills/add-print-process-master-mente/scripts/validate_qfm.py printform/MasterXxxMente.qfm
```

複数ファイルもまとめて確認できる。

```powershell
python .agents\skills\add-print-process-master-mente\scripts\validate_qfm.py printform\MasterShainMente.qfm printform\MasterMeishoMente.qfm
```

```bash
python3 .agents/skills/add-print-process-master-mente/scripts/validate_qfm.py printform/MasterShainMente.qfm printform/MasterMeishoMente.qfm
```

## 確認手順

1. 対象 XAML の `DoOutputJsonCommand` が残っていないことを確認する。
2. qfm 検証スクリプトを実行する。
3. XAML を変更した場合は `check-xaml` または XML 構文確認を行う。
4. `git diff --check` を実行する。
5. WPF クライアントをビルドする。

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"
```

6. 印刷サーバー環境が使える場合は、画面で F6 または印刷ボタンを押し、PDF表示画面が開くことを確認する。実行できない場合は理由を作業ログへ記録する。

## ログ

作業完了後は `Doc/aicoding_log.md` に、対象 View / ViewModel / qfm、qfm 検証、ビルド結果を記録する。
