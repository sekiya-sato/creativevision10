## [2026-07-08] 13:00 展示会受注入力 (JuchuInputView) 作成
### Agent
- kimi-k2.6 : opencode-go : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：HachuInputView を参考に JuchuInputView（展示会受注入力）を新規作成。Tran12Jyuchu テーブル対応。
### 実施内容
- CvWpfclient/ViewModels/04Juchu/JuchuInputViewModel.cs: HachuInputViewModel をベースに Tran12Jyuchu / EnumUri01 / 得意先 へ置換。一覧取得、明細編集、印刷コマンド、バーコード入力を実装。
- CvWpfclient/Views/04Juchu/JuchuInputView.xaml: HachuInputView.xaml をベースにタイトル・バインディング・列名を受注/得意先へ置換。Width=1400 を維持。
- CvWpfclient/Views/04Juchu/JuchuInputView.xaml.cs: Escape キーで一覧タブへ戻る処理を追加。
- printform/JuchuInput_header.qfm: HachuInput_header.qfm をコピーして Shift_JIS 保存。タイトル/ヘッダーを「受注」「得意先」に置換。
- printform/JuchuInput_detail.qfm: HachuInput_detail.qfm をコピーして Shift_JIS 保存。同上。
- CvWpfclient/Models/MenuData.cs: 「展示会受注入力」の addInfo を「準備中」から更新。
### 技術決定 Why
- HachuInput と JuchuInput は TranAllHeader 継承構造が同一（明細・合計・税・メモなど）。型と名称を置換するだけで再利用可能と判断。
- CalcFlag の符号は EnumUri01.Uriage/UriSale を正、Henpin/HenSale を負とした（モデルの OnKubunChanged とは別に ViewModel 側でも制御）。
- qfm はバイナリ読み込み→decode('shift_jis')→テキスト置換→encode('shift_jis') の経路で安全に作成。
### 影響範囲
- 新規ファイル 4 件、既存ファイル 1 件 (MenuData.cs) の改修。ビルドは 0 error 0 warning。
### 確認
- Build: CvWpfclient 0 error / 0 warning

---

## [2026-07-08] 11:27 HachuInput qfm 構造修正
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：`HachuInput_header.qfm` / `HachuInput_detail.qfm` を `ShopUriageInput_header.qfm` / `ShopUriageInput_detail.qfm` の構造を壊さずに修正する。
### 実施内容
- printform/HachuInput_header.qfm: `ShopUriageInput_header.qfm` の `RecHeader` / `RecData` 構造を基準に、発注用の見出し・出力列へ調整した。
- printform/HachuInput_detail.qfm: `ShopUriageInput_detail.qfm` の `item1` グループ、`RecData grouplevel=1 breaktype=2`、`RecDetail` 構造を維持し、発注用の見出しと `itemN` 対応へ調整した。
### 技術決定 Why
- PrintStream のヘッダ・グループ制御を既存の動作済み qfm と同じ構造に保つため、既存 qfm の record/group 構造を基準にして表示名とデータ参照だけを変更した。
### 確認
- `validate_qfm.py` で `HachuInput_header.qfm` / `HachuInput_detail.qfm` が OK になることを確認した。

---

## [2026-07-08] 10:10 HachuInputView 区分選択と明細列表示修正
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：`HachuInputView` の区分選択を「発注 / 発注返品」の2つに変更し、明細の P/S を画面から削除し、数量・単価・金額を右詰めにする。
### 実施内容
- CvWpfclient/ViewModels/03Hatchu/HachuInputViewModel.cs: `KubunOptions` を `発注` と `発注返品` の2件に変更
- CvWpfclient/Views/03Hatchu/HachuInputView.xaml: 明細 DataGrid の P/S 列を削除。数量・単価・金額は既存の右寄せ表示/編集スタイルを維持
### 技術決定 Why
- P/S は「画面からのみ削除」の指定のため、内部明細 `Kubun`、印刷 qfm、印刷 SQL は変更せず、画面列だけを削除した。
### 確認
- XAML XML 読み込み：成功
- CRLF 確認：成功
- git diff --check：成功
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj`：成功（0 warning / 0 error、通常 sandbox では NuGet.Config 権限エラーのため権限付きで再実行）

---

## [2026-07-08] 09:56 発注入力画面作成
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：`CvWpfclient.Views._03Hatchu.HachuInputView` を `ShopUriageInputView` 参考で作成し、対象テーブルを `Tran13Hachu` とする。qfm(header/detail)追加、log、commit、`../cv10` への ff マージまで実施する。
### 実施内容
- CvWpfclient/Views/03Hatchu/HachuInputView.xaml: 一覧・詳細 2 タブ構成、発注ヘッダー入力、明細 DataGrid、一覧/明細印刷ボタンを追加
- CvWpfclient/Views/03Hatchu/HachuInputView.xaml.cs: 詳細タブで Esc 押下時に一覧へ戻る処理を追加
- CvWpfclient/ViewModels/03Hatchu/HachuInputViewModel.cs: `BasePlainLightMenteViewModel<Tran13Hachu>` 化、一覧条件、明細同期、仕入先/倉庫/社員/商品/色/サイズ選択、合計再計算、印刷 SQL を実装
- CvWpfclient/Models/MenuData.cs: `発注入力` の準備中表示を解除
- printform/HachuInput_header.qfm: 発注一覧印刷フォームを Shift_JIS で追加
- printform/HachuInput_detail.qfm: 発注明細印刷フォームを Shift_JIS で追加
- .omo/2026-07-08-hachu-input-plan.md: 実装計画を作成
### 技術決定 Why
- `ShopUriageInputView` と同じ一覧/詳細タブ、`CreateListQueryParam()`、`QueryListSqlParam`、`RunPrintPdfAsync` の流れに合わせ、一覧条件と印刷対象がずれない構成にした。
- 明細は ViewModel 所有の `ObservableCollection<Tran99Meisai>` に展開し、保存直前に `CurrentEdit.Jmeisai` へ同期することで DataGrid の追加/削除通知を安定させた。
- qfm は repo 既存ルールに合わせ、`printform/` 配下で `data.txt` 入力、A4 縦、Shift_JIS(cp932) とした。
### 確認
- XAML/qfm XML 読み込み：成功
- qfm validator：`HachuInput_header.qfm` / `HachuInput_detail.qfm` ともに OK
- CRLF 確認：編集ファイル OK
- git diff --check：成功
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj`：成功（0 warning / 0 error、通常 sandbox では NuGet.Config 権限エラーのため権限付きで再実行）

