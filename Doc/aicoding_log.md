## [2026-06-23] 15:06 MasterShohin サブリスト追加削除後の選択行調整
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：MasterShohinMenteView の「色サイズ」「原価」「品質」「名称」など、行の「削除」「追加」ボタンがあるものについて、追加後は追加行、削除後は最終行へカレント行を移動する
### 実施内容
- CvWpfclient/ViewModels/01Master/MasterShohinMenteViewModel.cs: 色サイズ、原価、品質、名称の各サブリスト追加後に追加行を選択し、削除後に残行の最終行を選択するよう修正
### 技術決定 Why
- 各サブリストは ViewModel 側の ObservableCollection と Selected プロパティで DataGrid 選択を制御しているため、行操作直後に該当 Selected プロパティへ明示的に反映した
### 確認
- `git diff --check` で空白エラーなし
- 編集ファイルの CRLF 維持を確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-23] 13:59 MasterYosanBrand 一覧SQLのJSON表示列修正
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex / VS2026
### 目的
- ユーザーからの要望：MasterYosanBrandMenteViewModel の CreateListMessage のSQL文をJSON関数を使い正しいものに変更し、VTenpo と VBrand をセットする
### 実施内容
- CvWpfclient/ViewModels/02Yosan/MasterYosanBrandMenteViewModel.cs: 一覧SQLで店舗・ブランドの表示情報を `json_object` により VTenpo/VBrand として返すよう修正
- CvWpfclient/Views/02Yosan/MasterYosanBrandMenteView.xaml: 一覧表示と詳細表示のバインドを VTenpo/VBrand のプロパティ参照へ修正。人間側の確認修正として入力欄サイズも調整
- CvBase/BaseDbYosan.cs: 人間側の確認修正として MasterYosanBrand の VTenpo/VBrand を ComputedColumn に変更し、SQL取得値を表示用に受け取れるよう調整
### 技術決定 Why
- JOINしたコード・名称を一時的な別名列ではなく MasterYosanBrand の VTenpo/VBrand にJSONとして復元することで、詳細フォームと一覧で同じ CodeNameView 表示プロパティを使えるようにした
### 確認
- `CvWpfclient/Views/02Yosan/MasterYosanBrandMenteView.xaml` の XML 構文チェック成功
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-23] 13:28 MasterYosanBrand メンテ画面の一覧条件と名称表示追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：MasterYosanBrand メンテ画面の一覧選択で店舗Id・ブランドIdを選択可能にし、表示で店舗IdにID/店舗CD/店舗名、ブランドIdにID/ブランドCD/ブランド名を表示する
### 実施内容
- CvWpfclient/ViewModels/02Yosan/MasterYosanBrandMenteViewModel.cs: 一覧取得前に条件ダイアログを表示し、店舗IdとブランドIdの複数選択条件を WHERE に反映する処理を追加。SQL取得時に店舗CD/店舗名/ブランドCD/ブランド名を JOIN して取得
- CvWpfclient/Views/02Yosan/MasterYosanBrandMenteView.xaml: 一覧列と詳細フォームに店舗CD/店舗名/ブランドCD/ブランド名の表示を追加
- CvBase/BaseDbYosan.cs: MasterYosanBrand に表示専用 ResultColumn プロパティを追加
- CvWpfclient/ViewModels/Sub/SelectParameter.cs, CvWpfclient/ViewModels/Sub/RangeParamViewModel.cs, CvWpfclient/Views/Sub/RangeParamView.xaml, CvWpfclient/Helpers/ViewModels/BaseMenteViewModel.cs: 汎用一覧条件ダイアログへ任意表示の取引先複数選択行を追加
### 技術決定 Why
- 既存の RangeParamView を拡張して通常画面では非表示の店舗選択行を追加し、今回の画面だけ店舗IdとブランドIdを同時に絞り込めるようにした。表示名は保存データに混ぜず ResultColumn と JOIN で一覧取得時に補完する構成にした
### 確認
- `CvWpfclient/Views/02Yosan/MasterYosanBrandMenteView.xaml` と `CvWpfclient/Views/Sub/RangeParamView.xaml` の XML 構文チェック成功
- 編集ファイルの CRLF 維持を確認
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-23] 13:15 MasterYosanBrand メンテ画面追加
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：MasterYosanBrand のメンテ画面を作成し、MenuData の「予算」カテゴリで「店ブランド予算マスタ」の下に追加する
### 実施内容
- CvWpfclient/ViewModels/02Yosan/MasterYosanBrandMenteViewModel.cs: MasterYosanBrand を BaseMenteViewModel で一覧・追加・修正・削除できる ViewModel を追加し、日付・店舗Id・ブランドId・予算金額の検証と店舗/ブランド選択コマンドを実装
- CvWpfclient/Views/02Yosan/MasterYosanBrandMenteView.xaml: 既存マスタメンテ画面と同じ ColorZone/Card/DataGrid 構成で MasterYosanBrand 直接編集画面を追加
- CvWpfclient/Views/02Yosan/MasterYosanBrandMenteView.xaml.cs: 新規 View の初期化コードを追加
- CvWpfclient/Models/MenuData.cs: 「■ 予算」の「店ブランド予算マスタ」直下に「店ブランド予算マスタメンテ」を追加
### 技術決定 Why
- 既存の一括予算画面は月次配分用の業務画面のため、MasterYosanBrand の直接メンテは BaseMenteViewModel の既存 CRUD 経路を再利用し、複合キーに必要な入力検証だけを個別実装した
### 確認
- 新規 XAML の XML 構文チェック成功
- 編集ファイルの CRLF 維持を確認
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-23] 12:05 RangeInputParamView に商品Id複数選択と入力バーコードを追加
### Agent
- Kimi K2.7-code : OpenCode : Sisyphus
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvWpfclient.Views.Sub.RangeInputParamView を変更し、商品名の上に商品Idの複数選択と「入力バーコード」の2項目を追加する
### 実施内容
- CvWpfclient/ViewModels/Sub/SelectInputParameter.cs: `ShohinIds`/`ShohinIdsText`/`InputBarcode` プロパティを追加
- CvWpfclient/Views/Sub/RangeInputParamView.xaml: 商品名行の上に「商品Id」複数選択行と「入力バーコード」入力行を追加（Window高さも 560→720 に拡張）
- CvWpfclient/Views/Sub/RangeInputParamView.xaml: 「入力バーコード」「商品名」のテキストボックス幅を約半分に縮小（Width=400、左寄せ）
- CvWpfclient/ViewModels/Sub/RangeInputParamViewModel.cs: `DoSelectShohinIdsCommand`/`ClearShohinIdsCommand` を追加し、MasterShohin からの複数選択結果をテキスト表示へ反映
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: `ListWhere` に `EXISTS(json_each(Jmeisai) AS b WHERE json_extract(b.value,'$.Id_Shohin') IN (...))` と `json_extract(b.value,'$.JanCode') = '...'` の条件を追加
### 技術決定 Why
- 既存の店舗Id/倉庫Id複数選択と同じ UI パターン（選択ボタン＋解除ボタン＋選択結果テキスト）を流用し、ユーザーが指定した JSON_EXTRACT 比較式をそのまま WHERE 条件に組み込んだ
### 確認
- 編集ファイルの CRLF 維持を確認
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-23] 10:41 店舗売上入力のタブ内見出しと遷移ボタン配置
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：一覧画面/伝票入力画面のタブTitleを空にし、タブ内上部行の左側へアイコン付き見出しと伝票詳細/一覧に戻るボタンを配置する
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 一覧画面タブと伝票入力タブの Header を空文字に変更
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 一覧画面タブ内の上部行へアイコン付き「一覧画面」表示と「伝票詳細」ボタンを追加
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 伝票入力タブ内の上部行を左右構成に変更し、左側へアイコン付き伝票番号表示と「一覧に戻る」ボタン、右側へ修正/追加/削除ボタンを配置
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: 一覧画面タブへ戻す GoToListCommand を追加
- Doc/aicoding_log.md: 作業ログを追記
### 技術決定 Why
- タブヘッダーを空にすると操作対象が分かりにくくなるため、タブ内の操作行にアイコン付き見出しと遷移ボタンを置き、既存の一覧ダブルクリックと同じ GoToDetailCommand 経路で伝票詳細へ移動する構成にした
### 確認
- 対象 XAML の XML 構文チェック成功
- 編集ファイルの CRLF 維持を確認
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-23] 10:33 店舗売上入力のESCタブ戻り対応
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：06Uriage/ShopUriageInputView の伝票入力画面で ESC キーを押したときは、一覧画面タブ表示にする
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml.cs: OnPreviewKeyDown を override し、伝票入力タブ表示中の ESC で SelectedTabIndex を 0 に戻す処理を追加
- Doc/aicoding_log.md: 作業ログを追記
### 技術決定 Why
- BaseWindow の既定 ESC は ExitCommand 実行による終了動作のため、ShopUriageInputView だけで先に ESC を処理し、一覧タブ表示中は従来の BaseWindow 処理へ委譲する構成にした
### 確認
- 編集ファイルの CRLF 維持を確認
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-23] 10:28 店舗売上入力タブ内ボタン配置と明細2行列幅同期
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：06Uriage/ShopUriageInputView で一覧取得ボタンを一覧画面タブへ、修正/削除/追加ボタンを伝票入力タブへ移動し、表示タブ時のみキーボード実行可能にする。明細DataGridの2行レイアウトで単価/上代単価/下代単価と各合計列の見た目を同じ列並びに整える
### 実施内容
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: ヘッダーツールバーから一覧取得/修正/削除/追加ボタンを外し、一覧取得を一覧画面タブのカードヘッダー、修正/削除/追加を伝票入力タブ内へ配置
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: F2/F3/F4/F5 の KeyBinding をタブ表示状態を判定する専用コマンドへ変更
- CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml: 明細DataGridの上段列へ名前を付け、RowDetailsTemplate の列幅を上段列の ActualWidth に追随させる構成へ変更
- CvWpfclient/ViewModels/06Uriage/ShopUriageInputViewModel.cs: 選択タブ別の一覧取得/修正/削除/追加ラッパーコマンドを追加し、SelectedTabIndex 変更時に CanExecute を更新
- CvWpfclient/Helpers/Converters/DoubleToGridLengthConverter.cs: DataGrid列の ActualWidth を RowDetails の GridLength に変換する converter を追加
- Doc/aicoding_log.md: 作業ログを追記
### 技術決定 Why
- WPF の Window InputBindings はボタンの Visibility だけでは抑止できないため、タブ表示状態を ViewModel の CanExecute に集約して、画面ボタンとキーボード操作の可否を同じ条件にした
- RowDetailsTemplate は DataGridColumn と別レイアウトのため、固定幅では列幅変更時にずれる。上段列の ActualWidth を GridLength へ変換して同期することで、単価/上代単価/下代単価と合計/上代合計/下代合計の位置を揃える
### 確認
- 対象 XAML の XML 構文チェック成功
- 編集ファイルの CRLF 維持を確認
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-23] 09:50 Views/Sub 複数選択表示の省略レイアウト修正
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：Views/Sub/ フォルダ以下の選択ダイアログで、複数選択を行う箇所のレイアウト崩れを RangeParamView.xaml の対応パターンを参考に修正し、commit まで行う
### 実施内容
- CvWpfclient/Views/Sub/RangeParamView.xaml: 選択済みID表示の TextBlock に MinWidth=0 を追加し、長文時の省略表示を安定化
- CvWpfclient/Views/Sub/RangeInputParamView.xaml: 複数選択結果表示スタイルに MinWidth=0 を追加し、取引先/倉庫の選択済み文字列が入力欄を押し出さないよう修正
- CvWpfclient/Views/Sub/SelectShohinView.xaml: ブランド/アイテム複数選択結果表示を SelectionResultText スタイルと折り返しツールチップへ統一
- CvWpfclient/Views/Sub/SelectMultiWinView.xaml: ヘッダーと選択中行情報を Grid + TextTrimming 構成に変更し、長いタイトル/名称でも操作ボタンを押し出さないよう修正
- Doc/aicoding_log.md: 800行超過のため既存ログを Doc/aicoding_log_007.md へアーカイブし、今回作業ログを新規作成
### 技術決定 Why
- 複数選択結果は選択件数が増えると表示文字列が長くなるため、TextBlock を縮小可能にし、画面上は省略表示、全文はツールチップで確認する構成に統一した
### 確認
- 対象 XAML 4ファイルの XML 構文チェック成功
- `git diff --check` で空白エラーなし
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---
