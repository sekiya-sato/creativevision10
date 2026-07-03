## [2026-07-03] 11:16 MasterYosanHanbaiMenteView 作成
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：販売員別予算マスタ(月)のメニュー名へ変更し、MasterYosanBrandMenteView を参考に MasterYosanHanbaiMenteView の View.xaml / View.xaml.cs / ViewModel.cs を作成する
### 実施内容
- CvWpfclient/Models/MenuData.cs: 販売員別予算マスタを「販売員別予算マスタ(月)」へ変更し、直後に「販売員予算マスタメンテ」を登録
- CvWpfclient/Views/02Yosan/MasterYosanHanbaiMenteView.xaml: MasterYosanHanbai の一覧と編集フォーム、販売員Id選択、追加・修正・削除・一覧取得ボタンを持つ画面を追加
- CvWpfclient/Views/02Yosan/MasterYosanHanbaiMenteView.xaml.cs: BaseWindow 継承の初期化コードを追加
- CvWpfclient/ViewModels/02Yosan/MasterYosanHanbaiMenteViewModel.cs: BaseMenteViewModel<MasterYosanHanbai> による一覧取得、登録、修正、削除、MasterShain 選択、入力検証を実装
### 技術決定 Why
- MasterYosanHanbai は VShain 表示列を持たないため、DBモデルは変更せず、3ファイル内で完結する販売員Id選択と補助表示にした
- 既存の MasterYosanBrandMenteView と同じ BaseMenteViewModel フローを踏襲し、直接編集画面の操作差分を最小化した
### 確認
- MasterYosanHanbaiMenteView.xaml のXML構文チェック成功
- 対象編集ファイルの CRLF 確認済み
- `git diff --check` エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` 成功：警告0、エラー0

---

## [2026-07-03] 09:52 ShopUriageInputViewModelの明細行No連番修正
### Agent
- GPT-5 : OpenAI : Sisyphus
### Editor
- OpenCode / VS2026
### 目的
- ユーザーからの要望：CvWpfclient.Views._06Uriage.ShopUriageInputView の伝票入力画面の明細で、行No が Jmeisai のJSON配列データのNo を正しくセットし、行追加・行削除時も正しい連番Noがセットされるように修正する
### 実施内容
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: `DeleteMeisai()` メソッドで行削除後に `RenumberMeisaiNo()` を呼び出すように修正
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: `RenumberMeisaiNo()` メソッドを新規追加し、EditMeisai の要素に対して 1 から連番で No を振り直す処理を実装
### 技術決定 Why
- 伝票明細の行Noは1からの連番であるべきだが、行削除後にNoを振り直していなかったため連番に抜けが生じる可能性があった
- `AddMeisai()` と `ApplyBarcodeMeisai()` は既に `Max(m => m.No) + 1` で計算しているため連番維持に問題なし
- `ApplyMeisaiFromCurrentEdit()` はDBからのJSONデータのNoをそのまま使用するため影響なし
- 最小限の修正として `DeleteMeisai()` 内でのみ `No` 振り直しを実施
### 確認
- `/mnt/c/Windows/System32/cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` が成功（0 警告 0 エラー）

---

## [2026-06-30] 08:47 RebuildTranAllの売上明細色サイズId再構築
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：CvDomainLogic/RebuildDb.cs の RebuildTranAll() で、まず Tran00Uriage の Jmeisai について Id_Col / Id_Siz が 0 の明細を Cd_Col / Cd_Siz 相当コードから MasterMeisho の Id に更新する
### 実施内容
- CvDomainLogic/RebuildDb.cs: Tran00Uriage.Jmeisai の Id_Col 更新 SQL と Id_Siz 更新 SQL を分割して追加
- CvDomainLogic/RebuildDb.cs: 更新 SQL ごとの `SELECT changes()` 取得と SQL エラー時のトランザクション中断処理を追加
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- 色は `MasterMeisho.Kubun='COL'` と色コードで直接解決し、サイズは `MasterShohin.SizeKu` をサイズ区分として `MasterMeisho` を引く必要があるため、更新 SQL を Id_Col 用と Id_Siz 用に分離した
- 既存 JSON 形式の `Code_Col` / `Code_Siz` を主キーにしつつ、ユーザー指定の Cd 系名称にも対応できるよう `Cd_Col` / `Cd_Siz` を fallback として扱った
### 確認
- `git diff --check` で空白エラーなし（Git の CRLF 変換警告のみ）
- `CvDomainLogic/RebuildDb.cs` の実ファイル改行が CRLF で統一済み
- ソース内の2本の UPDATE SQL を in-memory SQLite で実行し、Id_Col / Id_Siz の最小更新を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvDomainLogic/CvDomainLogic.csproj"` が成功（0 警告 0 エラー）
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx"` が成功（0 警告 0 エラー）

