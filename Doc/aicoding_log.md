## [2026-06-20] 17:33 MasterTokuiMenteView の請求情報タブ選択リスト化
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：MasterTokuiMenteView の「支払情報」タブ表示を「請求情報」に変更し、締日1,2,3 入金月、入金日 を MasterShiireMenteView を参考に選択リストへ変更する
### 実施内容
- CvWpfclient/Views/01Master/MasterTokuiMenteView.xaml: タブ見出しを「請求情報」に変更し、締日1/2/3、入金月、入金日を `MaterialDesignOutlinedComboBox` に置換
- CvWpfclient/ViewModels/01Master/MasterTokuiMenteViewModel.cs: `ShimeBiItems` と `PayMonthItems` を追加し、仕入先マスターメンテと同じ候補リストを利用できるよう変更
### 技術決定 Why
- 締日と入金日は取引先共通基底の `EnShime1` / `EnShime2` / `EnShime3` / `EnPayDay` にバインドし、既存の int 保存値を維持したまま選択リスト化した
### 確認
- `git diff --check` で空白エラーなし
- 編集ファイルの CRLF を確認
- 通常の `CvWpfclient/CvWpfclient.csproj` ビルドは起動中の `CreativeVision10 (11992)` と Visual Studio による DLL ロックで失敗
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj -p:OutDir=C:\gitroot\documents\new2022\cv10\.omo\build\CvWpfclient\"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-20] 17:14 MasterShohinMenteView の商品選択条件と価格レイアウト調整
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：MasterShohinMenteView から呼び出される商品選択画面で、商品Idの複数選択ではなくブランドIdの複数選択に変更し、ID from/to の幅固定、画像幅縮小、価格4項目の横並びと右詰め3桁区切り表示を行う
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterShohinMenteViewModel.cs: 一覧取得前の RangeParamView 呼び出しを商品マスタ専用に override し、複数ID選択対象を `MasterMeisho` の `Kubun='BRD'` に変更。選択IDは `Id_Brand IN (...)` として商品一覧条件へ反映
- CvWpfclient/ViewModels/Sub/SelectParameter.cs, RangeParamViewModel.cs, BaseMenteViewModel.cs: 複数ID選択の表示名 `IdsDisplayName` を追加し、通常画面は従来表示、商品マスタでは「ブランドId」と表示できるよう変更
- CvWpfclient/Views/Sub/RangeParamView.xaml: ID (終了) TextBox の幅を ID (開始) と同じ固定幅に変更
- CvWpfclient/Views/01Master/MasterShohinMenteView.xaml: 画像列幅を 320 から 220 に縮小し、元上代/上代、原価/仕入単価を横一列に配置。価格4項目を右詰め、`N0` 形式で表示
### 技術決定 Why
- `RangeParamView` は商品マスタ以外でも使われるため、共通Viewは表示名拡張に留め、商品マスタ側だけ選択対象テーブルとWHERE列を差し替えて既存のID from/to・名前検索を維持した
### 確認
- `git diff --check` で空白エラーなし
- `CvWpfclient/Views/Sub/RangeParamView.xaml` と `CvWpfclient/Views/01Master/MasterShohinMenteView.xaml` の XML 構文チェック成功
- 編集ファイルの CRLF を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` は通常実行で Microsoft SDKs 権限エラーになったため、承認付きで再実行しビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-20] 16:41 ShopUriageInputViewModel の商品検索SQLをJSON関数化
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：Tran01Tenuri.Jmeisai の実データ構造を踏まえ、ShopUriageInputViewModel.cs の ListWhere で selectParam.ShohinCdLike による商品検索を JSON 関数で正しく行う
### 実施内容
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: `Jmeisai LIKE` の単純検索を廃止し、`json_each(Jmeisai)` で明細を展開して `Code_Shohin` / `Mei_Shohin` の部分一致、または `MasterShohin` の `Code` / `Name` 部分一致から得た `Id` と明細 `Id_Shohin` の一致で検索するSQLに変更
### 技術決定 Why
- `Jmeisai` は JSON 配列で、商品名や商品IDは各明細オブジェクト内の `Mei_Shohin` / `Id_Shohin` に格納されるため、文字列全体への `LIKE` ではなく SQLite JSON 関数で明細単位に判定する必要がある
### 確認
- `git diff --check` で空白エラーなし
- `CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs` の CRLF を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-20] 16:18 SelectWinViewModel / SelectMultiWinViewModel の最大件数制限を AppGlobal.Application.Limit に統一
### Agent
- kimi-k2.6 : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：Sub/SelectWinViewModel.cs と Sub/SelectMultiWinViewModel のデータ取得時に AppGlobal.Application.Limit を使用して最大件数を制限する
### 実施内容
- CvWpfclient/ViewModels/Sub/SelectWinViewModel.cs: `QueryListParam` / `QueryListSimpleParam` のコンストラクタに `maxCount: AppGlobal.Application.Limit` を追加し、サーバー問い合わせ時に最大件数を制限するよう変更
- CvWpfclient/ViewModels/Sub/SelectMultiWinViewModel.cs: `QueryListParam` / `QueryListSimpleParam` のコンストラクタに `maxCount: AppGlobal.Application.Limit` を追加。`SelectAll` コマンド内のハードコードされた `100` も `AppGlobal.Application.Limit` に置き換え、上限超過時のメッセージに最大件数を表示するよう変更
### 技術決定 Why
- `QueryListParam` は既に `MaxCount` プロパティと `AddWhereOrder` で `limit` 句生成に対応しており、`AppGlobal.Application.Limit`（デフォルト100件）を渡すだけでサーバー側のクエリ結果が制限される。`SelectAll` の上限判定も同じ設定値を参照することで、選択画面全体で一貫した件数制御を実現した
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-20] 07:09 .github/copilot-instructions.mdをAGENTS.mdと整合
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：AGENTS.md と .github/copilot-instructions.md を比較し、違っている部分を合わせる
### 実施内容
- .github/copilot-instructions.md: ソリューション名を `creativevision10.slnx` に修正、ビルドコマンドを WSL2 cmd.exe 形式に統一、CR+LF / SQLite 3.46 / protobuf-net.Grpc / CommunityToolkit を追記、Architecture / Priority Workflow / SkillOpt-Based Skill Maintenance セクションを追加、Coding & WPF Standards を AGENTS.md と整合
### 技術決定 Why
- GitHub Copilot 向けの指示ファイルでありつつ、AGENTS.md（OpenCode 用）と矛盾しないよう統合。Copilot 特有の有用な項目（Client/Server OS、C# 14 Usage、WPF clipping 注意）は維持
### 確認
- `file` で .github/copilot-instructions.md の CRLF 改行を確認
- `git diff --check` で空白エラーなし
- git commit 完了

---

## [2026-06-20] 07:00 MasterShiireMenteView 支払情報の締日選択方式変更
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：MasterShiireMenteView の締日1,2,3 を MasterSysKanriMenteView の締日の選択方式に合わせ、支払月を「当月、翌月、翌々月、3ヶ月後、4ヶ月後」の選択で 0-4 に割り当て、支払日も締日の選択方式に合わせる
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterShiireMenteViewModel.cs: 締日選択用の ShimeBiItems と支払月選択用の PayMonthItems を追加
- CvWpfclient/Views/01Master/MasterShiireMenteView.xaml: 支払情報タブの締日1/2/3・支払日を EnumShime 選択 ComboBox に変更し、支払月を 0-4 の選択 ComboBox に変更
- Doc/aicoding_log.md: 作業内容を先頭へ追記
### 技術決定 Why
- 既存の MasterTorihiki に既存の EnShime1/EnShime2/EnShime3/EnPayDay ラッパーがあるため、保存値の int は維持したまま MasterSysKanriMenteView と同じ EnumShime 選択方式へ揃えた
### 確認
- 対象 XAML の XML 構文チェックでエラーなし
- 対象ファイルの CRLF を確認（LFOnly=0）
- `git diff --check` で空白エラーなし
- 通常権限のビルドは SDK 参照権限で失敗したため、権限付きで `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` を実行し、ビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-20] 06:48 MasterShohinMenteView 名称タブ区分名配置変更
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：MasterShohinMenteView.xaml の詳細画面・名称で、区分とコードの間に区分名を表示する
### 実施内容
- CvWpfclient/Views/01Master/MasterShohinMenteView.xaml: 名称タブの JsubGrid で区分名列を区分列の直後へ移動し、名称列を残り幅表示へ変更
- Doc/aicoding_log.md: 作業内容を先頭へ追記
### 技術決定 Why
- 既存の MasterGeneralMeisho.Kbname バインディングを再利用し、ViewModel や保存処理を変更せず列配置だけで要望を満たした
### 確認
- 対象 XAML の CRLF を確認（Mixed=False）
- `git diff --check` で空白エラーなし
- 通常権限のビルドは SDK 参照権限で失敗したため、権限付きで `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` を実行し、ビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-19] 22:06 SysTableSpecView 印刷レイアウトにテーブルComment/旧テーブル名/フィールド説明を出力
### Agent
- kimi-k2.7-code : opencode-go : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：SysTableSpecView で印刷レイアウトの"スキーマ名"部分に Table.Comment 属性を出力し、追加のテーブル情報として旧テーブル名を、各プロパティの長さの次の列に OldTableCommentAttr の説明を出力する
### 実施内容
- CvWpfclient/ViewModels/00System/SysTableSpecViewModel.cs: SchemaName 定数を削除。テーブル型から CommentAttribute.Content / OldTableCommentAttr.Name を取得し CSV の item2 / item10 に出力。各プロパティの OldTableCommentAttr.Content を取得し CSV の item6 に出力（ObservableProperty の生成フィールドにも対応）
- printform/SysTableSpec.qfm: item6 / item10 に position length を追加。明細の空欄列を item6 表示に、ヘッダを「説明」に変更。ページヘッダに「旧テーブル名」ラベルと item10 表示を追加。定義種別を同じ行の右側に移動
### 技術決定 Why
- Table.Comment はカンマを削除してから出力。OldTableCommentAttr は [ObservableProperty] 対応でプロパティに直接付かない場合は生成されたキャメルケースの backing field から読み取る
### 影響範囲
- 修正したファイルのみ
### 確認
- `dotnet build CvWpfclient/CvWpfclient.csproj` でビルド成功（0 warnings / 0 errors）
- qfm validator で Shift_JIS / 構造ともに OK

---

## [2026-06-19] 15:51 CvBase プロジェクトの KeyDml / PrimaryKey 文字列リテラルを nameof() に変更
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvBase プロジェクト内の KeyDml 属性など、プロパティ名を文字列リテラルで記述している部分を nameof() に変更し、リネーム時の安全性を高める
### 実施内容
- CvBase/BaseDb*.cs 各ファイル: [KeyDml(..., "PropName")] を [KeyDml(..., nameof(PropName))] に変更
- CvBase/BaseDb*.cs 各ファイル: [PrimaryKey("Id", AutoIncrement = true)] を [PrimaryKey(nameof(Id), AutoIncrement = true)] に変更
- コメントアウトされた SQL 式（json_extract(...)）はプロパティ名ではないため文字列リテラルのまま残す
### 技術決定 Why
- nameof() を使うことで、対象プロパティをリネームした際にコンパイルエラーで検知でき、属性とプロパティの不一致を防ぐ。KeyDml のカラムリスト、PrimaryKey の Id ともに該当プロパティが存在するため nameof() に置き換え可能
### 影響範囲
- CvBase プロジェクト全 .cs ファイルの KeyDml / PrimaryKey 属性（コメントアウト部を除く）
### 確認
- `dotnet build CvBase/CvBase.csproj` でビルド成功（0 warnings / 0 errors）を確認
- grep で [PrimaryKey("Id"] および KeyDml 内のプロパティ名文字列リテラルが残存していないことを確認

---

## [2026-06-19] 08:17 aicoding_log ファイルの並べ替えと日付修正
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：AGENTS.md のログ記述仕様を「新しいものがTOPにくる」ように変更したことに伴い、Doc/aicoding_log.md および過去アーカイブ aicoding_log_[001-006].md を新しい日付順に並べ替え、先頭の Log-Format サンプルを削除する
### 実施内容
- Doc/aicoding_log.md, Doc/aicoding_log_001.md 〜 aicoding_log_006.md: 全エントリを日付・時刻の降順にソート
- 各アーカイブファイルの先頭にあった Log-Format テンプレート / AIツール説明 / アーカイブルールなどを削除し、実際のログエントリのみに整理
- Doc/aicoding_log_003.md: `[2024-06-07] 11:43 MainMenuウィンドウ右上ボタンのMaterialDesignアイコン化` を git log 確認により `[2026-04-08] 11:43` に修正
- Python スクリプトでソート・検証を実施し、全 242 エントリの降順・重複・抜けを確認
### 技術決定 Why
- 手作業での並べ替えはミスが発生しやすいため、正規表現でエントリを抽出・datetime でソート・CRLF で書き出す Python スクリプトを使用した
- 2024-06-07 のエントリは git log で対応コミット（2026-04-08 11:44、作業時間 11:40-11:43）を発見したため、単純な年の入力ミスと判断して修正した
### 確認
- 検証スクリプトで全ファイルの降順 OK、重複タイムスタンプなし、合計 242 エントリの抜けなしを確認
- `file` コマンドで全ファイルが UTF-8 / CRLF であることを確認

---
## [2026-06-19] 07:50 RangeParamView Id複数選択レイアウト調整とToolTip折り返し対応
### Agent
- kimi-k2.7-code : opencode-go : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient の Sub/RangeParamView で、Id の複数選択件数が多い場合に表示が長くなり他の部品のレイアウトが崩れるため、grid.columnspan を増やしつつ元のwindowsサイズを超えず、他の部品のレイアウトを崩さないよう調整する。またTooltipが長くなった場合は折り返して見やすくする
### 実施内容
- CvWpfclient/Views/Sub/RangeParamView.xaml: 親 Grid の最右列を Auto から 90 に固定し、Id 選択行を DockPanel で ColumnSpan="6" に展開。選択結果 TextBlock は TextTrimming で切り、ToolTip は MaxWidth=600, TextWrapping=Wrap で折り返し表示
### 技術決定 Why
- Auto 列が選択結果テキストの DesiredWidth に引きずられて Grid 全体が広がっていたため、最右列を固定幅にして DockPanel の残りスペースで TextTrimming を確実に機能させた。ToolTip は Popup として親サイズ制約を受けないため、内部 TextBlock に MaxWidth と TextWrapping を与えて折り返し表示にした
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-18] 15:55 在庫・掛再更新画面（StockKakeUpdateView）の実装
### Agent
- kimi-k2.7-code : opencode-go : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：Views._31Monthly.StockKakeUpdateView の View / ViewModel を作成し、サーバ API CvFlag.Msg051_SummaryRealStock を呼び出す。画面は年月 yyyy/MM - yyyy/MM、実行/キャンセルボタン、実行時確認メッセージ、サーバ返答メッセージ表示エリアのシンプル構成とする
### 実施内容
- CvWpfclient/ViewModels/31Monthly/StockKakeUpdateViewModel.cs: Helpers.BaseViewModel を継承し、年月 From/To、処理メッセージ、進捗、処理中フラグの ObservableProperty を追加。実行コマンドで入力検証、確認ダイアログ、対象年月分の Msg051_SummaryRealStock ストリーミング呼び出し、進捗とサーバメッセージの更新を実装
- CvWpfclient/Views/31Monthly/StockKakeUpdateView.xaml: MaterialDesign を使ったシンプルレイアウト（ColorZone ヘッダー、年月入力 Card、進捗 ProgressBar、処理メッセージ GroupBox、実行/キャンセル Button）を追加。既存の StockKakeUpdateView.xaml.cs は変更なし
### 技術決定 Why
- Msg051_SummaryRealStock は QueryMsgStreamAsync でのストリーミング応答であること、リクエスト型 SummaryRealDateParameter が単一月度しか受け取らないことを確認。画面の From-To 範囲に対して対象月をループし、各月をストリーミングで処理してメッセージと進捗を反映させた。キャンセルボタンは BaseViewModel.ExitCommand にバインドし、処理中は入力欄を無効化する
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- 編集ファイルの CRLF 化と `git diff --check` での空白エラーなしを確認

---

## [2026-06-18] 13:59 ExDatabaseOption.ClearPools の専用接続タイムアウトを 1 秒に統一
### Agent
- kimi-k2.7-code : opencode-go : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvBaseSqlite\ExDatabaseOption.cs の ClearPools() 処理で、他プロセスが WAL を開いている場合に FinalizeWalFiles で発生する待ちへの対応。専用接続のタイムアウトを 1 秒に設定する
### 実施内容
- CvBaseSqlite/ExDatabaseOption.cs: `BuildConnectionString` に `defaultTimeout` パラメータを追加し、`FinalizeWalFiles` の `Pooling=False` 専用接続で `DefaultTimeout=1` を指定。以前追加した `PRAGMA busy_timeout=500;` は削除
### 技術決定 Why
- `SqliteConnectionStringBuilder.DefaultTimeout` は秒単位の int 型で 500ms を直接指定できないこと、かつ他プロセスによる WAL ロック待ちを接続オープン段階から制限したいことから、接続文字列で `DefaultTimeout=1` を指定し、コマンド実行時の待ちロックタイムアウトを 1 秒に統一した
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvBaseSqlite/CvBaseSqlite.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- テスト実行は .NET 10 SDK の Microsoft.Testing.Platform 移行により `dotnet test` が未対応のため実施できず（既存の制約）

---

## [2026-06-18] 13:52 ExDatabaseOption.ClearPools の専用接続タイムアウトを 500ms に設定
### Agent
- kimi-k2.7-code : opencode-go : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvBaseSqlite\ExDatabaseOption.cs の ClearPools() 処理で、DB の専用接続オープンのタイムアウトを 500ms に設定する
### 実施内容
- CvBaseSqlite/ExDatabaseOption.cs: `FinalizeWalFiles` メソッドの `Pooling=False` 専用接続オープン後に `PRAGMA busy_timeout=500;` を実行するよう追加
### 技術決定 Why
- `SqliteConnectionStringBuilder.DefaultTimeout` は秒単位の int 型であり 500ms を直接指定できないため、SQLite の `busy_timeout` プラグマを接続オープン直後に発行して 500ms の待ちロックタイムアウトを設定した
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvBaseSqlite/CvBaseSqlite.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-18] 12:44 SelectMultiWindow Esc 押下時の Ambiguous match エラー対応
### Agent
- kimi-k2.7-code : opencode-go : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：BaseWindow.TryExecuteViewModelCommand で SelectMultiWindow の Esc 押下時に「Ambiguous match found for ExitCommand」エラーが発生する原因調査と対応
### 実施内容
- CvWpfclient/ViewModels/Sub/SelectMultiWinViewModel.cs: `[RelayCommand] public void Exit()` を削除し、基底 `BaseViewModel.OnExit()` を override して `ClientLib.ExitDialogResult(this, false)` を呼ぶように変更。これにより `BaseViewModel` で定義済みの `ExitCommand` と Source Generator 生成の `ExitCommand` の重複を解消
### 技術決定 Why
- `BaseViewModel` に `ICommand ExitCommand` が既に存在するため、派生クラスで `[RelayCommand] void Exit()` とすると Source Generator が同名の `IRelayCommand ExitCommand` を生成し、`Type.GetProperty("ExitCommand")` で AmbiguousMatchException が発生していた。継承元のコマンドを再利用しつつ、閉じる動作だけを override することで最小差分で解決した
### 確認
- 変更ファイルの LSP 診断でエラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-18] 12:03 DB定義書出力画面追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- VS2026
### 目的
- ユーザーからの要望：printform/SysTableSpec.qfm のフォーマットを使い、旧システムからの出力CSVおよび出力例を参考に、00System/SysTableSpecViewModel.cs、00System/SysTableSpecView.xaml、00System/SysTableSpecView.xaml.cs を作成する。PrintMasterShainCardView を参考にし、サーバから取得したテーブル一覧を複数選択Windowで選択して印刷する。MenuData には「汎用マスタメンテ」の下へ追加し、commit まで行う。
### 実施内容
- CvWpfclient/ViewModels/00System/SysTableSpecViewModel.cs: サーバの `Msg042_GetTableList` でテーブル一覧を取得し、既存 `SelectMultiWinView` で複数選択したテーブルのモデル定義から `SysTableSpec.qfm` 用の11列CSVを生成するViewModelを追加。
- CvWpfclient/Views/00System/SysTableSpecView.xaml: `PrintMasterShainCardView` と同じ `BaseWindow` / F6印刷構成で、テーブル複数選択と印刷実行を行う画面を追加。
- CvWpfclient/Views/00System/SysTableSpecView.xaml.cs: 画面初期化用code-behindを追加。
- CvWpfclient/Models/MenuData.cs: 「管理メニュー / テスト画面」の「汎用マスタメンテ」直下に「DB定義書出力」を追加。
- printform/SysTableSpec.qfm: 旧システムのDB定義書フォーマットとして印刷フォームに追加。
### 技術決定 Why
- テーブル一覧取得は既存 `Msg042_GetTableList` を利用し、サーバ側APIを増やさずに既存の複数選択Windowへローカルデータとして渡す構成にした。
- 印刷データは旧CSVの11列構成に合わせ、フィールド定義行とインデックス定義行を `PrintByCsvParam` で qfm に渡すことで、既存の印刷サーバ経路をそのまま使う構成にした。
### 確認
- `SysTableSpecView.xaml` をXMLとして読み込み、構文エラーなしを確認。
- `python .agents\skills\add-print-process-master-mente\scripts\validate_qfm.py printform\SysTableSpec.qfm` は、旧フォーム由来のページ位置が標準A4縦テンプレートと異なるため position 警告で終了することを確認。
- `git diff --check` で空白エラーなしを確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認。

---

## [2026-06-18] 12:02 MasterConfigメンテ画面追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- VS2026
### 目的
- ユーザーからの要望：MasterConfig のメンテ画面を作成し、MenuData の「マスター」配下で「システム管理マスタ」の下に追加して commit まで行う。
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterConfigMenteViewModel.cs: `BaseMenteViewModel<MasterConfig>` を使った一覧取得、追加、修正、削除用ViewModelを追加し、`Category,Name` 並びとカテゴリ/フラグ名の必須チェックを実装。
- CvWpfclient/Views/01Master/MasterConfigMenteView.xaml: `MasterConfig` の一覧DataGridと詳細編集欄を持つMaterialDesign系メンテ画面を追加。
- CvWpfclient/Views/01Master/MasterConfigMenteView.xaml.cs: 画面初期化用code-behindを追加。
- CvWpfclient/Models/MenuData.cs: 「■ マスター」配下の「システム管理マスタ」直下に「MasterConfigメンテ」を追加。
### 技術決定 Why
- `MasterConfig` は `Code` を持たないため、既定の `ListOrder=Code` を使わず `Category,Name` に上書きした。
- 既存のマスターメンテ画面と操作感を合わせるため、`MasterMeishoMenteView` 系の左右分割、ColorZone、Card、DataGrid、TabControl の構成を流用した。
### 確認
- `MasterConfigMenteView.xaml` をXML parseし、構文エラーなしを確認。
- `git diff --check` で空白エラーなしを確認。
- 編集ファイルがUTF-8 BOMなし / CRLFであることを確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認。

---

## [2026-06-18] 08:37 MainMenu天気表示への気象庁概要予報ToolTip追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- VS2026
### 目的
- ユーザーからの要望：MainMenuView.xaml の天気表示エリアで、気象庁 `overview_forecast/{地域コード}.json` の詳細情報をToolTip表示する。既存 `WeatherRegion` とは別に、環境設定で気象庁の地域コードを固定リストから選択できるようにする。
### 実施内容
- CvWpfclient/AppGlobal.cs: `JmaWeatherAreaCode` を追加し、実行時設定更新でも `WeatherRegion` と独立して保持できるように変更。
- CvWpfclient/Models/ClientSettingsDocument.cs, CvWpfclient/Services/SystemSettingsStore.cs, CvWpfclient/appsettings.json: `Application:JmaWeatherAreaCode` を追加し、デフォルトを東京都 `130000` として `clientsettings.json` へ保存可能にした。
- CvWpfclient/ViewModels/00System/SysSetConfigViewModel.cs, CvWpfclient/Views/00System/SysSetConfigView.xaml: 気象庁予報区の固定コードリストとComboBoxを追加し、一覧外コードは東京へフォールバックするようにした。
- CvWpfclient/ViewModels/MainMenuViewModel.cs, CvWpfclient/Views/MainMenuView.xaml: 気象庁概要予報JSONを既存天気更新と同じ初回/30分タイマー/設定反映後のタイミングで直接HTTP取得し、天気表示エリアのToolTipへ整形表示するようにした。
### 技術決定 Why
- 既存 `WeatherRegion` はOpenWeatherMap/gRPCの現在天気表示で使用されているため、気象庁の府県予報区コードは `JmaWeatherAreaCode` として別設定に分離した。
- 気象庁概要予報は単純なHTTP JSON取得で足りるため、既存 `IWeatherService` へ混ぜず、MainMenu側で独立して取得する構成にした。
### 確認
- `https://anko.education/webapi/jma` の予報区コード表と `https://www.jma.go.jp/bosai/forecast/data/overview_forecast/130000.json` のJSON形式を確認。
- `MainMenuView.xaml` / `SysSetConfigView.xaml` をXML parseし、構文エラーなしを確認。
- `git diff --check` で空白エラーなしを確認。
- 編集ファイルがUTF-8 BOMなし / CRLFであることを確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認。

