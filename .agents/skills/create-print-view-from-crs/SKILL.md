---
name: create-print-view-from-crs
description: Migrates a Biz/Browser print dialog (CRS script) and its PrintStream qfm template into a new WPF View + ViewModel + qfm under CvWpfclient. Use when given a legacy .txt CRS file and a .qfm file, and you need to produce a new print-only screen in 01Master or similar.
---

# Create Print View from CRS + QFM

このスキルは、Biz/Browser の印刷ダイアログ（CRS スクリプト）と付属の PrintStream qfm 帳票を解析し、`CvWpfclient` 向けの新規印刷画面（View / ViewModel / qfm）を作成する手順です。

## 前提

- 先に `wpf-project-guide` と `wpf-view-workflow` を読み、WPF 共通規約を確認する。
- 印刷処理の基礎は `add-print-process-master-mente` を参照する。
- qfm の構文は `author-printstream-qfm` を参照する。
- `BaseMenteViewModel<T>` を継承し、`DoOutputPdfCommand` を使う印刷専用画面を作成する前提。

## いつ使うか

- Biz/Browser の `.txt` CRS ファイルと `.qfm` ファイルを与えられ、「これを WPF 画面に移行して」という指示を受けたとき
- 印刷範囲指定、バーコード選択、画像印刷などの印刷専用画面を新規作成するとき
- 元の CRS 画面は `OnTouch` で SQL を組み立てて印刷スプールしていたパターン

## 解析手順（CRS スクリプト）

1. **ファイルを開き、構造を把握する**
   - `SatooDialog` の `Title` → 画面タイトル / ViewModel の `Title`
   - `GroupBox` → 印刷範囲指定の条件グループ
   - `CvnetComboBox` / `CvnetBtList` → コード範囲選択（From / To）
   - `OptionButton` / `OptionItem` → バーコード種類やその他選択肢
   - `Button.OnTouch` → SQL の組み立て、qfm ファイル名の切り替え

2. **抽出する要素のリスト**

   | CRS 要素 | WPF への移行先 |
   |---|---|
   | `Title` | `ViewModel.Title` |
   | `CodeHin1` / `CodeHin2` | コード範囲 From / To（`SelectCodeParam`） |
   | `CodeHin3` / `CodeHin4` | 追加範囲 From / To（独自プロパティ） |
   | `OptionButton.Value` | `bool` / `int` プロパティ（`IsCode39` など） |
   | `OnTouch` 内の SQL | `PrintBySqlParam` の元ネタ |
   | `qfm_file` の切り替え | `FormFile` の条件付き返却 |
   | `image_path` の取得 | SQL 内の画像パス生成ロジック |

3. **SQL の読み取り方**
   - `select` 列を抽出し、qfm の `item1`, `item2`... との対応を作る
   - `where` 条件の `:1`, `:2`... パラメータを、画面の入力項目と対応させる
   - `json_extract` や `NVL` / `decode` などの関数を、そのまま SQL 文字列に流用できる
   - `order by` があれば `query.AddWhereOrder()` で引き継ぐ

4. **qfm ファイル名の切り替えパターン**
   - `if (OptionButton2.Value==0)` で `qfm_file` が変わる → `FormFile` を条件で切り替え
   - `UserFlg == 10` などの特殊分岐は、必要に応じて実装 or 保留

## 作成手順（ViewModel）

1. **新規クラスを作成**
   - `CvWpfclient/ViewModels/01Master/PrintXxxViewModel.cs`
   - `BaseMenteViewModel<MasterShain>` または対応するマスタ型を継承
   - `using CvWpfclient.Helpers;` を追加

2. **印刷条件プロパティを追加**

```csharp
[ObservableProperty]
string tenpoCodeFrom = string.Empty;

[ObservableProperty]
string tenpoCodeTo = "99999999";

[ObservableProperty]
bool isCode39 = true;

public PrintXxxViewModel() {
    SelectCodeParam = new() { DisplayName = "社員" };
}

protected override string? SelectCodeDisplayName => "社員";
```

3. **FormFile を条件切り替え**

```csharp
protected override string? FormFile => IsCode39 ? "PrintXxx39.qfm" : "PrintXxx.qfm";
```

4. **ListWhere をオーバーライド**

```csharp
protected override string? ListWhere => BuildListWhere();

string? BuildListWhere() {
    var codeWhere = BuildSelectCodeWhere(SelectCodeParam);
    var clauses = new List<string>();
    if (!string.IsNullOrEmpty(codeWhere))
        clauses.Add(codeWhere);
    if (!string.IsNullOrWhiteSpace(TenpoCodeFrom) && long.TryParse(TenpoCodeFrom, out var tenpoFrom))
        clauses.Add($"id_Tenpo >= {tenpoFrom}");
    if (!string.IsNullOrWhiteSpace(TenpoCodeTo) && long.TryParse(TenpoCodeTo, out var tenpoTo))
        clauses.Add($"id_Tenpo <= {tenpoTo}");
    return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
}
```

5. **PrintBySqlParam を実装**