---

## [2026-06-29] 17:35 SysGeneralMenteViewの一覧取得ボタン左寄せ
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：CvWpfclient.Views._00System.SysGeneralMenteView のみ、「一覧取得」ボタンの場所を左に寄せる
### 実施内容
- CvWpfclient/Views/00System/SysGeneralMenteView.xaml: ヘッダー右側の操作群にあった「一覧取得」ボタンを、タイトル直後の左側操作群へ移動
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- 他のメンテ画面と同じく、タイトル横に区切り線を置いて「一覧取得」を配置し、保存・削除・追加とは左右で役割を分けた
### 確認
- `CvWpfclient/Views/00System/SysGeneralMenteView.xaml` の XML parse OK
- `git diff --check` で問題なし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` が成功（0 警告 0 エラー）

---

## [2026-06-29] 17:28 SysGeneralMenteView の一覧取得条件再指定対応
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：CvWpfclient.Views._00System.SysGeneralMenteView でデータを Id desc で取得し、table選択後の画面で「一覧取得」ボタンから ID の from-to と件数のみ再指定できるようにする
### 実施内容
- CvWpfclient/ViewModels/00System/SysGeneralMenteViewModel.cs: 一覧取得を `Id DESC` に変更し、ID範囲と件数条件を保持して `QueryListParam` に反映する処理を追加
- CvWpfclient/Views/00System/SysGeneralMenteView.xaml: 既存の再読込操作を「一覧取得」ボタンへ変更し、F5 から同じ条件再指定処理を呼び出すよう修正
- CvWpfclient/ViewModels/Sub/SelectParameter.cs: RangeParamMiniView で名前欄を任意表示にするための `IsNameVisible` を追加
- CvWpfclient/Views/Sub/RangeParamMiniView.xaml: `IsNameVisible` が false の場合は名前欄を非表示にし、汎用メンテでは ID 範囲と件数のみ指定できるよう修正
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- table選択直後の初期表示は従来通り自動取得し、再取得時だけ条件ダイアログを開くことで既存の画面遷移を維持した
- 既存の `RangeParamMiniView` を名前欄の表示制御付きで再利用し、ID from-to と件数だけを受け付ける UI に限定した
### 確認
- `CvWpfclient/Views/00System/SysGeneralMenteView.xaml` と `CvWpfclient/Views/Sub/RangeParamMiniView.xaml` の XML parse OK
- `git diff --check` で問題なし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` が成功（0 警告 0 エラー）

---

