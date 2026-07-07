## [2026-07-07] 16:49 改善バックログF（堅牢性/保守性）の実施
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：`.omo/IMPROVEMENTS.md` の改善案 F「堅牢性 / 保守性」を実施する
### 実施内容
- CvWpfclient/Helpers/ViewModels/BaseViewModel.cs: `IViewModelLifecycle` を追加し、BaseViewModel 系で Exit/Init 実行、実行中コマンド判定、CancelCommand 実行を扱う共通入口を実装
- CvWpfclient/Helpers/Windows/BaseWindow.cs: DataContext が `IViewModelLifecycle` の場合は型付き入口を優先し、非対応 ViewModel だけ既存リフレクション処理にフォールバックするよう変更
### 技術決定 Why
- BaseWindow から ViewModel のコマンド名文字列と全プロパティ走査を直接扱う責務を減らし、BaseViewModel 系のライフサイクル処理を共通化する
- `MainMenuViewModel` や `WebPdfViewModel` など BaseViewModel 非継承の画面を一括変更しないため、旧リフレクション経路は互換用に残す
- 派生 ViewModel の `InitCommand` は CommunityToolkit のソースジェネレータで生成されるため、BaseViewModel に同名プロパティを追加せず、メソッド経由で実行する
### 影響範囲
- CvWpfclient の BaseWindow 派生画面の ESC 終了、初期化コマンド実行、非同期コマンド実行中判定、終了時キャンセル処理
### 確認
- git diff --check：成功
- XAML XML パース：CvWpfclient/Views と CvWpfclient/Resources 配下で成功
- dotnet build CvWpfclient/CvWpfclient.csproj：通常出力先は CreativeVision10 (33368) と Visual Studio (12848) の DLL ロックで失敗。別 OutDir 指定で再実行し成功（0 警告 / 0 エラー）

---

## [2026-07-07] 16:36 改善バックログB（ウィンドウ最小サイズ）の実施
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：`.omo/IMPROVEMENTS.md` の改善案 B「ウィンドウ最小サイズ / サイズ定数」を実施する
### 実施内容
- CvWpfclient/Helpers/Windows/BaseWindow.cs: 表示サイズ確定後に既定最小サイズ 640x480 を適用する処理を追加。小型ダイアログは初期サイズを上限にして見た目を変えないよう調整
- CvWpfclient/Resources/UICommon.xaml: 標準ウィンドウサイズ `StandardWindowWidth` / `StandardWindowHeight` を追加
- CvWpfclient/Views/**/*.xaml: `Width="1244" Height="900"` の標準画面サイズ指定を共通リソース参照へ置換
### 技術決定 Why
- `BaseWindow.EnsureWithinDisplayBounds()` による縮小後も極端な手動リサイズで下部操作列が見切れないよう、BaseWindow 側で共通の最小サイズを持たせる
- Login など 640x480 より小さい既存画面は初期サイズを最小値として扱い、既存表示サイズを不用意に拡大しない
- 多数の画面で重複していた 1244x900 を共通リソース化し、今後の標準サイズ変更箇所を一本化する
### 影響範囲
- CvWpfclient の BaseWindow 派生画面全般、および 1244x900 を使う標準サイズ画面
### 確認
- XAML XML パース：CvWpfclient/Views と CvWpfclient/Resources 配下で成功
- `Width="1244" Height="900"` 残存確認：0 件
- git diff --check：成功
- dotnet build CvWpfclient/CvWpfclient.csproj：初回は NuGet.Config 権限で失敗。権限付き再実行で成功（0 警告 / 0 エラー）

---

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