---

## [2026-06-17] 16:16 ExDatabaseOption.ClearPools() の WAL/SHM クリーンアップ整理
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：SQLite 終了時に db-shm / db-wal が残らないように、CvBaseSqlite の ExDatabaseOption.ClearPools() を改変し、.db のみが残る状態にする。
### 実施内容
- CvBaseSqlite/ExDatabaseOption.cs: ClearPools(string) の制御フローを整理。
  - 接続文字列または DB ファイル名の両方を受け付ける GetDatabasePath() による実 DB パス解決を維持。
  - PRAGMA optimize を pool クリア前に実行。
  - SqliteConnection.ClearAllPools() で pooled 接続を解放。
  - Pooling=False の専用接続で PRAGMA wal_checkpoint(TRUNCATE) → PRAGMA journal_mode=DELETE を実行し WAL を収束。
  - ロックが外れた後に -wal / -shm をリトライ付きで削除。
### 技術決定 Why
- ExDatabaseSqlite.GetDbConn() は Pooling=True / Cache=Shared / WAL 有効化のため、終了時に db のみに戻すには pool クリア後に非 pooled 接続で checkpoint と journal_mode=DELETE を行い、ファイルハンドルが完全に解放されてから sidecar を削除する必要があるため。
- 既存呼び出し（CvServer/Program.cs の ClearPools(connStr)）に合わせ、文字列引数が接続文字列でもファイル名でも動作する形を維持。
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvBaseSqlite/CvBaseSqlite.csproj"` でビルド成功（0 warnings / 0 errors）を確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"` でビルド成功（0 warnings / 0 errors）を確認。
- テスト実行は .NET 10 SDK の Microsoft.Testing.Platform 移行により `dotnet test` が未対応のため実施できず（既存の制約）。

---

## [2026-06-17] 15:35 CvServer shutdown try/catch簡潔化
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- VS2026
### 目的
- ユーザーからの要望：CvServer の `Program.cs` 173行目 `app.Lifetime.ApplicationStopping.Register` の冗長な try/catch を簡潔にする。
### 実施内容
- CvServer/Program.cs: `ApplicationStopping` の shutdown checkpoint、DB close、SQLite pool cleanup の例外処理を `RunShutdownStep` に集約し、ネストした try/catch を削減。
### 技術決定 Why
- checkpoint、DB close、pool cleanup を停止時の独立したステップとして扱い、checkpoint 失敗時も close と pool cleanup が続行できる構成を維持しながら、同じ警告ログ形式で例外を扱うため。
### 確認
- `git diff --check` で空白エラーなしを確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"` でビルド成功（0 warnings / 0 errors）を確認。

