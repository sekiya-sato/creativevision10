## [2026-07-28] 09:48 MainMenuView 気温グラフへの降水量バー追加
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：MainMenuView の気温グラフに、視認性を保った降水量の棒グラフを追加する
### 実施内容
- `CodeShare/IWeather.cs`、`CvServer/Services/WeatherService.cs`: OpenWeatherMap の3時間降水量を共有DTOへ追加し、未提供時は0 mmとして扱うようにした
- `CvWpfclient/ViewModels/MainMenuViewModel.cs`: 降水量を予報チャート用データへ引き渡すようにした
- `CvWpfclient/Views/MainMenuView.xaml(.cs)`: 半透明・幅制限付きの降水量バー、凡例、ホバー時の降水量表示を追加した
- `CvWpfclient/Resources/UIColors*.xaml`: ライト/ダークテーマ用の降水量バー色を追加した
### 技術決定 Why
- 降水量は気温と尺度が異なるため、独立した最大値で棒の高さを正規化し、温度の面・折れ線より背面に描画して温度推移の可読性を維持した
### 確認
- `vscmdcodex.bat dotnet build CvServer/CvServer.csproj --no-restore -p:BaseOutputPath=...`: 警告0、エラー0
- `vscmdcodex.bat dotnet build CvWpfclient/CvWpfclient.csproj --no-restore -p:BaseOutputPath=...`: 警告0、エラー0
- MainMenuView.xaml と色リソースのXML解析、CRLF、`git diff --check`: 問題なし

---

## [2026-07-27] 14:02 一覧行移動時の他端末更新検知と再取得案内
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：MasterMenteなどで一覧表示後に他端末更新された行へ移動した場合、理解可能なエラーを表示して一覧再取得を促す。
### 実施内容
- `CodeShare/ICoreService.cs`: 楽観排他と想定外エラーの共通メッセージコードを追加。
- `CvBase/Parameters.cs`: `QueryByIdParam` に一覧取得時点の `ExpectedVdu` を追加し、未指定時は従来どおり照合しない互換動作を維持。
- `CvServer/Services/HandlerClass.cs`: 詳細取得時に `ExpectedVdu` とDBの `Vdu` を照合し、不一致時は楽観排他エラーを返すよう変更。
- `CvWpfclient/Helpers/ViewModels/BaseLightMenteViewModel.cs`: 軽量一覧の詳細キャッシュによる確定を廃止し、行移動ごとに `Vdu` を照合。スクロール中の古い応答は現在選択行と一致する場合だけ処理する。
- `CvWpfclient/Helpers/ViewModels/BaseMenteViewModel.cs`: 楽観排他時に一覧・詳細を破棄し、［一覧取得（F5）］での再取得を促す共通メッセージを追加。
- `Tests/TestServer/TestServer.cs`: `QueryByIdParam` の古い `Vdu` が競合エラーとなり、現在の `Vdu` は正常取得できる回帰テストを追加。
### 技術決定 Why
- 詳細キャッシュだけでは一覧取得後の他端末更新を検知できず、保存時まで古い内容を保持する。詳細取得に一覧時点の `Vdu` を渡してサーバーで照合することで、最新内容の暗黙上書きを避けつつ、全軽量メンテ画面へ一貫して適用する。
### 影響範囲
- `BaseLightMenteViewModel` を継承するマスターメンテ、システムログイン、伝票入力画面の一覧行移動時の詳細取得。
- 共通CRUDの更新・削除時に既存の楽観排他エラーが返った場合も、同じ再取得案内を表示する。
### 確認
- `Tests/TestServer/bin/Debug/net10.0/TestServer.dll`: 合計33件、成功33件、失敗0件。
- `vscmdcodex.bat dotnet build CvWpfclient/CvWpfclient.csproj --no-restore`: 成功（0警告0エラー）。
- 変更6ファイルとログのCRLFを確認し、`git diff --check` を通過。

---

## [2026-07-27] 14:35 V*列一括再同期の性能改善と実行時間表示（Phase8）
### Agent
- Claude Opus 5 : Anthropic
### Editor
- ClaudeCode
### 目的
- ユーザーからの要望：Phase6の実機確認で「1回目2332件・2回目0件」で正常動作を確認したが所要約20分だったため短縮を検討する。あわせて実行結果メッセージに開始時間・終了時間を表示する。
### 実施内容
- `CvDomainLogic/MasterCascadeDb.cs`: 全走査回数を削減。(1) `ResyncVRulesByTable` を新設しV*列22ルールを対象テーブル単位の1文へ統合（22回→10回、MasterShohinは8回→1回）。(2) `ResyncJsub` でJsubのCd/MeiとKbnameを1文へ統合（10回→5回）。(3) `ResyncJcolsiz` は対象Idを1回だけ抽出しUPDATEをId指定に変更（2回→1回）。(4) `RebuildDerivedShohinColSiz` をIN句(500件単位)での一括Delete+Insertに変更。(5) `RunResync` にStopwatchを入れルール単位の所要時間を必ずログ出力。
- `CvServer/Services/HandlerClass.cs`: `BuildResyncSummary` を追加し、応答メッセージに更新行数・開始時刻・終了時刻・所要時間（サーバ側実測）を含めるよう変更。
- `CvWpfclient/ViewModels/00System/SysExecMiscViewModel.cs`: 実行開始時点で開始時刻を先に表示（数分かかるため）。
- `Tests/TestServer/`: 件数の意味変更に合わせて期待値を修正し、応答メッセージに開始/終了/所要が含まれることを検証するアサーションを追加。
### 技術決定 Why
- 原因はインデックス不足ではなく「同じテーブルを列ごとに何度も全走査する」コード構造だった。`DerivedShohinColSiz.Id_Shohin` には既に `KeyDml("n1")` のインデックスがあることを確認済み。MasterShohin は Jgenka/Jcolsiz/Jgrade/Jsub を含む幅の広い行のため、8回読むか1回読むかで読み取りI/Oが大きく変わる。
- 全件再同期は本質的に全走査が必要なので `Id_*` 列へのインデックス追加では速くならない。走査回数自体を減らす方針を採った（合計約35回→約16回）。
- 統合版では参照先が見つからない場合に `ifnull(サブクエリ, 現在値)` で現在値を残すようにし、dangling参照の行の旧名称を温存する従来動作を維持した。
- V*列をテーブル単位に統合した結果、更新行数の意味が「列ごとの合計」から「更新された行数」に変わる。1行で3列が古い場合に従来3と数えていたものが1になるため、Phase6で観測した2332より小さくなるのは正常。テストの期待値もこの意味に合わせて修正した。
- ルール単位の所要時間を常にログ出力することにした。残るボトルネックを推測で追わず、次回の実行結果から実データで特定できるようにするため（出力先は logs/server.log）。
### 影響範囲
- 再同期処理の内部SQLのみ。伝播の結果（どの列がどう更新されるか）は変わらないため、マスタ改名時の動作には影響しない。
- 応答メッセージが複数行になる。画面の実行結果欄は TextWrapping="Wrap" で複数行表示に対応済み。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（0警告0エラー）。
- `Tests/TestServer/bin/Debug/net10.0/TestServer.exe`: 合計32 / 成功32 / 失敗0。冪等性・順序保持・dangling参照の温存が引き続き緑であることを確認。
- 実DB(9.5GB)での短縮効果はユーザー側の次回実行で確認する。ルール単位の所要時間がログに出るため、残るボトルネックはそこで特定できる。

