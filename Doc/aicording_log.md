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