---

## [2026-07-07] 作業時間不明 catchブロック日本語文字化け修正
### Agent
- kimi-k2.6 : opencode-go/kimi-k2.6 : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient の 3 マスター ViewModel の catch ブロック内で日本語が文字化けしていた箇所を修正する
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterTokuiMenteViewModel.cs: `Cancelエラー:` と `データ取得失敗:` に文字化けを修正
- CvWpfclient/ViewModels/01Master/MasterShiireMenteViewModel.cs: 同上
- CvWpfclient/ViewModels/01Master/MasterEndCustomerMenteViewModel.cs: 同上
### 技術決定 Why
- UTF-8 日本語が Shift_JIS / EUC-JP として誤デコードされた典型的な文字化け（mojibake）であり、元の語彙と文脈から復元した
### 確認
- CvWpfclient ビルド：0 エラー、0 警告で成功

---

## [2026-07-07] 17:19 名称リスト追加削除ボタン実装
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：仕入先・得意先・顧客マスターの名称リストタブに「削除」「追加」ボタンを実装し、コミット後に `../cv10` へ ff マージする
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterShiireMenteViewModel.cs: 名称リストを `EditJsub` で編集し、追加・削除・保存前同期・区分リスト取得を追加
- CvWpfclient/ViewModels/01Master/MasterTokuiMenteViewModel.cs: 名称リストを `EditJsub` で編集し、追加・削除・保存前同期・区分リスト取得を追加
- CvWpfclient/ViewModels/01Master/MasterEndCustomerMenteViewModel.cs: 名称リストを `EditJsub` で編集し、追加・削除・保存前同期・区分リスト取得を追加
- CvWpfclient/Views/01Master/MasterShiireMenteView.xaml: 名称リストタブに削除・追加ボタン、区分 ComboBox、コード検索列を追加
- CvWpfclient/Views/01Master/MasterTokuiMenteView.xaml: 名称リストタブに削除・追加ボタン、区分 ComboBox、コード検索列を追加
- CvWpfclient/Views/01Master/MasterEndCustomerMenteView.xaml: 名称リストエリアに削除・追加ボタン、区分 ComboBox、コード検索列を追加
### 技術決定 Why
- `CurrentEdit.Jsub` 直接バインドでは行追加・削除の通知とキャンセル時の巻き戻しが不安定になるため、既存の社員・商品マスターと同じ `ObservableCollection<MasterGeneralMeisho>` へ展開して保存時に同期する方式にした
- 区分入力は自由入力ではなく ComboBox にし、既存 `MasterGeneralMeisho.OnKbChanged` の区分名更新で不正コード例外が起きにくい形にした
### 確認
- XAML XML 読み込み：成功
- CRLF 改行確認：成功
- git diff --check：成功
- dotnet build CvWpfclient/CvWpfclient.csproj：成功（0 warning / 0 error）

---

## [2026-07-07] 16:56 BaseViewModel 実行中判定の再帰修正
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：ESCキーで画面を閉じる際に `BaseViewModel.cs` の `HasRunningCommand` で `StackOverflowException` が発生する問題を修正する
### 実施内容
- CvWpfclient/Helpers/ViewModels/BaseViewModel.cs: コマンド列挙時に `PropertyType` が対象コマンド型へ代入可能なプロパティだけ `GetValue()` するよう修正
### 技術決定 Why
- `HasRunningCommand` 自身を含む全プロパティに対して `GetValue()` していたため、`HasRunningCommand` 評価中に同プロパティを再評価して再帰していた
- 先に型で絞り込むことで `bool`/`string` など非コマンドプロパティを評価対象から外し、ESC時の実行中コマンド判定だけを安全に行う
### 影響範囲
- CvWpfclient の BaseViewModel 系画面における ESC終了時の実行中コマンド判定、および終了時キャンセルコマンド列挙
### 確認
- git diff --check：成功
- dotnet build CvWpfclient/CvWpfclient.csproj：権限申請がワークスペース都合で拒否されたため未実施

---

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
