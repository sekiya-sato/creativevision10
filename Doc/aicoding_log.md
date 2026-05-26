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

## [2026-05-23] 18:12 SQLite WAL checkpointの定期実行追加
### Agent
- GPT-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvServer SchedulerService を使って、毎日 2:00 に、sqlite の `PRAGMA wal_checkpoint(TRUNCATE);` を実行するようにする。修正、commitまで
### 実施内容
- CvServer/Services/SchedulerService.cs: 毎日 02:00 実行用の定数と起動時登録メソッドを追加し、SQLite WAL checkpoint を専用 `SqliteConnection` で実行して結果をログ出力する処理を実装
- CvServer/Program.cs: `ApplicationStarted` で `SchedulerService.RegisterDailySqliteWalCheckpointTask()` を呼び、サーバ起動時に定期ジョブを自動登録するよう変更
- Tests/TestServer/TestServer.cs: 02:00 cron でジョブ登録されること、および `PRAGMA wal_checkpoint(TRUNCATE);` 実行結果が取得できることを確認するテストを追加
- Tests/TestServer/TestServer.csproj: .NET 10 の MSTest Runner を直接実行できるよう `OutputType` と `TestingPlatformDotnetTestSupport` を追加
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- WAL checkpoint は共有中の `ExDatabase` 接続へ直接流すより、同じ接続文字列から開いた短命の `SqliteConnection` で実行するほうが、既存の NPoco トランザクションや常駐接続状態に影響しにくく、定期メンテナンス処理として安全なため
- スケジューラ登録は gRPC コントラクト拡張ではなく `SchedulerService` 内の起動時登録メソッドに閉じることで、`CodeShare` など下位レイヤーを変更せず、サーバ側だけの最小差分で毎日 02:00 のジョブを追加できるため
### 確認
- `dotnet run --project "Tests/TestServer/TestServer.csproj"` で TestServer のテスト 5 件成功を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"` で CvServer のビルド成功（0 warnings / 0 errors）を確認

---

## [2026-05-23] 18:23 WAL checkpoint busy判定ログ調整
### Agent
- GPT-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvServer SchedulerService を使った SQLite WAL checkpoint 追加について、Oracle レビューで指摘された busy 状態の扱いを反映し、commit まで完了する
### 実施内容
- CvServer/Services/SchedulerService.cs: `wal_checkpoint(TRUNCATE)` の戻り値から `busy/log/checkpointed` を long として取り出す helper を追加し、`busy > 0` の場合は成功ログではなく警告ログを出すよう調整
- Doc/aicording_log.md: Oracle レビュー反映の追補ログを追記
### 技術決定 Why
- SQLite の `wal_checkpoint(TRUNCATE)` は例外なく戻っても `busy > 0` なら未反映フレームが残り得るため、完了ログと同一扱いにすると運用時の解釈が甘くなる。成功と一部保留をログレベルで分けることで、定期メンテナンス結果を正しく観測できるようにした
### 確認
- `dotnet run --project "Tests/TestServer/TestServer.csproj"` で TestServer のテスト 5 件成功を再確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"` で CvServer のビルド成功（0 warnings / 0 errors）を再確認

---

## [2026-05-19] 13:38 DatePickerTodayButtonBehavior の MaterialDesign 継承化
### Agent
- GPT-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient の DatePickerTodayButtonBehavior の AttachCalendarStyle で、できるだけソースコードを減らし、MaterialDesign の Style を引き継ぐようにする。各部品に対する SetValue() をなくし、全体で MaterialDesign の Style 適用を行う
### 実施内容
- CvWpfclient/Resources/UICalendar.xaml: 今日ボタン付き Calendar 用 ControlTemplate を新規 ResourceDictionary へ移し、MaterialDesign のリソースを使う共有テンプレートへ整理
- CvWpfclient/App.xaml: MaterialDesign3.Defaults.xaml の後に UICalendar.xaml を MergedDictionaries へ追加し、共有テンプレートをアプリ全体から参照可能にした
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: FrameworkElementFactory による部品ごとの SetValue 実装を削除し、MaterialDesign ベース Style + 共有 ControlTemplate を適用する最小構成へ整理
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: CalendarOpened 時に Today ボタンの Click と有効状態だけを設定する形へ縮小し、disable 時のハンドラ解除とテンプレート未解決時の安全な early return を追加
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- MaterialDesign の見た目を維持したまま独自差分を最小化するには、Behavior 内で各 UI 部品へ値を流し込むより、共有 ControlTemplate を ResourceDictionary へ分離して CalendarStyle から差し替える構成のほうが保守しやすく、今後の MaterialDesign 更新にも追従しやすいため
- Today ボタンの押下処理だけを Behavior 側に残すことで、見た目は XAML リソース、振る舞いは C# に分離し、ユーザー要望どおり AttachCalendarStyle のソース量を減らした
### 確認
- `python3` の `xml.etree.ElementTree` で `CvWpfclient/App.xaml` と `CvWpfclient/Resources/UICalendar.xaml` の XML 整形式を確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- Oracle レビューで disable 時の Today ボタン配線解除とテンプレート取得の安全化が必要と確認し、該当最小修正を反映した

---

## [2026-05-19] 12:58 DatePicker前景色リソース型不一致の修正
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：DatePicker 表示修正後に `Foreground` へ `System.Windows.Style` が設定される実行時エラーが発生したため、原因を確認して修正する
### 実施内容
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: `Foreground` に指定していた `MaterialDesignCalendarPortraitForeground` を、既存画面でも前景 Brush として使っている `MaterialDesignBody` へ置き換え
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- `MaterialDesignCalendarPortraitForeground` は実行時に `Style` として解決され、Brush を要求する `Control.Foreground` に適用すると `XamlParseException` / `InvalidOperationException` になるため、型が合う Brush リソースへ戻した
- 今日ボタンの `PrimaryHueMidBrush` は既存画面で Brush として使用されているため維持し、透明文字対策と型安全性の両立を優先した
### 確認
- `git diff --check` で空白エラーがないことを確認
- `[xml](Get-Content -Raw CvWpfclient\Views\06Uriage\ShopUriageInputView.xaml)` で対象 XAML の XML 構文が有効であることを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` で WPF クライアントのビルド成功（0 warnings / 0 errors）を確認

---

## [2026-05-19] 12:51 DatePicker今日ボタンの文字表示修正
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：DatePicker の背景は直ったが、DatePicker の上側の表示文字が透明で見えないため、他の文字やボタンも含めて見えなくなっていないか確認し修正する
### 実施内容
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: MDIX の Calendar ベーススタイル参照キーを `MaterialDesignDatePickerCalendarPortrait` から実在する `MaterialDesignCalendarPortrait` へ修正
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: Calendar と CalendarItem に `MaterialDesignPaper` / `MaterialDesignCalendarPortraitForeground` を明示し、カレンダー上部ヘッダーや日付表示の前景色が透明継承にならないよう修正
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: フッターの「今日」ボタンへ `PrimaryHueMidBrush` の前景色を明示し、背景とのコントラストを確保
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- `MaterialDesignDatePickerCalendarPortrait` は MaterialDesignThemes 5.3.1 の DLL 内に存在せず、`MaterialDesignCalendarPortrait` が Calendar 用の実在スタイルキーだったため、ベーススタイルを正しく継承して CalendarButton / CalendarDayButton 等の MDIX 表示設定を維持する構成にした
- 独自 ControlTemplate に置き換えると CalendarStyle の背景・前景 Setter がテンプレート内の `CalendarItem` へ渡らないため、CalendarItem 側にも MaterialDesign の背景色・前景色リソースを明示した
### 確認
- `git diff --check` で空白エラーがないことを確認
- `[xml](Get-Content -Raw CvWpfclient\Views\06Uriage\ShopUriageInputView.xaml)` で対象 XAML の XML 構文が有効であることを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` で WPF クライアントのビルド成功（0 warnings / 0 errors）を確認

