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