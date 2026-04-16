## [YYYY-MM-DD] hh:mm 作業タイトル
### Agent
- [使用した AI Model 名 : AI Provider 名]
### Editor
- [使用したエディタ: 不明な場合は"VS2026", 例 "VS2026", "VSCode", "OpenCode", "GitHubCopilot-Cli"] 
### 目的
- ユーザーからの要望：[内容]
### 実施内容
- [プロジェクト名]/[ファイル名]: [変更内容の要約]
### 技術決定 Why
- [例: ProtobufのOrder欠番を避けるため、既存のFlag定義を維持しつつ新機能を追加した]
### 影響範囲 (省略可)
- 大規模変更の場合は影響範囲を明記。修正したファイルのみの場合は省略
### 確認
- [Buildした結果を確認。クロスプラットフォームの場合はBuild Error がでる可能性があるので省略可]

---
## [2026-04-16] 12:00 専用の郵便番号検索結果選択ダイアログの実装
### Agent
- gemini-3.1-pro-preview : github-copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：郵便番号検索で複数結果が出た場合、専用の住所選択ダイアログ（郵便番号と住所の2カラム表示）を表示したい
### 実施内容
- CvWpfclient/Views/Sub/SelectPostalAddressView.xaml: 新規作成。SelectWinViewをベースにPostalAddressItem専用のUI（2カラム）へ変更
- CvWpfclient/Views/Sub/SelectPostalAddressView.xaml.cs: 新規作成。DataGridへの初期フォーカス・選択処理を実装
- CvWpfclient/ViewModels/Sub/SelectPostalAddressViewModel.cs: 新規作成。PostalAddressItemのリストと直接バインディング
- CvWpfclient/Helpers/PostalAddressSearchHelper.cs: 検索結果が複数ある場合、SelectWinViewの代わりにSelectPostalAddressViewを呼び出すよう修正し、dynamicによる表示用ラッパークラスを削除
### 技術決定 Why
- 既存のSelectWinViewModelはdynamic型やBaseDbClassに依存しており、API通信用データ(PostalAddressItem)のUI表示で型安全性を失っていたため。専用VMとViewを作成し、直接バインディングすることで型安全を確保しつつUIも2カラムに最適化した。
### 確認
- `dotnet build "CvWpfclient/CvWpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド成功を確認。

---