```csharp
protected override QueryListSqlParam? PrintBySqlParam {
    get {
        var query = CreateListQueryParam();
        var sql = $@"
select A.Code, A.Name,
coalesce(json_extract(A.Jdetail, '$.yobi1'), '') 画像,
A.id_Tenpo,
coalesce(T.Name, '') 店舗名,
coalesce((select S.Name from MasterSysKanri S limit 1), '') 自社名,
case when coalesce(json_extract(A.Jdetail, '$.yobi1'), '')='' then 0 else 1 end 画像表示判定用
from MasterShain A
left join MasterTokui T on T.Id = A.id_Tenpo
{query.AddWhereOrder()}
";
        return new QueryListSqlParam(typeof(MasterShain), sql, query.Parameters);
    }
}
```

6. **InitCommand**

```csharp
[RelayCommand]
async Task Init() => await DoList(CancellationToken.None);
```

## 作成手順（View）

1. **新規 XAML を作成**
   - `CvWpfclient/Views/01Master/PrintXxxView.xaml`
   - `helpers:BaseWindow` を継承
   - `Width="800" Height="600"` 程度の印刷ダイアログサイズ

2. **InputBindings**
   - `F6` → `DoOutputPdfCommand`
   - `Esc` → `ExitCommand`

3. **レイアウト構成**
   - `ColorZone` ツールバーに「印刷」「戻る」ボタン
   - `Grid` 内に範囲指定入力（コード From/To、店舗ID From/To）
   - `RadioButton` でバーコード種類選択（CODE39 / NW7）
   - `InverseBooleanConverter` を使う場合は `App.xaml` に登録済みか確認
   - 「印刷実行」ボタン（中央配置）
   - 対応ラベル情報（A-one 品番など）を `TextBlock` で表示

4. **バインディング例**

```xml
<TextBox Text="{Binding SelectCodeParam.FromCode}" materialDesign:HintAssist.Hint="From" />
<TextBox Text="{Binding SelectCodeParam.ToCode}" materialDesign:HintAssist.Hint="To" />
<TextBox Text="{Binding TenpoCodeFrom}" materialDesign:HintAssist.Hint="From" />
<TextBox Text="{Binding TenpoCodeTo}" materialDesign:HintAssist.Hint="To" />
<RadioButton Content="CODE39" IsChecked="{Binding IsCode39}" />
<RadioButton Content="NW7" IsChecked="{Binding IsCode39, Converter={StaticResource InverseBooleanConverter}}" />
```

## 作成手順（QFM）

1. **元の QFM をコピー**
   - `Doc/wrk/xxx.qfm` → `printform/PrintXxx.qfm`
   - ファイル名は `FormFile` プロパティと完全一致させる

2. **Shift_JIS で保存**
   - UTF-8 のまま保存しないこと
   - WSL/Linux の場合: `iconv -f UTF-8 -t SHIFT_JIS xxx.qfm > xxx.qfm.sjis && mv xxx.qfm.sjis xxx.qfm`

3. **datarecord/item を調整**
   - `PrintBySqlParam` の `select` 列順と一致させる
   - `length` は CSV 項目の想定長さ（コード12、名称80、画像パス120など）

4. **バーコード種類の切り替え**
   - 元の QFM が NW7（`type="2"`）ならそのまま
   - CODE39 版（`type="1"`）が必要ならコピーして `sed -i 's/type="2"/type="1"/g'`
   - または `cp PrintXxx.qfm PrintXxx39.qfm` して変更

5. **カード型レイアウトの注意**
   - `page/position` が標準 A4 縦（`x=8 y=8 width=156 height=272`）と異なる場合は、カード型レイアウトとして意図的に変更していることを確認
   - `region` が複数連結（`link="Rgn02"`）している場合はカード型レイアウトの可能性が高い
   - validator の位置チェックは「標準ではない」としてスキップしても構わないが、XML 構造は検証する

## 作成手順（MenuData）

```csharp
new("社員証カード印刷", typeof(Views._01Master.PrintXxxView), addInfo:"社員証カード型印刷"),
```

## 確認手順

1. **QFM 検証**

```bash
python3 .agents/skills/add-print-process-master-mente/scripts/validate_qfm.py printform/PrintXxx.qfm
```

- カード型レイアウトの場合は位置チェックが出る可能性がある → それは無視して構造を確認

2. **WPF ビルド**

```powershell
C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"
```

3. **XAML 構文確認**
   - `check-xaml` スキルを使うか、XML として読み込めることを確認

4. **Converter 確認**
   - `InverseBooleanConverter` など独自 Converter を使った場合は `App.xaml` に登録されているか確認

## ログとコミット

- `Doc/aicoding_log.md` に対象 View / ViewModel / qfm、CRS ファイル名、ビルド結果を記録
- `git add -A && git commit` でコミット

## 関連スキル

- `wpf-project-guide`: WPF 共通規約
- `wpf-view-workflow`: 画面単位の作成手順
- `add-print-process-master-mente`: 印刷処理の基礎
- `author-printstream-qfm`: qfm の構文ガイド
- `check-xaml`: XAML 検証

## 更新履歴

- **v0.1.0 (2026-06-05)**: 社員証カード印刷画面の作成から初版を作成