---

## [2026-06-17] 15:05 CvServer ExDatabase Scoped化
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- VS2026
### 目的
- ユーザーからの要望：CvServer で使っている `ExDatabase` を Singleton から Scoped（リクエストごと）に変更する。影響度調査、実装計画、修正、確認、コミットまで実施する。追加指示として `SchedulerService` での DB clone を廃止する。
### 実施内容
- CvServer/Program.cs: `ExDatabase` の DI 登録を `AddSingleton` から `AddScoped` に変更。起動時 `AppGlobal.Init` と shutdown checkpoint は root provider 直取得ではなく明示スコープ内で `ExDatabase` を取得するよう修正。
- CvServer/Services/SchedulerService.cs: `ExDatabase` コンストラクタ注入と `CloneDb()` を廃止。`SchedulerService` は `IServiceScopeFactory` を保持し、スケジュール実行単位でスコープを作成して履歴登録、集計、SQLite WAL checkpoint を同一スコープ内 DB で処理するよう変更。
- Tests/TestServer/TestServer.cs: `SchedulerService` のコンストラクタ変更に合わせ、テスト用 `ServiceProvider` から `IServiceScopeFactory` を渡すよう修正。
### 技術決定 Why
- `CoreService` / `LoginService` など通常 gRPC リクエストで使用する DB はリクエストスコープで扱い、同一 `ExDatabase` インスタンスの共有を避ける。
- `SchedulerService` はスケジュール登録を維持するため singleton のまま残し、scoped DB を直接保持しない構成にした。長寿命 singleton から scoped service を保持するとスコープ寿命と矛盾するため、ジョブ実行時だけ `IServiceScopeFactory.CreateScope()` で DB を取得する。
### 影響範囲
- CvServer の DB 接続 lifetime。通常 gRPC リクエスト、起動時初期化、停止時 WAL checkpoint、スケジューラ自動実行履歴/集計/checkpoint に影響。
### 確認
- `graphify-out/GRAPH_REPORT.md` の freshness が現 HEAD `d5165df4` と一致していることを確認。
- `rg` で CvServer 内の `CloneDb()` 利用が消えていること、`ExDatabase` 登録が `AddScoped` になっていることを確認。
- `git diff --check` で空白エラーなしを確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"` でビルド成功（0 errors、既存の CvPrints/IKVM warning 212 件）を確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build Tests/TestServer/TestServer.csproj"` でビルド成功（0 warnings / 0 errors）を確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet run --project Tests/TestServer/TestServer.csproj --no-build"` で MSTest 6 件成功を確認。