---

## [2026-05-19] 12:22 DatePicker今日ボタンの背景スタイル修正
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：CvWpfclient の DatePickerTodayButtonBehavior を ShopUriageInputView から使用できる実装にした後、DatePicker の背景が透明になっているため、MaterialDesign に合わせた見やすいスタイルへ変更する。修正、確認、log、commit まで実行する
### 実施内容
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: 今日ボタン付き Calendar ControlTemplate の `PART_Root` を StackPanel から Border へ変更し、`MaterialDesignPaper` の背景と `MaterialDesignDivider` の枠線を描画するよう修正
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: CalendarItem と今日ボタンの縦並び構造は維持しつつ、背景付き Border 配下の StackPanel に格納して透明表示を防止
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- `CalendarStyle` の背景系 Setter はテンプレート内で描画要素に反映しないと見た目に現れないため、ControlTemplate のルートで MaterialDesign の Paper/Divider リソースを明示的に使用した
- `ShopUriageInputView.xaml` 側の DatePicker 利用方法は既に `helpers:DatePickerTodayButtonBehavior.IsEnabled="True"` で確立済みのため、画面側ではなく Behavior のテンプレート定義に修正を閉じた
### 確認
- `git diff --check` で空白エラーがないことを確認
- `[xml](Get-Content -Raw CvWpfclient\Views\06Uriage\ShopUriageInputView.xaml)` で対象 XAML の XML 構文が有効であることを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` で WPF クライアントのビルド成功（0 warnings / 0 errors）を確認

---

## [2026-05-19] 12:02 ShopUriageInputView用DatePicker今日ボタン対応
### Agent
- GPT-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient の DatePickerTodayButtonBehavior を改変し、ShopUriageInputView から使用できる今日ボタン（今日の日付をセット）付きの DatePicker を実装する。修正、確認、log、commit まで実行する
### 実施内容
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: Popup の visual tree を直接差し替える方式から、DatePicker の CalendarStyle に今日ボタン付き ControlTemplate を適用する方式へ変更
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: Loaded 後に CalendarStyle を差し替えるようにし、`IsEnabled` が false に戻った場合は元の CalendarStyle を復元する処理を追加
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: 今日ボタン押下時に `SelectedDate` / `DisplayDate` を当日へ設定してドロップダウンを閉じる既存契約を維持しつつ、`DisplayDateStart` / `DisplayDateEnd` / `BlackoutDates` に反する日はボタンを無効化するように変更
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- `ShopUriageInputView.xaml` 側にはすでに `helpers:DatePickerTodayButtonBehavior.IsEnabled="True"` が設定済みだったため、画面側ではなく behavior 本体を安定化するのが最小差分だった
- WPF の `Popup` は DatePicker 本体と別 visual tree になりやすく、直接差し替え方式はテンプレート再適用で壊れやすいため、Calendar の ControlTemplate 差し替えへ寄せて今日ボタンを安定表示できる構成にした
### 確認
- `lsp_diagnostics` で `CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs` に diagnostics がないことを確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet clean CvWpfclient/CvWpfclient.csproj && dotnet build CvWpfclient/CvWpfclient.csproj"` でクリーン後のビルド成功（0 warnings / 0 errors）を確認
- `ShopUriageInputView.xaml` の計上日 DatePicker に `helpers:DatePickerTodayButtonBehavior.IsEnabled="True"` が既存設定済みであることを確認し、追加の XAML 変更なしで対象画面から利用されることを確認

---

## [2026-05-18] 17:00 ストリーミング進捗処理の重複統合
### Agent
- GitHub Copilot : OpenAI
### Editor
- VS2026
### 目的
- ユーザーからの要望：添付した処理の重複部分を統合して簡易化し、write-log と commit まで実行する
### 実施内容
- CvServer/Services/CoreServiceStreaming.cs: StreamStepProgress から StreamMsg への変換と進捗ストリーム転送を共通メソッドへ抽出し、Convert/Summary ハンドラの重複を削減
- CvDomainLogic/ConvertDb.cs: 変換ステップの進捗ループを共通ランナー呼び出しへ置き換え、ステップ定義中心の実装へ整理
- CvDomainLogic/SummaryDb.cs: 月次集計と実在庫集計の進捗ループを共通ランナー呼び出しへ置き換え、重複ロジックを削減
- CvDomainLogic/StreamStepProgressRunner.cs: ステップ開始・完了・例外処理・進捗率計算をまとめる共通ランナーを追加
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- StreamStepProgress の逐次通知パターンが CvServer と CvDomainLogic に分散して重複していたため、メッセージ変換とステップ実行をそれぞれ1箇所へ集約し、今後のステップ追加や表示文言変更の修正点を最小化した
### 影響範囲
- CvServer の gRPC ストリーミング進捗通知
- CvDomainLogic の変換処理・集計処理の進捗列挙
### 確認
- Visual Studio コンテキストの `run_build` でワークスペースのビルド成功を確認
- `dotnet build "Cv.slnx"` は実行環境からソリューションファイルを直接解決できず MSB1009 となったため、観測を記録したうえで Visual Studio コンテキストのビルド結果を採用

---

## [2026-05-13] 16:00 郵便番号API検索の3〜7桁対応
### Agent
- gpt-5.5 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvServer側 SearchByPostalCodeService および CvWpfclient側 PostalAddressSearchHelper を修正し、3桁から7桁までの郵便番号検索に対応する。確認、ログ、commit まで行う
### 実施内容
- CvServer/Services/SearchByPostalCodeService.cs: 郵便番号正規化を7桁固定から3〜7桁許可へ変更し、半角数字・全角数字をASCII数字へそろえて日本郵便APIへ渡すよう修正
- CvServer/Services/SearchByPostalCodeService.cs: 入力不正メッセージを「3桁から7桁」に更新し、3〜6桁は前方一致、7桁は完全一致として扱うコメントへ変更
- CvWpfclient/Helpers/PostalAddressSearchHelper.cs: gRPC呼び出し前に3〜7桁の郵便番号へ正規化し、不正入力は警告ダイアログで返すよう修正
- CvWpfclient/Helpers/PostalAddressSearchHelper.cs: サーバーから InvalidInput が返った場合はエラーではなく警告として表示するよう修正
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- 日本郵便Biz API仕様では郵便番号の `search_code` は3桁以上の数値を受け付け、7桁未満は入力値から始まるデータのパターン検索になるため、サーバー側の7桁固定バリデーションを3〜7桁へ緩和した
- 呼び出し元ViewModelを個別修正せず `PostalAddressSearchHelper` に入力正規化を集約することで、既存の4画面すべてに同じUXと入力ルールを適用できるようにした
### 影響範囲
- CvServer の郵便番号API検索サービス
- CvWpfclient のマスターメンテ系郵便番号検索ボタンからの住所検索
### 確認
- `lsp_diagnostics` で `CvServer/Services/SearchByPostalCodeService.cs` と `CvWpfclient/Helpers/PostalAddressSearchHelper.cs` に問題がないことを確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（warning 0 / error 0）を確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"` は並列復元時に一度 `NuGet.targets` の既存ファイルエラーが出たが、単独再実行でビルド成功（warning 0 / error 0）を確認

