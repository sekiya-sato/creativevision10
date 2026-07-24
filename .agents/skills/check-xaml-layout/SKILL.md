---
name: check-xaml-layout
description: Detects and fixes visual layout defects in CvWpfclient XAML - clipped text, insufficient margins, bottom/right cut-off, missing ScrollViewer, hardcoded sizes, and design inconsistency against shared MaterialDesign styles. Complements check-xaml (syntax/resource/binding) with a rendering-oriented layout review.
---

# Check XAML Layout

このスキルは、`CvWpfclient` の XAML について **見た目のレイアウト崩れ** を検出して修正するためのチェックリストとワークフローです。構文・リソース・バインディングの検証は `check-xaml`、実画面での目視確認は `verify-wpf-screen-runtime`、WPF全体の共通規約は `wpf-project-guide` が担当します。本スキルはその中間にあたる「ソース上の危険パターン検出 → 修正 → 目視確認」の一連を担います。

このスキルが検出・修正する対象：

- 文字の見切れ（`TextTrimming`/`TextWrapping` 欠如、固定幅過小、ラベル切れ）
- 余白不足（`Margin`/`Padding` 欠如、隣接コントロールの密着）
- 下端・右端の見切れ（`ScrollViewer` 欠如、固定サイズ、最下部ボタンの隠れ）
- レイアウト崩れ（`Grid` 行列定義の不整合、固定サイズのハードコード、`Alignment` 誤用）
- デザイン不統一（共通スタイル未使用、`SolidColorBrush` の直接指定、`DynamicResource` 未使用）

## いつ使うか

- 「XAMLのデザイン崩れ／レイアウト崩れをチェックして」「余白が足りない」「文字が見切れている」と依頼されたとき
- 画面を新規作成・大幅改修したあと、見た目の破綻がないか確認したいとき
- 複数画面をまとめて棚卸しし、見た目の品質を底上げしたいとき
- `check-xaml` で構文・リソースは通ったが、実画面でレイアウトが崩れる疑いがあるとき

## このスキルの責務と関連スキル

- 本スキル: XAML の **視覚的レイアウト崩れ** の検出と修正
- `check-xaml`: 構文・名前空間・リソース参照・コンバーター・バインディングパスの検証
- `verify-wpf-screen-runtime`: `CvServer`+`CvWpfclient` を起動しての実画面目視確認
- `wpf-project-guide`: 共通リソース・`BaseWindow`・`DynamicResource`・レイアウト注意点の共通規約
- `update-design-mente`: マスターメンテ画面を `MasterShohinMenteView` 系デザインへ統一するとき

構文とレイアウトの両方を見たい場合は、先に `check-xaml` で構文・リソースを通してから本スキルでレイアウトを見る。

## 前提とする共通スタイル（Cv固有）

修正時は独自スタイルを増やさず、`CvWpfclient/Resources/UIFormStyles.xaml` などの既存キーを優先する。主なもの：

- `FormLabel` / `SettingLabel`: フォームラベル（`VerticalAlignment=Center`、余白付き）
- `FormTextBox` / `NumericFormTextBox`: 入力欄（`Margin="0,4"`、右寄せは Numeric 側）
- `FormComboBox` / `FormDatePicker` / `FormPasswordBox`: 入力コントロール
- `MeisaiReadOnlyTextBlock` / `MeisaiRightReadOnlyTextBlock`: 明細読み取り（`TextTrimming=CharacterEllipsis` 付き）
- `SelectionResultText`: 選択結果表示（`TextTrimming` 付き）
- `MenteSearchTextBox` / `SearchTextBox`: 検索ボックス
- `MenteDataGridColumnHeader`: DataGrid ヘッダー（テーマカラー）
- `ToolCommandButton` / `BudgetActionButtonStyle`: 操作ボタン

色・ブラシはテーマ対応のため `DynamicResource`（例 `MaterialDesignPaper`、`MaterialDesign.Brush.Primary.*`）を使い、`SolidColorBrush` の直接指定を新設しない。

## 検出する不具合カテゴリと危険パターン

### 1. 文字の見切れ（テキストクリップ）