---
## [2026-07-27] 14:10 V*列の扱いをAGENTS.mdとテーブル定義に明記（AI向け恒久メモ）
### Agent
- Claude Opus 5 : Anthropic
### Editor
- ClaudeCode
### 目的
- ユーザーからの要望：Master系とTran系でのV*列の扱いについて、今後もAIが誤解しないようソースに明記する。
### 実施内容
- `AGENTS.md`: 「Data Model: V*列 (CodeNameView) **IMPORTANT**」節を Build Rule の前に追加。Tran系＝時点値の監査値で伝播禁止、Master系＝常に現行値でMasterCascadeDbが伝播、JSON内スナップショット(Jsub/Jcolsiz/KubunName/Derived)も伝播対象、V*列追加時はVRules登録が必須、json_valid ガードの必要性、Msg047による一括再同期、設計書の参照先を記載。
- `CvBase/DefineDataTable.cs`: テーブル種別（Sys/Master/Tran/Summary/Derived）を説明している既存コメントブロックに、V*列の扱いがこのテーブル種別で決まることを追記。Summary系はV*列を持たずJOIN前提であることも明記。
### 技術決定 Why
- 記載先を AGENTS.md にした。全AIエージェント（OpenCode/Codex/Copilot/ClaudeCode）が最初に読む唯一の共通指示ファイルであり、クラスコメントだけでは該当ファイルを開かない限り気付けないため。逆に各Master系テーブルに個別に書くのは冗長で更新漏れを生むため避けた。
- `DefineDataTable.cs` を2つ目の記載先にしたのは、既にテーブル種別の分類を説明しているコメントがあり、V*列の意味論がその分類に従うという構造をそのまま接続できるため。テーブル一覧を見に来たAIが必ず通る場所でもある。
- Phase7で追記した `TranAllHeader` と `CodeNameView` のXMLコメントと合わせ、(1)AI共通指示、(2)テーブル分類、(3)Tran系基底クラス、(4)V*列の型定義 の4箇所で同じ方針に到達できる状態にした。
### 影響範囲
- ドキュメント・コメントのみ。動作への影響はない。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（0警告0エラー）。
- `Tests/TestServer/bin/Debug/net10.0/TestServer.exe`: 合計32 / 成功32 / 失敗0。

---
## [2026-07-27] 14:00 V*列の意味論をコード上に明文化（Phase7: 仕様コメント整備）
### Agent
- Claude Opus 5 : Anthropic
### Editor
- ClaudeCode
### 目的
- ユーザーからの要望：`.omo/20260727_master_vcolumn_sync_design.md` のPhase7を実施。「V*列が伝票の時点値かマスタの現行値か」をコードを読むだけで判別できるようにする。実機確認（Phase6）はユーザー側で実施するため、その担当区分も設計書に明記する。
### 実施内容
- `CvBase/BaseDb2Trans.cs`: `TranAllHeader` のXMLコメントに、V*列は伝票作成時点の名称を保持する監査値でありマスタ改名時に伝播しないこと、現行名称が必要ならId_*からJOINすること、Master系は逆に常に現行名称へ同期されることを明記。
- `CvBase/Share/BaseDbDefinition.cs`: `CodeNameView` のXMLコメントに同趣旨を追記。あわせて「Master系にV*列を追加した場合は MasterCascadeDb.VRules への登録も必須（未登録は VRules_CoverAllMasterVColumns が検出する）」を明記。
- `CvWpfclient/ViewModels/05Shiire/ShiireSlipPrintViewModel.cs`: 内側SQLの直前に、名称はV*列（時点値）・住所はマスタJOIN（現行値）で取得元が異なるのは意図的な仕様であることを明記。未使用だった `soName` エイリアスを削除。
- `.omo/20260727_master_vcolumn_sync_design.md`: Phase6を「ユーザー側で実施（AI作業対象外）」と明記し、DDLはUpdateDb.versionsで自動適用されること・SysUpdateDbのMemoで結果確認できること・M4（Tran系が伝播しないこと）が最重要であることを追記。Phase7完了とチェックリストの消化状況も反映。
### 技術決定 Why
- 方針の記載先を `CodeNameView` にも置いた。V*列を新設する開発者が最初に触る型であり、ここに「VRulesへの登録が必要」と書いておかないと伝播漏れが起きる（テストで検出はされるが、意図を知らずに落ちるとテストが誤りだと判断されかねない）。
- `ShiireSlipPrintViewModel` の未使用 `soName` は残さず削除した。参照0件を確認済み。「倉庫名も現行値で取れるのに使っていない」という誤読を招き、時点値を使うという意図が伝わらなくなるため。
- Tran系の変更はXMLコメントのみに留めた。列定義・SQL・入力ViewModel・qfmには一切手を入れていない（方針1のとおり現状維持）。
### 影響範囲
- コメントとSQL内の未使用エイリアス削除のみ。動作への影響はない。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（0警告0エラー）。
- `Tests/TestServer/bin/Debug/net10.0/TestServer.exe`: 合計32 / 成功32 / 失敗0。
- `soName` の参照が0件であることを grep で確認してから削除。