## [2026-06-29] 14:44 SysExecMiscViewの商品名称再構築ボタン対応
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：CvWpfclient.Views._00System.SysExecMiscView の Testケース02 ボタンを変更し、サーバI/F の CvFlag.Msg046_MasterShohinMeishoRebuild を呼び出して実行するようにする
### 実施内容
- CvWpfclient/ViewModels/00System/SysExecMiscViewModel.cs: Test02処理を商品名称マスタ再構築処理へ変更し、CvFlag.Msg046_MasterShohinMeishoRebuild を ICoreService.QueryMsgAsync で呼び出すよう修正
- CvWpfclient/Views/00System/SysExecMiscView.xaml: Testケース02 ボタンの表示とコマンドバインディングを商品名称再構築用に変更
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- サーバ側には Msg046_MasterShohinMeishoRebuild のハンドラが既に存在するため、CvWpfclient側のみ既存の ICoreService 呼び出しパターンを流用し、サーバ処理には変更を加えない
- 実行結果欄には開始・成功・失敗を明示し、サーバ応答が負の Code を返した場合は Option/DataMsg を利用してエラー内容を表示する
### 確認
- `CvWpfclient/Views/00System/SysExecMiscView.xaml` の XML parse OK
- `git diff --check` で問題なし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` が成功（0 警告 0 エラー）

---

## [2026-06-29] 12:15 DefineDataTable の IDerivedClass 対応テーブル作成処理を修正
### Agent
- Kimi K2.7-code : OhMyOpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvBase/DefineDataTable.cs のテーブル作成ループで、IDerivedClass を実装する型は CreateDerivedTable() を使用し、実行前にテーブル存在を確認して不在時のみ作成する。force 時は Drop 後に再作成する。
### 実施内容
- CvBase/DefineDataTable.cs: テーブル作成ループ内で `typeof(IDerivedClass).IsAssignableFrom(tableType)` を判定し、派生テーブルは `CreateDerivedTable` を使用するよう変更
- CvBase/DefineDataTable.cs: `EnsureDerivedTable` ヘルパーを追加し、force 時は Drop 後に再作成、非 force 時はテーブル不在時のみ作成・データ投入を実行
- CvBase/DefineDataTable.cs: ループ外に残っていた `DerivedShohinColSiz` の個別 `CreateDerivedTable` 呼び出しを削除
### 技術決定 Why
- `ExDatabase.CreateDerivedTable<T>()` は `isForce=true` の場合のみ実処理（Drop → Create → データ投入）を行うため、テーブル不在時も `true` を渡して実際の作成とデータ投入を行うようにした
- 存在確認は `ExDatabase.IsExistTable(Type)` で行い、force 時以外はテーブルが既存の場合に冗長な処理をスキップする
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvServer/CvServer.csproj"` が成功（0 警告 0 エラー）

---

## [2026-06-28] 16:38 README/setup 文面校正と Markdown 整理
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：`readme.md` と `setup.md` の誤字脱字、不自然な言い回し、Markdown 表現を見直して読みやすく整える
### 実施内容
- readme.md: プロジェクト説明の日本語表現と表記ゆれを修正し、見出し・箇条書き・リンク文言を整理
- setup.md: 手順書を見出し、番号付き手順、箇条書き、コードブロック中心の Markdown に再構成
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- 内容自体は変えず、公開ドキュメントとして初見でも追いやすい構造を優先し、インデント列挙を標準的な Markdown 構造へ置き換えた
### 確認
- `readme.md` / `setup.md` の CRLF 正規化を実施
- `git diff --check -- readme.md setup.md Doc/aicoding_log.md` で問題なしを確認

---

## [2026-06-27] 14:44 MainMenuステータス表示領域の高さ制限
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：MainMenuView のサーバステータス、クライアントステータスを含むエリアの高さを初期表示程度に制限し、全文はToolTipで表示する
### 実施内容
- CvWpfclient/Views/MainMenuView.xaml: Server Status / Client Status の上段Gridと各カードに MaxHeight と ClipToBounds を設定し、ステータス本文にToolTipを追加
- CvWpfclient/Models/MenuData.cs: WPFビルドを阻害していた旧DB変換メニューの View 型参照名を実在する ConvertDbView / ConvertSelectedView に修正
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- ステータス文字列が折り返しで縦に伸びても上段レイアウトを押し下げないよう、表示領域をクリップし、全文確認は同一BindingのToolTipへ委ねた
- 検証ビルドで発見した Concvert 系の型参照は実在クラス名と不一致だったため、参照名のみを修正してビルド可能な状態に戻した
### 確認
- `CvWpfclient/Views/MainMenuView.xaml` の XML パース成功
- 編集ファイルの CRLF 確認成功
- `git diff --check` 成功
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` が成功（0 警告 0 エラー）

---

## [2026-06-27] 08:41 MainMenu天気パネルの気象庁ページ導線追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：MainMenuView の天気パネルで、気象庁概要予報の表示元をクリックして既定ブラウザで開けるようにする
### 実施内容
- CvWpfclient/ViewModels/MainMenuViewModel.cs: JmaWeatherAreaCode から気象庁予報ページURLを組み立て、ClientLib.OpenUrlAsync で開く OpenJmaWeatherOverviewSourceCommand を追加
- CvWpfclient/Views/MainMenuView.xaml: 天気カード内に気象庁ページを開くアイコン付きボタンを追加
### 技術決定 Why
- 概要予報のJSON取得URLではなく、JmaWeatherAreaCode に対応する気象庁の利用者向け予報ページを開くことで、表示元確認の導線として自然に扱えるようにした
### 確認
- `CvWpfclient/Views/MainMenuView.xaml` の XML パース成功
- 編集ファイルの CRLF 確認成功
- `git diff --check` 成功
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` が成功（0 警告 0 エラー）

