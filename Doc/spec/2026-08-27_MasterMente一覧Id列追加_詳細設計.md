# MasterMente系 一覧DataGridへのId列追加 詳細設計

## 背景・目的

MasterMente系（`コード / 名前 / 略称 / ...` の並びで一覧DataGridを持つマスタメンテ画面）で、一覧の左端に `Id` 列が無い。
運用上、コードのみでは同一コード重複時の区別や問い合わせ時の一意特定がしづらいため、一覧の先頭に `Id` 列を追加し、ロック（固定）列も `Id / コード / 名前` の3列に広げる。

`Id` の表示書式は、既存の `CodeNameDisplay.Format`（`CodeNameViewDisplayConverter.cs`）が定義している「`(Id) コード 名称`」という全社共通の「Idは括弧書き」表記に合わせ、`(123)` の形式で表示する。これにより後続のコード列（括弧なし）と視覚的に区別できる。

## 対象画面

`コード / 名前 / 略称` を一覧列に持つ、以下7画面が対象（`FrozenColumnCount="2"` かつ `Header="コード"` 列を持つグループ）。

| ファイル | ViewModel | DataGrid x:Name |
|---|---|---|
| [MasterShohinMenteView.xaml](CvWpfclient/Views/01Master/MasterShohinMenteView.xaml) | MasterShohinMenteViewModel | ShohinGrid |
| [MasterTokuiMenteView.xaml](CvWpfclient/Views/01Master/MasterTokuiMenteView.xaml) | MasterTokuiMenteViewModel | TokuiGrid |
| [MasterShiireMenteView.xaml](CvWpfclient/Views/01Master/MasterShiireMenteView.xaml) | MasterShiireMenteViewModel | ShiireGrid |
| [MasterShainMenteView.xaml](CvWpfclient/Views/01Master/MasterShainMenteView.xaml) | MasterShainMenteViewModel | ShainGrid |
| [MasterEndCustomerMenteView.xaml](CvWpfclient/Views/01Master/MasterEndCustomerMenteView.xaml) | MasterEndCustomerMenteViewModel | CustomerGrid |
| [MasterMaterialMenteView.xaml](CvWpfclient/Views/01Master/MasterMaterialMenteView.xaml) | MasterMaterialMenteViewModel | MaterialGrid |
| [MasterMeishoMenteView.xaml](CvWpfclient/Views/01Master/MasterMeishoMenteView.xaml) | MasterMeishoMenteViewModel | MeishoGrid |

### 対象外（理由）

- `MasterConfigMenteView.xaml` … 一覧列が `カテゴリ/フラグ名/値/...` で、コード・名前・略称型のマスタではない。
- `MasterSysKanriMenteView.xaml` … 単一レコード編集画面で一覧DataGridを持たない。
- `TranShopPromotionMenteView.xaml` / `TranTokuiPromotionMenteView.xaml` / `MasterYosanHanbaiMenteView.xaml` / `MasterYosanBrandMenteView.xaml` … 既に先頭に `Id` 列（`Header="Id"`、素の数値表示）を持っている。今回の変更対象は「コード/名前/略称型で Id 列が無い画面」なので対象外。ただし表示書式が `(123)` ではなく素の数値になっている点は本設計のスコープ外の別課題として扱う（必要なら別途申告）。

## 変更内容

対象7ファイルそれぞれで、一覧DataGridに対して以下2点を変更する。

### 1. `FrozenColumnCount` を `2` → `3` に変更

ロック列を `Id / コード / 名前` の3列にする。

```diff
- AutoGenerateColumns="False" EnableRowVirtualization="True" FrozenColumnCount="2" GridLinesVisibility="Horizontal"
+ AutoGenerateColumns="False" EnableRowVirtualization="True" FrozenColumnCount="3" GridLinesVisibility="Horizontal"
```

### 2. `DataGrid.Columns` の先頭に `Id` 列を追加

`コード` 列（各画面の最初の `DataGridTextColumn`）の直前に挿入する。`Binding` は列の並び順に関わらず既にListData行に存在する `Id` プロパティ（`long`）を使う。表示は `CodeNameDisplay.Format` と同じ「括弧付きId」記法に統一するため `StringFormat="({0})"` を用いる（新規コンバータは不要）。

```xml
<DataGridTextColumn Width="70"
	Binding="{Binding Id, StringFormat={}({0})}"
	Header="Id" />
```

適用例（`MasterShohinMenteView.xaml`）:

```diff
  <DataGrid.Columns>
+ 	<DataGridTextColumn Width="70"
+ 		Binding="{Binding Id, StringFormat={}({0})}"
+ 		Header="Id" />
  	<DataGridTextColumn Width="130"
  		Binding="{Binding Code}"
  		Header="コード" />
  	<DataGridTextColumn Width="220"
  		Binding="{Binding Name}"
  		Header="名前" />
  	...
```

`MasterMeishoMenteView.xaml` のみ現状「区分・コード・名前・略称」の順（`区分` が `コード` より前）だが、`Id` 列は他画面と同様に一覧の最先頭（`区分` のさらに前）へ置く。ロック列は「`Id` を含めて何列固定するか」という運用一貫性の指示なので、`区分` 列を含めて3列（`Id / 区分 / コード`）ではなく `Id / コード / 名前` の3列という指示文言を優先し、`FrozenColumnCount="3"` は他画面と同じ「先頭3列」を固定する扱いとする（＝ `Id / 区分 / コード` が固定される）。列順の変更は行わず `Id` 追加のみとする。

## 影響範囲

- **ViewModel変更なし** — `ListData` の行オブジェクトは既に `Id`（`long`）を保持している（例: `MasterShohinMenteViewModel.cs:129` の `x.Id == selectedShohinIdAfterList` 参照、各画面の詳細タブヘッダーで `CurrentEdit.Id` を表示済み）。列追加はXAMLのみの変更。
- **既存の `Id` 表示との整合** — 詳細タブヘッダー（`TabItem.Header` の `Id:{0}` 表記）や `SearchTextBoxAssist` 経由の参照先表示（`(Id) コード 名称`）とは別書式だが競合はしない。一覧列は単独で `Id` のみ括弧表示する。
- **ソート** — `DataGridTextColumn` は `Binding` のパスで自動ソートされるため `SortMemberPath` 指定は不要（`Id` は単純プロパティ）。
- **幅** — 既存の「素のId数値」列（例: `TranShopPromotionMenteView.xaml`）は `Width="90"` だが、括弧付き表示は桁が増えるため `Width="70"` を基本としつつ、Idが大きくなりやすい画面（Shohin等、将来的に5桁超が見込まれる場合）は `80` に調整可能とする。実装時に文字が切れないか目視確認する。

## 確認手順

1. `dotnet build "CvWpfclient/CvWpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド確認。
2. 各画面を起動し、一覧取得（F5）後に以下を目視確認:
   - 先頭列に `(Id)` 形式で表示される
   - 横スクロールしても `Id / コード / 名前` の3列が固定されたままになる
   - 既存の行選択・詳細タブ表示・修正/削除/追加が従来通り動作する

## 作業記録

完了後、`Doc/aicoding_log.md` に本設計に基づく実装内容を記録する（`update-design-mente` スキルのログ書式を流用）。
