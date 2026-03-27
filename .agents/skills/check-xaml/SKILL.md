---
name: check-xaml
description: Validates XAML files for syntax errors, resource references, namespace declarations, and optionally binding paths against ViewModels. Designed for WPF/XAML projects in .NET development.
---

# Check XAML

このスキルは、WPF/XAMLファイルの品質チェックを実行し、以下のエラーを検出します：
- 構文エラー（閉じタグの不一致、不正な属性など）
- 名前空間宣言の欠落
- リソース参照の不備（StaticResource/DynamicResourceの存在確認）
- コンバーター参照の検証
- **オプション**: バインディングパスのViewModel整合性チェック

## このスキルを使用するタイミング

以下の状況でこのスキルを使用してください：

- XAMLファイルを新規作成または大幅に変更した後
- ビルドエラーやランタイムバインディングエラーが発生している場合
- コードレビュー前の品質チェック
- リファクタリング後の整合性確認
- ユーザーが明示的に「XAMLをチェックして」「XAMLエラーを確認して」と依頼した場合

Cvnet10 では、WPF作業の共通規約は `wpf-project-guide`、画面単位の追加・改修手順は `wpf-view-workflow` が担当します。本スキルはその後段の検証専用スキルです。

## 使用方法

### 基本的な使い方

```
XAMLファイル [ファイルパス] をチェックしてください
```

または

```
check-xaml [ファイルパス]
```

### バインディングパス検証をスキップする場合

ユーザーが「バインディングチェックは不要」「バインディング検証を無視して」と指示した場合、
バインディングパス検証をスキップしてください。

```
XAMLファイル [ファイルパス] をチェックしてください（バインディング検証は不要）
```

## チェック項目

### 1. 構文エラーチェック（必須）

以下の構文エラーを検出します：

- **開始タグと閉じタグの不一致**
  ```xml
  <!-- ❌ 悪い例 -->
  <Button Content="OK" />
  </Button>
  
  <!-- ✅ 良い例 -->
  <Button Content="OK" />
  ```

- **属性値のクォーテーション不備**
  ```xml
  <!-- ❌ 悪い例 -->
  <Button Content=OK />
  
  <!-- ✅ 良い例 -->
  <Button Content="OK" />
  ```

- **不正なXML文字（エスケープ未処理）**
  ```xml
  <!-- ❌ 悪い例 -->
  <TextBlock Text="A & B" />
  
  <!-- ✅ 良い例 -->
  <TextBlock Text="A &amp; B" />
  ```

### 2. 名前空間宣言チェック（必須）

使用されているプレフィックスに対して、名前空間宣言が存在するかを確認します。

```xml
<!-- ❌ 悪い例: md名前空間が宣言されていない -->
<Window x:Class="MyApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <md:Card />
</Window>

<!-- ✅ 良い例 -->
<Window x:Class="MyApp.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:md="http://materialdesigninxaml.net/winfx/xaml/themes">
    <md:Card />
</Window>
```

### 3. リソース参照チェック（必須）

`StaticResource`や`DynamicResource`で参照されているリソースキーが、以下の場所に定義されているかを確認します：

- 同一ファイル内の`<Window.Resources>`や`<UserControl.Resources>`
- `App.xaml`の`<Application.Resources>`
- `App.xaml`でマージされている`ResourceDictionary`ファイル

```xml
<!-- ❌ 悪い例: PrimaryBrushが定義されていない -->
<Button Background="{StaticResource PrimaryBrush}" />

<!-- ✅ 良い例 -->
<Window.Resources>
    <SolidColorBrush x:Key="PrimaryBrush" Color="Blue" />
</Window.Resources>
<Button Background="{StaticResource PrimaryBrush}" />
```

### 4. コンバーター参照チェック（必須）

`Converter`プロパティで参照されているコンバーターがリソースとして定義されているかを確認します。

```xml
<!-- ❌ 悪い例: BoolToVisibilityConverterが定義されていない -->
<TextBlock Visibility="{Binding IsVisible, Converter={StaticResource BoolToVisibilityConverter}}" />

<!-- ✅ 良い例 -->
<Window.Resources>
    <local:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />
</Window.Resources>
<TextBlock Visibility="{Binding IsVisible, Converter={StaticResource BoolToVisibilityConverter}}" />
```

### 5. バインディングパス検証（オプション）

**デフォルトで有効ですが、ユーザーが無効化を指示した場合はスキップします。**

`Binding`のPathプロパティが、対応するViewModelクラスに存在するかを確認します。

#### ViewModelの特定方法

