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

## cv10 実績パターン

- テーブル一覧や DB 定義書のようにサーバー側の一覧を選ばせる印刷画面では、既存の `CvFlag.Msg042_GetTableList` と `SelectMultiWinViewModel.SetLocalData(...)` を優先する。新規 endpoint は、既存 flag で不足する場合だけ検討する。
- legacy qfm が固定の CSV 形を期待する場合は、SQL印刷へ寄せず `PrintByCsvParam` で `data.txt` の列順を明示する。
- 商品バーコード系の印刷では、`MasterShohin` と `DerivedShohinColSiz` の組み合わせを先に確認する。JAN 判定は `Jan1` / `Jan2` / `Jan3` を使い、印刷件数上限は `AppGlobal.Application.Limit` を明示的に確認してから出力する。
- `__serverimg__` を含む帳票 SQL は、サーバー側の `ReplaceServerSqlQuery()` 経路を確認してから qfm 側の画像項目と対応させる。

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

### SQL 印刷の列設計

- `Vdc` / `Vdu` は DB 上の ticks 値をそのまま出さず、`__serverdate__(Vdc) Vdcdate`、`__serverdate__(Vdu) Vdudate` のように変換して SELECT する。qfm 側では `S0.4"/"S4.2"/"S6.2" "S8.2":"S10.2":"S12.2` 形式で表示できる。
- qfm の先頭項目は既存帳票に合わせ、必要なら `Id, Vdcdate, Vdudate, Code, Name...` の順にする。順序を変えた場合は `itemN` と `datasrc` を必ず追従させる。
- `CreateListQueryParam()` と `query.AddWhereOrder()` を使い、検索条件・並び順・最大件数を画面一覧と揃える。
- SELECT の列名は CSV ヘッダーには出ないが、SQL検証時に意味が追えるよう `Shain`、`PayMethod`、`BankAccount1` など用途が分かる別名を付ける。

### JSON / SerializedColumn の扱い

- `CodeNameView` 系の JSON 列は、qfm に JSON 文字列を直接渡さず、帳票用の表示文字列へ展開する。

```sql
trim(ifnull(json_extract(VShain,'$.Cd'),'') || ' ' || ifnull(json_extract(VShain,'$.Mei'),'')) Shain
```

- `MasterToriDetail` のように `JsonProperty` が付いている詳細列は、C# プロパティ名ではなく JSON キーを確認する。例: `BankAccount1` は `$.Bank1`。
- 空値で余計な空白が出ないよう、コード + 名称は `trim(ifnull(...) || ' ' || ifnull(...))` を使う。
- JSON列が存在しない、または画面表示値を SQL だけで再現しづらい場合は、SQL印刷に固執せず `PrintByCsvParam` を検討する。

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
- 1レコードに項目が多い一覧は、1行に詰め込まず `record` の高さを広げ、コード/名称、更新情報、住所、支払条件、振込先など意味単位で複数行へ分ける。
- `datarecord/item/position length` はモデルの項目名・`ColumnSizeDml`・表示内容から決める。目安はコード12、名称80、略称/カナ100、住所60、電話20、`CodeNameView` 表示100、振込先30。
- qfm の表示幅 `text/position width` は用紙上のレイアウト幅で、CSV項目長とは別に考える。A4縦の明細領域では `x + width <= 150` を守る。
- ヘッダー行と明細行の項目名・位置を同じ意味単位で揃え、長い帳票では `font size="70"` など既存帳票より少し小さいフォントも検討する。
- 追加後に qfm の `itemN` 数、帳票内の `datasrc="itemN"` 参照数、SELECT/CSV列数が一致することを確認する。

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

validator の position warning は、legacy 帳票との差分を示すだけの場合がある。文字コード、XML構造、`itemN` と `datasrc`、SELECT/CSV列数が正しいかを分けて判断する。

## 確認手順

1. 対象 XAML の `DoOutputJsonCommand` が残っていないことを確認する。
2. qfm 検証スクリプトを実行する。
3. XAML を変更した場合は XML整形式・xmlns・resource・Converter・Bindingの確認を行う。
4. SQL印刷の場合は、可能なら `CvServer/server-cv00.db` など実DBに対して SELECT を実行し、列数が qfm の `itemN` 数と一致することを確認する。
5. `git diff --check` を実行する。
6. WPF クライアントをビルドする。

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"
```

7. 印刷サーバー環境が使える場合は、画面で F6 または印刷ボタンを押し、PDF表示画面が開くことを確認する。実行できない場合は理由を作業ログへ記録する。

## ログ

作業完了後は `Doc/aicoding_log.md` に、対象 View / ViewModel / qfm、qfm 検証、ビルド結果を記録する。