---

## [2026-06-17] 12:14 店舗売上入力バーコード入力追加
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：ShopUriageInputView の「明細削除」ボタンの隣に「バーコード入力」ボタンを追加し、バーコード読取画面で JAN を読み込んだ行を店舗売上明細へ反映する。
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 明細操作ボタン行に「バーコード入力」ボタンを追加。
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: バーコード入力画面の起動、確定行の明細反映、同一 JanCode 明細の数量加算、明細金額と伝票合計の再計算を追加。
- CvWpfclient/ViewModels/Sub/InputBarcodeViewModel.cs: `DerivedShohinColSiz` の `Jan1` / `Jan2` / `Jan3` 完全一致検索、`MasterShohin` 取得、同一バーコード時の数量加算、確定用 `Tran99Meisai` 変換を実装。
- CvWpfclient/Views/Sub/InputBarcodeView.xaml: 「バーコード読取」ラベル、バーコード TextBox、読取結果 DataGrid、確定ボタンを持つ BaseWindow 画面を追加。
- CvWpfclient/Views/Sub/InputBarcodeView.xaml.cs: 初期表示時にバーコード TextBox へフォーカスする code-behind を追加。
### 技術決定 Why
- 商品名と単価は `MasterShohin`、色サイズと JAN 判定は `DerivedShohinColSiz` を使い、既存の商品選択・色サイズ選択と同じ現行DB構造に合わせた。
- 親画面の `EditMeisai` が明細編集と合計計算の責務を持つため、バーコード画面は `Tran99Meisai` 候補を返し、親 ViewModel 側で行No採番、既存 JanCode への数量加算、合計再計算を行う構成にした。
### 確認
- `CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml` と `CvWpfclient/Views/Sub/InputBarcodeView.xaml` をXMLとして読み込み、構文エラーなしを確認。
- `git diff --check` で空白エラーなしを確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認。

