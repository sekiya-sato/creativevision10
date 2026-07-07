## [2026-07-07] 15:47 改善バックログE（正しさ系）の実施
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：`.omo/IMPROVEMENTS.md` の「3. 推奨実施順」1番、E 正しさ系（リーク/握り潰し/ダークテーマ/SQL）を実施する
### 実施内容
- CvWpfclient/ViewModels/MainMenuViewModel.cs: `IDisposable` を実装し、ThemeChanged/MainThemeChanged の購読解除、時計/天気 `DispatcherTimer` の停止、Dispose 後の遅延タイマー生成防止を追加
- CvWpfclient/Views/MainMenuView.xaml.cs: MainMenu 終了時に ViewModel の `Dispose()` を呼び出す処理を追加
- CvWpfclient/Helpers/ClientLib.cs: `catch(Exception)` による握り潰しをやめ、Show/ShowDialog 差で想定される `InvalidOperationException` に限定
- CvWpfclient/Helpers/ViewModels/BaseMenteViewModel.cs, CvWpfclient/ViewModels/01Master/MasterShohinMenteViewModel.cs, CvWpfclient/ViewModels/00System/SysLoginViewModel.cs: 検索条件の Code/Name/JAN/LoginId を `@0` 形式のパラメータへ移行し、LIKE は `%`/`_` をエスケープ
- CvWpfclient/ViewModels/02Yosan/ShopBudgetReportViewModel.cs: 店舗コード範囲を `QueryListSqlParam.Parameters` に移し、印刷 SQL のユーザー入力連結を除去
- CvWpfclient/Resources/UIColors.xaml, CvWpfclient/Resources/UIColors.Dark.xaml: 符号色と DataGrid 交互行背景のテーマ別ブラシを追加
- CvWpfclient/Views/Sub/SelectShohinView.xaml, CvWpfclient/Views/08Zaiko/ZaikoQueryView.xaml, CvWpfclient/Helpers/Converters/NumericSignBrushConverter.cs: 固定 White/Beige/hex 色を DynamicResource またはテーマリソース参照へ変更
- Doc/aicoding_log.md: 既存ログが 883 行だったため `Doc/aicoding_log_009.md` に退避し、新規ログを作成
### 技術決定 Why
- MainMenu の静的イベント購読と `DispatcherTimer` は再ログイン/再生成時に古い ViewModel を保持し得るため、View の Closed から明示解放する
- SQL は既存の `QueryListSqlParam`/`QueryListParam` が持つ `Parameters` と `@0` 形式に合わせ、文字列連結の攻撃面と LIKE ワイルドカード誤一致を縮小する
- ダークテーマは `ThemeService` が `UIColors.xaml` と `UIColors.Dark.xaml` を差し替える構造のため、画面側は DynamicResource に寄せてテーマ追随させる
### 影響範囲
- CvWpfclient の MainMenu ライフサイクル、メンテ系検索条件、店舗予算印刷 SQL、在庫問合せ/商品選択のテーマ表示、数値符号色表示
### 確認
- git diff --check：成功
- XAML XML パース：ZaikoQueryView.xaml / SelectShohinView.xaml / UIColors.xaml / UIColors.Dark.xaml 成功
- CRLF 確認：変更した 12 ファイルすべて CRLF
- dotnet build CvWpfclient/CvWpfclient.csproj：初回は NuGet.Config 権限で失敗。権限付き再実行で成功（0 警告 / 0 エラー）

---