---
## [2026-07-27] 13:40 V*列の一括再同期をgRPCと画面に公開（Phase5: Msg047）
### Agent
- Claude Opus 5 : Anthropic
### Editor
- ClaudeCode
### 目的
- ユーザーからの要望：`.omo/20260727_master_vcolumn_sync_design.md` のPhase5を実施。マスタ改名時の伝播（Phase3で自動化済み）とは別に、DB変換後や取りこぼしを修復するための一括再同期を管理者が実行できるようにする。
### 実施内容
- `CodeShare/ICoreService.cs`: `CvFlag.Msg047_MasterVColumnResync = 47` を追加（欠番だった47を使用）。
- `CvServer/Services/QueryMsgService.cs`: `_handlers` に Msg047 を登録。
- `CvServer/Services/HandlerClass.cs`: `HandleMasterVColumnResync` を追加。Serializableトランザクション内で `MasterCascadeDb.ResyncAll(errors)` を実行し、更新行数を応答に返す。
- `CvWpfclient/ViewModels/00System/SysExecMiscViewModel.cs`: `MasterVColumnResyncAsync` コマンドを追加（既存の商品名称再構築と同型）。
- `CvWpfclient/Views/00System/SysExecMiscView.xaml`: 「V*列再同期」ボタンを WrapPanel に追加。
- `Tests/TestServer/TestServer.cs`: Msg047 の結合テストを追加。古いVBrand・空文字のVSoko・古いJsub.Meiを作ってから実行し、すべて現行値になること、2回目が「更新行数=0」になることを検証。
### 技術決定 Why
- 一部ルールだけ失敗した場合は成功扱いにせず `Code < 0` で返すことにした。`ResyncAll` はバッチとして他ルールの処理を継続するため、成功で返すと「更新0件＝既に同期済み」と「更新0件＝全ルール失敗」を利用者が区別できず、修復したつもりで放置される危険があるため。失敗内容は Option に列挙する。
- 応答の DataMsg は「更新行数=N」の文字列とした。既存の Msg046 はサーバのバージョン情報（InfoServer）を返しているが処理結果と無関係なため踏襲しなかった。
- PackIcon の `Kind="DatabaseSync"` は MaterialDesignThemes 5.3.2 のアセンブリ内に実在することを確認して採用した（XAMLのenum値は誤りでもビルドが通り実行時に落ちるため）。
### 影響範囲
- 管理者用システム処理画面にボタンが1つ増える。既存機能への影響はない。
- 再同期はV*列22件＋Jsub5テーブル×2種＋KubunName＋Jcolsizを1文ずつ更新し、Jcolsizに変更があった商品はDerivedShohinColSizを再構築する。全件走査となるため実行時間はデータ量に比例する。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（0警告0エラー）。
- `Tests/TestServer/bin/Debug/net10.0/TestServer.exe`: 合計32 / 成功32 / 失敗0。
- `check-xaml` 手順で SysExecMiscView.xaml を検証：構文・名前空間・リソース参照・コンバーター・バインディングパスすべて問題なし。

---
## [2026-07-27] 13:15 JSON内の名称スナップショットの伝播を実装（Phase4: Jsub/Jcolsiz/区分名/Derived）
### Agent
- Claude Opus 5 : Anthropic
### Editor
- ClaudeCode
### 目的
- ユーザーからの要望：`.omo/20260727_master_vcolumn_sync_design.md` のPhase4を実施。V*列に加えて、JSON列に埋め込まれた名称スナップショット（Jsub・Jcolsiz・区分名）と、そこから導出される DerivedShohinColSiz もマスタ改名時に同期する。
### 実施内容
- `CvDomainLogic/MasterCascadeDb.cs`: `CascadeJsonRule` と `JsubRules`（5テーブル）を追加。R2=Jsub内のCd/Mei、R3=区分名（MasterMeisho.KubunNameとJsubのKbname）、R4=MasterShohin.Jcolsizの色/サイズ名称、R5=DerivedShohinColSizの再構築を実装。`CascadeFromMaster` に `kubun`/`oldCode` の任意引数を追加。`ResyncAll` にもJSON系の全件再同期を追加し、ルール単位のエラー記録を `RunResync` に共通化。
- `CvServer/Services/HandlerClass.cs`: フックから `(item as MasterMeisho)?.Kubun` と `(org as IBaseCodeName)?.Code` を渡すよう変更。
- `CvDomainLogic/RebuildDb.cs`: `WHERE EXISTS (-` の構文エラーを修正（`RebuildMasterShohin2Meisho` の後半、Id_Siz補完が実行時に必ず失敗していた既存不具合）。
- `Tests/TestServer/MasterCascadeDbTests.cs`: 8件追加（Jsubの順序保持、5テーブル網羅、Jcolsiz＋Derived、区分名、区分コード変更時のスキップ、不正JSON混在への耐性、ResyncAllのJSON系）。
### 技術決定 Why
- Jsubの `Kb`/`Kbname` の意味を実コードで確定させた。`Kb`=`MasterMeisho.Kubun`、`Kbname`=`Kubun='IDX'` かつ `Code=Kb` の行の `Name`（`MasterShohinMenteViewModel.DoGetKubun` が `Kubun='IDX' and Code between 'B01' and 'B10'` を取得し、`MasterGeneralMeisho.OnKbChanged` がその Name を Kbname にセットしている）。
- `MasterMeisho.KubunName` の伝播から `Kubun='IDX'` の行自身を除外した。区分定義行の KubunName を IDX/IDX 行の Name で上書きすると意図しない値になる（初期データではIDX行のKubunName='名称区分'、IDX/IDX行のName='名称区分インデックス'）。ResyncAllでも同じ除外を適用し既存データを壊さないようにした。
- 区分コード自体が変更された場合は区分名を伝播せず警告ログのみとした。Kubun/SizeKu/Kb の参照先が失われる区分体系の変更であり、伝播では解決できないため。
- `DerivedShohinColSiz` は個別UPDATEを書かず `DeleteSql`→`InsertSql` で再構築した。当テーブルは Jcolsiz からの完全導出であり、導出定義を二重管理しないため（HandlerDerivedと同じ手順）。再構築対象の商品Idは差分条件で抽出するため、R4実行前に確定させている。
- `ResyncAll` のJSON系は MasterMeisho を left join する集合演算1文で実装した（当初案の「参照Idを列挙して1件ずつ」は件数が多いと実用時間に収まらないため）。
- JSON配列を扱う全SQLに `列 is not null and json_valid(列)` を付けた。`json_each` は不正JSONで例外を投げるため、Phase2のV*列と同じ耐性を持たせた。
- Phase2で `ResyncVRule` に書いた「SQLiteのUPDATEは対象テーブルに別名を付けられない」というコメントは誤りだったので訂正した。SQLiteの qualified-table-name は AS alias を許容し、`RebuildDb.cs:53` も本番で `UPDATE MasterShohin AS S` を使っている。
### 影響範囲
- MasterMeisho の改名時に、V*列に加えて Jsub（5テーブル）・Jcolsiz・区分名・DerivedShohinColSiz が更新される。DerivedShohinColSiz の再構築は該当商品ごとに Delete+Insert となるため、多数の商品が参照する色/サイズの改名では処理時間が伸びる（改名は低頻度のため許容）。
- Tran系には差分なし（伝票の時点名称は従来どおり保持）。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（0警告0エラー）。
- `Tests/TestServer/bin/Debug/net10.0/TestServer.exe`: 合計31 / 成功31 / 失敗0。

