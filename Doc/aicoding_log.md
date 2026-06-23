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