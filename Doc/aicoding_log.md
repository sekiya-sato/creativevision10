## [2026-08-16] 入金・支払明細の旧区分コードを KIN 区分へ対応付け
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：`Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md` の未決事項を洗い出して提示し、決定できたものから順に対応する。今回は「入金明細の `Id_Kin` が移行データで全件 0」の1件。
### 事前調査で判明した事実
- `Tran06Nyukin` 2,302件の `Jmeisai` は `Id_Kin = 0` / `Mei_Kin = ""` で、`Code_Kin` に旧コード `80` / `82` / `85` / `88` / `89` が残っていた。手入力の4件だけが正しく `Id_Kin` を持つ。
- 原因は `ConvertDbTran.BuildKinMeisaiList()` の `getMeisho("PAY", code)`。`MasterMeisho` に `Kubun = 'PAY'` は存在せず（実在するのは `KIN` の `01`〜`05`）、常に `null` を返していた。加えて旧コードと `KIN` のコード体系が違うため、区分名で引き直しても一致しない。
- この状態では 2026-08-14 に実装した消込画面の区分別集計が、実データでは「名称空欄の1グループ」に全額集約される。
### ユーザー決定
- 旧 `80`（現金）→ `01` 現金入金、旧 `82`（振込）→ `01` 現金入金、旧 `85` → `02` 振込手数料、旧 `88` → `04` 相殺入金、旧 `89` → `05` その他入金。
- 旧82は入金手段として旧80と同じ扱いにして `01` へ寄せる。`KIN` の `03` 手形入金に対応する旧コードは実データに無い。
### 実施内容
- `CvDomainLogic/ConvertDbTran.cs`: 対応表 `KinKubunCodeMap` と `getKinMeisho()` を追加し、`BuildKinMeisaiList()` の `getMeisho("PAY", code)` を置換した。対応表に無い旧コードは従来どおり `Id_Kin = 0` のままとし `Code_Kin` へ旧コードを残す。
- `Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md`: 2.1.4 を新設して事実・原因・対応表・実装方針を記録。2.1.2-3 から参照を張り、末尾へ 2026-08-16 の追記を加えた。
### 技術決定 Why
- **対応表を `ConvertDbTran` に置いた理由**: 旧システム固有のコード体系を新コードへ寄せる知識であり、変換層以外から参照されない。入金・支払は同じ `BuildKinMeisaiList()` を通るため、1箇所の修正で両方に効く。
- **未知コードで例外を投げない理由**: 変換は5万件規模の一括処理で、途中停止すると再実行コストが高い。従来の挙動（`Id_Kin = 0` + 旧コード保持）を残せば、消込画面では「未分類」として金額が消えずに見える。
- **移行済みDBを再変換しない理由**: 全変換は数分かかるうえ、変換後に手入力された4件が失われる。`Jmeisai` を `json_set` で書き換える一括SQLで是正する。
- **`UpdateDb.cs` へ入れなかった理由**: 旧システムからの移行データにしか当たらない一過性の是正であり、`UpdateDb` が担うスキーマ移行とは性質が違う。今回はSQLをこのログへ記録して手動適用とする。
### 移行済みDBの是正SQL（手動適用）
`Tran06Nyukin` と `Tran07Shiharai` の2本を実行する。冪等（旧コードが残る行だけを対象にするため再実行しても増減しない）。
```sql
UPDATE Tran06Nyukin AS t
SET Jmeisai = (
  SELECT json_group_array(json(v)) FROM (
    SELECT CASE WHEN k.Id IS NULL THEN j.value
                ELSE json_set(j.value,'$.Id_Kin',k.Id,'$.Code_Kin',k.Code,'$.Mei_Kin',k.Name) END AS v
    FROM json_each(t.Jmeisai) AS j
    LEFT JOIN MasterMeisho AS k
      ON k.Kubun = 'KIN'
     AND k.Code = CASE json_extract(j.value,'$.Code_Kin')
                    WHEN '80' THEN '01' WHEN '82' THEN '01' WHEN '85' THEN '02'
                    WHEN '88' THEN '04' WHEN '89' THEN '05' END
    ORDER BY j.key))
WHERE t.Jmeisai LIKE '[%'
  AND EXISTS (SELECT 1 FROM json_each(t.Jmeisai) AS j2
              WHERE json_extract(j2.value,'$.Code_Kin') IN ('80','82','85','88','89'));
```
`Tran07Shiharai` はテーブル名2箇所を置き換えた同一のSQLを実行する。