---

## [2026-05-13] 13:02 MainMenuViewのテーマ別ウィンドウアイコン切替
### Agent
- gpt-5.5 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient の MainMenuView でテーマ切り替えをしたときに ICO ファイルも切り替え、default と purple 以外は `cv10purple.ico` を使う。修正、確認、log、commit まで行う
### 実施内容
- CvWpfclient/Views/MainMenuView.xaml: 固定の `cv10.ico` 指定を `DynamicResource WindowIcon` に変更し、メインテーマの ResourceDictionary 差し替えに合わせてウィンドウアイコンが更新されるよう修正
- CvWpfclient/Resources/UIMainTheme.Default.xaml: default テーマ用の `WindowIcon` に `cv10.ico` を追加
- CvWpfclient/Resources/UIMainTheme.Green.xaml: green テーマ用の `WindowIcon` に `cv10purple.ico` を追加
- CvWpfclient/Resources/UIMainTheme.Orange.xaml: orange テーマ用の `WindowIcon` に `cv10purple.ico` を追加
- CvWpfclient/Resources/UIMainTheme.Red.xaml: red テーマ用の `WindowIcon` に `cv10purple.ico` を追加
- CvWpfclient/Resources/UIMainTheme.Purple.xaml: purple テーマ用の `WindowIcon` に `cv10purple.ico` を追加
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- 既存の `MainThemeService` が `UIMainTheme.*.xaml` を差し替えているため、code-behind や ViewModel から Window を直接操作せず、テーマリソースとして `WindowIcon` を定義して `DynamicResource` で追従させる構成にした
### 影響範囲
- CvWpfclient の MainMenuView ウィンドウアイコン表示
### 確認
- `python3 -c "import xml.etree.ElementTree as ET; files=['CvWpfclient/Views/MainMenuView.xaml','CvWpfclient/Resources/UIMainTheme.Default.xaml','CvWpfclient/Resources/UIMainTheme.Green.xaml','CvWpfclient/Resources/UIMainTheme.Orange.xaml','CvWpfclient/Resources/UIMainTheme.Red.xaml','CvWpfclient/Resources/UIMainTheme.Purple.xaml']; [ET.parse(f) for f in files]; print('XAML XML parse OK')"` で変更XAMLのXML整形式を確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-05-12] 13:44 MasterShohin選択のSelectShohinView差し替え
### Agent
- gpt-5.5 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient の ViewModel で参照している `MasterShohin` 選択画面を `Sub/SelectShohinView` 使用に差し替える
### 実施内容
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: 店舗売上入力の明細商品選択を汎用SelectWinから `SelectShohinView` 呼び出しへ変更し、選択した `MasterShohin` を明細へ反映するよう修正
- CvWpfclient/ViewModels/08Zaiko/ZaikoQueryViewModel.cs: 在庫問い合わせ画面の商品CD From/To の検索ボタンを `SelectShohinView` 呼び出しへ変更し、既存検索条件を引き継いだ商品選択ができるよう修正
- CvWpfclient/ViewModels/Sub/SelectShohinViewModel.cs: 商品検索画面自身の商品CD From/To の検索ボタンも `SelectShohinView` 呼び出しへ変更し、旧 `SelectCode<MasterShohin>` 参照を解消
- Doc/aicording_log.md: 既存画面からの呼び出し対応を追記
### 技術決定 Why
- `SelectShohinView` は商品名・ブランド・アイテム・JANを条件に使えるため、従来の汎用コード選択より商品検索の要件に合う。既存の `ClientLib.ShowDialogView` と `SelectedShohin` 取得に寄せることで、選択ダイアログの戻り値契約を維持した
### 影響範囲
- 店舗売上入力の商品選択
- 在庫問い合わせの商品CD範囲選択
- 商品検索画面の商品CD範囲選択
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- `CvWpfclient/ViewModels` 配下に `ShowSelectDialog<MasterShohin>` / `SelectCode<MasterShohin>` が残っていないことを確認

---

