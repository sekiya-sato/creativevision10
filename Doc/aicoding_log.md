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