---

## [2026-06-17] 11:06 BaseWindow ディスプレイ収まり調整
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：BaseWindow の OnContentRendered 時に、ウィンドウの位置・サイズが実際のディスプレイ領域を超えていたらディスプレイに収まるよう調整する
### 実施内容
- CvWpfclient/Helpers/Windows/BaseWindow.cs: OnContentRendered 内で EnsureWithinDisplayBounds を呼び出すよう変更。user32.dll の MonitorFromWindow / GetMonitorInfo を使い、ウィンドウが存在するモニターの作業領域を取得。VisualTreeHelper.GetDpi で取得した DPI スケールで物理ピクセルを WPF の DIP に変換し、ウィンドウの Left / Top / Width / Height を作業領域内に制限する処理を追加。NativeMethods クラスを同ファイル内に追加。
### 技術決定 Why
- プライマリディスプレイだけでなく、ウィンドウが存在するモニターを特定して補正するため Win32 API を使用した。DPI スケーリング環境でも正しく収まるよう、GetMonitorInfo の物理ピクセルを VisualTreeHelper.GetDpi で変換した。
### 影響範囲
- CvWpfclient/Helpers/Windows/BaseWindow.cs のみ。BaseWindow を継承するすべての業務画面に影響。
### 確認
- BaseWindow.cs の CRLF を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-16] 16:45 商品バーコードブック印刷画面追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- VS2026
### 目的
- ユーザーからの要望：MasterPrintBarcodeView.xaml / MasterPrintBarcodeView.xaml.cs / MasterPrintBarcodeViewModel.cs を作成し、MenuData のマスター配下「顧客マスタメンテ」の下に「商品バーコードブック」を追加する。旧画面 `.omo/20260616-barcodebook.txt` の qfm を既存 `printform/MasterPrintBarcode*.qfm` に対応させ、展示会Id/ブランドIdは複数選択、商品CD/商品名は部分一致、最大件数超過時はエラーメッセージで止める。
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterPrintBarcodeViewModel.cs: `MasterShohin` を基点に、SKU出力時は `DerivedShohinColSiz` を left join する印刷SQLを追加。商品/SKU、JAN/CODE39/NW7 の組み合わせで `MasterPrintBarcode002.qfm` / `MasterPrintBarcode0021.qfm` / `MasterPrintBarcode0022.qfm` / `MasterPrintBarcodeSho.qfm` / `MasterPrintBarcodeNw7.qfm` / `MasterPrintBarcodeCode39.qfm` を切り替えるよう実装。
- CvWpfclient/ViewModels/01Master/MasterPrintBarcodeViewModel.cs: 展示会IdとブランドIdの `MasterMeisho` 複数選択、商品CD `S.Code LIKE`、商品名 `S.Name LIKE` の条件を追加。印刷前に `AppGlobal.Application.Limit` と同条件の出力件数を比較し、超過時はエラーメッセージを表示して印刷を中止するよう実装。
- CvWpfclient/Views/01Master/MasterPrintBarcodeView.xaml: 既存印刷画面に合わせた BaseWindow / MaterialDesign レイアウトで、条件入力、出力方法、バーコード種類、印刷実行ボタンを追加。
- CvWpfclient/Views/01Master/MasterPrintBarcodeView.xaml.cs: 画面初期化用 code-behind を追加。
- CvWpfclient/Models/MenuData.cs: マスター配下の「顧客マスタメンテ」の下に「商品バーコードブック」を追加。
### 技術決定 Why
- 旧 `HC$MASTER_SHOHIN` / `HC$MASTER_SHOHIN_JAN` ではなく、現行DBの `MasterShohin` と `DerivedShohinColSiz` を使用し、旧帳票qfmが期待する列順だけを維持した。
- 「出力区分」は不要条件のためUIとSQL条件から除外し、旧画面の `使用FLG` 条件も追加しなかった。
- 件数制限は商品件数ではなく帳票出力行数で判定するため、商品出力とSKU出力で同じ FROM/WHERE の `count(*)` を事前実行する方式にした。
### 確認
- `python .agents\skills\add-print-process-master-mente\scripts\validate_qfm.py printform\MasterPrintBarcode002.qfm printform\MasterPrintBarcode0021.qfm printform\MasterPrintBarcode0022.qfm printform\MasterPrintBarcodeSho.qfm printform\MasterPrintBarcodeNw7.qfm printform\MasterPrintBarcodeCode39.qfm` は既存バーコードqfmの用紙位置が標準A4縦 position ではないため位置チェックでエラーを検出。既存レイアウトqfmのため無変更。
- `CvWpfclient/Views/01Master/MasterPrintBarcodeView.xaml` をXMLとして読み込み、構文エラーなしを確認。
- `C:\gitroot\ut\sqlite3.exe -readonly CvServer\server-user163.db` で SKU出力SQLと商品出力SQLの主要列参照、および件数SQLが実DBで通ることを確認。
- `git diff --check` で空白エラーなしを確認。
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認。