## [2026-05-12] 13:39 商品検索選択画面のSQL取得内容修正
### Agent
- gpt-5.5 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：Oracleレビューで指摘された `SelectShohinViewModel` の商品検索SQLを、選択時に完全な `MasterShohin` を返せる形へ修正する
### 実施内容
- CvWpfclient/ViewModels/Sub/SelectShohinViewModel.cs: 商品検索SQLを `SELECT M.*` に変更し、選択結果が部分ロードの `MasterShohin` にならないよう修正
- CvWpfclient/ViewModels/Sub/SelectShohinViewModel.cs: ブランドCD・アイテムCDの範囲条件を `M.VBrand` / `M.VItem` のJSON抽出から、`MasterMeisho` への left join と `Brd.Code` / `Item.Code` 判定へ変更
- Doc/aicording_log.md: Oracleレビュー後の修正内容を追記
### 技術決定 Why
- 指示ファイルが商品マスタとブランド/アイテム名称マスタの結合条件を明示していたため、検索条件は `Id_Brand` / `Id_Item` の参照先コードで判定し、選択結果は後続処理で完全な商品マスタとして扱えるよう `M.*` を取得する構成にした
### 確認
- `lsp_diagnostics` で `CvWpfclient/ViewModels/Sub/SelectShohinViewModel.cs` に問題がないことを確認
- `python3 -c "import xml.etree.ElementTree as ET; ET.parse('CvWpfclient/Views/Sub/SelectShohinView.xaml'); print('XAML XML parse OK')"` で XAML のXML整形式を確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-05-12] 13:20 商品検索選択画面 SelectShohinView の新規作成
### Agent
- gpt-5.5 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`Doc/wrk/instruction-20260512-Desktop-SearchShohin.txt` に従い、CvWpfclient の商品検索画面 `Sub/SelectShohinView` を新規作成し、Write-Log と Git-Commit まで行う
### 実施内容
- CvWpfclient/Views/Sub/SelectShohinView.xaml: 商品CD・商品名・ブランドCD・アイテムCD・JANの検索条件画面と、商品一覧選択画面を1つのBaseWindow内で切り替える2画面構成を追加
- CvWpfclient/Views/Sub/SelectShohinView.xaml.cs: 新規Viewの初期化 code-behind を追加
- CvWpfclient/ViewModels/Sub/SelectShohinViewModel.cs: `QueryListSqlParam` による商品検索、`DerivedShohinColSiz` のJAN部分一致 `EXISTS` 条件、`SearchTextBox + SelectWinView` による商品/ブランド/アイテムCD範囲選択、選択確定時に `MasterShohin` を返す処理を追加
- .sisyphus/2026-05-12_desktop_search_shohin.md: 指示内容、要求整理、実装方針、確認予定の作業メモを追加
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- 既存の `SelectShohinColSizView` / `SelectWinView` / `ZaikoQueryViewModel` のパターンに合わせ、サーバ側契約を変更せずに `CvFlag.Msg101_Op_Query` と `QueryListSqlParam` で検索を実装することで、既存通信経路と選択ダイアログの戻り値契約を維持した
- 一覧DataGridは `Grid` の `*` 行に配置し、下部操作ボタンを `Auto` 行に分離することで、右端・下端の見切れを避ける構成にした
### 影響範囲
- CvWpfclient のサブ選択画面（新規 `SelectShohinView` / `SelectShohinViewModel`）
### 確認
- `lsp_diagnostics` で `CvWpfclient/ViewModels/Sub/SelectShohinViewModel.cs` と `CvWpfclient/Views/Sub/SelectShohinView.xaml.cs` に問題がないことを確認
- `python3 -c "import xml.etree.ElementTree as ET; ET.parse('CvWpfclient/Views/Sub/SelectShohinView.xaml'); print('XAML XML parse OK')"` で XAML のXML整形式を確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-05-08] 11:29 ZaikoQueryの在庫0条件と結果タブ操作改善
### Agent
- GPT-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：ZaikoQueryView / ZaikoQueryViewModel に在庫0含有チェック条件を追加し、一覧の素材・シーズン列を半幅化し、追加結果タブに閉じるボタンを追加したい
### 実施内容
- CvWpfclient/ViewModels/08Zaiko/ZaikoQueryViewModel.cs: `IncludeZeroStock` 条件を追加し、`ClearConditions` で初期化するよう修正。`BuildShohinClauses` に `SummaryStock` の商品単位集計 `SUM(Su)` による在庫0除外条件を追加した
- CvWpfclient/Views/08Zaiko/ZaikoQueryView.xaml: 検索条件エリアに「在庫0を含める」CheckBox を追加し、一覧の素材・シーズン列幅を半分へ調整。結果タブのヘッダ領域に閉じるボタンを追加して既存 `CloseStockTabCommand` に接続した
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- ユーザー指定の在庫0判定が `SummaryStock` の `Sum(Su)` 基準だったため、checkbox OFF 時のみ商品単位集計で除外する最小差分に留めつつ、結果タブ削除は既存 `CloseStockTab` ロジックを再利用して画面挙動の一貫性を保った
### 影響範囲
- CvWpfclient の在庫問い合わせ画面（ZaikoQueryView / ZaikoQueryViewModel）の検索条件・一覧表示・結果タブ操作
### 確認
- `lsp_diagnostics` で `CvWpfclient/ViewModels/08Zaiko/ZaikoQueryViewModel.cs` に問題がないことを確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- goal / QA / code quality / security / context mining の再レビューで blocking issue 解消を確認

---

