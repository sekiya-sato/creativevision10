## [2026-08-22] CvWpfclient の XAML関連部品集約

### Agent
- OpenAI Codex

### Editor
- Codex

### 目的
- XAMLで利用する共通スタイル、DataGrid補助処理、ブラシコンバーター補助処理の重複を削減する。
- 既存の表示、リソースキーの適用範囲、DataGrid操作、業務別コンバーターの責務を維持する。

### 実施内容
- 在庫・配分照会4画面で重複していた使用中の6スタイルを `UIStockQueryStyles.xaml` へ集約し、既存の汎用キーと衝突しない `StockQuery*` キーへ変更した。
- `NumericSignBrushConverter` は `UIStockQueryStyles.xaml` 内で定義し、MergedDictionaryロード中の `StaticResource` 解決を辞書内で完結させた。
- 4画面すべてで未使用だった `StockSokoCell` は共有先へ移さず削除した。
- 3画面で重複していた期限超過セルスタイルを `UIFormStyles.xaml` の `OverdueTextBlock` へ集約した。
- `DataGridCellEnterNavigation` と `DataGridSelectionBehavior` のセル取得処理を `DataGridCellHelper` へ集約した。
- `NumericSignBrushConverter` と `TranKubunBrushConverter` は業務別の型を維持し、ブラシリソース解決とフォールバック生成のみ `ResourceBrushHelper` へ集約した。

### 検証
- 変更対象XAMLのXML解析: 成功。
- 共通リソースキーの定義数・利用数と旧キーが残っていないことを確認した。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj --no-restore -p:UseAppHost=false -v:minimal`: 成功（警告0、エラー0）。
- UTF-8 BOMなし、CRLF、`git diff --check`を確認する。

---

## [2026-08-22] CvWpfclient の通信処理共通化・不要呼び出し削減

### Agent
- OpenAI Codex

### Editor
- Codex

### 目的
- CvWpfclient プロジェクト内に限定し、同一の gRPC 照会・実行処理を通信専用ヘルパーへ集約する。
- 未使用コードと不要な非同期処理を削減し、既存の業務処理、XAML、空画面、Sample画面の動作を維持する。

### 実施内容
- `CoreServiceClient` を追加し、型付きSQL照会、通常一覧照会、実行要求の生成・送信・応答判定を共通化した。
- 各 ViewModel の既存ローカルメソッドは1行の委譲として残し、呼び出し側、継承関係、XAMLコマンド名を維持した。
- `ClientLib` からプロジェクト内で未使用の `ExitAll`、`GetActiveWin`、`SetDataGridDic` を削除した。
- URL起動時の不要な `Task.Run` と、処理を転送するだけの `async` / `await` を削減した。
- `BaseViewModel` への通信処理集約、帳票ラッパー・選択コマンドの追加共通化は、責務過多を避けるため実施しなかった。
- 応答後のキャンセル判定が異なる月次処理2箇所は、動作を変えないため共通化対象から外した。
- 空の View / ViewModel と `SampleView` / `SampleViewModel` は変更していない。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj --no-restore -p:UseAppHost=false -v:minimal`: 成功（警告0、エラー0）。
- `git diff --check`、UTF-8 BOMなし、CRLFを確認する。

---

## [2026-08-22] CvServer の不要呼び出し削減・共通化

### Agent
- OpenAI Codex

### Editor
- Codex

### 目的
- CvServer プロジェクト内に限定し、重複処理と不要な呼び出しを削減する。
- JWT設定、サーバー情報、印刷パス解決を共通化し、既存の認証・印刷・gRPC動作を維持する。

### 実施内容
- `SchedulerService.ExecuteSqliteWalCheckpoint` で二重実行されていた `PRAGMA optimize` を1回に修正した。
- `PrintPdfService` の `Wait` / `Result` とPDF生成待機中の `Thread.Sleep` を、`await` とキャンセル対応 `Task.Delay` に変更した。
- `JwtSettings` を追加し、JWT検証・発行で使用するIssuer、Audience、SecretKey、有効期間、署名設定を共通化した。
- `LoginService` のJWT生成、応答生成、`SysHistJwt` 登録を共通化した。
- `AppGlobal.Shared` を共有インスタンスとしてDI登録し、サーバー情報取得時の都度生成を廃止した。将来利用予定の `Counter` は維持した。
- `PrintServerPathResolver` を追加し、印刷処理とワークファイル削除処理のパス解決を、従来と同じContentRoot・PrintBaseDir基準で共通化した。
- 未使用DIは将来利用の可能性があるため維持した。
- `HandlerClass` の検索・書き込み処理は、共通化により処理順が見えにくくなることを避けるため現状を維持した。
- 800行を超えていた旧 `Doc/aicoding_log.md` を `Doc/aicoding_log_014.md` へ退避した。

### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj --no-restore -p:OutputPath=obj\CodexBuildOutput\`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet run --project Tests\TestServer\TestServer.csproj --no-restore`: 168件成功、0件失敗。
- `git diff --check`、UTF-8 BOMなし、CRLFを確認する。

---