---
## [2026-07-27] 12:45 マスタ更新時のV*列伝播をサーバへ組み込み（Phase3: HandleUpdateフック）
### Agent
- Claude Opus 5 : Anthropic
### Editor
- ClaudeCode
### 目的
- ユーザーからの要望：`.omo/20260727_master_vcolumn_sync_design.md` のPhase3を実施。Phase2で作成した伝播ロジックを、マスタ更新の実経路（CoreServiceのHandleUpdate）から呼び出すようにする。
### 実施内容
- `CvServer/Services/HandlerClass.cs`: `HandleUpdate` の `_db.Update(item)` 直後、`CompleteTransaction` の前にV*列伝播フックを追加。マスタ更新と同一トランザクション・同一 `vdate` で `MasterCascadeDb.CascadeFromMaster` を呼ぶ。`HandleInsert`/`HandleBulkInsert`/`HandleDelete`/`HandleDeleteById` は変更していない。
- `CvDomainLogic/MasterCascadeDb.cs`: 伝播要否の判定を `NeedsCascade(itemType, newItem, orgItem)` として追加。伝播元マスタ4型かつCode/Nameが変化した場合のみtrueを返す。
- `Tests/TestServer/TestServer.cs`: `CoreServiceTests` にgRPC経路の結合テストを追加。`Msg201_Op_Execute`＋`UpdateParam` で名称マスタを改名し、参照している商品マスタの `VBrand` が現行名称になること、および略称のみの変更では参照側の `Vdu` が動かないことを検証。
- `Tests/TestServer/MasterCascadeDbTests.cs`: `NeedsCascade` の判定、`HandleUpdate` と同順序を再現した自己参照時の `Vdu` 整合、`vdate` 未指定時の挙動の3件を追加。
### 技術決定 Why
- 判定条件をインラインに書かず `NeedsCascade` として切り出した。フックの配線ミス（条件式の誤りで伝播が走らない/常に走る）を単体テストで検出できるようにするため。既存の `ITranSoko` 判定はインラインだが、こちらは条件が3項あり誤りやすい。
- `CascadeFromMaster` へ `HandleUpdate` の `vdate` をそのまま渡した。請求先が自社（`MasterTokui.Id_Paysaki` が自分自身）のケースでは更新元の行自身が伝播対象になるため、伝播側で別採番するとクライアントへ返す `Vdu` とDB上の値がずれ、同一画面からの次回保存が楽観排他（-9901）で弾かれる。
- 伝播をトランザクション内に置き、SQLエラーは送出させた。マスタ更新だけが成功してV*列が古いまま残る状態を作らないため（`HandleUpdate` の既存 catch が `AbortTransaction` する）。
- 実機確認の代替として、既存の `CoreServiceTests` ハーネス（インメモリSQLite＋実 `CoreService`）で gRPC 経路そのものを検証した。加えてフックを一時的に無効化して当該テストのみが失敗することを確認し、テストが空振りしていないことを実証した。
### 影響範囲
- マスタ（MasterMeisho/MasterTokui/MasterShain/MasterShiire）の更新時に、参照側22箇所のV*列へ最大22本のUPDATEが追加で流れる。Code/Nameが変わらない更新では1本も流れない。
- Tran系のV*列は対象外のため、伝票の時点名称は従来どおり保持される（差分なし）。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（0警告0エラー）。
- `Tests/TestServer/bin/Debug/net10.0/TestServer.exe`: 合計23 / 成功23 / 失敗0。
- フック無効化時: 合計23 / 失敗1（`Update_MasterMeishoRename_CascadesToReferencingMasterVColumn` のみ）→ 復旧後に再度全緑を確認。