## [2026-05-08] 09:51 在庫問い合わせ画面の検索・在庫明細タブ実装
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：`Doc/wrk/instruction-20260507-Desktop-SearchZaiko.txt` に従い、`CvWpfclient.Views._08Zaiko.ZaikoQueryView` の在庫問い合わせ画面を作成し、Write-Log と Git-Commit まで行う。
### 実施内容
- CvWpfclient/ViewModels/08Zaiko/ZaikoQueryViewModel.cs: 商品CD、色CD、商品名、倉庫CD、ブランドCD、アイテムCD、最大件数の検索条件を追加し、`CvFlag.Msg101_Op_Query` と `QueryListSqlParam` による商品一覧・在庫数・移動中数量・商品別在庫明細取得を実装。
- CvWpfclient/ViewModels/08Zaiko/ZaikoQueryViewModel.cs: 商品一覧行のダブルクリックで商品別の在庫明細タブを生成し、倉庫別Totalと色サイズ別在庫数を `DataTable` で表示する処理を追加。
- CvWpfclient/Views/08Zaiko/ZaikoQueryView.xaml: 検索条件入力、検索実行ボタン、対象商品マスタ一覧、商品別在庫明細タブ表示用DataGridをMaterialDesignベースで作成。
- CvWpfclient/Models/MenuData.cs: 在庫問合せメニューの説明を準備中から実機能説明へ変更。
### 技術決定 Why
- サーバ側のQuery I/Fを拡張せず、既存の `QueryListSqlParam` によるSQL取得へ寄せることで、在庫問い合わせ専用の集計表示をWPF側で組み立てつつ既存通信経路を維持した。
- 色サイズ列は商品ごとに可変になるため、固定DTOではなく `DataTable` を使い、TabItemごとに倉庫行とSKU列を動的生成する構成にした。
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` を実行し、warning 0 / error 0 でビルド成功を確認。
- `git diff --check` で空白エラーなしを確認。
- `CvWpfclient/Views/08Zaiko/ZaikoQueryView.xaml` をXMLとして読み込み、構文エラーなしを確認。

---

## [2026-04-30] 12:30 SchedulerService を使った毎日AM2:00のMSg050_Summary集計スケジュール実装
### Agent
- big-pickle : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：SchedulerService を使って毎日 AM2:00 に CvFlag.MSg050_Summary の集計処理を実行するにはどうすればよいか、実装を含めて回答が欲しい
### 実施内容
- CodeShare/IScheduler.cs: SchedulerTaskType enum に RunSummary = 2 を追加
- CvServer/Services/SchedulerService.cs: ExDatabase をコンストラクタで受け取り、ExecuteTaskAsync メソッドで RunSummary 分岐を実装。Payload から yyyymm を取得（空なら当月）、内部で SummaryDb を生成して SummaryAllAsyncStream を呼び出し
### 技術決定 Why
- 既存の NCrontab.Scheduler 基盤を活用し、新たな DI 登録を追加せずに ExDatabase から SummaryDb を内部生成することで、既存の依存関係を維持しつつ集計処理をスケジュール実行できるようにした。cron 式 "0 2 * * *" を使用して毎日 AM2:00 に実行。
### 影響範囲
- CvServer プロジェクト内の 2 ファイル（IScheduler.cs, SchedulerService.cs）の変更
### 確認
- CvServer プロジェクトのビルドが 0 警告・0 エラーで成功（dotnet build CvServer/CvServer.csproj）

---

## [2026-05-04] 16:06 CvServer の不要な System.Text.Encoding.CodePages 参照削除
### Agent
- GitHub Copilot : OpenAI
### Editor
- VS2026
### 目的
- ユーザーからの要望：CvServer で不要になっている `System.Text.Encoding.CodePages` 参照を削除し、write-log と commit まで実施する
### 実施内容
- CvServer/CvServer.csproj: `net10.0` で不要となった `System.Text.Encoding.CodePages` の `PackageReference` を削除
- Directory.Packages.props: 上記参照削除に伴い未使用となった `System.Text.Encoding.CodePages` の中央管理バージョン定義を削除
- Doc/aicording_log.md: 今回の作業記録を追記
### 技術決定 Why
- .NET 10 ではフレームワーク提供ライブラリに対する不要な直接 `PackageReference` が `NU1510` の対象となるため、SJIS 利用コードは維持したまま不要参照だけを削除して依存関係を簡素化した
### 確認
- `dotnet build "C:\gitroot\documents\new2022\cv10\CvServer\CvServer.csproj"` でビルド成功を確認

---

## [2026-04-30] 11:56 DataGrid自動スクロール処理の再フォーカス抑止
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`helpers:DataGridSelectionBehavior.AutoScrollToSelectedItem` がソリューション全体で使われているか確認し、未使用なら削除して今回の DataGrid キー操作ループが再発しないようにしたい
### 実施内容
- CvWpfclient/Helpers/Behaviors/DataGridSelectionBehavior.cs: `AutoScrollToSelectedItem` は 17 箇所で使用中だったため削除せず、SelectionChanged 時の処理を再選択・CurrentCell設定・フォーカス移動から、選択行への `ScrollIntoView` のみに変更
### 技術決定 Why
- `AutoScrollToSelectedItem` はマスター画面や売上入力画面で使用中のため削除すると既存XAMLがビルド不能になる。今回のループ原因は、連続キー操作中に SelectionChanged ごとに非同期で選択・セル・フォーカスを再設定する点にあるため、プロパティは維持しつつ副作用をスクロールだけに限定した。
### 影響範囲
- `helpers:DataGridSelectionBehavior.AutoScrollToSelectedItem` を使用する DataGrid の選択行自動スクロール
### 確認
- `rg -n "helpers:DataGridSelectionBehavior\.AutoScrollToSelectedItem" CvWpfclient\Views` で 17 箇所の使用を確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認。

---

## [2026-04-30] 11:51 色サイズ選択画面のDataGridキー操作ループ対策
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- OpenCode
### 目的
- ユーザーからの要望：SelectShohinColSizView の一覧で上下キーを素早く動かすとループするような動きになるため、過去ログの DataGrid 選択行点滅修正を確認し、同様の観点でこの画面のみ修正したい
### 実施内容
- CvWpfclient/Views/Sub/SelectShohinColSizView.xaml: DataGrid の `helpers:DataGridSelectionBehavior.AutoScrollToSelectedItem` を削除し、連続 SelectionChanged 時にフォーカス・スクロール制御を再投入しないよう修正
### 技術決定 Why
- 過去ログでは DataGrid の連続選択移動時に `DataGridSelectionBehavior` の非同期フォーカス制御が競合して点滅する問題が記録されていた。選択ダイアログでは DataGrid 標準のキー移動で十分なため、初期選択位置への一度だけのフォーカス制御は残し、SelectionChanged ごとの自動スクロールだけを外す最小修正とした。
### 確認
- `SelectShohinColSizView.xaml` の XML 構文解析が成功することを確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認。

---

## [2026-04-30] 11:42 店舗売上入力の色サイズ選択画面追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- OpenCode
### 目的
- ユーザーからの要望：ShopUriageInputView の明細入力で、商品Idに紐づく DerivedShohinColSiz の色サイズを選択する専用画面を追加し、Id_Col と Id_Siz を同時に確定したい
### 実施内容
- CvWpfclient/Views/Sub/SelectShohinColSizView.xaml: DerivedShohinColSiz の色CD・色名・サイズCD・サイズ名・JAN1 を表示して選択する専用ダイアログを追加
- CvWpfclient/Views/Sub/SelectShohinColSizView.xaml.cs: 専用選択画面の初期化 code-behind を追加
- CvWpfclient/ViewModels/Sub/SelectShohinColSizViewModel.cs: Id_Shohin 必須、サイズ選択時は Id_Col でも絞り込む DerivedShohinColSiz 照会処理を追加
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: カラー選択・サイズ選択を新しい専用画面へ差し替え、選択行から Id_Col / Code_Col / Mei_Col / Id_Siz / Code_Siz / Mei_Siz / JanCode を同時反映するよう変更
### 技術決定 Why
- 商品選択ダイアログは軽量取得のため MasterShohin.Jcolsiz を常に取得できない。DerivedShohinColSiz を直接照会することで、既存伝票の再編集時でも商品Idを基準に正しい色サイズ候補を取得できるようにした。
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認。
- `SelectShohinColSizView.xaml` の XML 構文解析が成功することを確認。

---

## [2026-04-30] 11:22 店舗売上一覧ダブルクリック時の明細タブ遷移修正
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：ShopUriageInputView.xaml の一覧DataGridで、ViewModel側の GoToDetail() は実行されているが明細Tabへ移動しない原因を調査し、DataGridRowが確定できる場合はクリック位置がDataGridCell上でなくても明細TABをGUI表示したい
### 実施内容
- CvWpfclient/Helpers/Behaviors/DataGridDoubleClick.cs: ダブルクリック位置から DataGridRow を解決する処理を強化し、行選択と SelectedItem バインディング更新をコマンド実行前に明示化
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: GoToDetailCommand の行アイテム引数を受け取り、確定した行を Current に反映してから明細Tabへ遷移するよう修正
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: TabControl.SelectedIndex と一覧DataGrid.SelectedItem のバインディングを Mode=TwoWay と明示
### 技術決定 Why
- 従来はダブルクリックされた行が特定できても、ViewModelの GoToDetail() がコマンド引数を使わず Current の更新完了に依存していたため、クリック位置やイベント順によって明細Tabへの表示反映が不安定になっていた。行アイテムをコマンド引数として扱い、選択行を確定してから SelectedTabIndex を変更することで、DataGridCell外の行クリックでも明細Tabへ遷移できるようにした。
### 影響範囲
- DataGridDoubleClick 添付ビヘイビアを使用する一覧ダブルクリック処理
- 店舗売上入力画面の一覧DataGridから明細Tabへの遷移
### 確認
- `git diff --check` で空白エラーなしを確認（CRLF変換の通常警告のみ）
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功、警告0・エラー0を確認

---

## [2026-04-28] 16:20 MainMenuViewの気温グラフ表示調整
### Agent
- gpt-5.4 : github-copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MainMenuView.xaml の気温グラフを枠内でできるだけ大きく見やすく表示し、軸と区切り線を Light 時は黒、Dark 時は白で見えるようにしたい
### 実施内容
- CvWpfclient/Views/MainMenuView.xaml: 気温チャートカードの Padding を 8 から 4 に縮小し、℃ラベル用の専用列をやめて左上オーバーレイ表示へ変更。`CartesianChart` に `DrawMargin` バインドを追加し、高さを 130 へ調整してカード内の表示領域を広げた
- CvWpfclient/ViewModels/MainMenuViewModel.cs: `ForecastMargin` を追加し、`ApplyForecastTheme` で `MainMenuChartTextColor` を軸ラベル色・区切り線色へ反映するよう変更。Light/Dark テーマ切替時に軸表示色も追従するようにした
### 技術決定 Why
- グラフの見やすさ改善はデータ処理を変えずにチャート周辺の余白を削るのが最小差分で安全なため、専用列削減・カード内余白圧縮・DrawMargin 調整で表示領域を拡大した
- 軸と区切り線の色は ViewModel で LiveCharts の Axis を組み立てているため、XAML の固定色ではなく `MainMenuChartTextColor` リソースへ寄せてテーマ切替と一貫性を保った
### 確認
- `python3 -c "import xml.etree.ElementTree as ET; ET.parse(r'CvWpfclient/Views/MainMenuView.xaml'); print('XML_OK')"` で XAML の XML 整形式を確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認
- Oracle レビューで、今回の変更範囲が要件適合かつ低リスクであることを確認

---

## [2026-04-27] 16:50 MasterMeishoMenteViewのコード/並び順入力幅調整
### Agent
- gpt-5.4 : github-copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MasterMeishoMenteView.xaml でコードの TextBox 幅を 1/3 程度にし、並び順は数値なので右寄せで 1/5 程度に縮めたい
### 実施内容
- CvWpfclient/Views/01Master/MasterMeishoMenteView.xaml: `CurrentEdit.Code` の TextBox に `Width="200"` と `HorizontalAlignment="Left"` を追加し、詳細フォーム内でフル幅に広がらないよう調整
- CvWpfclient/Views/01Master/MasterMeishoMenteView.xaml: `CurrentEdit.Odr` の TextBox に `Width="120"`、`HorizontalAlignment="Left"`、`TextAlignment="Right"` を追加し、数値入力向けの幅と右寄せ表示に調整
### 技術決定 Why
- 親列が `*` 幅のままでも対象 2 項目だけを局所的に短くでき、他の入力欄や全体レイアウトへ波及しにくい最小差分にするため固定幅 + 左寄せを採用した
- 並び順は数値項目のため、`TextAlignment="Right"` を付けて視認性と入力時の整列性を優先した
### 確認
- `python3 -c "import xml.etree.ElementTree as ET; ET.parse(r'CvWpfclient/Views/01Master/MasterMeishoMenteView.xaml'); print('XML_OK')"` で XML 整形式を確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認

---
## [2026-04-23] 17:52 ClientSettingsStore の部分更新保存対応
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient の ClientSettingsStore で設定ファイルを空白や初期値で上書きしないようにし、定義外の JSON 内容も削除せず、確認後に commit まで行いたい
### 実施内容
- CvWpfclient/Services/SystemSettingsStore.cs: `clientsettings.json` の全文再シリアライズをやめ、既存 JSON を `JObject` として読み込んで指定キーだけを部分更新する `SaveConfigurationOverrides` を追加。中間ノードが非オブジェクトの場合は保存失敗にし、temp file + `File.Replace` / `File.Move` による原子的保存へ変更
- CvWpfclient/ViewModels/00System/SysSetConfigViewModel.cs: 環境設定保存時に URL / LoginId / LoginPass の変更有無を判定し、空欄入力では既存ログイン値を保持したまま、明示変更分だけ永続化するよう修正。URL変更時のホスト再起動には実効値をまとめて渡し、失敗時は `AppGlobal` を元値へ戻すよう調整
- CvWpfclient/App.xaml.cs: テーマ保存処理を `SaveConfigurationOverrides` 経由の単一キー更新へ変更し、テーマ変更で `clientsettings.json` 全体を書き換えないよう修正
### 技術決定 Why
- `ClientSettingsDocument` の全文保存では未知 JSON や未入力項目を安全に保持できないため、既存ファイルをベースに必要キーだけをパッチ更新する方式へ切り替えるのが要件に最も近く、かつ既存コードへの影響を最小化できるため
### 確認
- `lsp_diagnostics` で `CvWpfclient/Services/SystemSettingsStore.cs`、`CvWpfclient/ViewModels/00System/SysSetConfigViewModel.cs`、`CvWpfclient/App.xaml.cs` に問題がないことを確認
- `dotnet build "CvWpfclient/CvWpfclient.csproj" /p:EnableWindowsTargeting=true` でビルド成功を確認
- `dotnet build "CvWpfclient/CvWpfclient.csproj" /p:EnableWindowsTargeting=true /p:UseAppHost=false` でビルド成功を確認
- Oracle / QA 再レビューで、未知 JSON 保持、非オブジェクト中間ノードでの fail-fast、空欄ログイン値の非上書き、URL変更時の再起動設定保持を確認

---
## [2026-04-23] 16:44 MainMenuView の下段ボタン横スクロール不具合修正
### Agent
- GitHub Copilot : OpenAI
### Editor
- VS2026
### 目的
- ユーザーからの要望：Window の横幅を縮めたときに下段の5つのボタン右側が隠れ、スクロールバーが役に立たない原因を解消したい。あわせて人間が修正した AGENTS.md も含めて commit したい
### 実施内容
- CvWpfclient/Views/MainMenuView.xaml: 下段アクション領域の親を `StackPanel` から `Grid` に変更し、`ScrollViewer` に有限幅が渡るよう調整して横スクロールが有効に働く構成へ修正
- AGENTS.md: ユーザー作業済みの変更を今回のコミット対象として同梱
### 技術決定 Why
- `ScrollViewer` の親が `StackPanel` だと横方向の測定が無制限になりやすく、スクロール対象の viewport が成立しないため、ボタン定義を崩さず親レイアウトだけを `Grid` に替えるのが最小差分で安全なため
### 確認
- `CvWpfclient/Views/MainMenuView.xaml` のエラー確認で問題が出ていないことを確認。
- `dotnet build "CvWpfclient/CvWpfclient.csproj" /p:EnableWindowsTargeting=true` でビルド成功を確認。

---
## [2026-04-23] 16:16 MainMenuView の下段操作ボタン横スクロール対応
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MainMenuView.xaml line 385 付近のバージョンアップ、環境設定など5つのボタンを、ウィンドウ縮小時に右側が隠れても横スクロールで表示できるようにしたい
### 実施内容
- CvWpfclient/Views/MainMenuView.xaml: 下段カード内の5ボタン行を `ScrollViewer` でラップし、`HorizontalScrollBarVisibility="Auto"` と `VerticalScrollBarVisibility="Disabled"` を設定して、通常時の配置を維持したまま縮小時だけ横スクロールできるよう修正
### 技術決定 Why
- 既存のボタンスタイルは `MinWidth` を持ち、左側固定カラムの影響でメイン領域が縮むと右端が見切れるため、ボタン定義やスタイルを変えずに対象行だけ `ScrollViewer` で包むのが最小差分で安全なため
### 確認
- Python の XML パースで `CvWpfclient/Views/MainMenuView.xaml` の構文が崩れていないことを確認。
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認。

---
## [2026-04-20] 15:55 MasterShohinMenteView の商品画像表示エリアのはみ出し改善
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MasterShohinMenteView で `Wpf:WebView2` の商品画像表示エリアが下スクロール時に上側へはみ出して見える表示崩れを改善したい
### 実施内容
- CvWpfclient/Views/01Master/MasterShohinMenteView.xaml: 商品画像表示コントロールを `Wpf:WebView2` から `Wpf:WebView2CompositionControl` へ置換し、`HorizontalAlignment` と `VerticalAlignment` を `Stretch` にして親 `Border` 内へ収まる構成へ修正
### 技術決定 Why
- WPF の通常の `WebView2` は `HwndHost` ベースのため `ScrollViewer` 配下で airspace 問題によるクリップずれが起きやすく、スクロール時のはみ出し対策としては `WebView2CompositionControl` への置換が最小変更で効果的なため
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認。

---
## [2026-04-20] 14:21 MainMenuViewModel のログ出力元クラス名修正
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`MainMenuViewModel.cs` のログ出力で `CvBase.NLogExtender\`1` ではなく元のクラス `MainMenuViewModel` が残るようにしたい
### 実施内容
- CvBase/NLogExtender.cs: `LogManager.GetCurrentClassLogger()` をやめ、ジェネリック型 `T` の完全名を `LogManager.GetLogger(...)` に渡すよう修正
### 技術決定 Why
- `GetCurrentClassLogger()` は実行位置である `NLogExtender<T>` 自身をロガー名にするため、呼び出し元の型名を維持するには `typeof(T)` ベースで明示的にロガー名を作る必要があるため
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認。

