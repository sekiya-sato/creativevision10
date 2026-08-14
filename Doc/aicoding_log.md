## [2026-08-14] 消込2画面を実データで検証し請求先絞り込みの不具合を修正
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- 消込機能を実データ（`server-user163.db`）で通し、楽観排他の競合検知まで実機確認する。
- 支払消込は実データが仕入1件/支払1件しかなく検証不能だったため、検証用データを投入する。
### 実施内容（検証データ投入）
- 仕入24件（仕入20＋返品4、掛計上日2026年07月、計1,056,000円）、支払3件（支払日2026年08月、計1,055,880円）、入金3件（得意先2090向け、2021年10月、計47,240円）を投入した。`ManualNo` に `CLTEST` プレフィックスを付けて識別できるようにした。
- 仕入は `Jmeisai='[]'` にした。在庫集計は `CROSS JOIN json_each(Jmeisai)` で明細を読むため、明細0件なら `SummaryRealStock` / `SummaryStock` へ影響せず、Rebuildを回しても不変である。
- 入金側も投入した。既存の入金2,299件は `Id_Torisaki=0` が2,205件、残り94件は `TenType=0`（倉庫）向けで、`TenType=1` に絞る入金消込では卸売上と対応しないため実データだけでは検証できなかった。
### 実施内容（不具合修正）
- `CvWpfclient/Helpers/ViewModels/BaseMatchingViewModel.cs`: 請求先・得意先Idの絞り込みが常に0件になる不具合を修正した。`SqlId()` を追加し、Idをパラメータではなく直接SQLへ埋め込む形にした（`BuildPaysakiSubQuery` / `BuildToriWhere`）。
- `CvWpfclient/Models/MenuData.cs`: 入金消込・支払消込の `addInfo` が「FIFOで突合し未回収残･未充当を確認(保存は未対応)」と旧仕様のままだったため、EndFlag方式の説明へ差し替えた。
### 技術決定 Why
- **Idを直接埋め込む理由**: `QueryListSqlParam.Parameters` は `string[]` なので、Idをパラメータで渡すと `Id_Paysaki = '1'` という比較になる。SQLiteは動的型のため整数列と文字列は一致せず、副問い合わせが常に0行になっていた。Idは `long` で数値以外を含み得ないため、サーバー側 `HandlePartialUpdate` の `Id IN (...)` と同じ理由で直接埋め込む方式に統一した。既存画面は取引先を「コード範囲（文字列）」で絞る作りだったため、この問題は今回Id比較を導入して初めて露呈した。
- **この不具合はビルドとテストでは検出できなかった**。警告0・テスト45/45成功でも一覧が空になる。実機で画面を開いて初めて判明したので、消込のような新しいSQL経路を足したときは実データでの一覧取得確認を必須にする。
- 検証は skill `verify-wpf-screen-runtime` に従い、`MainMenuViewModel` と `BaseMatchingViewModel` へ環境変数フックを一時的に入れてViewModel経由で画面と条件をセットした。座標クリックではなく UI Automation でボタンを名前解決してクリックした。フックは検証後に削除済み（`TEMP-VERIFY` の残存なしを確認）。
### 検証結果
- `UpdateDb` の `26_08_14_01` が実DBへ適用され `EndFlag` 列が両テーブルに存在（`DbVersion=26081401`）。
- 支払消込: 仕入25件/支払3件を取得、仕入計1,091,200・支払計1,055,880。`消込実行` で「25件消込しました」→ `Tran03Shiire.EndFlag=1` が25件。
- 入金消込: 売上15件/入金3件を取得、売上計47,380・入金計47,240。「15件消込しました」→ 対象15件のみ `EndFlag=1`（残り50,296件は不変）。
- `Jmeisai` の `Id_Kin` 別集計は1伝票2明細（手形入金＋振込手数料）も区分ごとに分解された。
- 楽観排他: 25件中13件目の `Vdu` をDBで書き換えて実行 → サーバーログ `部分更新 競合検知 Tran03Shiire Id=13`、画面は「他端末で更新されたため消込しませんでした（1件も更新していません）」。**Id 1〜12 はUPDATE実行済みだったが `EndFlag` は25件すべて1のままで、部分適用が発生していない**ことをDBで確認した。
- 成功時の一覧自動再取得も動作し「未反映の変更 0 件」へ戻った。
### 影響範囲
- `CvWpfclient/Helpers/ViewModels/BaseMatchingViewModel.cs`、`CvWpfclient/Models/MenuData.cs`
- `Doc/spec/2026-08-12_CV10機能完成度チェックリスト.md`（8.4.1 実機検証の結果を追加）
- 投入した検証データは後続検証のため削除せず残す。消込済みになった仕入25件・売上15件の `EndFlag=1` もそのまま。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj -p:OutputPath=obj\ClaudeBuildOutput\`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj -p:OutputPath=obj\ClaudeBuildOutput\`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet run --project Tests\TestServer\TestServer.csproj`: 合計45 / 成功45 / 失敗0。
- `git diff --check`: 成功。変更3ファイルを UTF-8(BOMなし) + CRLF へ正規化。
### 残課題
- **サーバー起動時のカレントディレクトリに注意**。`ConnectionStrings:sqlite` が相対パス `server-user163.db` なので、cwd が違うとその場所に空DBが新規作成され、初期データだけの別DBを見てしまう。今回リポジトリ直下に誤って作成し削除した。起動は cwd を `CvServer` にする（`--contentroot` だけでは不十分）。
- クライアントも作業ディレクトリを exe と揃えないと MaterialDesignThemes のリソース解決に失敗して起動時エラーになる。
- 元帳（`TokuiLedger` / `ShiireLedger`）への `*` 列追加（ドラフト 2.1.2-6）は未着手のため、消込結果は消込画面でしか見えない。
- `Id` をSQLへ直接埋め込む対処は消込画面のみに入れた。他画面が今後Id比較を追加する場合は同じ罠を踏むため、`QueryListSqlParam` に数値パラメータを渡せるようにするか、共通ヘルパへ切り出すかを検討する。