---

## [2026-06-26] 11:29 MainMenu天気パネルの高さ可変化
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：MainMenuView.xaml で最大化などにより Height が変化したとき、StackPanel x:Name="WeatherPanel" のエリアを自然に拡大する
### 実施内容
- CvWpfclient/Views/MainMenuView.xaml: 右側メインGridの行配分を見直し、選択中メニュー行とWeatherPanel行へ高さ増加分を分配するよう変更
- CvWpfclient/Views/MainMenuView.xaml: WeatherPanel を StackPanel から Grid に変更し、天気カードと気温推移チャートが行高に追従して縦方向へ伸びるよう変更
- Doc/aicoding_log.md: 800行超過見込みのため既存ログを Doc/aicoding_log_008.md へアーカイブし、今回作業ログを新規作成
### 技術決定 Why
- 最大化時の余り高さが選択中メニュー行だけに入っていたため、WeatherPanel行を Auto から star 行へ変更し、チャートの固定 Height を MinHeight に置き換えて、通常サイズの最低高を保ちながら拡大時だけ伸びる構造にした
### 確認
- `CvWpfclient/Views/MainMenuView.xaml` の XML パース成功
- 編集ファイルの CRLF 確認成功
- `git diff --check` 成功
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` が成功（0 警告 0 エラー）

---
## [2026-07-02] 12:03 店舗売上入力の明細P/S選択追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：`CvWpfclient.Views._06Uriage.ShopUriageInputView` の伝票明細に仮行NoとP/S選択を追加し、明細 `Kubun` に P=0 / S=1 を保存する
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 明細グリッド先頭列に仮行Noを表示し、2段目の RowDetails に Pプロパー / Sセールの選択 ComboBox を追加
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: 明細用P/S選択肢を追加し、店舗売上の明細 `Kubun` を伝票ヘッダ区分で上書きせず P=0 / S=1 に正規化する処理へ変更
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- `Tran99Meisai.Kubun` を明細ごとの P/S 区分として扱うため、保存時のヘッダ区分上書きをやめ、既存データの 11/21 は S、その他は P として表示できるよう正規化した
### 確認
- `ShopUriageInputView.xaml` の XML parse OK
- `git diff --check` で問題なし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` が成功（警告0、エラー0）