---

## [2026-06-16] 15:55 MasterShohinMenteView の検索ダイアログを Sub.SelectShohinView に変更
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：MasterShohinMenteView の検索ダイアログを、Sub.SelectShohinView を使用するよう変更する
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterShohinMenteViewModel.cs: 一覧取得時の検索ダイアログを RangeParamView から Sub.SelectShohinView に変更。BeforeListAsync をオーバーライドし、Sub.SelectShohinView で選択された商品の Id を SelectCodeParam.Ids に設定して一覧表示するよう変更。不要となった SelectCodeDisplayName オーバーライドを削除。
### 技術決定 Why
- 既存の Sub.SelectShohinView / SelectShohinViewModel を流用し、新たな View / ViewModel / DI 登録を追加せずに商品検索ダイアログを統一した。
- 選択された1商品のみを一覧に表示する形に変更（元の RangeParamView は範囲条件で複数件表示していた）。
### 影響範囲
- MasterShohinMenteView の「一覧取得」(F5) 動作：商品選択ダイアログが表示され、選択後に該当商品1件が一覧に表示される。
### 確認
- lsp_diagnostics で MasterShohinMenteViewModel.cs に診断なしを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet clean CvWpfclient/CvWpfclient.csproj"` 後、`C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-16] 15:45 管理者用システム処理画面の追加
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：「管理メニュー」の「旧DBからの選択変換処理」の次に「管理者用システム処理」を追加し、SampleViewModel の TestDatabaseClose / TestDatabaseReOpen をボタンで実行できる画面を新規作成する
### 実施内容
- CvWpfclient/Models/MenuData.cs: 「旧DBからの選択変換処理」の次に「管理者用システム処理」を追加
- CvWpfclient/ViewModels/00System/SysExecMiscViewModel.cs: TestDatabaseClose / TestDatabaseReOpen コマンドを SampleViewModel からコピーし、実行前に確認メッセージを表示する ViewModel を追加
- CvWpfclient/Views/00System/SysExecMiscView.xaml: ConcvertDbView を参考に ColorZone ヘッダー、実行ボタンカード、実行結果 GroupBox、キャンセルボタンを配置
- CvWpfclient/Views/00System/SysExecMiscView.xaml.cs: BaseWindow を継承する code-behind を追加
### 技術決定 Why
- 既存の ConcvertDbView の外枠パターンを踏襲し、今後のボタン追加に備えて WrapPanel でボタンを配置した
- SampleViewModel の TestDatabaseClose / TestDatabaseReOpen 処理をそのまま ViewModel に移設し、実行前に MessageEx.ShowQuestionDialog で確認するようにした
- 実行結果は ResultMessage プロパティに TextBox で表示し、処理中は IsProcessing でボタンを無効化して二重実行を防止した
### 確認
- lsp_diagnostics で SysExecMiscViewModel.cs / MenuData.cs に診断なしを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-16] 13:43 ConcvertSelectedView の一覧とログを横並びに変更
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：Views/00System/ConcvertSelectedView.xaml の「変換プログラム一覧」と「ストリーミングログ」を縦ではなく横に均等に並べる
### 実施内容
- CvWpfclient/Views/00System/ConcvertSelectedView.xaml: 内部 Grid を RowDefinitions Auto/*、ColumnDefinitions */* に変更し、実行設定 Card を 0 行目に跨がせ、変換プログラム一覧を 1 行 0 列、ストリーミングログを 1 行 1 列に配置して幅を均等にした
### 技術決定 Why
- 上下分割では表示領域が狭くなるため、実行設定を上に固定し、残り領域を左右で半分ずつ使う Grid レイアウトに変更した
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-16] 13:22 旧DBからの選択変換処理画面の追加
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：「管理メニュー」に「旧DBからの選択変換処理」を追加し、変換プログラムをチェックボックス付き一覧で選択して実行できる画面を新規作成する
### 実施内容
- CvWpfclient/Models/MenuData.cs: 「旧DBからの変換処理」の次に「旧DBからの選択変換処理」を追加
- CvWpfclient/ViewModels/00System/ConcvertSelectedViewModel.cs: QueryMsgAsync(CvFlag.Msg043_ConvertList) で変換プログラム一覧を取得し、DataGrid のチェックボックスで選択した項目を QueryMsgStreamAsync(CvFlag.Msg044_ConvertSelected / Msg045_ConvertSelectedInit) で実行する ViewModel を追加
- CvWpfclient/Views/00System/ConcvertSelectedView.xaml: ConcvertDbView を参考に ColorZone ヘッダー、実行設定カード、チェックボックス付き DataGrid、ストリーミングログ GroupBox、実行/キャンセルボタンを配置
- CvWpfclient/Views/00System/ConcvertSelectedView.xaml.cs: BaseWindow を継承する code-behind を追加
### 技術決定 Why
- 既存の ConcvertDbViewModel のストリーミング進捗表示パターンを踏襲し、選択実行版として QueryMsgAsync と QueryMsgStreamAsync を組み合わせた
- チェックボックス選択は SelectMultiWinView と同じく DataGridTemplateColumn + ObservableObject の IsSelected プロパティで実装し、全選択/全解除コマンドを追加した
- テーブル初期化の有無は bool プロパティを RadioButton で切り替え、App.xaml の InverseBooleanConverter を再利用した
### 確認
- 編集ファイルの CRLF を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-16] 12:52 旧DBからの変換処理画面の追加
### Agent
- kimik-k2.7-code : OpenAI : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient に「旧DBからの変換処理」画面を追加し、管理メニューから起動できるようにする
### 実施内容
- CvWpfclient/Models/MenuData.cs: 「管理メニュー / テスト画面」の「汎用マスタメンテ」の次に「旧DBからの変換処理」を追加
- CvWpfclient/ViewModels/00System/ConcvertDbViewModel.cs: テーブル初期化の有無、進捗、ストリーミングログを持ち、QueryMsgStreamAsync で CvFlag.Msg040_ConvertDb / Msg041_ConvertDbInit を呼び出す ViewModel を追加
- CvWpfclient/Views/00System/ConcvertDbView.xaml: SysSetConfigView を参考にした ColorZone ヘッダー、実行設定カード、ストリーミングログ GroupBox、実行/キャンセルボタンを配置
- CvWpfclient/Views/00System/ConcvertDbView.xaml.cs: BaseWindow を継承する code-behind を追加
### 技術決定 Why
- 既存の SampleViewModel の QueryMsgStreamAsync パターンを踏襲し、CommunityToolkit.Mvvm の [RelayCommand(IncludeCancelCommand = true)] で非同期実行とキャンセルを実装した
- テーブル初期化の選択は bool プロパティを RadioButton で切り替え、App.xaml の InverseBooleanConverter を再利用した
- ストリーミングログは ListBox に ObservableCollection<string> をバインドし、新しいメッセージを先頭に表示するよう Insert(0, ...) で更新する SampleViewModel と同じ方式を採用した
### 確認
- lsp_diagnostics で ConcvertDbViewModel.cs / ConcvertDbView.xaml.cs に診断なしを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-16] 11:51 ConvertDb 選択変換メソッド追加
### Agent
- kimi-k2.7-code : OpenAI : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvDomainLogic/ConvertDb.cs の 33 行目にあった steps 定義を外出しし、ConvertAllAsyncStream とは別に ConvertSelectAsyncStream(string[] selectedTask, bool isInit = true) を作成して、指定タスク名のみを定義順に実行できるようにする
### 実施内容
- CvDomainLogic/ConvertDb.cs: steps 配列をクラスレベルの静的フィールド _stepDefinitions へ外出し、BuildSteps ヘルパーでタスク名から実行用ステップを生成、ConvertSelectAsyncStream を新規追加
### 技術決定 Why
- 全実行と選択実行で同じタスク定義を共有するため、Func<ConvertDb, bool, int> 型の静的配列として定義を一元化し、実行時に this を束ねた Func<bool, int> へ変換して StreamStepProgressRunner に渡した
### 影響範囲
- CvDomainLogic/ConvertDb.cs のみ
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvDomainLogic/CvDomainLogic.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- lsp_diagnostics で ConvertDb.cs に診断なしを確認