- **長文の可能性がある `TextBlock` に `TextTrimming`/`TextWrapping` が無い**
  幅が固定・親が `Stretch` でないと末尾が切れる。読み取り明細は `MeisaiReadOnlyTextBlock` 系を使う。
  ```xml
  <!-- ❌ 悪い例: 長い名称が黙って切れる -->
  <TextBlock Text="{Binding TokuiName}" Width="120" />

  <!-- ✅ 良い例 -->
  <TextBlock Text="{Binding TokuiName}" Style="{StaticResource MeisaiReadOnlyTextBlock}" />
  <!-- または -->
  <TextBlock Text="{Binding TokuiName}" TextTrimming="CharacterEllipsis" ToolTip="{Binding TokuiName}" />
  ```
- **ラベルや Button.Content に対して幅が小さすぎる固定 `Width`**
  日本語ラベルは想定より横に伸びる。固定幅より `MinWidth`＋`Auto` を優先。
- **`FontSize` を大きくした要素をコンテナ高さが吸収できていない**（縦の見切れ）。

### 2. 余白不足

- **隣接する入力コントロール/ボタンに `Margin` が無く密着している**
  共通スタイル（`FormTextBox` は `Margin="0,4"` 等）を使えば解消することが多い。
- **`Card`/`Border`/`GroupBox` の内側に `Padding` が無く、枠と中身が接触**
- **画面外周（ルート `Grid`）に余白が無く、端に貼り付いている**
  ```xml
  <!-- ✅ 例: 外周に余白 -->
  <Grid Margin="16">
  ```
- **DataGrid セルに `Padding` が無く、文字が罫線に接触**（`helpers:DataGridAssist.CellPadding` や列ヘッダースタイルで統一）。

### 3. 下端・右端の見切れ（cv10 で最も多い）

`wpf-project-guide` の通り、既存画面は **下端・右端が切れやすい**。重点確認する。

- **縦に伸びるフォーム／明細を包む `ScrollViewer` が無い**
  ウィンドウを縮めると最下部が隠れる。
  ```xml
  <!-- ✅ 例 -->
  <ScrollViewer VerticalScrollBarVisibility="Auto">
      <StackPanel> ... </StackPanel>
  </ScrollViewer>
  ```
- **最下部の操作ボタン行が、可変高さ領域と同じ `*` 行に置かれ押し出される**
  ボタン行は `Height="Auto"` の専用行に分離し、可変領域を `*` にする。
  ```xml
  <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />  <!-- ヘッダー -->
      <RowDefinition Height="*" />     <!-- 明細/フォーム（可変） -->
      <RowDefinition Height="Auto" />  <!-- 操作ボタン（常に見える） -->
  </Grid.RowDefinitions>
  ```
- **`Window`/`BaseWindow` の固定 `Height`/`Width` が内容に対して小さい、または `ResizeMode` 固定で内容が入り切らない**
- **`DataGrid` や `TabControl` の下に補足情報・ボタンがあり、グリッドが `*` を食い尽くして隠れる**。

### 4. レイアウト崩れ（構造の不整合）

- **`Grid.Row`/`Grid.Column` の値が `RowDefinitions`/`ColumnDefinitions` の数を超えている**（末尾要素が最終行に潰れて重なる）。
- **要素数と行列定義の数がずれている**（自動で 0 行目に重なる）。
- **横幅を固定でハードコードして合計が親を超える**（右端がはみ出す）。可変は `*`、内容依存は `Auto`。
- **`HorizontalAlignment="Left"` のまま `Stretch` を期待している入力欄**（右に間延び or 潰れ）。
- **`StackPanel Orientation="Horizontal"` に幅可変要素を入れて折り返さない**（右端見切れ）。可変は `Grid`/`DockPanel` を使う。

### 5. デザイン不統一

- **共通スタイルがあるのに素の `TextBox`/`Button`/`ComboBox` を使っている**
- **`Foreground`/`Background` に固定色や `SolidColorBrush` を直接指定**（ダークテーマで破綻）。`DynamicResource` に置換。
- **同種画面で余白・ボタン配置・列順がバラバラ**。近い既存画面（`MasterShohinMenteView` など）に合わせる。

## 静的検出パターン（grep 例）

全 View を機械的に洗い出すための出発点。ヒットは候補であり、文脈確認のうえ判定する。