---
## [2026-07-02] 12:23 店舗売上入力の明細P/S表示調整
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：`CvWpfclient.Views._06Uriage.ShopUriageInputView` の明細行P/S選択表示が空欄になるため、人間の修正を含めて調整する
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 人間修正の `行No` 表記を維持しつつ、P/S選択列幅を拡張し、ComboBox の高さ・フォントを調整
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: RowDetails 内の P/S ComboBox の `ItemsSource` を `Window` 祖先参照から `DataGrid.DataContext` 参照へ変更
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- RowDetails 内では `Window` 祖先参照より、同じ DataGrid 配下の `DataContext.MeisaiKubunOptions` を参照する方が安定し、列幅不足による選択値の非表示も避けられるため
### 確認
- `ShopUriageInputView.xaml` の XML parse OK
- `git diff --check` で問題なし
- `ShopUriageInputView.xaml` が UTF-8 BOM + CRLF、LF-only/CR-only なしであることを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` が成功（警告0、エラー0）

---
## [2026-07-02] 12:27 店舗売上入力の明細P/Sセル余白最小化
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：`CvWpfclient.Views._06Uriage.ShopUriageInputView` の明細P/S選択セルのサイズと余白を再調整する
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: P/S選択用の明細専用 `MeisaiKubunComboBox` / `MeisaiKubunComboBoxItem` スタイルを追加し、Height/MinWidth/FontSize/Padding/DropDown高さを小さく設定
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: P/S列幅を 96 に戻し、RowDetails の MinHeight と Margin、セル内 Margin を最小寄りに調整
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- `MaterialDesignOutlinedComboBox` は明細2段目の小セルでは枠と項目高さが大きく、行高に対して過剰な余白が出るため、P/Sセル専用の軽量な ComboBox スタイルで局所的に詰めた
### 確認
- `ShopUriageInputView.xaml` の XML parse OK
- `git diff --check` で問題なし
- `ShopUriageInputView.xaml` が UTF-8 BOM + CRLF、LF-only/CR-only なしであることを確認
- 通常出力先の `CvWpfclient` ビルドは起動中の `CreativeVision10 (13424)` と Visual Studio のDLLロックで失敗
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj -o .omo/build/CvWpfclient"` が成功（警告0、エラー0）

---
## [2026-07-02] 12:31 店舗売上入力の明細列幅圧縮
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：`CvWpfclient.Views._06Uriage.ShopUriageInputView` の明細行を右端まで表示できるよう列幅を調整する
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 明細の行No、商品Id、商品名、色Id、サイズId、数量、単価、上代単価、下代単価、明細担当Idの列幅を圧縮
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 終端スペーサ列を 0 にして、明細担当Id列まで表示領域に収まりやすくした
- Doc/aicoding_log.md: 今回作業ログを先頭に追記
### 技術決定 Why
- 明細右端の明細担当Id検索ボタンまで表示するため、数量列を 54px に狭め、検索ボタン付き列はボタン幅を残せる最小寄りの幅にした
### 確認
- `ShopUriageInputView.xaml` の XML parse OK
- `git diff --check` で問題なし
- `ShopUriageInputView.xaml` が UTF-8 BOM + CRLF、LF-only/CR-only なしであることを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj -o .omo/build/CvWpfclient"` が成功（警告0、エラー0）

---
## [2026-07-03] 11:06 SalesStaffBudgetMasterView 作成
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：ShopBrandBudgetMasterView をもとに、販売員別予算マスタ画面とViewModelを作成し、MasterYosanHanbai と MasterShain 参照を使用する
### 実施内容
- CvWpfclient/Views/02Yosan/SalesStaffBudgetMasterView.xaml: 販売員選択、年月、月予算、休日、日別予算グリッド、作成・読込・登録・削除・自動配分・再計算ボタンを持つ画面を作成
- CvWpfclient/ViewModels/02Yosan/SalesStaffBudgetMasterViewModel.cs: MasterYosanHanbai の月次読込・削除・一括登録、MasterShain 選択、日別配分・累計再計算処理を実装
- CvWpfclient/Models/MenuData.cs: 販売員別予算マスタの addInfo を準備中から実装内容の説明へ更新
### 技術決定 Why
- 既存の ShopBrandBudgetMasterView と同じ月次一括配分操作を維持し、Id_Tenpo + Id_Brand 条件だけを Id_Shain 条件に置換することで画面操作と保存仕様の差分を最小化した
- MasterYosanHanbai には表示用の VShain computed 列がないため、社員名表示は画面の MasterShain 選択結果で保持した
- 元の ShopBrandBudgetMasterView のボタンは各コマンドに対応しているため、不要ボタンとしての削除は行わなかった
### 確認
- SalesStaffBudgetMasterView.xaml のXML構文チェック成功
- 対象編集ファイルの CRLF 確認済み
- `git diff --check` エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` 成功：警告0、エラー0

---