---
## [2026-04-17] 00:08 MainMenuの気温チャート縦軸目盛り表示修正
### Agent
- gpt-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MainMenuViewModel.cs の縦軸設定が効いていないように見えるため、気温チャートの縦軸表示を 5,10,15,20 のような 5 刻みにしたい
### 実施内容
- CvWpfclient/ViewModels/MainMenuViewModel.cs: 気温チャートの `ForecastYAxes` に `ForceStepToMin = true` を追加し、`MinStep = 5` を自動調整ではなく固定の 5 刻みとして扱うよう修正
### 技術決定 Why
- LiveChartsCore の `MinStep` は最小間隔の指定だけでは自動目盛り計算に吸収されるため、5 刻み表示を確実に反映するには `ForceStepToMin = true` を併用する必要があるため
### 確認
- `lsp_diagnostics` で `CvWpfclient/ViewModels/MainMenuViewModel.cs` にエラーがないことを確認。
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認。

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

## [2026-04-28] 16:42 メインメニュー温度チャートの軸線透明度調整
### Agent
- gemini-3.1-pro-preview : github-copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MainMenu の気温グラフで、X/Y軸の線を transparent 80% まで薄くし、確認・ログ・commit まで完了したい
### 実施内容
- CvWpfclient/ViewModels/MainMenuViewModel.cs: `ApplyForecastTheme` でテーマ連動の `textColor` から `separatorColor = textColor.WithAlpha(51)` を生成し、`ForecastXAxes` / `ForecastYAxes` の `SeparatorsPaint` に適用して軸線だけを 20% 不透明度（80%透明）へ調整
### 技術決定 Why
- `LiveChartsCore` の仕様に従い、ラベル用（`LabelsPaint`）とグリッド用（`SeparatorsPaint`）でペイントオブジェクトを分け、既存テーマカラーの透過度（Alpha）のみを変更する最小限の修正とした
### 影響範囲
- メインメニューの天気予報チャート表示のみ
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認