---
## [2026-08-14] 部分更新に楽観排他を導入し実装を簡素化
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- 直前のコミット `3db7830` で新設した `HandlePartialUpdate()` を `HandleUpdate()` に近づけ、楽観排他を入れる。
- 併せて `HandlePartialUpdate()` とその付随処理の冗長さを解消する。
### 実施内容
- `CvBase/Parameters.cs`: `PartialUpdateRow` へ `ExpectedVdu` を追加した（`PartialUpdateRow(long Id, long ExpectedVdu, string[] Values)`）。命名は既存の `QueryByIdParam.ExpectedVdu` に合わせた。`PartialUpdateParam` のコメントへ楽観排他の契約（1件でも競合すれば全体rollback、部分適用しない）を明記した。
- `CvServer/Services/HandlerClass.cs`: `HandlePartialUpdate()` を書き換えた。
  - 楽観排他: 行ごとに `UPDATE {table} SET {列} , Vdu = @ WHERE Id = @ AND Vdu = @ExpectedVdu` を実行し、更新行数が0なら `AbortTransaction()` して `CvMsgErrorCode.ConcurrentUpdate` と `ConcurrentUpdateMessage` を返す。`HandleUpdate()` と同じエラーコード・文言定数・トランザクション構造にした。競合したIdはログと `DataMsg` に残す。
  - 簡素化: `BuildPartialUpdateGroupKey()` を削除。同値行のグループ化と900件ごとの `Id IN (...)` チャンク分割も削除した。`PartialUpdateReservedColumns` を `PartialUpdateDeniedColumns` へ統合して配列1本・チェック1回にした。`CreatePartialUpdateError()` を削除し `HandleUpdate()` と同じく `CreateErrorResponse()` を直接呼ぶようにした。`TryResolvePartialUpdateColumns()` を `TryValidatePartialUpdate()` へ改名し、型・列・行の検証を1か所へ集約した。
- `CvWpfclient/Helpers/ViewModels/BaseMatchingViewModel.cs`:
  - `MatchingDenRow` へ `Vdu` を追加し、一覧取得時に `d.Vdu` を保持するようにした（`DenSelectColumns` は既に `h.Vdu` を選択済みなので派生ViewModelは変更なし）。
  - `ExecuteKesikomi()` の差分抽出を Id のリストから行のリストへ変え、`PartialUpdateRow(r.Id, r.Vdu, [値])` を送るようにした。
  - `CvMsgErrorCode.ConcurrentUpdate` を専用処理にした。サーバー側でrollback済みなので「1件も更新していない」ことを文言に含め、一覧を破棄して `一覧取得` での再取得を促す（`BaseMenteViewModel.HandleConcurrentUpdate()` と同じ方針）。
  - 成功時は `OriginalEndFlag` を手で書き換える代わりに `OnSearchAsync()` で一覧を再取得するようにした。「サーバー更新行数が要求と違う」注記は不要になったため削除した。