---
## [2026-07-27] 12:25 Master系V*列の変更時同期ロジック新設（Phase2: V*列伝播）
### Agent
- Claude Opus 5 : Anthropic
### Editor
- ClaudeCode
### 目的
- ユーザーからの要望：Tran系のV*列は伝票の時点名称として物理列を維持し、Master系のV*列は速度と処理の単純化のため物理列を維持したうえで、参照元マスタ（MasterMeisho等）のCode/Name変更時に同期するロジックを組み込む。`.omo/20260727_master_vcolumn_sync_design.md` のPhase2を実施。
### 実施内容
- `CvDomainLogic/MasterCascadeDb.cs`: 新規。Master系V*列の伝播定義 `CascadeVRule` を22件（唯一の正）定義し、`IsCascadeSource`（伝播元はMasterMeisho/MasterTokui/MasterShain/MasterShiireの4型）、`CascadeFromMaster`（マスタ改名時の伝播）、`ResyncAll` / `ResyncAll(List<string>)`（保守用の全件再同期）、`CountDanglingRefs`（参照先欠損の調査）を実装。JSON系（Jsub/Jcolsiz/Kbname/KubunName）はPhase4のToDoコメントとして明示。
- `Tests/TestServer/MasterCascadeDbTests.cs`: 新規。インメモリSQLiteで12件。伝播・冪等性・空V*列の修復・自己参照(VPaysaki)・型別分岐(MasterShiire)・dangling参照・定義マップとクラス定義の整合性検証（VRules_AreConsistentWithEntityDefinitions / VRules_CoverAllMasterVColumns）。
### 技術決定 Why
- 伝播はSQLのUPDATE文で実施（対象行をFetchしてループ更新しない）。MasterShohinは十万行規模になり得るため。差分がある行のみ更新する条件をWHEREに入れ冪等にした。
- `[ForeignKey]` 属性からの自動導出は行わず明示マップを唯一の正とした。`Id_Paysaki` は宣言型ごとに参照先が異なり（MasterTokui→MasterTokui、MasterShiire→MasterShiire）基底の属性では表現できないため。代わりにマップとクラス定義の齟齬・登録漏れを検出する単体テストを追加して腐りを防いだ。
- SQLiteの `json_extract` は不正JSONに対しNULLではなく `malformed JSON` 例外を投げるため、`case when json_valid(col) then col else '{}' end` で包んだ。`ALTER TABLE ADD COLUMN ... DEFAULT ''` 直後の空文字が1行でもあるとマスタ改名がロールバックする実害があり、テストで検出した。`OR` 条件に `json_valid()=0` を並べる形は評価順が保証されないため採らず、短絡評価が保証される `CASE` を使用。
- `CascadeFromMaster` に呼び出し側の `Vdu` 値を渡す引数を追加した。自己参照（請求先が自社）で更新元の行自身が伝播対象になり、内部で別途採番するとクライアントへ返す `Vdu` とDB上の値がずれて次回保存が楽観排他で弾かれるため。
- `ResyncAll` は例外を握り潰すと22ルール中の失敗が黙って飛ばされるため、失敗内容を返すオーバーロードを追加した（Phase5でエラー提示に使う）。
### 影響範囲
- 新規2ファイルのみ。既存ソースの変更なし。CvBase（Tran系含む）に差分なし。伝播の呼び出し（CvServer側フック）はPhase3で実施するため、本コミット時点では実行経路から呼ばれない。
- `.omo/20260727_master_vcolumn_sync_design.md` にPhase1・Phase2の完了と申し送り3点（vdate引き渡し・ResyncAllのエラー返却・json_validガード）、ビルド手順（vscmdclaude.bat、dotnet test不可）を反映（.omoはコミット対象外）。
### 確認
- `vscmdclaude.bat dotnet build creativevision10.slnx`: 成功（0警告0エラー）。
- `Tests/TestServer/bin/Debug/net10.0/TestServer.exe`: 合計19 / 成功19 / 失敗0（新規12＋既存SummaryDbTests7）。
- .NET 10 SDK + Microsoft.Testing.Platform では `dotnet test` が使用不可のため、ビルド後のexeを直接実行して確認。

---
## [2026-07-26] 09:13 Tran系V*列（マスタ重複保持）方式の比較検討メモ作成
### Agent
- Kimi K3 : Moonshot AI
### Editor
- OpenCode
### 目的
- ユーザーからの要望：TranAllHeader の Id_Soko/VSoko に代表されるTran系のマスタ重複保持について、(1)物理保持(現状)、(2)[ComputedColumn]+SQL再構成、(3)Idのみ保持+JOIN の3方式を比較し全体最適を検討する。計画のみで結果は .omo に保存。
### 実施内容
- `.omo/20260726_tran_vcolumn_comparison_plan.md`: 現状調査（V*列25個・書き込み3経路・読み込み3経路・伝播機構なし・印刷の名称/住所混在不整合）を基に3方式を多観点比較し、案2を条件付き推奨とする計画を新規作成。
### 技術決定 Why
- `[ComputedColumn]` のDDL除外インフラ（ExDatabase.cs:104）と MasterYosanBrand の json_object(JOIN)再構成パターンが既にリポジトリ内で確立済みのため、XAMLバインドとエンティティ形状を維持したまま正規化できる案2が移行コスト・一貫性の両面で最適と判断。案3はCodeNameView統一パターンを崩し改修量最大のため非推奨。スナップショット要件の有無はユーザー判断待ちとして [blocked] 明記。
### 確認
- 調査のみ（計画メモ作成）。ソース改修なしのためビルド確認は不要。メモはCRLF・UTF-8で作成済み。

---
## [2026-07-25] 13:49 システム管理マスタ帳票に標準倉庫を追加
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：システム管理マスタの印刷レイアウトにも標準倉庫を表示する。
### 実施内容
- `CvWpfclient/ViewModels/01Master/MasterSysKanriMenteViewModel.cs`: 印刷CSVの末尾へ標準倉庫のコード・名称を追加。
- `printform/MasterSysKanriMente.qfm`: 未使用のitem29・item30を標準倉庫のコード・名称へ割り当て、税率3の後に標準倉庫行を追加。後続の項目は1行下へ移動。
### 技術決定 Why
- 既存の未使用データ項目を再利用してCSV定義と帳票の対応を維持し、MasterSysmanの保存項目はId_Sokoのみという設計を変えずに印刷できるようにした。
### 確認
- `validate_qfm.py printform/MasterSysKanriMente.qfm`: 成功。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj --no-restore`: 成功（0警告、0エラー）。

---

## [2026-07-25] 13:42 システム管理マスタに標準倉庫選択を追加
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：MasterSysman に追加された Id_Soko を画面で選択・表示できるようにする。
### 実施内容
- `CvWpfclient/ViewModels/01Master/MasterSysKanriMenteViewModel.cs`: 標準倉庫のコード・名称を表示専用で保持し、再読込時に Id_Soko から倉庫を取得する処理と倉庫選択コマンドを追加。
- `CvWpfclient/Views/01Master/MasterSysKanriMenteView.xaml`: 標準倉庫Idの検索ボタン付き入力欄、およびコード・名称の表示欄を追加。
### 技術決定 Why
- MasterSysman には Id_Soko のみを保存し、コード・名称は ViewModel の表示専用状態として取得することで、保存データを増やさずに選択内容を判別可能にした。
### 確認
- `git diff --check`、XAML XML構文確認を実施。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj --no-restore`: 成功（0警告、0エラー）。

