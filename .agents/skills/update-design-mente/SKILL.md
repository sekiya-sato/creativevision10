---
name: update-design-mente
description: Guides the agent through unifying the material-design inspired layout across master maintenance windows in Cvnet10Wpfclient.
---

# Update Design Mente

このスキルは、Cvnet10Wpfclient のマスターメンテ画面を `MasterShohinMenteView` ベースの ColorZone + Card + MaterialDesignOutlined のデザインに揃えるためのワークフローとチェックリストを提供します。WPF全体の共通規約は `wpf-project-guide`、個別画面の基本手順は `wpf-view-workflow` を前提とし、本スキルはそのうえでデザイン統一に特化します。

## 目的

- マスタ群の見た目と操作感を一貫させる
- 支払情報や詳細内容タブの配置改善を行い、既存のデータ構造に影響を与えずに UI/UX を向上させる
- MasterShohinMenteView で使われている共通リソース（ColorZone、Card、DataGridAssist、FormTextBox、SearchTextBox）をテンプレートとして再利用する

## いつ使うか

- 商品マスタ以外のマスタ画面（社員、仕入、名称、顧客など）をデザイン統一する要求が出たとき
- 支払情報タブや詳細タブなど複数のセクションを持つ画面で、RowDefinitions/TabItem 構成を再配置する必要があるとき
- 登録日/修正日など共通の情報表示を追加する場合

## 手順

1. 既存の対象ビュー（例: `MasterShiireMenteView.xaml`）を読み込み、`MasterShohinMenteView.xaml` の構成を参照しながらColorZoneヘッダー、Cardレイアウト、MaterialDesignOutlined 入力をマッピングする
2. 支払情報タブでは `RowDefinitions` を再計算し、支払フラグ／支払方法／仕入先ブロックを1つずつ明確な行へ移動。仕入先検索は DockPanel や SearchTextBox を使い文言を統一する
3. 詳細内容タブの下部に `登録日/修正日` 表示を追加するには `MultiBinding` などで `InfoLabel` を構成し、`Row3` あたりに新しい `TextBlock` を配置してタイムスタンプを `FormTextBox` 風に見せる
4. 更新後は `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド確認（可能な限り）
5. `Doc/aicording_log.md` に作業履歴とスキル利用の説明を記録する

## 設計のポイント

- `ColorZone` は `Mode="PrimaryMid"` と `Color="PrimaryHueMidBrush"` を併用して統一感を出す
- Card 内のフォームは `helpers:FormTextBox` / `helpers:SearchTextBox` などのスタイルを利用し、余計な SolidColorBrush を直接指定しない
- DataGrid には `helpers:DataGridAssist.CellPadding` や `PrimaryHueMidBrush` の ColumnHeader を使い、既存のスタイルに合わせる
- 支払フラグなどのチェックボックスは `MaterialDesignCheckBox` か `CheckBox` の `Style` を MaterialDesign 風に揃える

## 報告/ログ

作業完了後は以下の形式で `Doc/aicording_log.md` に記録します：

```
## [YYYY-MM-DD] hh:mm 作業タイトル
### Agent
- [使用モデル : 提供者]
### Editor
- OpenCode
### 目的
- ...
### 実施内容
- 変更ファイルと要点
### 技術決定 Why
- 判断理由
### 確認
- dotnet build ... 成功 / 未実行
```

## よくある質問

### Q: 支払情報タブの Row を増やしたら既存バインディングが壊れる？
A: TabItem 内の Grid 構造を壊さず、`Grid.Row` 属性だけを調整すればバインディングに影響はありません。行の追加は `RowDefinitions` に `Height="Auto"` で柔軟に対応します。

### Q: ヘッダーの文字列やカラーはどこから取得する？
A: `MasterShohinMenteView` で使われている `ColorZone` や `Card` の URIs を参照し、同じリソースキー（例: `PrimaryHueMidBrush`）を利用することでテーマ連携できます。

### Q: 新しい skill で必ずビルドする必要がある？
A: 可能な限り `dotnet build` を実行してください。時間的制約がある場合、代わりに `dotnet build` を実行した旨をログで報告し、未実行理由を記載します。

## 更新履歴

- **v0.1.0 (2026-03-23)**: 初版（マスタ画面のデザイン統一および支払タブ改善に関する記録）