### 技術決定 Why
- **楽観排他と簡素化が同じ方向を向いていた**。行ごとに `Vdu` を照合するなら更新は1行ずつになるため、同値行のグループ化とチャンク分割そのものが不要になる。結果として `HandlePartialUpdate()` 周辺は4メンバー約140行から2メンバー約100行になり、SQLite のパラメータ上限対策も消えた。
- **競合時は全件rollback**（ユーザー確認済み）。部分適用を許すと戻り値と画面表現が複雑になり、どこまで反映されたかを利用者が追えない。単一トランザクションで全件戻し、再取得させるほうが状態が単純である。既存の `HandleBulkInsert()` も途中失敗時は全体を戻す方針で揃っている。
- **成功後は一覧を自動再取得**（ユーザー確認済み）。`PartialUpdateResult` へ新 `Vdu` を持たせて画面の行だけ書き換える案より単純で、`EndFlag` と入金集計も含めて画面が常にDBと一致する。代償はスキャン位置が先頭へ戻ることと再クエリ1回。
- **`ExpectedVdu` を `WHERE` へ入れる方式にした理由**: `HandleUpdate()` のように事前 `SELECT` で照合すると1行あたり2文になる。条件付き `UPDATE` なら1文で済み、更新行数0で「Vdu不一致または削除済み」を同時に検出できる。エラーコードと文言は `HandleUpdate()` と同一なのでクライアントの扱いは変わらない。
- 禁止列リスト方式は前回の決定どおり維持した。属性で許可列を宣言する案は部分更新の適用箇所が増えた時点で再判断する（ドラフト4章-10）。
### 影響範囲
- `CvBase/Parameters.cs`、`CvServer/Services/HandlerClass.cs`、`CvWpfclient/Helpers/ViewModels/BaseMatchingViewModel.cs`
- `Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md`（2.1.3 の楽観排他の記述を差し替え）
- `Doc/spec/2026-08-12_CV10機能完成度チェックリスト.md`（8.4 の「意図的に実装しなかった点」から楽観排他を外した）
- `PartialUpdateParam` の利用者は消込画面のみなので、他画面への影響はない。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj -p:OutputPath=obj\ClaudeBuildOutput\`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj -p:OutputPath=obj\ClaudeBuildOutput\`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet run --project Tests\TestServer\TestServer.csproj`: 合計45 / 成功45 / 失敗0。
- 変更3ファイルを UTF-8(BOMなし) + CRLF へ正規化。`git diff --check`: 成功。
### 残課題
- **楽観排他の動作を自動テストで確認できていない**。`CoreService` のハンドラには既存テストが1件も無く（`Tests/TestServer` は `SummaryDb` と `MasterCascadeDb` を直接叩く構成）、テスト専用の実行プログラムを増やさない方針（AGENTS.md 3章）に従って実行時確認へ回した。
- 競合検知の実行時確認には、2クライアントで同じ伝票を一覧取得するか、一覧取得後にDBの `Vdu` を直接書き換える操作が必要。
- 前回からの引き継ぎ分（実行時の画面確認、支払消込の実データ不足、元帳の `*` 列追加）は未解消のまま。

---
## [2026-08-14] 消込画面をEndFlag方式へ差し替え（Phase1仕様 2.1.1 の実装）
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- `Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md` 2.1 の消込仕様確定を受け、2.1.1「確定内容が既存実装に与える影響」を実装する。
- FIFO自動充当と `TranKesikomi` 新設案を廃止し、伝票単位の `EndFlag` ＋CheckBox方式へ差し替える。
### 実施内容（スキーマ）
- `CvBase/BaseDb2Trans.cs`: `Tran00Uriage` / `Tran03Shiire` へ `EndFlag`（`int`）と `EnEndFlag`（`EnumYesNo`）を追加。既存 `IsPrint` と同形。
- `CvBase/UpdateDb.cs`: `26_08_14_01` として両テーブルへ `ADD COLUMN EndFlag NUMBER not null default 0` を追加。既存伝票は全て未消込(0)で移行される。
### 実施内容（サーバー）
- `CvBase/Parameters.cs`: `PartialUpdateParam(ItemType, Columns, Rows)` / `PartialUpdateRow(Id, Values)` / `PartialUpdateResult(UpdatedCount)` を新設。
- `CvServer/Services/HandlerClass.cs`: `Msg201_Op_Execute` へ `PartialUpdateParam` 分岐と `HandlePartialUpdate()` を追加。指定列と `Vdu` のみを単一トランザクションで更新する。
  - 列名は対象型にマップされた実プロパティ名と一致したものだけを使ってSET句を組み立てる（クライアント文字列はSQLへ渡らない）。
  - `Id` / `Vdc` / `Vdu` は指定不可。`Vdu` はサーバー側で `Common.GetVdate()` を採番する。
  - 付随処理（在庫再集計・V*列伝播・Derived更新）を実行しないため、禁止列リストで該当列を拒否する。
  - 同値の行は `Id IN (...)` で900件ずつまとめる。グループキーは値を長さ付きで連結して境界の曖昧さを避ける。
