# Check XAML Skill

WPF/XAMLファイルの品質チェックを実行するOpenCodeスキルです。

## 概要

このスキルは、WPF開発におけるXAMLファイルの以下のエラーを検出します：

- ✅ **構文エラー** - タグの不一致、属性の問題など
- ✅ **名前空間宣言** - 使用されているプレフィックスの検証
- ✅ **リソース参照** - StaticResource/DynamicResourceの存在確認
- ✅ **コンバーター参照** - Converterの定義チェック
- 🔧 **バインディングパス検証（オプション）** - ViewModelプロパティとの整合性

## インストール

このスキルはローカルプロジェクト用のため、既に `.agents/skills/check-xaml/` ディレクトリに配置されています。

OpenCodeが自動的に認識します。

## 使い方

### 基本的な使い方

```
XAMLファイル CvWpfclient/Views/04Juchu/JuchuInputView.xaml をチェックしてください
```

または

```
check-xaml CvWpfclient/Views/04Juchu/JuchuInputView.xaml
```

### バインディング検証をスキップする場合

```
XAMLファイル CvWpfclient/Views/MainWindow.xaml をチェックしてください（バインディング検証は不要）
```

## 出力例

```
## XAML検証結果: CvWpfclient/Views/04Juchu/JuchuInputView.xaml

### ✅ 合格項目
- 構文エラー: なし
- 名前空間宣言: OK (helpers, vm, materialDesign など)
- バインディングパス: すべて検証OK (34個のバインディングを確認)

### ⚠️ 警告
- バインディングパス: プロパティ「OldPropertyName」が見つかりません（行234）
  → ViewModelに該当プロパティを追加するか、バインディングパスを修正してください

### ❌ エラー
なし

### 📋 チェック詳細
- 合計行数: 456行
- リソース参照数: 12個 (すべて定義済み)
- バインディング数: 34個 (うち1個に警告)
- コンバーター使用: なし

### 🎯 推奨事項
1. 「OldPropertyName」バインディングを修正
```

**注**: 正常な項目はサマリーのみを表示し、エラー・警告は詳細に報告します。

## チェック項目の詳細

### 1. 構文エラーチェック
- XMLとして正しく解析できるか
- 開始タグと閉じタグの整合性
- 属性値のクォーテーション

### 2. 名前空間宣言チェック
- `xmlns:md="..."` などの宣言が存在するか
- 未宣言のプレフィックスを検出

### 3. リソース参照チェック
- `{StaticResource SomeKey}` のキーが定義されているか
- App.xamlおよびMergedDictionariesも確認

### 4. コンバーターチェック
- `Converter={StaticResource ...}` の参照先を検証

### 5. バインディングパス検証（オプション）
- `{Binding PropertyName}` のプロパティがViewModelに存在するか
- ユーザーの指示でスキップ可能

## プロジェクト固有の設定

このスキルは **Cv** プロジェクトに最適化されています：

### ディレクトリ構造
```
CvWpfclient/
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
- 同じサブディレクトリ構造を維持

## 制限事項

- `x:Bind` 構文は未サポート（`{Binding ...}` のみ対応）
- 動的DataContextの場合、バインディング検証の精度が低下
- 複雑なマークアップ拡張のネストは未サポート

## ライセンス

このスキルは Cv プロジェクト専用です。

## 更新履歴

- **v1.1.0** (2026-03-19): 報告フォーマット改善
  - 正常項目はサマリーのみ表示（詳細リスト削減）
  - エラー・警告は詳細報告を維持
- **v1.0.0** (2026-03-19): 初回リリース