- `json_group_array(json(v))` の `json()` は必須。`json_set` の結果はサブクエリ境界で JSON サブタイプを失い、`json()` で包まないと**文字列の配列として二重エンコード**される（`["{\"No\":1,...}"]`）。
- `ORDER BY j.key` も必須。明細が2行以上ある入金が732件あり、`LEFT JOIN` を挟むと元の行順が保証されない。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build CvDomainLogic\CvDomainLogic.csproj --no-restore`: 成功（警告0、エラー0）。
- 是正SQLは実DB `server-user163.db` に対し `UPDATE` を `SELECT` へ置き換えた形で変換結果を検証した。単一明細・3明細の両方で、`Id_Kin` / `Code_Kin` / `Mei_Kin` が期待値になり、`No` / `Kingaku` / `Memo` が保持され、行順も維持されることを確認した。
- `git diff --check`: 成功。
### 残課題
- **是正SQLは未適用**。読み取り専用接続で検証しただけで、実DBは書き換えていない。
- 変換ロジックの修正は**再変換を実行していない**ため、旧システムからの通しでの動作確認は未実施。
- `Tran07Shiharai` は実データ4件（全て手入力で `Id_Kin` は正しい）のため、支払側は是正SQLの対象行が0件で実効検証ができない。
- 本書で洗い出した他の未決事項（2.1.2-6 元帳の `*`、2.6 の1〜6、2.7 の1〜6、2.8-4、3章の調整伝票詳細、2.4、2.5、4章-10）は未着手。特に 2.6-3 の `IsPay` は全50,311件が `0` のため、推奨案どおり条件を追加すると売掛が全件消える。次に判断が必要なのはこの1件。

---

## [2026-08-16] 更新経路の責務分担整理と副作用オーケストレーションの集約
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：`CvDomainLogic` と `CvServer` のテーブルに対するロジックの組み込み方を整理し、適切な境界になっているか判断する。できるだけ `CvDomainLogic` へ寄せたいが、クライアントへのインターフェースは `CvServer` である。
- チェックリストと調査結果を提示し、方針をユーザーが決定してから設計案を作り、承認後に実装した。
### 事前調査と決定事項
- 調査の結果、層構成そのものは正しく、問題は `CvServer` 内の重複だと判断した。全面移設はコード量が増えるだけなので行わない（ユーザー判断）。
- 決定事項：トランザクション・楽観排他・業務イベントログは `CvServer` が持つ。読み取りは生SQL経由のまま「ゆるいルール」を維持し、更新はサーバ側で処理する。`CvDomainLogic` からの `CodeShare` 参照は必要なら可。POSは暫定実装のため触らない。業務単位が複数トランザクションに割れている問題（配分・予算の4画面）は今回のスコープ外とする。
- 責務分担の基準を次のとおり定めた。副作用の**宣言**は `CvBase` のマーカーインターフェース、副作用の**実行**は `CvDomainLogic`、副作用の**起動順序**とトランザクション・排他・ログは `CvServer`。
### 実施内容
- `CvServer/Services/WriteEffectRunner.cs`: 新規。`HandlerClass` の6メソッドに散在していた副作用の分岐（在庫再集計・引当再計算・派生展開・V*列伝播）を `Before` / `After` / `FlushReserve` / `AfterPartialUpdate` に集約。`PartialUpdateDeniedColumns` も副作用の実装と同じファイルへ移した。
- `CvDomainLogic/DerivedDb.cs`: 新規。`CvServer/Services/HandlerDerived.cs` を移設（削除）。gRPCにも`CodeShare`にも依存しない純粋なテーブル処理のため。使われていなかった `itemType` 引数を落とし、実行行数を返すようにした。
- `CvServer/Services/HandlerClass.cs`: 6経路を `WriteEffectRunner` 呼び出しへ置換。`SetCreatedAuditValues` が採番した `vdate` を返すようにし、`LogEffects` で副作用の実行行数をログへ出す。`CreateConvertDb()` を追加してOracle接続の組み立て3箇所を集約。
- `CvServer/Services/QueryMsgStreamService.cs`: Oracle接続の組み立てを `CreateConvertDb()` へ集約。
- `Tests/TestServer/WriteEffectRunnerTests.cs`: 新規12件。
- `CvBase/BaseDbJodai.cs`, `CvDomainLogic/JodaiDb.cs`, `CvDomainLogic/MasterCascadeDb.cs`, `CvWpfclient/ViewModels/01Master/MasterJouDaiBulkChangeViewModel.cs`: `HandlerDerived` を指すコメントを `DerivedDb` へ追随。
### 技術決定 Why
- **`WriteEffectRunner` を `CvDomainLogic` ではなく `CvServer` に置いた理由**: このクラスが表しているのは「順序」であり、順序はトランザクションを張る側の責務だから。個々の計算は従来どおり `SummaryDb` / `MasterCascadeDb` / `DerivedDb` を呼ぶ。全面移設すると変換層が増えてコード量が膨らむというユーザー判断にも沿う。
- **`public` にした理由**: `Tests/TestServer` は既に `CvServer` を参照している。従来「更新の手順」はテスト可能な単位になっておらず、`MasterCascadeDbTests` が `// --- HandleUpdate と同じ流れ ---` と手順を手で再現していた（オーケストレーションが実質3重管理）。`public` にすることで手順そのものをテストできる。
- **在庫と引当で扱いを変えた理由**: `CalcTran2SummaryStock` は `calcFlag` の符号による差分の加減算で、対象行をDBから読んで計算する。したがって更新・削除では行が変わる前に反転する必要があり、一括登録でもまとめられず行ごとに呼ぶ。一方 `CalcHaibun2Reserve` はキー単位の引き直しなので、一括登録では `HashSet` に貯めて最後に1回で足りる。この非対称を `After` の `reserveKeys` 引数と `FlushReserve` で表現した。
- **`if / else if` を独立した `if` に変えた理由**: 従来は `IDerivedOrigin` に該当すると `ITranSoko` の在庫計算がスキップされていた。現在 `IDerivedOrigin` は `MasterShohin` と `TranJodai` だけで両方を満たす型が無いため**挙動は変わらない**が、両方を満たす型が将来現れると在庫が欠落するため解消した。
- **`CvBaseOracle` 参照を `CvServer` に残した理由**: 接続先の決定はサーバーの責務であり、`CvDomainLogic` は `ConvertDb` として組み立て済みのものを受け取るだけでよい。
### 副次的に修正した不具合
- `HandleDelete` / `HandleDeleteById` に `try`/`catch` が無く、途中で例外が出ても `AbortTransaction` されずトランザクションが開いたままになっていた（`HandleInsert` / `HandleUpdate` にはあった）。`Insert` / `Update` と同じ形へ揃えた。
- `FetchExistingBaseDbItem` が `_db.Fetch(...)?.First()` で、行が0件のときに `?? new()` へ到達せず `InvalidOperationException` になっていた。他端末が削除済みの行を更新・削除すると `ConcurrentUpdate` ではなく汎用例外が返っていたため、`FirstOrDefault()` にして `ConcurrentUpdate` を返すようにした。`HandleDeleteById` は同じ行を2回読んでいたので1回にまとめた。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx` でソリューション全体のビルド成功（0 warnings / 0 errors）を確認。
- `Tests\TestServer\bin\Debug\net10.0\TestServer.exe` で 63/63 成功を確認（既存51件＋新規12件）。`dotnet test` は .NET 10 SDK と Microsoft.Testing.Platform の既知の非対応により実行できないため、テスト実行ファイルを直接起動した。
- 新規テストで固定した挙動: 追加・削除での引当の増減、倉庫変更時に旧キーと新キーの両方が引き直されること、一括登録では `FlushReserve` まで再計算されず同一キーが1回にまとまること、更新が「反転→更新→再計算」の順序であること、削除は反転だけで完結し後処理で在庫を触らないこと、商品マスタの追加・更新・削除に派生テーブルが追随すること、V*列伝播が更新時のみ走りCode/Name無変更なら走らないこと、部分更新は `EndFlag` を含むときだけ引当を引き直すこと、禁止列に副作用が読む列が揃っていること。
- `git diff --check` で空白エラーなし。`sed` と新規ファイル作成でLFになった6ファイルをCRLFへ戻し、BOM有無を維持したことを確認。
### 残課題
- 業務単位が複数トランザクションに割れている問題（`ShopHaibunInputViewModel` などが削除N回＋一括登録1回を別々のgRPC呼び出しで行う）は未着手。途中で失敗すると指示が消えたままになる。
- `HandlePartialUpdate` は引当以外の副作用を呼ばない。`PartialUpdateDeniedColumns` が正しいことに依存した設計で、今回もその前提を維持している（同じファイルに置いて気づきやすくしただけ）。

---

## [2026-08-16] CvBase テーブル定義への Comment 属性の全面付与
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：`CvBase` のテーブル群へ `Comment` 属性をできるだけ充実させる。目的はソースの可読性向上と、今後AIがテーブルを適切に扱えるようにすること。
- 事前に `CvWpfclient` 側の仕様（DB定義書出力）を確認し、付与可能な範囲をリストアップしてユーザー承認を得てから実施した。
### 事前調査（仕様確認）
- `Comment` 属性の消費先は3箇所で、いずれも**クラス属性のみ**を読んでいた。`ExDatabase.CreateComment`（MariaDB の `ALTER TABLE ... COMMENT`、SQLite/Oracle は no-op）、`ExDatabase.GetComment`（`Msg042_GetTableList` のテーブル一覧の説明列）、`SysTableSpecViewModel.GetClassComment`（DB定義書のテーブル説明）。
- **カラム（プロパティ）の `Comment` は現状どこからも読まれていなかった**。`ExDatabase.cs` のカラムコメント生成は意図的にコメントアウト済み（`AttributeClass.cs` の「カラムコメントは変更時に問題あるので使用しない」）で、`GetPropertyDescription` は `OldTableCommentAttr.Content`（第2引数）と `ForeignKey` だけを連結していた。
- 印刷側は対応済みだった。CSV の item6（説明）は `printform/SysTableSpec.qfm` の `Txt09`（`datasrc="item6"`）へ割当済みで、**qfm の修正は不要**。
- 実テーブル47件のクラス `Comment` は全件付与済み。未付与はサブテーブル定義・共通基底クラスの18件のみ。プロパティ側は `BaseDbClass.Id/Vdc/Vdu` の3件を除き0件だった。
### 実施内容
- `CvWpfclient/ViewModels/00System/SysTableSpecViewModel.cs`: `GetPropertyDescription` の先頭でカラムの `Comment` を読むように変更。これが無いとプロパティへ付けた `Comment` が定義書に出ない。テーブルコメントと同様にカンマを除去する。
- `CvBase/BaseDb*.cs`, `CvBase/Share/BaseDbDefinition.cs`: DB列になるプロパティ639件へ `Comment` を付与。うち605件は既存の `///` サマリからの移植、34件は `///` が無いため実装から起こした（`MasterConfig` 5件、`MasterShipping` 9件、`PosSeisanSummary` 7件、`PosPaymentDetail` 4件ほか）。
- `CvBase/BaseDb*.cs`: クラス `Comment` 未付与の18型へ付与。サブテーブル定義（`MasterSysTax`、`Tran99Meisai`、`TranJodaiCond/Shop/Meisai` 等）にはどのJSON列へ格納されるかを、共通基底（`TranAllHeader`、`TranKinHeader`、`MasterTorihiki`、`TranCalcBase`）には単独の実テーブルを作らない旨を記載した。
### 技術決定 Why
- **既存の `///` サマリを機械的に移植した理由**: 454件のプロパティが既に日本語のXMLドキュメントを持っていたが、XMLドキュメントは実行時のリフレクションから読めないため定義書にも出ず、AIから見える形になっていなかった。文言を起こし直すより、既にレビュー済みの記述を反映用メタデータへ複写する方が正確で差分も追いやすい。
- **印刷都合の50字トリムを行わなかった理由**: 当初は qfm の item6 が `length="100"`（cp932）である点に合わせて全角50字で切る実装にしたが、目的が可読性とAIの理解であるため情報量を優先した。長文は文単位で約120字まで積む方式とし、印刷時のクリップは許容する。
- **`<see cref="X"/>` を参照名へ置換してからタグを除去した理由**: タグごと削除すると「`TranHaibun` の `EndFlag`=0 の `Su` 合計」が「の の =0 の 合計」になり、参照関係というAIにとって最も有用な情報が失われる。
- **`[Ignore]` / `[JsonIgnore]` / `[ResultColumn]` / `[ComputedColumn]` を対象外にした理由**: `TryGetColumnSpec` が定義書から除外している実DB列ではないプロパティと基準を揃えた。`TranTokuiPromotion.TokuiCode` などの一覧表示専用列がこれに当たる。
- **共通基底クラスへのクラス `Comment` 付与が安全な理由**: `CommentAttribute` は `Inherited` を既定（true）のままにしており、`Attribute.GetCustomAttribute` も既定で継承を辿る。`TranAllHeader` / `TranKinHeader` / `MasterTorihiki` / `SummaryRealStock` の派生テーブルは全て自前の `Comment` を持つため、基底の文言が実テーブルへ漏れることはない。`BaseDbClass` / `BaseDbHasAddress` など純粋な基盤型はテーブルではないためクラス属性を付けていない。
- **DBスキーマへの影響が無いこと**: `CreateComment` は SQLite で即 return するため、現行のSQLite運用ではDDLも実データも変化しない。MariaDB利用時のみクラス `Comment` が `ALTER TABLE` に埋め込まれるため、文言には `'` を含めていない。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj` でビルド成功（0 warnings / 0 errors）を確認。
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx` でソリューション全体のビルド成功（0 warnings / 0 errors）を確認。
- `Tests\TestServer\bin\Debug\net10.0\TestServer.exe` で 51 件全て成功を確認（`dotnet test` は .NET 10 SDK と Microsoft.Testing.Platform の既知の非対応により実行不可のため、テスト実行ファイルを直接起動した）。
- ビルド済み `CvBase.dll` をリフレクションで読み、`SummaryRealStock` / `MasterShipping` / `Tran00Uriage` のクラスおよび各列の `Comment` が取得できること、`[Ignore]` 付きの `EnIsPay` には付与されていないことを確認。
- `git diff --check` で空白エラーなし、差分が挿入行のみ（削除0行）であることを確認。ファイルのBOM有無と CRLF は生バイトで判定して維持した。
### 残課題
- `Tran02PosSeisan` の金種列（`Mai10000` 等）のように、実装から起こした文言はドメイン知識に基づく確認が望ましい。
- `SysHistJwtSub.IpAddress` / `MacAddress` の `Comment` には元の `///` に含まれるNPocoの実装メモがそのまま入っている。列の意味だけに絞るなら要整理。