---

## [2026-06-16] 10:43 MainMenu月齢アイコンの画像近似表示修正
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：MainMenuView.xaml の月表示イメージを添付画像の 1-28 日表示に近づけ、29日と30日は28日と同じ表示にする
### 実施内容
- CvWpfclient/Views/MainMenuView.xaml: 月アイコンを黄色円と青い遮蔽円の重ね合わせに整理し、黒表示と不透明度 Binding を廃止して添付画像に近い黄/青表示へ変更
- CvWpfclient/ViewModels/MainMenuViewModel.cs: 月相表示用日付を 1-28 日に丸め、29日と30日を28日相当として扱うように変更
### 技術決定 Why
- 既存の同サイズ円をずらす簡易方式を維持することで、複雑な Path 生成を追加せずに三日月の曲線形状と上弦/下弦の左右方向を表現した
- 添付画像は 1-28 日を4行に分け、29日と30日は28日と同じ扱いのため、旧暦日付の取得元は維持しつつ表示用日付だけを丸めた
### 確認
- MainMenuView.xaml の XML 読み込み成功
- 編集ファイルの UTF-8 BOM なし、CRLF のみを確認
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-16] 09:10 MainMenu月齢アイコンの三日月表示修正
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：MainMenuView.xaml 89行目の月の表示で三日月部分を黄色で塗りつぶす
### 実施内容
- CvWpfclient/Views/MainMenuView.xaml: 月アイコンの黄色表示を矩形クリップから黄色円と黒い遮蔽円の重ね合わせへ変更し、三日月部分が黄色の曲線形状で残るよう修正
- CvWpfclient/ViewModels/MainMenuViewModel.cs: 旧クリップ矩形の Binding を月の暗部移動量 `MoonShadowOffset` に置き換え、満ち欠け方向に応じて遮蔽円を左右へ移動するよう修正
### 技術決定 Why
- 矩形クリップでは三日月の内側境界が直線になるため、同サイズの円をずらして重ねることで内側も曲線の三日月形状にした
### 確認
- MainMenuView.xaml の XML 読み込み成功
- 編集ファイルの UTF-8 BOM なし、CRLF のみを確認
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-15] 12:10 LoginRefreshAsync でもクライアント情報を履歴に送信
### Agent
- kimi-k2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient で LoginRefreshAsync を呼び出したときにも LoginAsync と同様にクライアント情報を履歴ファイルに保存する
### 実施内容
- CvWpfclient/ViewModels/00System/LoginViewModel.cs: Refresh コマンドで生成する LoginRefresh に `Info = Common.SerializeObject(SubGetInfo())` を追加し、LoginAsync と同じクライアント情報（IP/MAC/マシン名/ユーザー/OS）をサーバーの SysHistJwt 履歴へ送信する
### 技術決定 Why
- LoginRefresh データコントラクトには Info プロパティが既に存在するが、クライアント側で未設定のためサーバーが空の Jsub で履歴を記録していた。LoginAsync と同じ SubGetInfo() を流用し、履歴情報の欠損を防いだ
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 errors、既存の unrelated warnings 2 件）を確認