```regex
# 固定 Width を持つ TextBlock（見切れ候補）
<TextBlock[^>]*\bWidth="\d+

# TextTrimming も TextWrapping も無い可能性がある TextBlock（長文バインド）
<TextBlock(?![^>]*Text(Trimming|Wrapping))[^>]*Text="\{Binding

# 直接色指定（テーマ非対応）
(Foreground|Background)="#[0-9A-Fa-f]{3,8}"
(Foreground|Background)="(Black|White|Gray|Red|Blue)"

# 固定ウィンドウサイズ
<(Window|helpers:BaseWindow)[^>]*\bHeight="\d+"[^>]*\bWidth="\d+"

# ScrollViewer 不在の確認（ファイル単位で有無を見る）
ScrollViewer
```

PowerShell/grep で対象を絞る例：

```bash
grep -rlE '(Foreground|Background)="#' CvWpfclient/Views
grep -Ln 'ScrollViewer' CvWpfclient/Views/**/*.xaml
```

## チェック実行手順

1. **対象の把握**: 対象 View を列挙し、`App.xaml` のマージ辞書と `UIFormStyles.xaml` の既存スタイルを確認する。
2. **静的スキャン**: 上記カテゴリと grep パターンで各 View を走査し、候補を行番号付きで収集する。
3. **文脈判定**: 候補ごとに、親コンテナ・行列定義・`Stretch` 有無を読み、本当に崩れるかを判定する（誤検出を落とす）。
4. **優先度付け**: 「文字見切れ・下端右端見切れ・操作不能」を高、「余白不足・軽微な不統一」を中〜低とする。
5. **修正**: 高確度から、共通スタイル流用・`Margin`/`Padding` 付与・`ScrollViewer` 追加・行定義の分離・`DynamicResource` 化で直す。独自スタイルは増やさない。
6. **ビルド確認**: 変更のたびに WPF クライアントをビルドする。
   ```bash
   /mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"
   ```
7. **目視確認**: 代表画面は `verify-wpf-screen-runtime` に従い実画面で確認する（下端・右端・余白・見切れ）。
8. **後片付けとログ**: CRLF/UTF-8 を保ち、`git diff --check` を確認し、`Doc/aicoding_log.md` に記録する。

## 修正方針の原則

- 既存の共通スタイル・リソースキーを最優先で流用し、`SolidColorBrush` やマジックナンバーを新設しない。
- 幅・高さは固定値より `Auto` と `*`、`MinWidth`/`MaxWidth` を優先する。
- 色・ブラシは `DynamicResource` にしてテーマ（Light/Dark、カラーバリエーション）で破綻させない。
- 「操作ボタン行」は常に `Height="Auto"` の専用行に置き、可変領域と分離する。
- バインディングやコマンド、`DataContext` 前提は壊さない。`Grid.Row`/`Grid.Column` の再配置に留める。
- `BaseWindow` 継承画面の初期化・Escape・Cancel の既定動作を壊さない。

## 報告フォーマット

`check-xaml` と同様、正常はサマリー、問題は詳細に報告する。

```
## レイアウトチェック結果: [ファイルパス or 範囲]

### 概要
- 対象: N ファイル / 検出 M 件（高 a / 中 b / 低 c）

### ❌ 高（見切れ・操作不能）
- [ファイル:行] カテゴリ: 症状 → 修正内容

### ⚠️ 中（余白・不統一）
- [ファイル:行] ...

### ℹ️ 低（軽微な統一）
- ...

### 修正済み / ビルド結果
- 修正 K 件、dotnet build 成功 / 未実行（理由）
- 目視確認: [画面名]（スクリーンショットで下端・右端・余白確認）
```

## 制限事項

- ソースからの静的検出は候補抽出であり、実際の崩れは解像度・DPI・データ長・テーマに依存する。高価値な画面は目視確認を併用する。
- 動的に生成される `DataTemplate` や `ItemsControl` の実寸は静的には判定しにくい。
- ピクセル単位の最終調整は実画面確認が前提。

## 更新履歴

- **v0.1.0 (2026-07-24)**: 初版。`check-xaml`（構文）から分離した視覚レイアウト崩れ検出・修正スキルとして作成。