---

## [2026-08-15] 引当数 ReserveQty と配分の入庫済フラグ EndFlag を追加
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- `SummaryRealStock` / `SummaryStock` へ引当数列 `ReserveQty` を、`TranHaibun` へ入庫済フラグ `EndFlag` を追加する。
- `TranHaibun` の追加・修正・削除のタイミングで、修正前後のデータを基に `Id_Soko` 単位の引当数（振り分け予定数）を更新する。
- 仕様ドラフト 2.8 の未決事項（引当数の保持方式・引当対象・解除条件）を確定する。設計を提示しユーザー承認を得てから実装した。
### 実施内容（スキーマ）
- `CvBase/BaseDb3Summary.cs`: `SummaryRealStock.ReserveQty` を追加。`SummaryStock` は `SummaryRealStock` の派生クラスなので定義は1箇所、テーブルは2つ。
- `CvBase/BaseDbHaibun.cs`: `TranHaibun.EndFlag`（+ `EnEndFlag`）を `Tran00Uriage.EndFlag` と同形で追加。引当集計用インデックス `nk2`（`Id_Soko` + SKU）と `ITranReserve` 実装、`using CvBase.Share` を追加。
- `CvBase/BaseDb2Trans.cs`: `ITranReserve` を `ITranSoko` の隣に定義。引当の源泉となるトランザクションを表す。
- `CvBase/UpdateDb.cs`: `26_08_15_01` を追加（`SummaryRealStock` / `SummaryStock` へ `ReserveQty`、`TranHaibun` へ `EndFlag`）。インデックス `nk2` は `CreateIndex` が `IF NOT EXISTS` で毎回発行するため UpdateDb 不要。
### 実施内容（集計）
- `CvDomainLogic/SummaryDb.cs`: `ReserveKey`（集計キー）、`CalcHaibun2Reserve()`（キー単位）、`CalcReserveQtyAll()`（全件）を追加。Rebuild 3経路（`CalcSummaryStockRange` / `CalcSummaryRealStock` / `CalcSummaryRealStockRange`）の再作成後に `CalcReserveQtyAll()` を呼ぶ。
- `CvServer/Services/HandlerClass.cs`: Insert / BulkInsert / Update / Delete / DeleteById / PartialUpdate の6経路へフックを追加。在庫更新と同一トランザクション内で実行する。
### 技術決定 Why
- **差分加減算ではなくキー単位の引き直しにした理由**: `ReserveQty` は累積値ではなく「現時点の引当残」なので、対象キーを `TranHaibun` から引き直せば常に正解になる。`ITranSoko` 系の「旧値を反転→新値を加算」と違い、呼び出し順やDB更新との前後関係に依存せず、通常更新値とRebuild値が原理的に一致する（仕様 2.2 の受入条件）。修正時は修正前と修正後の両方のキーを渡す。
- **`SummaryRealStock.ReserveQty` を `SummaryStock` の月次合計ではなく `TranHaibun` から直接引く理由**: `SummaryRealStock` の再作成は `SumMonth <= 対象年月` で打ち切るため、未来日付の配分指示が引当から漏れる。`Su` は「その年月時点の在庫」で正しいが、引当は時点値なので月の打ち切りをしない。
- **Rebuild で範囲指定せず全件を対象にする理由**: `ReserveQty` は `TranHaibun` だけが源泉で、DELETE→再INSERTされた行の内容に依存しない。全件引き直しが常に正解で、範囲を絞る意味がない。
- **`HAVING SUM(Su) <> 0` と INSERT 併用の理由**: 引当が0になったキーに0行を作らず、逆に在庫実績が無いSKUへの引当では行を新規作成する（有効在庫がマイナスで見える）。0クリアと `INSERT ... ON CONFLICT` の2文構成で、引当が消えたキーも確実に0になる。
- **`PartialUpdateDeniedColumns` を変更しなかった理由**: 倉庫・SKU・`Su`・`DenDay` は既に禁止列で、部分更新で変わり得るのは `EndFlag` だけ。禁止列に足すと消込画面と同じ EndFlag 部分更新の経路が塞がるため、ハンドラ側でフックした。対象Idは検証済みの `long` なので `Id IN (...)` へ直接埋め込み、1クエリでまとめて読む。
- **`BulkInsert` でキーを `HashSet` に貯めてループ後に1回だけ計算する理由**: 配分は `ShopHaibunInputViewModel` 等が数百件を一括登録するため、行ごとに引き直すとSQL発行数が行数に比例する。
- **引当対象を全 `EnumHaibun` にした理由**: ユーザー指示が `EndFlag` だけでの判定。ドラフト 2.8-2 は `Hatsukai`（入荷前の割付）除外案も併記していたが、除外が必要なら `Kubun <> 0` を1箇所足すだけで対応できる。
### 副次的に見つけた不具合の修正
- `CvDomainLogic/SummaryDb.cs` の `CalcSummaryRealStock()` が常に SQLite 構文エラーになっていた。`deleteSql`（`DELETE FROM SummaryRealStock`）に文の区切り `;` が無く、後続の `Insert Into` と1コマンドで連結していたため。テストが無く露呈していなかったが、今回追加した全件Rebuildのテストで発覚したので `;` を追加した。**現在庫の全件再集計（`Msg051_SummaryRealStock`）は今まで動作していなかった**ことになる。
### 実施内容（ドキュメント）
- `Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md`: 2.8.1（決定と実装）を新設。2.2、4章の2と9、4.1の移行表、承認対象の推奨案一覧（2.8-1〜3を「済」）、6章の追記を更新。
- `Doc/spec/2026-08-12_CV10機能完成度チェックリスト.md`: 受注残管理表の残課題に、引当数列は追加済みだが未利用である旨を追記。
### 影響範囲
- `CvBase/BaseDb2Trans.cs`、`CvBase/BaseDb3Summary.cs`、`CvBase/BaseDbHaibun.cs`、`CvBase/UpdateDb.cs`
- `CvDomainLogic/SummaryDb.cs`、`CvServer/Services/HandlerClass.cs`、`Tests/TestServer/SummaryDbTests.cs`
- `Doc/spec/` の2ファイル
- 既存の在庫計算（`Su` / `InQty` / `OutQty` / `TransitQty`）と掛集計のロジックは変更していない。`ReserveQty` は独立した新規列である。
- 移行時は `TranHaibun` 0件のため全キー `ReserveQty = 0`。`ALTER TABLE ADD COLUMN` は定数既定値なので `SummaryStock` 349万行 / `SummaryRealStock` 150万行でもメタデータ変更のみ。
### 検証
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`: 成功（警告0、エラー0）。
- `C:\gitroot\UT\vscmd.bat dotnet run --project Tests\TestServer\TestServer.csproj`: 合計51 / 成功51 / 失敗0（新規6件）。
- 追加したテスト: 追加・削除での増減と実在庫非干渉、`EndFlag` 0→1→0 の解除と復帰、倉庫・年月変更時のキー移動、通常更新値と `CalcReserveQtyAll()` の一致＋現在庫の全月合算、月次Rebuild後の引当数保持、全件Rebuild後の引当数復元。
- 既存テストの `GetStockSnapshot()` に `ReserveQty` を追加し、Rebuild冪等性の比較対象に含めた。
- `PrepareStockTables()` へ `TranHaibun` 作成を追加。`CalcSummaryRealStockRange_...` は本番と同じユニークインデックスが必要になったため `PrepareStockTables()` 利用へ変更した（`ON CONFLICT` は対象がユニーク制約でないと prepare 時にエラーになる）。
- `git diff --check`: 成功。変更9ファイルすべて UTF-8(BOMなし) + CRLF を確認。
### 残課題
- **実データでの実機検証は未実施**。`TranHaibun` が0件で配分画面から実データを作っていないため、今回はテストのみ。配分入力画面（`ShopHaibunInput` / `HachuHaibunInput`）で実際に登録・削除して引当数が動くことは、配分機能に着手する時点で確認する。
- `EndFlag` を操作する画面が無い。振り分け後の入庫を記録する画面（または入庫処理からの自動更新）は未実装で、現状は引当が解除されない。
- 有効在庫（`Su - ReserveQty`）を表示する画面は未実装。列を用意するところまで。
- ドラフト 2.8-4（調整数の保持先）は未決のまま。3章の調整専用伝票と併せて判断する。
- `CalcReserveQtyAll()` の0クリアが `SummaryStock` 349万行のフルスキャンになる。夜間バッチ前提で許容したが、問題化する場合は `ReserveQty <> 0` の部分インデックスを検討する（`KeyDml` が部分インデックス非対応）。

---
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
## [2026-08-15] 取引区分 enum を業務別に分離し一覧表示名を付与
### Agent
- Codex : GPT-5 : Sekiya Sato Codex(home)
### 目的
- 取引区分 enum の `Comment` を WPF の一覧選択名と対応させ、受注・発注で仕入・売上用 enum を共用していた状態を解消する。
### 実施内容
- `CvBase/BaseDb2Trans.cs`: `EnumUri01` と `EnumShiire` へ `Comment` を付与し、受注用 `EnumJuchu` と発注用 `EnumHachu` を追加した。`Tran12Jyuchu.EnKubun` と `Tran13Hachu.EnKubun` をそれぞれ専用 enum へ変更した。
- `CvBase/BaseDbJodai.cs` と `CvBase/BaseDbHaibun.cs`: 画面・業務名称に対応する各 enum 値へ `Comment` を付与した。
- `CvWpfclient`: 受注入力、発注入力、発注配分入力の選択肢・既定値・表示変換を専用 enum へ変更した。
### 互換性
- `Kubun` の物理型・値（10 / 20 / 30 / 99）および `CalcFlag` 判定は不変のため、既存 DB・JSON・帳票 SQL の移行は不要。
### 検証
- `CvBase`、`CvWpfclient`、`Tests/TestServer` のビルドと `git diff --check` を実施する。