---

## [2026-06-15] 11:51 社員証カード印刷の範囲指定条件変更
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：PrintMasterShainCardView の範囲指定を、社員Id from-to、社員Code from-to、店舗Id複数選択に変更し、ViewModel側も合わせて修正する
### 実施内容
- CvWpfclient/Views/01Master/PrintMasterShainCardView.xaml: 範囲指定UIを「Id」「社員Code」「店舗Id」へ変更し、店舗Idは複数選択ボタンと解除ボタン、選択内容表示へ変更
- CvWpfclient/ViewModels/01Master/PrintMasterShainCardViewModel.cs: 社員Id from-to 条件、社員Code from-to 条件、店舗Id IN 条件を生成するよう修正し、店舗Id複数選択コマンドを追加
### 技術決定 Why
- 店舗条件は店舗Code範囲ではなく MasterShain.Id_Tenpo の複数Id指定として扱うため、既存の ShowMultiSelectDialog と AddSelectedIdInClause を使い、SQL条件を `A.Id_Tenpo IN (...)` に統一した
### 確認
- PrintMasterShainCardView.xaml の XML 読み込み成功
- 編集ファイルの UTF-8 BOM なし、CRLF のみを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-15] 11:20 環境設定画面のclientsettings保存項目追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：SysSetConfigViewModel で WeatherRegion / FitPosition / Limit を clientsettings.json に保存できるようにし、FitPosition は左右上下の組み合わせ選択、Limit は数値右詰め、WeatherRegion はテキスト入力にする。環境設定画面全体も改善する
### 実施内容
- CvWpfclient/AppGlobal.cs: WeatherRegion / FitPosition / Limit を実行中設定へ反映できるよう UpdateConfigValues を拡張
- CvWpfclient/Services/SystemSettingsStore.cs: clientsettings.json へ数値型 Limit を保持したまま保存できる SaveConfigurationValues を追加
- CvWpfclient/ViewModels/00System/SysSetConfigViewModel.cs: WeatherRegion / FitPosition / Limit の読込、検証、保存、一時反映を追加