1. XAMLの`x:Class`属性から、対応するViewを特定
2. 命名規則に基づいてViewModelを推測：
   - `Views/SomeView.xaml` → `ViewModels/SomeViewModel.cs`
   - `MyView.xaml` → `MyViewModel.cs`
3. ViewModelクラスのプロパティ一覧を取得

#### チェック対象

```xml
<!-- ✅ ViewModelにIsEnabledプロパティが存在する -->
<Button IsEnabled="{Binding IsEnabled}" />

<!-- ❌ ViewModelにNonExistentPropertyが存在しない -->
<Button Content="{Binding NonExistentProperty}" />

<!-- ✅ ItemsSourceバインディング先のコレクション内のプロパティ -->
<DataGrid ItemsSource="{Binding DetailItems}">
    <DataGrid.Columns>
        <!-- DetailItemsのアイテム型のNoプロパティをチェック -->
        <DataGridTextColumn Header="No" Binding="{Binding No}" />
    </DataGrid.Columns>
</DataGrid>
```

#### スキップ条件

以下の場合、バインディングパス検証をスキップします：

- ユーザーが「バインディング検証は不要」「バインディングチェックをスキップ」と指示した場合
- ViewModelファイルが見つからない場合（警告を出力）
- DataContextが動的に設定されている場合

## チェック実行手順

### ステップ1: ファイルの読み込み

指定されたXAMLファイルを読み込み、構文エラーをチェックします。

```bash
# Readツールでファイルを取得
```

### ステップ2: 名前空間宣言の検証

XAMLルート要素の名前空間宣言を抽出し、使用されているプレフィックスとマッチングします。

### ステップ3: App.xamlのリソース確認

プロジェクト内の`App.xaml`を読み込み、定義されているリソースキーと`MergedDictionaries`をリストアップします。

```bash
# App.xamlの場所を特定
find . -name "App.xaml" -type f
```

### ステップ4: リソース参照の検証

`StaticResource`/`DynamicResource`で参照されているキーが、ステップ3で取得したリソースに存在するかを確認します。

正規表現例：
```regex
\{(?:StaticResource|DynamicResource)\s+([a-zA-Z0-9_]+)\}
```

### ステップ5: コンバーター参照の検証

`Converter={StaticResource ...}`パターンを検出し、リソースに定義されているかを確認します。

### ステップ6: バインディングパス検証（オプション）

**ユーザーが無効化を指示していない場合のみ実行**

1. XAMLファイルのパスからViewModelファイルパスを推測
2. ViewModelファイルを読み込み、プロパティ一覧を抽出
3. `Binding`のPathプロパティと照合

正規表現例：
```regex
\{Binding\s+([a-zA-Z0-9_.]+)
```

ViewModelプロパティ抽出（C#）：
```regex
public\s+\w+(?:<[^>]+>)?\s+([a-zA-Z0-9_]+)\s*\{
```

### ステップ7: 結果の報告

検出されたエラーと警告を、以下の形式で報告します：

#### 報告フォーマットの原則

**エラー・警告**: 詳細に報告（すべて列挙）
- すべてのエラーを行番号とともに詳細に記載
- すべての警告を具体的に記載
- 修正方法の提案を含める

**正常項目**: サマリーと主要項目のみ
- 「✅ 合格項目」セクションは簡潔に（項目名と "OK" のみ）
- バインディングパス検証が正常な場合は、詳細な一覧を出さず「すべて正常」とサマリーのみ報告
- リソース参照が正常な場合も、すべてを列挙せず「すべて定義済み」とサマリーのみ報告
- ただし、特に重要な項目（DataContext設定、主要なコマンドバインディングなど）は代表例として1-3個程度言及可能

#### 報告例（簡潔版）

```
## XAML検証結果: [ファイルパス]

### ✅ 合格項目
- 構文エラー: なし
- 名前空間宣言: OK (helpers, vm, materialDesign など)
- バインディングパス: すべて検証OK (50個以上のバインディングを確認)

### ⚠️ 警告
- リソース参照: 未定義のリソースキー「PrimaryBrush」が使用されています（行123）
  → App.xaml または Resources/UIColors.xaml に定義を追加してください

### ❌ エラー
- 構文エラー: 閉じタグ不一致 </Button> （行456）
  → 開始タグが自己完結型 <Button .../> のため、閉じタグは不要です
- コンバーター参照: 未定義のコンバーター「BoolToVisConverter」（行789）
  → App.xaml の Application.Resources に以下を追加してください:
    <helpers:BoolToVisibilityConverter x:Key="BoolToVisConverter" />

### 📋 チェック詳細
- 合計行数: 415行
- リソース参照数: 11個 (うち2個が未定義)
- バインディング数: 50個 (すべて検証OK)
- コンバーター使用: なし

### 🎯 推奨事項
1. [修正が必要な項目の優先順位付きリスト]
2. [将来的な改善提案]
```