---

## [2026-07-24] 14:28 MainMenu 気温グラフの横軸ラベルを表示幅に応じて間引き
### Agent
- GPT-5 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：MainMenuView のグラフエリアが小さいとき、横軸の文字表示を数個おきに省略する。
### 実施内容
- `CvWpfclient/Views/MainMenuView.xaml.cs`: プロット幅と最小ラベル間隔から横軸ラベルの表示間隔を算出し、狭い表示領域ではラベルを間引くよう変更。最終時刻のラベルは常に表示する。
### 技術決定 Why
- 従来は予報点が36件を超えた場合だけ間引いていたため、点数が少なくてもグラフ幅が狭い場合にラベルが重なっていた。描画可能幅に基づく上限を併用し、ウィンドウサイズの変化にも追従させた。
### 確認
- `MainMenuView.xaml` のXML構文確認、`git diff --check` を実施。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj --no-restore -p:BaseOutputPath=C:\gitroot\new2022\cv10-codex\artifacts\mainmenu-chart-label-build`: 成功（0警告、0エラー）。

---

## [2026-07-24] 13:56 MainMenu の気温推移グラフをWPF標準描画へ置換
### Agent
- GPT-5.6 : OpenAI
### Editor
- Codex
### 目的
- ユーザーからの要望：SkiaSharp 4.x系への更新問題を解消するため、MainMenuのグラフを現行機能と外観を極力保ったWPF標準のCanvas/Polyline描画へ置換する。
### 実施内容
- `Directory.Packages.props`、`CvWpfclient/CvWpfclient.csproj`: `LiveChartsCore.SkiaSharpView.WPF` と `SkiaSharp.Views.WPF` のパッケージ参照を削除。
- `CvWpfclient/ViewModels/MainMenuViewModel.cs`: 時間別予報を描画専用モデルへ整理し、5℃単位の縦軸範囲を算出するよう変更。
- `CvWpfclient/Views/MainMenuView.xaml`、`CvWpfclient/Views/MainMenuView.xaml.cs`: Canvas、Polyline、Polygon、Ellipse、TextBlockで折れ線・塗りつぶし・目盛・ラベルを描画し、系列上の近傍ポイントにガイド線、強調マーカー、日時・気温のポップアップを表示。
### 技術決定 Why
- LiveChartsCoreとSkiaSharp.Views.WPFを同時に撤去し、WPF標準コントロールだけで既存のデータ取得、30分更新、テーマ切替、5℃目盛、平滑な線、ポイント表示、ツールチップ相当の操作を維持するため。
### 確認
- `MainMenuView.xaml` のXML構文、イベント接続、テーマリソース参照を確認。
- `LiveChartsCore`、`SkiaSharp` のソース・プロジェクト参照が残っていないことを確認。
- `C:\gitroot\UT\vscmd.bat dotnet build CvWpfclient\CvWpfclient.csproj -p:BaseOutputPath=C:\gitroot\new2022\cv10-codex\artifacts\canvas-chart-build`: 成功（0警告、0エラー）。

---
## [2026-07-24] 09:45 MasterSysKanriMenteView 入力欄の文字が表示されない不具合を修正
### Agent
- Claude Opus 4.8 : Anthropic
### Editor
- Claude Code
### 目的
- ユーザーからの要望：MasterSysKanriMenteView でTextBoxに値は存在するが表示されない状態を、この画面のみ修正する。
### 実施内容
- `CvWpfclient/Views/01Master/MasterSysKanriMenteView.xaml`: 入力TextBox 20箇所の Style を `FormTextBox`(MaterialDesignOutlinedTextBox) から `MaterialDesignTextBox`(下線スタイル) へ変更。
### 技術決定 Why
- 前コミット(84fc78e)で無効キー`MaterialDesignBody*`を`FormTextBox`へ置換したが、当画面は外部Label＋固定`Height="30"`のコンパクト構成で、Outlined(浮動ラベル前提)は30px内でテキスト描画領域がクリップされ、値はあるのに不可視になっていた。下線系`MaterialDesignTextBox`はコンパクト高でも文字が表示され、テーマ対応(ライト/ダーク)も維持。隣接のDatePicker/ComboBox(30px)とも高さ整合。
### 確認
- `dotnet build C:\gitroot\new2022\cv10-claude\CvWpfclient\CvWpfclient.csproj`: 成功（0警告/0エラー）。
- 実画面(MasterSysKanriMenteView)を一時フックで起動し`PrintWindow`でキャプチャ、会社名/住所/TEL/税率/事業者登録番号等の値が表示されることを目視確認。確認後フック削除。

---
## [2026-07-24] 09:15 CvWpfclient XAMLのレイアウト崩れ点検・修正と check-xaml-layout スキル作成
### Agent
- Claude Opus 4.8 : Anthropic
### Editor
- Claude Code
### 目的
- ユーザーからの要望：CvWpfclientプロジェクト全体のXAMLをチェックし、デザイン崩れ・レイアウト崩れ・余白不足・文字見切れを修正する。あわせて今回のチェックに有用なスキルを他スキルとフォーマットを合わせて作成し、実画面での目視確認も行う。
### 実施内容
- `.agents/skills/check-xaml-layout/SKILL.md`: 新規作成（視覚レイアウト崩れ検出・修正専用スキル。check-xamlは構文/リソース/バインディング、本スキルは見切れ・余白・崩れ・不統一を担当）。
- `.agents/skills/wpf-project-guide/SKILL.md`・`.agents/skills/check-xaml/SKILL.md`: 新スキルへの相互参照を追加。
- 9 View(HachuInput/JuchuInput/ShiireInput/ShopUriageInput/ShukkaUriageInput/StockInput/IdoInputOut/IdoInputSoku/InputBarcode): `AlternatingRowBackground="Beige"` → `{DynamicResource DataGridAlternatingRowBackgroundBrush}`（ダークテーマ対応、ZaikoQueryView準拠）。
- `Views/01Master/MasterSysKanriMenteView.xaml`: 無効スタイルキー `Style="{DynamicResource MaterialDesignBody*}"`(20箇所、素TextBox化していた)を`FormTextBox`へ／〒住所行の列重複(住所1/住所2)を解消／Mail欄の負マージン`-17`撤廃で右端はみ出し解消。
- `Views/05Shiire/ShiireSlipPrintView.xaml`・`Views/01Master/PrintMasterShainCardView.xaml`: 検索欄の固定色`Background="White"`除去（`MenteSearchTextBox`既定の`MaterialDesignPaper`に委譲。ライトはほぼ白で視認性維持、ダーク追従）。
- `Views/00System/SysAutoExecHistoryView.xaml`・`SysLoginHistoryView.xaml`: GridSplitterの固定色`DarkGray` → `{DynamicResource MaterialDesignDivider}`。
- Cd+Mei表示に`TextTrimming="CharacterEllipsis"`追加(計12箇所: JuchuInput3/ShopUriage4/ShukkaUriage3/StockInput2)。
- `Views/Sub/AutoExecHistoryParamMiniView.xaml`・`RangeParamMiniView.xaml`: 操作ボタン行に右下余白(`0,16,16,16`)。`Views/02Yosan/ShopBudgetReportView.xaml`: 注意書きに`TextWrapping="Wrap"`。
### 技術決定 Why
- 固定色(Beige/White/DarkGray)はダークテーマで破綻するため既存DynamicResource/共通スタイルに寄せた。`MaterialDesignBody*`は存在しないキーでDynamicResource解決に失敗し素の既定TextBoxになっていたため`FormTextBox`へ差し替え。長い名称の無音見切れは`TextTrimming`で省略表示化。
- `Background="White"`除去は直近コミット87d6597(白背景追加)の見た目を変更するが、`MenteSearchTextBox`が`MaterialDesignPaper`を既に持ちライトの視認性を保ちつつダークにも追従するため、テーマ的に妥当と判断。
### 影響範囲
- CvWpfclient/Views 配下の実体View 17ファイル＋スキル3ファイル。空`<Grid />`スタブ168ファイルは対象外。
### 確認
- `dotnet build C:\gitroot\new2022\cv10-claude\CvWpfclient\CvWpfclient.csproj`（絶対パス指定でcv10-claudeを明示ビルド）: 成功（0警告/0エラー）。
- 実画面確認: MainMenuViewModelに一時フックを入れMasterSysKanriMenteView/ShiireSlipPrintViewを起動、`PrintWindow`(PW_RENDERFULLCONTENT)でキャプチャし見切れ・余白・整列・テーマを目視確認。確認後フックは削除。
- `git diff --check`クリーン、変更ファイルはCRLF/UTF-8統一。

---
## [2026-07-23] 16:45 印刷ダイアログの入力スタイルをRangeInputParamView準拠に統一
### Agent
- kimi-k2.6 : OhMyOpenCode
### Editor
- OpenCode
### 目的
- ユーザーからの要望：印刷ダイアログのTextBoxをRangeInputParamViewのような枠付き（outlined）スタイルに変更。日付入力もDatePickerに変更。項目どうしの余白もRangeInputParamViewを参考に調整する。
### 実施内容
- `CvWpfclient/Views/05Shiire/ShiireSlipPrintView.xaml`: 
  - 仕入日をTextBoxからDatePicker（FormDatePickerスタイル、DateYmd8Converter）に変更
  - 伝票NO・手入力NOをMaterialDesignTextBoxからFormTextBox（outlined）に変更、HintAssist.Hint追加
  - 仕入先・倉庫のMenteSearchTextBoxからBackground="White"を削除しMargin="0,4"に統一
  - 区切り文字を"～"から"-"に変更、列幅140→120、Margin 24→16,12,16,12
  - 取引区分ComboBoxからBackground="White"を削除
- `CvWpfclient/ViewModels/05Shiire/ShiireSlipPrintViewModel.cs`: DenDayFrom/Toをstring型からDateTime?型に変更。BuildPrintSqlParam内の日付処理をDateTime?.Value.ToString("yyyyMMdd")に変更。
- `CvWpfclient/Views/01Master/PrintMasterShainCardView.xaml`: 社員Id・社員CodeのMenteSearchTextBoxからBackground="White"とHeight="36"を削除しMargin="0,4"に統一。区切り文字を"-"に変更、Margin 24→16,12,16,12。
- `CvWpfclient/Views/01Master/MasterPrintBarcodeView.xaml`: 商品CD・商品名のFormTextBoxからBackground="White"とHeight="55"を削除しMargin="0,4"に統一。Margin 24→16,12,16,12、列幅130→120。
### 技術決定 Why
- RangeInputParamViewではFormTextBox（MaterialDesignOutlinedTextBoxベース）を使用しており、枠線付きの一貫性のあるデザインになっている。印刷ダイアログも同じスタイルに統一することで、ユーザー体験の一貫性を向上させた。DatePickerに変更することで日付入力の使い勝手を改善。
### 確認
- `dotnet build CvWpfclient/CvWpfclient.csproj`: 成功（0警告 / 0エラー）。

---
## [2026-07-23] 16:11 印刷ダイアログ入力項目の背景色を白に統一
### Agent
- kimi-k2.6 : OhMyOpenCode
### Editor
- OpenCode
### 目的
- ユーザーからの要望：`CvWpfclient.Views._05Shiire.ShiireSlipPrintView` のTextBox/ComboBox入力項目の背景色を白（検索ボックスと同色）に変更する。他の印刷系ダイアログも同様にチェックする。
### 実施内容
- `CvWpfclient/Views/05Shiire/ShiireSlipPrintView.xaml`: 12 TextBox + 1 ComboBox に `Background="White"` を追加（MaterialDesignTextBox、MenteSearchTextBox、MaterialDesignComboBox スタイルの入力項目すべて）。
- `CvWpfclient/Views/01Master/PrintMasterShainCardView.xaml`: 4 TextBox に `Background="White"` を追加（MenteSearchTextBox スタイル）。
- `CvWpfclient/Views/01Master/MasterPrintBarcodeView.xaml`: 2 TextBox に `Background="White"` を追加（FormTextBox スタイル）。
- その他の印刷系ダイアログ（ShippingConfirmDetailPrintView、IdoSokuDetailBookPrintView、IdoDetailBookPrintView、HhtUnupdatedDataPrintView、NouhinBookPrintView、NouhinBookPrintCustomView）は空の `<Grid />` スタブのみで入力項目がないため、対象外。
### 技術決定 Why
- 印刷ダイアログのWindow背景は `AppCommonBackgroundBrush` (AntiqueWhite #FAEBD7) であり、`MaterialDesignTextBox` / `MenteSearchTextBox` / `FormTextBox` / `MaterialDesignComboBox` の既定背景は Transparent のため、入力欄がウィンドウ背景色と同化して視認性が低下していた。検索ボックスと同じ白色にすることで入力項目を明確に区別できるようにした。
### 確認
- `dotnet build CvWpfclient/CvWpfclient.csproj`: 成功（0警告 / 0エラー）。

---
## [2026-07-23] 06:50 仕入伝票印刷(ShiireSlipPrint)の View/ViewModel 作成と qfm 調整
### Agent
- Claude Opus 4.8 : Anthropic
### Editor
- Claude Code (Sekiya Sato Claude)
### 目的
- ユーザーからの要望：`CvWpfclient.Views._05Shiire.ShiireSlipPrintView` の作成。View/ViewModel と qfm(ShiireSlipPrint.qfm を一部修正)を、印刷系のプロジェクト標準(ShopBudgetReportView 等)に合わせて実装。実際の印字例(仕入返品伝票)に一致する SQL を生成し印刷ロジックへ渡す。
### 実施内容
- CvWpfclient/Views/05Shiire/ShiireSlipPrintView.xaml: スタブから ShopBudgetReport 準拠の印刷ダイアログへ実装。ColorZone ヘッダ + 印刷範囲(仕入日 / 仕入先 / 倉庫 / 伝票NO / 手入力NO の各範囲 + 取引区分コンボ) + 「印刷実行」ボタン。F6=DoOutputPdf / Esc=Exit。仕入先・倉庫は MenteSearchTextBox + SearchTextBoxAssist で選択。
- CvWpfclient/ViewModels/05Shiire/ShiireSlipPrintViewModel.cs: BaseViewModel 派生。範囲条件を ObservableProperty で保持し、SelectXxx コマンドで MasterShiire / MasterTokui(TenType=0) を選択。DoOutputPdf で QueryListSqlParam を組み、RunPrintPdfAsync("ShiireSlipPrint.qfm", …) で PDF 出力(ShopBudgetReport の印刷ヘルパを踏襲)。
  - SQL は Jmeisai を json_each で明細1行=CSV1行へ展開し、qfm の item1..item46 順(datasrc)に一致する 46 列を SELECT。仕入先 / 倉庫 / 自社(MasterSysman Id=1)の住所を LEFT JOIN。数量計 / 金額計 / 上代計は伝票単位の window 合計。消費税は「請求時一括」固定、総合計=金額合計。
- printform/ShiireSlipPrint.qfm: 一部修正のみ。page title「自社納品伝票」→「仕入伝票」。item4(予備コード)の decode 書式 `"("@")"` を空へ変更(データ無しで "()" を出さない)。Shift_JIS(cp932)維持。
### 技術決定 Why
- 実 DB(server-dev.db)の Tran03Shiire には Tax/Total 列が無く、SuTotal/KingakuTotal/JodaiTotal のみ存在。印字例も消費税「請求時一括」・総合計=金額合計であるため、欠番列で SQL が壊れるのを避けつつ印字例に一致させるべく、Tax/Total へ依存せず金額合計の window 合計と固定文字で再現した。
- 印刷データ供給は ShopBudgetReport / ShiireInput 明細印刷と同じ QueryListSqlParam(SELECT 列順=qfm item 順)。CSV はヘッダ無し・SELECT 列順で data.txt へ出力される(PrintPdfService / WriteDynamicCsv)ため、未参照 item(8,9,15…)も '' で列位置を維持。
- レガシー .omo の「伝票処理区分 / 印刷区分」は現行 Tran03Shiire に対応列が無いため入力条件からは除外。ただし印字例に合わせ、伝票上の「伝票処理区分」欄は "商品仕入" 固定表示、「取引区分」は Kubun ラベル表示とした。
### 確認
- dotnet build CvWpfclient/CvWpfclient.csproj: 成功(0警告 / 0エラー)。
- 生成 SQL を server-dev.db に対して実行し構文/列解決を検証(FieldCount=46=item 数、明細 JSON 展開・住所 JOIN・json_extract すべて解決)。Tran03Shiire は空のため rows=0。
- staged は対象3ファイルのみ、git diff --check クリーン、qfm は staged blob も cp932 で title「仕入伝票」を確認。

---
## [2026-07-22] 16:10 出荷売上/店舗売上入力に伝票サマリーカードを導入(Phase3後半)
### Agent
- Claude Opus 4.8 : Anthropic
### Editor
- Claude Code (Sekiya Sato Claude)
### 目的
- ユーザーからの要望：Input系最適化 Phase3の上代/下代系(出荷売上ShukkaUriage・店舗売上ShopUriage)へ、発注基準の標準要素を適用する。
### 実施内容