---
## [2026-04-30] 15:06 Creative Vision 10 全体仕様書のGoogle Document作成
### Agent
- GPT-5 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：プロジェクトの全体仕様書を Google Document に作成する
### 実施内容
- Google Drive/Creative Vision 10 全体仕様書: `readme.md`、`setup.md`、`creativevision10.slnx`、`Directory.Packages.props`、`graphify-out/GRAPH_REPORT.md`、主要な `CodeShare` / `CvServer` / `CvWpfclient` / `CvBase` / `CvDomainLogic` / `Tests` を確認し、全体仕様書を新規 Google Document として作成
- .sisyphus/20260430_google_doc_project_spec.md: 参照情報と作成方針の作業メモを作成
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- 全体仕様書は実装の詳細を単一画面・単一モジュールから推測せず、既存 README、graphify の中核ノード、ソリューション構成、契約定義、サーバ起動設定、WPF メニュー構成、DB 定義、ドメインロジック、テストから横断的に根拠を集める必要があるため
### 確認
- Google Docs connector の `get_document_text` で、作成先 documentId `1ilMF9Zr7RsTe6gidqexQrHqqzt7_UEHszybAYksQY2s`、タイトル `Creative Vision 10 全体仕様書`、tabId `t.0`、本文 17 章の `HEADING_1` 見出しを確認
- Google Drive の `text/html` エクスポートで、タイトル、h1 見出し、本文フォント、章順が HTML 構造として出力されることを確認
- コード変更はないため、dotnet build は未実行

---

