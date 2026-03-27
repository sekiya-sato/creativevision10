---
name: wpf-view-workflow
description: Defines the standard workflow for creating or updating individual WPF Views and ViewModels in Cvnet10Wpfclient, including menu wiring and validation steps.
---

# WPF View Workflow

このスキルは、`Cvnet10Wpfclient` の個別画面に対する新規作成・既存改修の実務手順を定義します。利用時は、先に `wpf-project-guide` の共通規約に従っている前提で進めます。

## 前提

- `wpf-project-guide` の内容を前提とする
- 共通リソース、`BaseWindow`、`DynamicResource`、レイアウト見切れ対策は本スキルではなく `wpf-project-guide` の規約に従う

## いつ使うか

- 新しい `*View.xaml` を追加するとき
- 対応する `*ViewModel.cs` を追加または更新するとき
- 既存のView / ViewModel を改修するとき
- 機能をメニューから起動可能にするため `Cvnet10Wpfclient/Models/MenuData.cs` を調整するとき

## 新規画面作成の基本手順

1. 近い既存画面を探し、配置・命名・継承関係をそろえる
2. `*View.xaml` を追加する
3. 対応する `*ViewModel.cs` を追加する
4. 必要に応じて `MenuData.cs` に起動導線を追加する
5. 共有リソースや共通スタイルを流用し、独自実装を最小化する
6. XAML / ビルド確認を行う

## 既存画面改修の基本手順

1. 対象Viewと対応ViewModelを両方確認する
2. 既存バインディング、コマンド、`DataContext` の前提を崩さないように変更する
3. code-behind にロジックを増やさず、可能な限りViewModelへ寄せる
4. 画面見切れ、既存スタイル破壊、共通リソースとの不整合を確認する
5. 変更規模に応じて XAML検証やWPFビルドを行う

## View 作成・改修の規約

- 既存命名規則に合わせて `*View.xaml` を使う
- `BaseWindow` を使うべき画面では素の `Window` を採用しない
- `ContentRendered` で `InitCommand` を重ねない
- 共通スタイルを使える箇所は `helpers:FormTextBox` など既存パターンを優先する
- リソース追加前に既存ResourceDictionaryを確認する

## ViewModel 作成・改修の規約

- 既存の基底ViewModelや周辺画面の実装パターンにそろえる
- 状態は `[ObservableProperty]`、操作は `[RelayCommand]` を優先する
- View都合のロジックを code-behind に逃がさず、必要最小限をViewModelへ置く
- 既存の保存、初期化、キャンセルフローを壊さない

## MenuData.cs を触る条件

- ユーザーが新規画面を起動可能にしたい場合は `Cvnet10Wpfclient/Models/MenuData.cs` の追加・更新を行う
- 内部利用のみの画面や既存導線から開かれる画面なら、不要なメニュー追加はしない

## よくある確認ポイント

- `View` と `ViewModel` の配置ディレクトリが対応しているか
- バインディング先のプロパティ名・コマンド名が一致しているか
- 既存の `DataTemplate`、Converter、ResourceKey を再利用できるか
- 追加した行やコントロールで下端・右端が切れていないか

## 検証手順

1. XAML変更がある場合、必要に応じて `check-xaml` を使う
2. 可能なら `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` を実行する
3. 形式確認が必要なら `dotnet format "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" --verify-no-changes` を使う

## 関連スキル

- `wpf-project-guide`: WPF全体規約
- `check-xaml`: XAML検証
- `update-design-mente`: マスターメンテ画面のデザイン統一
- `change-sublist-to-observablecollection`: DataGridのサブリスト通知問題の修正

## 更新履歴

- **v0.1.0 (2026-03-27)**: 画面単位の新規作成・改修手順を分離して初版作成