## プロジェクト固有の設定

このプロジェクト（Cvnet10）では、以下の命名規則とディレクトリ構造を前提とします：

### ディレクトリ構造
```
Cvnet10Wpfclient/
├── Views/
│   └── 04Juchu/
│       └── JuchuInputView.xaml
├── ViewModels/
│   └── 04Juchu/
│       └── JuchuInputViewModel.cs
├── App.xaml
└── Resources/
    ├── UIColors.xaml
    ├── UICommon.xaml
    └── UIMainWindow.xaml
```

### 命名規則
- View: `*View.xaml`
- ViewModel: `*ViewModel.cs`
- ViewとViewModelは同じサブディレクトリ構造（例: `Views/04Juchu/` → `ViewModels/04Juchu/`）

### リソースファイル
App.xamlでマージされているResourceDictionaryファイル：
- `Resources/UIColors.xaml`
- `Resources/UICommon.xaml`
- `Resources/UIMainWindow.xaml`

これらのファイルもリソース参照チェック時に確認対象とします。

## 実装例

### チェックスクリプトの例

エージェントは以下のような手順でチェックを実行します：

```javascript
// 疑似コード
async function checkXaml(filePath, skipBindingCheck = false) {
  // 1. XAMLファイルを読み込み
  const xamlContent = await readFile(filePath);
  
  // 2. 構文チェック
  const syntaxErrors = checkSyntax(xamlContent);
  
  // 3. 名前空間チェック
  const namespaceErrors = checkNamespaces(xamlContent);
  
  // 4. App.xamlとリソースファイルを読み込み
  const appXaml = await readFile("App.xaml");
  const resourceFiles = extractMergedDictionaries(appXaml);
  const allResources = await loadAllResources(resourceFiles);
  
  // 5. リソース参照チェック
  const resourceErrors = checkResourceReferences(xamlContent, allResources);
  
  // 6. コンバーター参照チェック
  const converterErrors = checkConverterReferences(xamlContent, allResources);
  
  // 7. バインディングパス検証（オプション）
  let bindingErrors = [];
  if (!skipBindingCheck) {
    const viewModelPath = inferViewModelPath(filePath);
    const viewModelContent = await readFile(viewModelPath);
    const properties = extractProperties(viewModelContent);
    bindingErrors = checkBindingPaths(xamlContent, properties);
  }
  
  // 8. 結果を報告
  return reportResults({
    syntaxErrors,
    namespaceErrors,
    resourceErrors,
    converterErrors,
    bindingErrors,
    skippedBinding: skipBindingCheck
  });
}
```

## よくある質問

### Q: 報告される情報が多すぎませんか？

A: **正常な項目はサマリーのみ表示**されます。詳細リストが表示されるのはエラーや警告がある場合のみです。これにより、問題がある箇所に集中でき、レビュー効率が向上します。

### Q: バインディング検証を無効化する方法は？

A: ユーザーが以下のいずれかの表現で指示した場合、バインディングパス検証をスキップします：
- 「バインディングチェックは不要」
- 「バインディング検証をスキップ」
- 「バインディングは無視して」

### Q: ViewModelが見つからない場合はどうなりますか？

A: 警告として報告し、バインディングパス検証をスキップします。他のチェックは継続します。

### Q: カスタムコントロールのプロパティはどう扱いますか？

A: カスタムコントロールの定義ファイルが読み込めない場合は、そのプロパティに対する検証をスキップします。

### Q: x:Bindはサポートしていますか？

A: 現在のバージョンでは`{Binding ...}`のみサポートしています。`{x:Bind ...}`は将来のバージョンで対応予定です。

## 制限事項

- `x:Bind`構文はサポート対象外
- 動的にDataContextを設定する場合、バインディング検証の精度が低下する可能性があります
- マークアップ拡張の複雑なネストはサポート対象外
- コンバーターのパラメーター検証は行いません

## 更新履歴

- **v1.1.0** (2026-03-19): 報告フォーマット改善
  - 正常項目はサマリーのみ表示（全バインディング列挙を廃止）
  - エラー・警告は詳細報告を維持し、修正提案を追加
  - チェック詳細セクションで統計サマリーを提供
- **v1.0.0** (2026-03-19): 初回リリース
  - 構文エラーチェック
  - 名前空間宣言チェック
  - リソース参照チェック
  - コンバーター参照チェック
  - オプショナルなバインディングパス検証
