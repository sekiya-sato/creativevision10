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

## [2026-06-04] 16:41 SummaryDbトランザクション例外対策
### Agent
- GPT-5 : OpenAI
### Editor
- VS2026
### 目的
- ユーザーからの要望：transaction中に例外が起きた場合への対処を考慮し、SummaryDbの対象処理へロールバック・ログ・再送出を実装、ログ、commitまで実施する。
### 実施内容
- CvDomainLogic/SummaryDb.cs: `ExecuteInTransaction` ヘルパーを追加し、トランザクション開始・更新件数取得・コミットを共通化
- CvDomainLogic/SummaryDb.cs: `CalcSummaryStock<T>` の倉庫側集計と移動先集計を例外時ロールバックとエラーログ付きの共通ヘルパー呼び出しへ置換
- CvDomainLogic/SummaryDb.cs: `CalcSummaryRealStock` と `CalcSummaryStockCumulative` を同じ安全化パターンへ統一
### 技術決定 Why
- 既存の各ブロック独立トランザクションの粒度は維持しつつ、例外時に `AbortTransaction()` でロールバックして上位へ再送出することで、部分更新の扱いを既存設計から大きく変えずに安全性を上げた
- `changes()` は直前SQLの影響件数に依存するため、更新件数取得順序を共通ヘルパー内へ固定し、ログ出力は例外時のみに限定した
### 確認
- Visual Studio のビルドで成功を確認
- `SummaryDb` に一致する自動テストは検出されず、テスト実行は未実施

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

## [2026-06-04] 15:26 CvAsset パフォーマンス・メモリリーク精査
### Agent
- Kimi-k2.6 : OpenCode : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：Phase 1〜4終了後、CvAssetプロジェクトのパフォーマンスとメモリリークを精査
### 実施内容
- CvAsset/CommonExtensions.cs: `WriteDynamicCsv` の `Any()`/`First()` による2回イテレーションを `GetEnumerator()` による1回イテレーションに最適化
- CvAsset/CommonClass.cs: `GetIPAddress` に `<remarks>` で「頻繁呼び出し時はキャッシュ検討」を追加
- CvAsset/CommonClass.cs: `ToDataTable` に `<remarks>` で「リフレクション使用のため大量データ注意」を追加
### 技術決定 Why
- `Any()` と `First()` はそれぞれ独立して列挙を開始するため、2回のイテレーションが発生。`GetEnumerator()` を使えば1回の列挙でヘッダー出力とデータ出力を完結できる
- `GetIPAddress` は `NetworkInterface.GetAllNetworkInterfaces()` を呼び出すため、高頻度呼び出しではオーバーヘッドが大きい
- `ToDataTable` は `type.GetProperties()` によるリフレクションを多用するため、大量データ変換時のパフォーマンス低下が顕著
- メモリリーク観点：`Aes` はPhase 1でメソッドスコープに移動済み。`Regex` は `RegexOptions.Compiled` で静的なので問題なし
### 確認
- `C:\Windows\System32\cmd.exe /d/c "C:\gitroot\UT\vscmd.bat dotnet build CvAsset/CvAsset.csproj"` でビルド成功（0 warnings / 0 errors）

---

## [2026-06-04] 16:17 Cv10 C#整合性精査スキル追加
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：今回の洗い出し作業を .agents/skills フォルダに skill 化する
### 実施内容
- .agents/skills/cv10-csharp-consistency-audit/SKILL.md: CvBase/CvDomainLogic/CvServer 等のC#サブプロジェクトを、整合性・命名規則・記述ブレ観点で監査し、.omo の人間作業用ドキュメントへ落とす手順を追加
### 技術決定 Why
- 同種の精査を再利用できるよう、調査観点、優先度、互換性維持方針、確認コマンドを skill として固定化した
### 確認
- SKILL.md の作成と内容確認を実施。ドキュメント追加のみのためビルドは未実施

---
## [2026-06-05] 16:00 SysAutoExecHistoryView に選択画面 RangeParamMiniView を追加
### Agent
- Sisyphus : OpenAI : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：SysAutoExecHistoryView に一覧表示前の選択画面を追加。選択条件は Id の from-to、開始日時の from-to、件数（デフォルト400）。
### 実施内容
- CvWpfclient/ViewModels/Sub/AutoExecHistorySelectParameter.cs: 自動実行履歴選択用パラメータクラスを新規作成（FromId/ToId/FromStartTime/ToStartTime/MaxCount/DisplayName）
- CvWpfclient/ViewModels/Sub/AutoExecHistoryParamMiniViewModel.cs: 選択画面用 ViewModel を新規作成（Initialize / OkCommand）
- CvWpfclient/Views/Sub/AutoExecHistoryParamMiniView.xaml: 選択画面 View を新規作成（ID from-to、開始日時 from-to DatePicker、件数、選択確定/戻るボタン）
- CvWpfclient/Views/Sub/AutoExecHistoryParamMiniView.xaml.cs: コードビハインドを新規作成
- CvWpfclient/ViewModels/00System/SysAutoExecHistoryViewModel.cs: BeforeListAsync をオーバーライドし選択画面を表示、ListWhere で ID・開始日時範囲の WHERE 句を構築、ListMaxCount を選択値に変更
### 技術決定 Why
- RangeParamMiniView は開始日時をサポートしていないため、影響範囲を限定するため専用の AutoExecHistoryParamMiniView / ViewModel を新規作成した
- 開始日時は DatePicker + DateYmd8Converter を使用し、DB の yyyyMMddHHmmss 形式に合わせて WHERE 句で 000000 / 235959 を付加する方式とした
- SysLoginViewModel の BeforeListAsync パターンを踏襲し、選択パラメータを保持して再表示時に前回値を復元するようにした
### 確認
- `C:\Windows\System32\cmd.exe /d /c "C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient/CvWpfclient.csproj"` でビルド成功（0 warnings / 0 errors）を確認

---

## [2026-06-05] 18:00 スキル作成: create-print-view-from-crs
### Agent
- Sisyphus : OpenAI : Build
### Editor
- OpenCode
### 目的
- ユーザーからの要望：今回の社員証カード印刷画面の作成作業を元に、`.agents/skills/` にスキルを作成する。CRSスクリプトとQFMファイルを解析して新しいView/ViewModel/QFMを生成する手順をスキル化する。
### 実施内容
- `.agents/skills/create-print-view-from-crs/SKILL.md`: 新規スキル作成。CRSスクリプト解析手順、ViewModel作成手順、View作成手順、QFM作成手順、MenuData追加、確認手順を含む
- スキルは既存の `wpf-project-guide`, `wpf-view-workflow`, `add-print-process-master-mente`, `author-printstream-qfm` を前提とし、Biz/BrowserからWPFへの移行という特定のユースケースにフォーカス
- スクリプトは既存の `validate_qfm.py` を流用するため、新規作成は不要と判断
### 技術決定 Why
- 既存スキルとの重複を避け、差分となる「CRS解析→WPF移行」というパターンを独立したスキルとして分離した
- スキル名は `create-print-view-from-crs` とし、機能を明確に表現
- カード型レイアウトと一覧型レイアウトの両方に対応できるよう、QFMのレイアウト判断基準を記載
### 確認
- スキル内容の整合性を確認。既存スキルとの重複・矛盾がないことを確認
- ビルドは未実施（スキルファイルのみの追加のため）

---
