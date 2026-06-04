## [2026-06-04] 10:36 WebpdfView F5リロード安定化
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：WebpdfView の View / ViewModel のリロード機能が正しく動いているか確認し、うまく動いていない場合は修正、ログ、commit まで実施する。
### 実施内容
- CvWpfclient/Views/Sub/WebpdfView.xaml.cs: WebView2 内にフォーカスがある状態でも F5 を捕捉できるよう `PreviewKeyDown` で `ReloadCommand` を明示実行する処理を追加
- CvWpfclient/ViewModels/Sub/WebpdfViewModel.cs: `Pdfdata` を空文字や一時URLにせず、最終URLの `cv_reload` クエリを付け直す方式に整理。同一ミリ秒内の連続実行でも URL が必ず変わるよう連番を追加
- Doc/aicoding_log_005.md: 800行を超えた既存ログをアーカイブ
### 技術決定 Why
- Window の `InputBindings` だけでは WebView2 内フォーカス時の F5 捕捉が不安定になり得るため、View の UI イベントで ViewModel のコマンドへ委譲した
- WebView2 の実行時例外を避けるため、空文字や一時URLへの遷移ではなく、表示対象 PDF URL 自体にキャッシュバスターを付ける方式を維持した
- `UriBuilder` によるURL正規化を避け、既存URL文字列の形を保ったまま `cv_reload` のみ差し替える実装にした
### 確認
- `git diff --check` で空白エラーなしを確認（既存ファイルの LF→CRLF 警告のみ）
- `WebpdfView.xaml` を XML として読み込み可能なことを確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認
- GUI 上の F5 実操作は未実施

---

## [2026-06-04] 13:55 CodeShare契約名とDTO整合性の整理
### Agent
- GPT-5 : OpenAI : Codex
### Editor
- Codex
### 目的
- ユーザーからの要望：CodeShareの実装計画1を実施し、`Msg042_GetTableList = 44`は`42`へ変更する。開発段階のためgRPC契約名変更も許容して実装計画2も積極的に整理し、`JapanPostBizOptions`をCvServer側へ移す。ログとcommitまで行う。
### 実施内容
- CodeShare/ICoreService.cs: `ICvnetCore.cs`を現行インターフェース名に合わせてリネームし、`CvFlag`へ`DataContract`/`EnumMember`を付与。`Msg042_GetTableList`の値を`42`へ変更
- CodeShare/PrintOperation.cs: `IPrintOperation.cs`をDTO実体名に合わせてリネーム
- CodeShare/ISchedulerService.cs: `IScheduler.cs`を`ISchedulerService`へリネームし、`AddTaskAsync`/`RemoveTaskAsync`/`RemoveAllTasksAsync`/`GetTasksAsync`へメソッド名を整理
- CodeShare/IPostalAddress.cs: 郵便番号検索契約を`CallContext`受けに統一し、検索結果DTOへ`DataMember`を追加。`JapanPostBizOptions`をCodeShareから削除
- CodeShare/IWeather.cs: 天気サービス契約を`CallContext`受けに統一
- CvServer/Services/JapanPostBizOptions.cs: 日本郵便API設定型をCvServer側へ移動
- CvServer/Services/SearchByPostalCodeService.cs, WeatherService.cs, SchedulerService.cs: 変更後のCodeShare契約名と`CallContext`へ追従
- CvWpfclient/Helpers/PostalAddressSearchHelper.cs, CvWpfclient/ViewModels/MainMenuViewModel.cs, CvWpfclient/ViewModels/00System/SysSchedulerJobMenteViewModel.cs: 変更後のgRPC契約呼び出しへ追従
- Tests/TestServer/TestServer.cs, readme.md, Doc/spec/spec.general.readme.md, Doc/opencode_usage_guide.md, Doc/opencode_prompts.md: 旧Core契約名の記述を現行名へ整理
### 技術決定 Why
- 開発段階で契約名変更が許容されるため、互換aliasを残さず公開契約名と実装名を一致させた
- `JapanPostBizOptions`はgRPC DTOではなくCvServer固有の外部API設定であるため、CodeShareからCvServerへ移して共有契約面を小さくした
- `CallContext`へ統一することで、JWTメタデータとキャンセル伝播の扱いを他のCodeShare gRPC契約と揃えた
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CodeShare/CodeShare.csproj"` でビルド成功（0 warnings / 0 errors）
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx"` でビルド成功（0 warnings / 0 errors）
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet run --project Tests/TestLogin/TestLogin.csproj --no-build"` で3件成功
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet run --project Tests/TestServer/TestServer.csproj --no-build"` で6件成功
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet format CodeShare/CodeShare.csproj --verify-no-changes --no-restore"` で差分なしを確認
- `git diff --check` で空白エラーなしを確認

---

## [2026-06-04] 15:15 CvAsset Phase 1 セキュリティ・バグ修正
### Agent
- Kimi-k2.6 : OpenCode : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvAssetプロジェクトの精査で発見したセキュリティ・バグをPhase 1として修正し、Build→Log→Commitを実施
### 実施内容
- CvAsset/CommonClass.cs: 
  - `static readonly Aes algorithm = Aes.Create()` を削除し、`EncryptString`/`DecryptString` メソッド内で `using var algorithm = Aes.Create()` に変更（スレッドセーフ化）
  - salt値をハードコードから `const string SaltValue` 定数化
  - PBKDF2イテレーション回数に「既存暗号化データ互換性に注意」のTODOコメントを追加
  - 空catchブロック内に `Debug.WriteLine` と「後方互換のため空文字を返す」TODOコメントを追加
