## [2026-07-24] 13:56 MainMenu の気温推移グラフをWPF標準描画へ置換
### Agent
- GPT-5.6 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：SkiaSharp 4.x系への更新問題を解消するため、MainMenuのグラフを現行機能と外観を極力保ったWPF標準のCanvas/Polyline描画へ置換する。
### 実施内容
- `Directory.Packages.props`、`CvWpfclient/CvWpfclient.csproj`: `LiveChartsCore.SkiaSharpView.WPF` と `SkiaSharp.Views.WPF` のパッケージ参照を削除。
- `CvWpfclient/ViewModels/MainMenuViewModel.cs`: 時間別予報を描画専用モデルへ整理し、5℃単位の縦軸範囲を算出するよう変更。
- `CvWpfclient/Views/MainMenuView.xaml`、`CvWpfclient/Views/MainMenuView.xaml.cs`: Canvas、Polyline、Polygon、Ellipse、TextBlockで折れ線・塗りつぶし・目盛・ラベルを描画し、系列上の近傍ポイントにガイド線、強調マーカー、日時・気温のポップアップを表示。
### 技術決定 Why
- LiveChartsCoreとSkiaSharp.Views.WPFを同時に撤去し、WPF標準コントロールだけで既存のデータ取得、30分更新、テーマ切替、5℃目盛、平滑な線、ポイント表示、ツールチップ相当の操作を維持するため。
### 確認
- `MainMenuView.xaml` のXML構文、イベント接続、テーマリソース参照を確認。
- `LiveChartsCore`、`SkiaSharp` のソース・プロジェクト参照が残っていないことを確認。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj -p:BaseOutputPath=C:\gitroot\new2022\cv10-codex\artifacts\canvas-chart-build`: 成功（0警告、0エラー）。

---
## [2026-07-24] 09:45 MasterSysKanriMenteView 入力欄の文字が表示されない不具合を修正
### Agent
- Claude Opus 4.8 : Anthropic
### Editor
- Claude Code
### 目的
- ユーザーからの要望：MasterSysKanriMenteView でTextBoxに値は存在するが表示されない状態を、この画面のみ修正する。
### 実施内容
- `CvWpfclient/Views/01Master/MasterSysKanriMenteView.xaml`: 入力TextBox 20箇所の Style を `FormTextBox`(MaterialDesignOutlinedTextBox) から `MaterialDesignTextBox`(下線スタイル) へ変更。
### 技術決定 Why
- 前コミット(84fc78e)で無効キー`MaterialDesignBody*`を`FormTextBox`へ置換したが、当画面は外部Label＋固定`Height="30"`のコンパクト構成で、Outlined(浮動ラベル前提)は30px内でテキスト描画領域がクリップされ、値はあるのに不可視になっていた。下線系`MaterialDesignTextBox`はコンパクト高でも文字が表示され、テーマ対応(ライト/ダーク)も維持。隣接のDatePicker/ComboBox(30px)とも高さ整合。
### 確認
- `dotnet build C:\gitroot\new2022\cv10-claude\CvWpfclient\CvWpfclient.csproj`: 成功（0警告/0エラー）。
- 実画面(MasterSysKanriMenteView)を一時フックで起動し`PrintWindow`でキャプチャ、会社名/住所/TEL/税率/事業者登録番号等の値が表示されることを目視確認。確認後フック削除。

---
## [2026-07-24] 09:15 CvWpfclient XAMLのレイアウト崩れ点検・修正と check-xaml-layout スキル作成
### Agent
- Claude Opus 4.8 : Anthropic
### Editor
- Claude Code
### 目的
- ユーザーからの要望：CvWpfclientプロジェクト全体のXAMLをチェックし、デザイン崩れ・レイアウト崩れ・余白不足・文字見切れを修正する。あわせて今回のチェックに有用なスキルを他スキルとフォーマットを合わせて作成し、実画面での目視確認も行う。
### 実施内容
- `.agents/skills/check-xaml-layout/SKILL.md`: 新規作成（視覚レイアウト崩れ検出・修正専用スキル。check-xamlは構文/リソース/バインディング、本スキルは見切れ・余白・崩れ・不統一を担当）。
- `.agents/skills/wpf-project-guide/SKILL.md`・`.agents/skills/check-xaml/SKILL.md`: 新スキルへの相互参照を追加。
- 9 View(HachuInput/JuchuInput/ShiireInput/ShopUriageInput/ShukkaUriageInput/StockInput/IdoInputOut/IdoInputSoku/InputBarcode): `AlternatingRowBackground="Beige"` → `{DynamicResource DataGridAlternatingRowBackgroundBrush}`（ダークテーマ対応、ZaikoQueryView準拠）。
- `Views/01Master/MasterSysKanriMenteView.xaml`: 無効スタイルキー `Style="{DynamicResource MaterialDesignBody*}"`(20箇所、素TextBox化していた)を`FormTextBox`へ／〒住所行の列重複(住所1/住所2)を解消／Mail欄の負マージン`-17`撤廃で右端はみ出し解消。
- `Views/05Shiire/ShiireSlipPrintView.xaml`・`Views/01Master/PrintMasterShainCardView.xaml`: 検索欄の固定色`Background="White"`除去（`MenteSearchTextBox`既定の`MaterialDesignPaper`に委譲。ライトはほぼ白で視認性維持、ダーク追従）。
- `Views/00System/SysAutoExecHistoryView.xaml`・`SysLoginHistoryView.xaml`: GridSplitterの固定色`DarkGray` → `{DynamicResource MaterialDesignDivider}`。
- Cd+Mei表示に`TextTrimming="CharacterEllipsis"`追加(計12箇所: JuchuInput3/ShopUriage4/ShukkaUriage3/StockInput2)。
- `Views/Sub/AutoExecHistoryParamMiniView.xaml`・`RangeParamMiniView.xaml`: 操作ボタン行に右下余白(`0,16,16,16`)。`Views/02Yosan/ShopBudgetReportView.xaml`: 注意書きに`TextWrapping="Wrap"`。
### 技術決定 Why
- 固定色(Beige/White/DarkGray)はダークテーマで破綻するため既存DynamicResource/共通スタイルに寄せた。`MaterialDesignBody*`は存在しないキーでDynamicResource解決に失敗し素の既定TextBoxになっていたため`FormTextBox`へ差し替え。長い名称の無音見切れは`TextTrimming`で省略表示化。
- `Background="White"`除去は直近コミット87d6597(白背景追加)の見た目を変更するが、`MenteSearchTextBox`が`MaterialDesignPaper`を既に持ちライトの視認性を保ちつつダークにも追従するため、テーマ的に妥当と判断。
### 影響範囲
- CvWpfclient/Views 配下の実体View 17ファイル＋スキル3ファイル。空`<Grid />`スタブ168ファイルは対象外。
### 確認
- `dotnet build C:\gitroot\new2022\cv10-claude\CvWpfclient\CvWpfclient.csproj`（絶対パス指定でcv10-claudeを明示ビルド）: 成功（0警告/0エラー）。
- 実画面確認: MainMenuViewModelに一時フックを入れMasterSysKanriMenteView/ShiireSlipPrintViewを起動、`PrintWindow`(PW_RENDERFULLCONTENT)でキャプチャし見切れ・余白・整列・テーマを目視確認。確認後フックは削除。
- `git diff --check`クリーン、変更ファイルはCRLF/UTF-8統一。

---
## [2026-07-23] 16:45 印刷ダイアログの入力スタイルをRangeInputParamView準拠に統一
### Agent
- kimi-k2.6 : OhMyOpenCode
### Editor
- OpenCode
### 目的
- ユーザーからの要望：印刷ダイアログのTextBoxをRangeInputParamViewのような枠付き（outlined）スタイルに変更。日付入力もDatePickerに変更。項目どうしの余白もRangeInputParamViewを参考に調整する。
### 実施内容
- `CvWpfclient/Views/05Shiire/ShiireSlipPrintView.xaml`: 
  - 仕入日をTextBoxからDatePicker（FormDatePickerスタイル、DateYmd8Converter）に変更
  - 伝票NO・手入力NOをMaterialDesignTextBoxからFormTextBox（outlined）に変更、HintAssist.Hint追加
  - 仕入先・倉庫のMenteSearchTextBoxからBackground="White"を削除しMargin="0,4"に統一
  - 区切り文字を"～"から"-"に変更、列幅140→120、Margin 24→16,12,16,12
  - 取引区分ComboBoxからBackground="White"を削除
- `CvWpfclient/ViewModels/05Shiire/ShiireSlipPrintViewModel.cs`: DenDayFrom/Toをstring型からDateTime?型に変更。BuildPrintSqlParam内の日付処理をDateTime?.Value.ToString("yyyyMMdd")に変更。
- `CvWpfclient/Views/01Master/PrintMasterShainCardView.xaml`: 社員Id・社員CodeのMenteSearchTextBoxからBackground="White"とHeight="36"を削除しMargin="0,4"に統一。区切り文字を"-"に変更、Margin 24→16,12,16,12。
- `CvWpfclient/Views/01Master/MasterPrintBarcodeView.xaml`: 商品CD・商品名のFormTextBoxからBackground="White"とHeight="55"を削除しMargin="0,4"に統一。Margin 24→16,12,16,12、列幅130→120。
### 技術決定 Why
- RangeInputParamViewではFormTextBox（MaterialDesignOutlinedTextBoxベース）を使用しており、枠線付きの一貫性のあるデザインになっている。印刷ダイアログも同じスタイルに統一することで、ユーザー体験の一貫性を向上させた。DatePickerに変更することで日付入力の使い勝手を改善。
### 確認
- `dotnet build CvWpfclient/CvWpfclient.csproj`: 成功（0警告 / 0エラー）。

---
## [2026-07-23] 16:11 印刷ダイアログ入力項目の背景色を白に統一
### Agent
- kimi-k2.6 : OhMyOpenCode
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`CvWpfclient.Views._05Shiire.ShiireSlipPrintView` のTextBox/ComboBox入力項目の背景色を白（検索ボックスと同色）に変更する。他の印刷系ダイアログも同様にチェックする。
### 実施内容
- `CvWpfclient/Views/05Shiire/ShiireSlipPrintView.xaml`: 12 TextBox + 1 ComboBox に `Background="White"` を追加（MaterialDesignTextBox、MenteSearchTextBox、MaterialDesignComboBox スタイルの入力項目すべて）。
- `CvWpfclient/Views/01Master/PrintMasterShainCardView.xaml`: 4 TextBox に `Background="White"` を追加（MenteSearchTextBox スタイル）。
- `CvWpfclient/Views/01Master/MasterPrintBarcodeView.xaml`: 2 TextBox に `Background="White"` を追加（FormTextBox スタイル）。
- その他の印刷系ダイアログ（ShippingConfirmDetailPrintView、IdoSokuDetailBookPrintView、IdoDetailBookPrintView、HhtUnupdatedDataPrintView、NouhinBookPrintView、NouhinBookPrintCustomView）は空の `<Grid />` スタブのみで入力項目がないため、対象外。
### 技術決定 Why
- 印刷ダイアログのWindow背景は `AppCommonBackgroundBrush` (AntiqueWhite #FAEBD7) であり、`MaterialDesignTextBox` / `MenteSearchTextBox` / `FormTextBox` / `MaterialDesignComboBox` の既定背景は Transparent のため、入力欄がウィンドウ背景色と同化して視認性が低下していた。検索ボックスと同じ白色にすることで入力項目を明確に区別できるようにした。
### 確認
- `dotnet build CvWpfclient/CvWpfclient.csproj`: 成功（0警告 / 0エラー）。

---
## [2026-07-23] 06:50 仕入伝票印刷(ShiireSlipPrint)の View/ViewModel 作成と qfm 調整
### Agent
- Claude Opus 4.8 : Anthropic
### Editor
- Claude Code (Sekiya Sato Claude)
### 目的
- ユーザーからの要望：`CvWpfclient.Views._05Shiire.ShiireSlipPrintView` の作成。View/ViewModel と qfm(ShiireSlipPrint.qfm を一部修正)を、印刷系のプロジェクト標準(ShopBudgetReportView 等)に合わせて実装。実際の印字例(仕入返品伝票)に一致する SQL を生成し印刷ロジックへ渡す。
### 実施内容
- CvWpfclient/Views/05Shiire/ShiireSlipPrintView.xaml: スタブから ShopBudgetReport 準拠の印刷ダイアログへ実装。ColorZone ヘッダ + 印刷範囲(仕入日 / 仕入先 / 倉庫 / 伝票NO / 手入力NO の各範囲 + 取引区分コンボ) + 「印刷実行」ボタン。F6=DoOutputPdf / Esc=Exit。仕入先・倉庫は MenteSearchTextBox + SearchTextBoxAssist で選択。
- CvWpfclient/ViewModels/05Shiire/ShiireSlipPrintViewModel.cs: BaseViewModel 派生。範囲条件を ObservableProperty で保持し、SelectXxx コマンドで MasterShiire / MasterTokui(TenType=0) を選択。DoOutputPdf で QueryListSqlParam を組み、RunPrintPdfAsync("ShiireSlipPrint.qfm", …) で PDF 出力(ShopBudgetReport の印刷ヘルパを踏襲)。
  - SQL は Jmeisai を json_each で明細1行=CSV1行へ展開し、qfm の item1..item46 順(datasrc)に一致する 46 列を SELECT。仕入先 / 倉庫 / 自社(MasterSysman Id=1)の住所を LEFT JOIN。数量計 / 金額計 / 上代計は伝票単位の window 合計。消費税は「請求時一括」固定、総合計=金額合計。
- printform/ShiireSlipPrint.qfm: 一部修正のみ。page title「自社納品伝票」→「仕入伝票」。item4(予備コード)の decode 書式 `"("@")"` を空へ変更(データ無しで "()" を出さない)。Shift_JIS(cp932)維持。
### 技術決定 Why
- 実 DB(server-dev.db)の Tran03Shiire には Tax/Total 列が無く、SuTotal/KingakuTotal/JodaiTotal のみ存在。印字例も消費税「請求時一括」・総合計=金額合計であるため、欠番列で SQL が壊れるのを避けつつ印字例に一致させるべく、Tax/Total へ依存せず金額合計の window 合計と固定文字で再現した。
- 印刷データ供給は ShopBudgetReport / ShiireInput 明細印刷と同じ QueryListSqlParam(SELECT 列順=qfm item 順)。CSV はヘッダ無し・SELECT 列順で data.txt へ出力される(PrintPdfService / WriteDynamicCsv)ため、未参照 item(8,9,15…)も '' で列位置を維持。
- レガシー .omo の「伝票処理区分 / 印刷区分」は現行 Tran03Shiire に対応列が無いため入力条件からは除外。ただし印字例に合わせ、伝票上の「伝票処理区分」欄は "商品仕入" 固定表示、「取引区分」は Kubun ラベル表示とした。
### 確認
- dotnet build CvWpfclient/CvWpfclient.csproj: 成功(0警告 / 0エラー)。
- 生成 SQL を server-dev.db に対して実行し構文/列解決を検証(FieldCount=46=item 数、明細 JSON 展開・住所 JOIN・json_extract すべて解決)。Tran03Shiire は空のため rows=0。
- staged は対象3ファイルのみ、git diff --check クリーン、qfm は staged blob も cp932 で title「仕入伝票」を確認。

---
## [2026-07-22] 16:10 出荷売上/店舗売上入力に伝票サマリーカードを導入(Phase3後半)
### Agent
- Claude Opus 4.8 : Anthropic
### Editor
- Claude Code (Sekiya Sato Claude)
### 目的
- ユーザーからの要望：Input系最適化 Phase3の上代/下代系(出荷売上ShukkaUriage・店舗売上ShopUriage)へ、発注基準の標準要素を適用する。
### 実施内容
