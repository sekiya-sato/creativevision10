---
name: wpf-project-guide
description: Provides the shared Cvnet10Wpfclient project conventions, resource usage, BaseWindow rules, validation flow, and build checks required before any WPF-related work.
---

# WPF Project Guide

このスキルは、`Cvnet10Wpfclient` に関わるすべての作業で最初に参照する共通ガイドです。個別画面の新規作成や改修の前提となる、プロジェクト全体の構成・MVVM規約・リソース利用・検証方針を定義します。

## いつ使うか

- `Cvnet10Wpfclient` 配下の `View` / `ViewModel` / `Helpers` / `Resources` / `MenuData.cs` を編集するとき
- XAML のレイアウト、テーマ、ResourceDictionary、`BaseWindow` の扱いが関係するとき
- WPF画面を新規作成する前に、プロジェクト共通ルールを確認したいとき
- 既存画面を改修する際に、既存の実装パターンと共通制約を確認したいとき

## このスキルの責務

- `Cvnet10Wpfclient` 全体の共通ルールを示す
- 画面単位の作業前提を整理する
- 詳細な画面追加・改修手順は `wpf-view-workflow` へ委譲する
- XAML検証は `check-xaml`、マスターメンテのデザイン統一は `update-design-mente` へ委譲する

## まず確認すること

1. 対象が本当に `Cvnet10Wpfclient` の変更か確認する
2. 影響ファイルを把握する
3. `Cvnet10Wpfclient/App.xaml` を確認し、既存の `MergedDictionaries` を把握する
4. 共有リソースを直接増やす前に、既存スタイルとリソースキーを再利用できるか確認する

## プロジェクト構造の前提

- View は主に `Cvnet10Wpfclient/Views/` 配下
- ViewModel は主に `Cvnet10Wpfclient/ViewModels/` 配下
- 共通リソースは `Cvnet10Wpfclient/Resources/` 配下
- 共通UI補助は `Cvnet10Wpfclient/Helpers/` 配下
- 画面起動導線は `Cvnet10Wpfclient/Models/MenuData.cs` が関わる場合がある

## 共通WPF / MVVM規約

- ViewModel は `ObservableObject`、`ObservableRecipient`、または既存の基底ViewModelを使う
- 状態は `[ObservableProperty]`、コマンドは `[RelayCommand]` または async relay command を優先する
- code-behind は初期化とUI固有イベント委譲に限定し、業務ロジックを持ち込まない
- 既存パターンがある場合は、新規流儀を作らず対象近傍の実装に合わせる

## Window / BaseWindow 規約

- 業務画面やマスターメンテ画面では `Window` ではなく `Cvnet10Wpfclient.Helpers.BaseWindow` を優先する
- `BaseWindow` は `InitCommand` 実行、EscapeによるClose、`*CancelCommand` 実行を既に持つ
- そのため `BaseWindow` 継承Viewに `ContentRendered` で `InitCommand` を再度起動する仕組みを追加しない

## Resource / Theme 規約

- テーマ対応が必要な色やブラシは `DynamicResource` を優先する
- 先に `Cvnet10Wpfclient/Resources/UIColors.xaml`、`Cvnet10Wpfclient/Resources/UICommon.xaml`、`Cvnet10Wpfclient/Resources/UIMainWindow.xaml` を確認する
- 直接 `SolidColorBrush` を散在させるより、既存キーや共通スタイルを優先する
- WPFリソースや例外が絡む場合は、`Cvnet10Wpfclient/App.xaml` と参照先ResourceDictionaryを先に読む

## レイアウト注意点

- 既存画面は下端・右端が切れやすいため、余白・`Grid` 構造・`ScrollViewer` 有無を重点確認する
- フォーム追加時は縦方向だけでなく横方向の収まりも確認する
- DataGrid や TabControl を含む画面では、最下部操作ボタンや補足情報が隠れないか注意する

## 画面単位スキルとの関係

- 個別の `*View.xaml` / `*ViewModel.cs` の新規作成や既存画面改修は `wpf-view-workflow` を併用する
- 画面単位スキルは本スキルの前提規約を継承する

## 関連スキルの使い分け

- `wpf-view-workflow`: 画面単位の新規作成・改修手順
- `check-xaml`: XAML構文、リソース、バインディングの検証
- `update-design-mente`: マスターメンテ画面を既存MaterialDesign系レイアウトへ統一するとき
- `change-sublist-to-observablecollection`: マスターメンテ画面のサブリスト通知不具合を修正するとき

## 推奨確認手順

1. 必要なView / ViewModel / Resource / MenuDataの変更を行う
2. XAML変更がある場合は必要に応じて `check-xaml` を使う
3. 可能なら `dotnet build "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` を実行する
4. 形式崩れが疑われる場合は `dotnet format "Cvnet10Wpfclient/Cvnet10Wpfclient.csproj" --verify-no-changes` を使う
5. 作業完了後は `Doc/aicording_log.md` に記録する

## 更新履歴

- **v0.1.0 (2026-03-27)**: `AGENTS.md` のWPF共通規約を分離して初版作成
