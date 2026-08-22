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