### 技術決定 Why
- `Aes` インスタンスはスレッドセーフではなく、`Key`/`IV` のセット・暗号化が競合するとデータ破損の可能性があるため、メソッドスコープに移動
- saltのハードコードは可読性・保守性が低いため定数化。値の変更は既存暗号化データ互換性を失うため保留
- 空catchブロックの即座修正は呼び出し側（LoginService, LoginViewModel 等5ファイル）の「空文字＝失敗」前提を壊す破壊的変更となるため、TODOコメントで将来対応を明示
### 確認
- `C:\Windows\System32\cmd.exe /d/c "C:\gitroot\UT\vscmd.bat dotnet build CvAsset/CvAsset.csproj"` でビルド成功（0 warnings / 0 errors）

---

## [2026-06-04] 15:18 CvAsset Phase 2 タイポ・命名・コメント統一
### Agent
- Kimi-k2.6 : OpenCode : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvAssetプロジェクトの精査で発見したタイポ・命名・コメント不整合をPhase 2として修正
### 実施内容
- CvAsset/CommonExtensions.cs: `ToAdjustCronDateTIme()` → `ToAdjustCronDateTime()`（タイポ修正）
- CvAsset/CommonClass.cs:
  - `EncryptLoginRequest` パラメーター `planePass` → `plainPass`（タイポ修正）
  - `CloneObject` private版 → `CloneObjectInternal`（public版との命名重複解消）
  - `DeepCopyValue` XMLコメント「ShallowCopy」→「DeepCopy」（実装とコメントの矛盾修正）
  - `GetVdate`, `FromUtcTicks`, `GetIPAddress`, `MonthNames` に `[英語訳]` と `<returns>` を追加（コメント統一）
### 技術決定 Why
- `ToAdjustCronDateTIme` の呼び出し側が存在しないため、安全に改名可能
- `planePass` はパラメーター名のみの変更で、呼び出し側はnamed parameterを使用していないため安全
- `CloneObject` private版は `DeepCopyValue` 内でのみ使用されていたため、改名は外部に影響なし
- `DeepCopyValue` は再帰的にプロパティをコピーする実装なので、コメントを「DeepCopy」に修正して実装と整合化
### 確認
- `C:\Windows\System32\cmd.exe /d/c "C:\gitroot\UT\vscmd.bat dotnet build CvAsset/CvAsset.csproj"` でビルド成功（0 warnings / 0 errors）

---

## [2026-06-04] 15:21 CvAsset Phase 3 実装改善
### Agent
- Kimi-k2.6 : OpenCode : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvAssetプロジェクトの精査で発見した実装改善点をPhase 3として修正
### 実施内容
- CvAsset/CommonClass.cs:
  - `GetVdate()` の `DateTime.Now.ToUniversalTime().Ticks` → `DateTime.UtcNow.Ticks`（冗長な変換を除去）
  - `GetIPAddress()` の `.ToList()` 重複を collection expression `[.. ]` に変更
  - `DeepCopyValue` に `<remarks>` で「循環参照時の無限ループ注意」を追加
- CvAsset/CommonExtensions.cs:
  - `IsOkRange` の `long.Parse` → `long.TryParse`（不正文字列での例外回避）
  - `ToUnixTime` のコメントに「秒」を明記
- CvAsset/CommonFileOperation.cs:
  - `LoadAsync`/`SaveAsync` を `JsonConvert` 直接呼び出しから `Common.SerializeObject`/`DeserializeObject` に統一
  - `SaveAsync` に `myObj is null` チェックを追加（`SerializeObject` の null 警告解消）
  - `Backup4GeneAsync` の戻り値を `Task<int>` → `Task`（常に0を返していたため無意味）
### 技術決定 Why
- `DateTime.Now.ToUniversalTime()` は `DateTime.UtcNow` と等価だが冗長なため統一
- `long.Parse` は不正入力で `FormatException` を投げるため、堅牢性向上のため `TryParse` に変更
- `SerializeObject`/`DeserializeObject` ラッパー内に共通オプション（NullValueHandling.Ignore 等）があるため、直接 `JsonConvert` を使うと設定が異なる可能性がある
- `Backup4GeneAsync` の戻り値 `int` は常に0で、呼び出し側でも未使用のためシグネチャを簡潔化
### 確認
- `C:\Windows\System32\cmd.exe /d/c "C:\gitroot\UT\vscmd.bat dotnet build CvAsset/CvAsset.csproj"` でビルド成功（0 warnings / 0 errors）

---

## [2026-06-04] 15:24 CvAsset Phase 4 構成整理
### Agent
- Kimi-k2.6 : OpenCode : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：CvAssetプロジェクトの精査で発見した構成上の問題をPhase 4として修正
### 実施内容
- CvAsset/CommonExtensions.cs: コメントアウトされた死コード（ジェネリック拡張メソッドの例、演算子解説、Span優先のサンプル等）を削除
- CvAsset/CommonExtensions.cs: `CommonExtensions` クラスのXMLコメントに `[Extension methods]` 英語訳を追加
### 技術決定 Why
- 死コードは可読性を低下させ、メンテナンスコストを増やすため削除。必要な情報はプロジェクトドキュメントやコードレビューで伝達すべき
- ファイル命名統一（`CommonClass.cs`/`CommonFileOperation.cs` → `Common.Utils.cs`/`Common.File.cs` 等）は呼び出し側への影響が大きいため、今回は見送り（将来の大規模リファクタリング時に検討）
### 確認
- `C:\Windows\System32\cmd.exe /d/c "C:\gitroot\UT\vscmd.bat dotnet build CvAsset/CvAsset.csproj"` でビルド成功（0 warnings / 0 errors）

---