## [2026-04-30] 16:01 MessageBoxViewの初期フォーカスを最も左側の表示ボタンに変更
### Agent
- gpt-5.4 / gemini-3.1-pro-preview : github-copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MessageBoxViewの表示時、初期フォーカスを最も左側に表示されているボタンへ設定する。
### 実施内容
- CvWpfclient/Helpers/MessageBoxView.xaml.cs: SetupButton内の個別のFocus指定を削除し、一括で左から順(LeftButton -> MiddleButton -> RightButton)に表示・有効・フォーカス可能状態を確認してフォーカスを当てる処理をDispatcher.InvokeAsync(DispatcherPriority.Loaded)にて追加。
### 技術決定 Why
- 画面描画後の適切なタイミング(Loaded時)で、左端から順に表示・操作可能なボタンへ確実にフォーカスを当てるため。以前のDefaultResultに依存するフォーカス制御を排除。
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` を再実行し、warning 0 / error 0 でビルド成功を確認。

---

## [2026-05-01] 12:28 SelectWinViewヘッダー右側への合計件数表示追加
### Agent
- gpt-5.4 / gemini-3.1-pro-preview : github-copilot
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`SelectWinView` のヘッダ部分で、現在の「選択画面」テキストエリアの右側に `合計件数{0}` を3桁区切り表示したい。View および ViewModel を修正し、write-log と commit まで行いたい
### 実施内容
- CvWpfclient/Views/Sub/SelectWinView.xaml: 上段 `ColorZone` ヘッダの `Title` 右側へ `Count` バインドの件数表示を追加し、表示書式を `合計件数{0:N0}` に変更。内側カードヘッダは既存の選択中項目表示だけを維持する構成へ戻した
- CvWpfclient/ViewModels/Sub/SelectWinViewModel.cs: `ListData` 差し替え時に `CollectionChanged` の購読を張り替え、コレクション件数の増減に応じて `Count` が自動更新されるよう修正。初期化経路の重複 `Count` 代入は削除した
### 技術決定 Why
- ユーザー指定の「選択画面」テキストエリア右側という位置に合わせるため、件数表示は内側カード見出しではなく上段 `ColorZone` タイトル行へ置くのが最も素直で差分も小さいため
- ViewModel 側は単なる表示用プロパティ追加ではなく、既存 `Count` を `ListData` の差し替え・増減に追従させることで、将来のコレクション変更でも表示件数がずれないようにしたため
### 確認
- `python3 -c "import xml.etree.ElementTree as ET; ET.parse(r'CvWpfclient/Views/Sub/SelectWinView.xaml'); print('XML_OK')"` で XAML の XML 整形式を確認
- `lsp_diagnostics` で `CvWpfclient/ViewModels/Sub/SelectWinViewModel.cs` に問題がないことを確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認（CodeShare.dll の一時ロック警告は再試行後に解消）

---

## [2026-05-12] 18:05 SelectShohinView検索条件領域のScrollViewer対応
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：CvWpfclient/Views/Sub/SelectShohinView.xaml の ScrollViewer 対応を行い、検索条件領域をスクロール可能にする。修正、確認、log、1-commitまで一連で実行する
### 実施内容
- CvWpfclient/Views/Sub/SelectShohinView.xaml: 検索モードカード内の検索条件 `UniformGrid` と補足文を `ScrollViewer` 配下へ移し、下部の戻る・一覧表示ボタンは固定行として維持するよう行構成を整理
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- 画面高さが不足した場合でも検索条件を縦スクロールでき、確定操作ボタンは常にカード下部に残すため。既存のバインディングや検索処理には手を入れず、Viewのレイアウト変更だけに限定した
### 確認
- `git diff --check` で空白エラーなしを確認
- PowerShell の `[xml](Get-Content -Raw)` で `SelectShohinView.xaml` のXML整形式を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功を確認（warning 0 / error 0）

---

## [2026-05-18] 15:34 SysLoginViewのレイアウト調整
### Agent
- GPT-5.4 : OpenAI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient の SysLoginView で、ID のTextBox枠を半分幅にし、"パス暗号化"ボタンをパスワードTextBoxの下へ移動し、作成日と修正日を1行表示からそれぞれ別行表示へ変更する。write-log と commit まで実行する
### 実施内容
- CvWpfclient/Views/00System/SysLoginView.xaml: ID表示TextBoxを幅150の左寄せに変更し、パス暗号化ボタンをパスワード直下へ移動、作成日・修正日をそれぞれ独立した行へ分割して日時Converter付きで表示するよう修正
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- 既存の2列GridとMaterialDesignスタイルを崩さず最小差分で要望を反映するため、既存項目のGrid.Rowだけを後方へシフトしてボタン位置を入れ替え、日付表示は単一MultiBindingから個別Bindingへ分離した
### 確認
- `python3 -c "import xml.etree.ElementTree as ET; ET.parse(r'CvWpfclient/Views/00System/SysLoginView.xaml'); print('XAML XML parse OK')"` で XAML の XML 整形式を確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-05-18] 17:21 メインテーマ切替時のウィンドウアイコン反映修正
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：インストールした実環境でメインテーマ切替時にウィンドウアイコンが切り替わらない件を修正し、`CvWpfclient/CvWpfclient.csproj` と `MainMenuView` / テーマリソースのアイコン反映経路を確認して、Velopack 配布後も切り替わる形にする
### 実施内容
- CvWpfclient/CvWpfclient.csproj: `cv10-*.ico` が WPF Resource として定義され、`CreativeVision10.g.resources` に埋め込まれることを確認。配布後の loose file 依存を避けるため既存の Resource 指定を維持
- CvWpfclient/Resources/UIMainTheme.*.xaml: `WindowIcon` の `UriSource` を `/cv10-*.ico` から `pack://application:,,,/cv10-*.ico` へ変更し、EXE 内 WPF Resource を明示参照するよう修正
- CvWpfclient/Services/MainThemeService.cs: メインテーマ適用時に `WindowIcon` リソースへ対象テーマの `BitmapFrame` を明示設定し、既存 Window が参照するアイコンリソースを更新する処理を追加
- CvWpfclient/Views/MainMenuView.xaml.cs: `MainThemeChanged` を購読し、テーマ切替時に `Window.Icon` を明示再設定する処理を追加
### 技術決定 Why
- 原因は、テーマ辞書内の相対的なアイコン URI と `DynamicResource` のみでは、Velopack 配布後の実環境で既存 Window の shell アイコン更新まで確実に伝播しない可能性があるため。アイコンは `csproj` の WPF Resource として EXE に埋め込まれているため、`pack://application:,,,/` で明示参照し、テーマ変更イベントで `Window.Icon` を再設定する構成にした
### 確認
- `git diff --check` で空白エラーなしを確認
- PowerShell の `[xml](Get-Content -Raw)` で `MainMenuView.xaml` と `UIMainTheme.*.xaml` の XML 整形式を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- `CreativeVision10.g.resources` に `cv10-default.ico` / `cv10-green.ico` / `cv10-orange.ico` / `cv10-red.ico` / `cv10-purple.ico` / `cv10.ico` が含まれることを確認

---

## [2026-05-19] 14:00 DatePickerTodayButtonBehaviorのMaterialDesign表示修正
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：CvWpfclient で DatePickerTodayButtonBehavior を使うとカレンダーポップアップ上部の表示部分が見えないため、標準の MaterialDesign DatePicker のように正常表示される形へ修正し、commit まで進める
### 実施内容
- CvWpfclient/Helpers/Behaviors/DatePickerTodayButtonBehavior.cs: CalendarStyle の ControlTemplate 差し替えを廃止し、DatePicker が生成した標準 Calendar を維持したまま Popup 下部に「今日」ボタンを追加する方式へ変更。ポップアップを閉じた時点で標準 Calendar を Popup 直下へ戻し、再オープン時に改めてフッターを差し込むよう整理
- CvWpfclient/Resources/UICalendar.xaml: Calendar 全体テンプレートを削除し、「今日」ボタン用フッターの Border スタイルだけを残す構成へ変更
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- 上部表示欠けの原因は、MaterialDesign の Calendar テンプレート全体を独自テンプレートで置き換えていたため、標準 DatePicker の選択日ヘッダー表示が崩れていたこと。テンプレートを再定義せず、標準 Calendar をそのまま使うことで MaterialDesign の表示仕様を維持しつつ、「今日」ボタンだけを追加できるようにした
### 確認
- `git diff --check` で空白エラーなしを確認
- PowerShell の `[xml](Get-Content -Raw)` で `UICalendar.xaml` と `ShopUriageInputView.xaml` の XML 整形式を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` は起動中の `CreativeVision10.exe` が出力 EXE をロックしてコピー工程で失敗したため、`/p:UseAppHost=false` 付きで再実行しビルド成功（0 warnings / 0 errors）を確認

---

## [2026-05-25] 16:13 CvBaseデータベース定義ドキュメント作成
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：CvBase プロジェクトで定義されている `[PrimaryKey]` や `[Comment]` 属性付きテーブル群を整理し、データベースのドキュメントを作成する
### 実施内容
- Doc/spec/spec.database.cvbase.md: CvBase の属性付きテーブル 29 件を、システム・マスター・トランザクション・集計・派生に分類し、共通基底列、作成状態、主キー、KeyDml、主要固有列、定義元を整理
- .sisyphus/2026-05-25_cvbase_database_doc.md: 調査方針と抽出結果の作業メモを作成
- Doc/aicording_log.md: 本作業ログを追記
### 技術決定 Why
- `DefineDataTable.Initialize()` の `CreateTable` 対象と `CreateDerivedTable<T>()` 対象を分けることで、属性上のテーブル候補と実際の初期作成対象の差分を確認しやすくするため
### 確認
- `Doc/spec/spec.database.cvbase.md` 内の対象テーブル行が 29 件であることを確認
- `git diff --check` で空白エラーなしを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvBase/CvBase.csproj"` でビルド成功を確認（warning 0 / error 0）

---
