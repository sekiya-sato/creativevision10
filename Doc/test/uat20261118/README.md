# Avalonia UI 部品検証（2026-11-18）

CvWpfclient の名称・商品マスタを参考に、一覧と編集フォームを持つ業務画面を1画面で仮構築した。実サーバ、DB、製品プロジェクトを参照しない、手動検証用の実行アプリである（単体テストランナー用プロジェクトではない）。

## 開発方針（2026-11-18）

現行Windows／WPF版の安定化と画面確定を優先する。Avaloniaへの製品移行は将来バージョン（2.0候補・番号と時期は未確定）で行い、Windows・Linux・macOSに対応する方針とする。本試作は将来検証用として保持し、現時点で追加開発や製品への組込みを進めない。将来の再開時は確定済みWPF画面を基準とし、各OSで動作を確認する。

## 起動

リポジトリルートから実行する。

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet build Doc/test/uat20261118/UatAvalonia.csproj
dotnet run --project Doc/test/uat20261118/UatAvalonia.csproj --no-build
```

.NET 10 を対象とし、Avalonia本体・Desktop・Fluent・DataGridは11.3.13、CommunityToolkit.Mvvmは8.4.2に固定。依存管理はこのフォルダ内で完結させ、製品の中央パッケージ管理・solutionには追加しない。Avalonia.X11の推移依存 `Tmds.DBus.Protocol` は、[GHSA-xrw6-gwf8-vvr9](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9) の修正バックポート0.21.3を直接指定している。

## 仮構築した部品

| CV10側の例 | 今回のAvalonia側 |
|---|---|
| ColorZone・Card・操作ボタン | Border・共有Style・WrapPanel・F2～F6 KeyBinding |
| 一覧DataGrid・選択・一覧／編集分割 | 読取専用DataGrid・SelectedItem・GridSplitter |
| TextBox・分類ComboBox・数値・日付・有効フラグ | TextBox・ComboBox・NumericUpDown・DatePicker・CheckBox |
| タブ・明細編集 | TabControl・編集DataGrid・ObservableCollectionへの行追加 |
| 仕入先選択・確認 | 非同期の結果付きWindow.ShowDialog |
| 商品マスター検索 | 商品ID・コード範囲、ブランド／アイテム複数選択、名称・略称・カナ、並び順、件数を持つダミー条件画面 |
| 一覧の段階取得 | 基本情報を全件取得し、DataGrid.LoadingRowで表示行だけ詳細をまとめて追加取得 |
| ファイル保存 | StorageProviderによるCSV保存先選択（利用者が保存した場合のみファイルを書き込む） |
| メッセージ・処理中・キャンセル | ステータス・ProgressBar・CancellationToken |
| 明暗テーマ・小画面 | Fluent Light／Dark・編集フォームScrollViewer・最小サイズ960×700 |

WPFのMaterialDesign用XAMLやBehaviorを直接流用するものではない。WPF固有のEnterセル移動・入力確定タイミング、動的列、各種検索コントロール、Converterの全種類、PDF／WebView、権限、実gRPC、実印刷は未対応。

## 操作確認

1. 起動すると架空商品40件の基本情報（コード・名称・カナ・分類）を取得する。最初の表示行だけ詳細を取得し、下へスクロールすると新たな表示行を取得する。「基本のみ→詳細取得中…→詳細取得済み」と詳細取得件数で確認できる。戻りスクロールはキャッシュを使う。
   検索ボタン／F5では、既存 `SelectShohinView` に似た条件ダイアログを開く。ブランド／アイテムの複数選択・解除が可能で、検索確定するとダミー一覧を再取得する。ダイアログの入力条件は反映しない。メイン画面の分類／検索語は絞込みに使い、変更すると旧一覧を無効化する。
2. 一覧を選ぶと編集用コピーを表示する。商品名や単価を変更しF2、確認で「はい」を選択して再検索するとメモリ内の変更を確認できる。修正時のコード変更は拒否する。
3. F4で追加する場合は未使用コードと商品名を入力する。同じコードは重複エラーとなる。F3は選択商品のメモリ内削除で、確認を取り消すと削除しない。
4. 「仕入先」の選択でダミーの仕入先を選ぶ。Esc／閉じるは選択取消。「色・サイズ明細」は直接セル編集と行追加を試す領域で、商品保存には含めない。
5. 「ダミーエラー」を有効にしてF5→ダイアログ内の検索を押すとエラーを表示する。基本情報の応答待ちは取消ボタン／Escでキャンセルする。詳細取得は一覧の再検索・条件変更・画面終了時に中断する。Escは処理がなければ基本タブへ戻り、基本タブでは画面を閉じる。
6. CSV保存は現在の一覧だけをUTF-8 BOM付きで出力する。詳細未取得の単価は「未取得」とし、CSV操作による全詳細取得は行わない。印刷F6はステータスメッセージだけを表示し、PDFを生成しない。

詳細はダミー遅延600msで仕入先・単価・有効フラグ・開始日・メモを補完する。描画時の要求を短時間まとめて取得し、同じ行の重複要求を抑える。詳細取得前は編集・修正・削除を無効化する。再検索・条件変更・保存後は詳細キャッシュを破棄し、旧応答を新一覧へ反映しない。

データはプロセス内だけで保持する。再起動すると初期40件に戻る。単価等はUI検証用で、製品の金額計算・業務保存を再実装していない。

## 描画キャプチャ

通常は指定不要。次を指定して起動すると標準サイズ、最小サイズ、明細タブのダーク表示、末尾スクロール、検索ダイアログをPNGで保存して終了する。`load-count.txt` には末尾へ移動後と先頭へ戻った後の詳細取得件数を出力する。

```powershell
$env:UAT_AVALONIA_CAPTURE_DIR = Join-Path $env:TEMP 'cv10-avalonia-preview'
dotnet run --project Doc/test/uat20261118/UatAvalonia.csproj --no-build
Remove-Item Env:UAT_AVALONIA_CAPTURE_DIR
```

PNGはクライアント領域のRenderTargetBitmap。OSのウィンドウ枠や検索以外の子ダイアログは含まない。通常起動時はこの環境変数を解除する。

## 確認済み・未確認

2026-11-18、Windows環境でbuild成功（警告0・エラー0）。起動時40件表示、仕入先選択の反映、F2確認→メモリ内保存、ダミー通信エラー後の操作復帰を実画面で確認した。標準1280×900・最小960×700・明細タブのダーク表示を描画し、画像を目視した。

追加・削除・CSV保存・処理中取消・IME変換中のキー動作・DPI変更・他OSは実操作未確認。新UIの操作性や製品版との業務同等性を受入済みとは扱わない。

追加分もWindowsでbuild成功（警告0・エラー0）。基本40件に対し初期表示の詳細16件、末尾へスクロール後26件、先頭へ戻った後も26件を確認した。F5からの商品検索画面、ブランド選択の条件欄反映、ダイアログからの一覧再取得を実画面で確認。ダイアログ条件の実検索連携、詳細取得エラー・通信断、製品側の遅延取得経路との同等性は未検証。