### 実施内容（クライアント）
- `CvWpfclient/Helpers/ViewModels/BaseMatchingViewModel.cs`: FIFO関連（`ApplyFifoAllocation` / `Allocated` / `Remain` / `Unapplied` / `AutoMatch` / `ClearMatch` / `IsUnmatchedOnly` / `ToriCodeFrom` / `ToriCodeTo` / `PickToriCode`）を全廃し、以下へ書き換えた。
  - 検索条件: 請求先Id（必須）、得意先Id（任意）、掛計上日 from-to（既定 先月1日〜末日）、支払日 from-to（既定 当月1日〜末日）。
  - 一覧: `MatchingDenRow` に消込Flg（`IsKesikomi`）と一覧取得時点の `OriginalEndFlag` を持たせ、差分だけを更新対象にする。消込済み伝票は初期ONで表示し、チェックを外して`消込実行`すると解除になる。
  - 入金内訳: `Jmeisai` を `Id_Kin` 単位でC#集計（`MatchingKinRow`）。明細が空の伝票はヘッダ `KingakuTotal` を「(区分未設定)」へ寄せる。
  - 合計: 伝票全件計 / チェック計 / 入金(支払)計 の3本を比較できるようにした。
- `NyukinMatchingViewModel` / `ShiharaiMatchingViewModel`: 新しい抽象メンバ（`PaysakiLabel` / `ToriLabel` / `GetDenEndFlag` / `PickToriMaster`）を実装し、`DenSelectColumns` へ `h.EndFlag` を追加した。
- `NyukinMatchingView.xaml` / `ShiharaiMatchingView.xaml`: 条件枠を5行へ再構成し、ボタンを「一覧取得(F5) / 消込実行(F6) / 全チェック / 全解除 / 条件クリア」へ差し替えた。左グリッド先頭に消込Flg列、右グリッドを区分別集計へ変更した。
### 技術決定 Why
- **`PartialUpdateParam` を新設した理由**: `Tran00Uriage` / `Tran03Shiire` は `ITranSoko` を実装するため、既存の `UpdateParam`（行全体を保存）で消込すると `HandleUpdate` が1件ごとに `CalcTran2SummaryStock()` を旧値反転＋新値加算で2回呼ぶ。`EndFlag` は在庫へ影響しないので無駄であり、数百件の消込では実害になる。汎用の部分更新経路を設けて該当列だけを更新した。
- **禁止列リスト方式を採った理由**: 部分更新は付随処理を実行しないため、在庫・掛集計・V*伝播に影響する列を通すと不整合になる。属性で許可列を宣言する方式（`[PartialUpdatable]` 相当）も検討したが、属性1個の新設とスキーマ側への注記が増えるため、適用箇所が増えた時点で再判断することにした。ユーザー確認済み。保守上の注意はドラフト4章-10へ記録した。
- **楽観排他を行わない**: `Vdu` を照合しないため他端末の消込を上書きする。伝票単位の目印であり金額を変えないためこの割り切りとした。更新行数が要求件数と異なるときは画面メッセージへ注記して気付けるようにした。厳密な排他が必要になれば `PartialUpdateRow` へ `ExpectedVdu` を足せる。
- **請求先の絞り込み**: `Id_Paysaki = X OR (Id = X AND Id_Paysaki IN (0, X))` とした。請求先が自社（`Id_Paysaki` が自分自身）の運用と `Id_Paysaki` 未設定(0)の両方が既存データにあり、単純な `Id_Paysaki = X` では請求先自身と未設定の得意先が漏れる。
- **入金内訳をクライアント側で集計**: `Jmeisai` は `[SerializedColumn]` のJSON列でSQLの `GROUP BY` が使えない。照会は `QueryListSqlParam` がDBマップ型を返す既存経路に載せるのが最小変更なので、`Jmeisai` を含めて取得しC#で展開した。集計キーは伝票へコピーされた `Code_Kin` ではなく外部キーの `Id_Kin` とした。
- **消込Flgに `DataGridTemplateColumn` を使った理由**: `DataGridCheckBoxColumn` はセルを離れるまで値をコミットしないため、チェック計の即時連動ができない。`UpdateSourceTrigger=PropertyChanged` を指定した `CheckBox` を置いた。
- 2.1.2 の残論点はまだ未承認なので、ドラフト6章の推奨案を採用して先行実装した。採用状況を6章の表へ「実装」列として明記し、実画面確認後に最終承認できるようにした。
### 影響範囲
- `CvBase/BaseDb2Trans.cs`、`CvBase/Parameters.cs`、`CvBase/UpdateDb.cs`
- `CvServer/Services/HandlerClass.cs`
- `CvWpfclient/Helpers/ViewModels/BaseMatchingViewModel.cs`
- `CvWpfclient/ViewModels/06Uriage/NyukinMatchingViewModel.cs`、`CvWpfclient/ViewModels/05Shiire/ShiharaiMatchingViewModel.cs`
- `CvWpfclient/Views/06Uriage/NyukinMatchingView.xaml`、`CvWpfclient/Views/05Shiire/ShiharaiMatchingView.xaml`
- `Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md`（2.1.1 に実装済み表記、2.1.3 新設、4章-10 追加、6章に実装列）
- `Doc/spec/2026-08-12_CV10機能完成度チェックリスト.md`（消込2画面を L2→L3、2章サマリ L2 14→12 / L3 59→61、4章の断絶3を解消、8.4 追加）
- `Doc/aicoding_log_012.md`（800行に近づいた既存ログを退避）
- `SummaryUriKake` / `SummaryKaiKake` の集計SQLは変更していない。消込は残高へ影響しない設計のため。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build CvServer\CvServer.csproj -p:OutputPath=obj\ClaudeBuildOutput\`: 成功（警告0、エラー0）。稼働中サーバーのDLLロックを避けるため出力先を分離した。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj -p:OutputPath=obj\ClaudeBuildOutput\`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet run --project Tests\TestServer\TestServer.csproj`: 合計45 / 成功45 / 失敗0。`dotnet test` は .NET 10 + Microsoft.Testing.Platform では VSTest target 非対応で失敗するため使わない（既知）。
- 変更9ファイルを UTF-8(BOMなし) + CRLF へ正規化した。`ps-crlf.ps1` は全 `*.cs` / `*.xaml` を書き換えるため使わず、対象ファイルのみ処理した。
- `git diff --check`: 成功。
### 残課題
- **実行時確認が未実施**。`CvServer` / `CvWpfclient` を起動した画面確認をしていない。特に以下は静的確認のみである。
  - `UpdateDb` の `26_08_14_01` が稼働DBへ流れること。
  - 請求先選択ダイアログ（`PrintPdfHelper.ShowSelectDialog`）の戻り値と `(Id) Cd Mei` 表示。
  - 消込Flgのチェック計即時連動と、`消込実行` 後の再一覧。
- 支払消込は実データが `Tran03Shiire` 2件 / `Tran07Shiharai` 0件しかないため、実運用相当の検証ができない。入金消込は `Tran00Uriage` 50,311件 / `Tran06Nyukin` 2,299件があるので実データで確認可能。
- 元帳（`TokuiLedger` / `ShiireLedger`）への `*` 列追加（ドラフト 2.1.2-6）は範囲外。これが入るまで消込結果は消込画面でしか見えない。
- 得意先Idを指定すると売上一覧は1社へ絞られるが入金は請求先配下すべての集計のままなので、合計比較が成立しない。仕様どおりだが画面のToolTipで補足しているだけである。
- `PartialUpdateParam` の禁止列リストは定数配列での列挙であり、今後スキーマへ在庫・掛に影響する列を足したときの追記漏れを検出できない。ドラフト4章-10 に記録した。

---
