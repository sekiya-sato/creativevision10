## [2026-08-18] I7 滞留・欠品の例外画面を実装
### Agent
- Opus 4.8 : Anthropic : Sekiya Sato Claude
### 目的
- 未適用課題 I7。確定済みなのに出荷処理されず放置された配分（滞留）を検出し、確定取消／強制完了で例外処理する。
### 仕様決定（2026-08-18、ユーザー確定 A〜E）
- A 滞留の定義: 確定日からの経過日数≥閾値（既定3日・画面可変）＋納品予定日超過も併記。
- B 例外操作: 確定取消（→未確定）と強制完了（全量欠品でEndFlag=1・引当解除）の2操作。
- C 欠品分の行き先: 再配分しない。出荷実績が減る＝受注残/発注残が残る形で自然に再配分対象になる。
- D 画面構成: `ShippingConfirmList`（purpose=滞留検出）を interactive 化。純帳票印刷版はfollow-up。
- E 権限: 倉庫/物流ロール、Mini-UAT対象。
### 詳細設計（実装前に作成）
- `Doc/spec/2026-08-18_I7_滞留・欠品例外_詳細設計.md` を新設。サーバは I2/I3 の既存経路を再利用し変更しない。
### 実施内容
- `ShippingConfirmListViewModel`: 空クラスを `BaseQueryViewModel` 派生へ置換。表示区分「滞留」/「欠品実績」。
  滞留=`EndFlag=0 AND KakuteiDay<>''` を経過日数・予定日超過つきで検出。欠品実績=`EndFlag=1 AND ShortSu>0` を read-only 照会。
  一覧は `TranHaibun` を取得し倉庫/出荷先/商品/色サイズをクライアント合成（出荷指示確定画面と同方式）。
- 例外操作（サーバ変更なし・既存 `Msg201_Op_Execute` 再利用）:
  確定取消=`ShippingCancelParam`、強制完了=`ShippingCreateParam`（実数量0→伝票を作らず `EndFlag=1`・引当解除、
  テスト済み `CreateShippingSlips_AllShortage` の挙動）。楽観排他競合は一覧破棄→再取得を促す。
- `ShippingConfirmListView.xaml` を実装。`MenuData` の「出荷指示一覧印刷」を「滞留・欠品例外(出荷指示一覧)」へ改名・addInfo更新。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`：成功（0 warnings / 0 errors）。
- クライアント閉じの変更のため `TestServer` 92件・`TestLogin` 7件は不変。確定取消・全量欠品完了・引当解除は
  `SummaryDbTests`（`CancelConfirm` / `CreateShippingSlips_AllShortage` / `ProcessShipping_*`）で検証済み。
### 完成度への影響
- 07Haibun の L0 が 14→13（滞留・欠品例外が L0→L3）。台帳 I7 を完了（純帳票印刷版は follow-up）。チェックリスト 3.6/16章を更新。

## [2026-08-18] H1-H4 納品予定日を追加し発注/受注入力・発注側納品予定照会を実装
### Agent
- Opus 4.8 : Anthropic : Sekiya Sato Claude
### 目的
- 未適用課題 H1-H4。発注・受注へ納品予定日を伝票単位で持たせ（決定 6.2）、入力と納期遅れ照会を可能にする。
### 詳細設計（実装前に作成）
- `Doc/spec/2026-08-18_H1-H4_納品予定日_詳細設計.md` を新設。
- 列名: ご指示の「NohinDay」は既存の `TranHaibun.NouhinDay` / `TranHoju.NouhinDay` に合わせ **`NouhinDay`** へ正規化（同義）。
- スコープ: H1（列＋発注/受注入力）と H2/H3（発注側の納品予定照会・納期遅れ）まで。帳票版納品予定表(H4)・受注側照会・
  残管理表への納期遅れ列・リードタイム自動計算(2.0)は follow-up。
### 実施内容
- スキーマ: `CvBase/BaseDb2Trans.cs` の `Tran13Hachu` / `Tran12Jyuchu` に `NouhinDay`（yyyyMMdd、空=未設定）を追加。
  `CvBase/UpdateDb.cs` に `26_08_18_01`（`ADD COLUMN NouhinDay TEXT NOT NULL DEFAULT ''`）を追加。
- 入力: `HachuInputView` / `JuchuInputView` のヘッダに納品予定日 DatePicker を追加。
  `HachuInputViewModel` / `JuchuInputViewModel` の `LightweightSelectColumns` へ `NouhinDay` を足し、修正保存で消えないようにした
  （`EndFlag` は保存後に再判定されるため軽量列に無いが、`NouhinDay` はユーザーデータなので必ず読む）。
- 納品予定照会（発注側）: `DeliveryScheduleInquiryViewModel` を空クラスから `BaseQueryViewModel` 派生へ置換。
  `QuerySqlListAsync<Tran13Hachu>` で納品予定日範囲・仕入先・未完了のみ・納期遅れのみで絞り、
  納期遅れ（`NouhinDay < 今日` かつ `EndFlag=0`）を遅延日数つきで表示。`DeliveryScheduleInquiryView.xaml` を実装。
- 在庫・掛・引当の集計SQLに影響しない列なのでサーバ挙動は不変。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`：成功（0 warnings / 0 errors）。
- `Tests\TestServer\bin\Debug\net10.0\TestServer.exe`：92/92 成功。`TestLogin`：7/7 成功（`DefineDataTable` が新列を作れることも確認）。
### 完成度への影響
- 03Hatchu の L0 が 2→1（納品予定照会が L0→L3）。台帳 H1-H4 は列追加＋入力＋発注側照会を実装（帳票・受注側照会は follow-up）。チェックリスト 3.2/3.3/15章を更新。

## [2026-08-18] F2 在庫強制調整入力を実装
### Agent
- Opus 4.8 : Anthropic : Sekiya Sato Claude
### 目的
- 未適用課題 F2 の在庫強制調整入力（登録）を実装。倉庫のSKUに調整数を入れて在庫を強制的に増減する。
### 詳細設計（実装前に作成）
- `Doc/spec/2026-08-18_F2_在庫強制調整入力_詳細設計.md` を新設。
- 前提確認: `StockForceInputViewModel` の旧コメント（「保存先が無い・案A/B」）は陳腐化。案A（`Tran61Chosei` 伝票）は
  実装済みで `ITranSoko`、汎用CRUD副作用(`WriteEffectRunner.CalcStock` が `is ITranSoko` で判定)が在庫へ反映するため
  **クライアントに閉じる**（サーバ・スキーマ・新規Msg不要）。
- スコープ: 入力（登録）を実装。取消（削除画面）・実績照会（帳票）・調整理由マスタ(`Id_Riyu`)は follow-up
  （`MasterMeisho` 調整理由区分が未定義のため、理由は当面メモで残す）。
### 実施内容
- `CvWpfclient/ViewModels/08Zaiko/StockForceInputViewModel.cs`: 空クラスを `BaseStockSheetInputViewModel<Tran61Chosei>`
  の派生へ置換。`LoadRowsAsync` は棚卸入力(一覧)と同じ（在庫SKU＋品番範囲指定時は在庫0SKUも）。
  `BuildDenpyo` は `EnKubun = EnumChosei.Kyosei`。理論在庫の初期流し込みはしない（調整数は0から入力）。
- `Views/08Zaiko/StockForceInputView.xaml`: 棚卸入力(一覧)Viewを土台に、棚番を外し「調整日/調整数/現在庫」に読み替え、
  「調整数を入力した行だけ登録・在庫へ即時反映・マイナスで減算」の注意書きにした。
- 登録で `SummaryRealStock.Su` と `SummaryStock.AdjustQty` が即時更新される（棚卸確定と同じ経路）。伝票なので全件Rebuildでも消えない。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`：成功（0 warnings / 0 errors）。
- クライアント閉じの変更のため `TestServer` 92件・`TestLogin` 7件は不変（回帰確認済み）。`Tran61Chosei` の在庫反映は `SummaryDbTests`（棚卸確定経路）で検証済み。
### 完成度への影響
- 08Zaiko の L0 が 1→0。在庫強制調整入力が L0→L3。台帳 F2 は入力を完了（取消・実績照会は follow-up）。チェックリスト 3.7/14章を更新。

## [2026-08-18] G0-4.3.1 完了済み伝票の編集時ワーニングを実装
### Agent
- Opus 4.8 : Anthropic : Sekiya Sato Claude
### 目的
- 未適用課題 G0-4.3.1 を実装。完了(`EndFlag=1`)は自動解除しない（4.3.1）ため、完了済みの発注・受注に紐付く
  仕入・出荷を編集しても残へ反映されない。利用者が気付けるよう保存後に警告する。
### 詳細設計（実装前に作成）
- `Doc/spec/2026-08-18_G0-4.3.1_完了済み伝票の編集時ワーニング_詳細設計.md` を新設。
- 方針: サーバ・スキーマ変更なし。保存成功後（`AfterInsert/Update/Delete`）にクライアントの読み取り(`Msg101_Op_Query`)で
  紐付く発注/受注の `EndFlag=1` を確認し、情報ダイアログを出す。保存はブロックしない。
### 実施内容
- `CvWpfclient/Helpers/ViewModels/BaseTranInputViewModel.cs`: `WarnIfLinkedZanCompletedAsync(zanType, relateNo1, ...)` を追加。
  `RelateNo1 > 0` のとき `SELECT * FROM {zanType} WHERE Id = @ AND EndFlag = 1` を投げ、1件でも返れば警告。読み取り失敗は握りつぶす。
- `ShiireInputViewModel`: `AfterInsert/Update/Delete` を override し `typeof(Tran13Hachu)`（発注）で呼ぶ。仕入返品(`HenpinInput`)も継承で同じ挙動。
- `ShukkaUriageInputViewModel`: 同様に `typeof(Tran12Jyuchu)`（受注）で呼ぶ。
- 同期の `AfterXxx` から `_ = WarnIf...Async(...)` の fire-and-forget で起動（保存は成功済みで警告表示の遅延・失敗は業務に影響しない）。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`：成功（0 warnings / 0 errors）。
- クライアント閉じの変更のため `TestServer` 92件・`TestLogin` 7件は不変（回帰確認済み）。
### 完成度への影響
- Lv判定は不変。台帳 G0-4.3.1 を完了、4章の「編集時ワーニングは未実装」を解消。チェックリスト 3.2/3.3/4章/13章を更新。

## [2026-08-18] I2/I3 出荷指示確定(商品/得意先)・出荷処理入力を実装し ShippingDb をサーバ接続
### Agent
- Opus 4.8 : Anthropic : Sekiya Sato Claude
### 目的
- 未適用課題 I2/I3 を実装し、配分→確定→出荷→引当解除の業務経路を画面から閉じる（台帳 実施順の提案 1）。
- 実装済み・テスト済みだった `ShippingDb` は gRPCハンドラ未接続だったため、まず接続する。
### 詳細設計（実装前に作成）
- `Doc/spec/2026-08-18_I2I3_出荷指示確定・出荷処理_詳細設計.md` を新設。業務モデル（未確定→確定→完了、確定取消）、
  サーバ設計（param型・ハンドラ・エラーコード・トランザクション）、クライアント設計（確定共通基底＋2サブクラス、出荷処理入力）、
  G/W/T、テスト計画を先に確定した。
- 実装中の判断: 実数量設定＋伝票作成を薄い `ShippingDb.ProcessShipping` に切り出し、CvDomainLogic単体でテスト可能にした
  （`CoreService` のDIを介さない）。楽観排他は先に全行検証して競合なら何も書かない（fail-fast）。
### 実施内容（サーバ）
- `CodeShare/ICoreService.cs`: `CvMsgErrorCode.ShippingUnavailable`(-9903) を追加。
- `CvBase/Parameters.cs`: `ShippingConfirmParam` / `ShippingCancelParam` / `ShippingCreateParam`(+`ShippingCreateRow`)、
  結果型 `ShippingConfirmResult` / `ShippingCancelResult` / `ShippingCreateResult`、割れSKUの `ShippingShortageDto` を追加。
- `CvDomainLogic/ShippingDb.cs`: `ProcessShipping` を追加（既存メソッドは不変）。
- `CvServer/Services/HandlerClass.cs`: `HandleOpExecute` に3分岐と `HandleShippingConfirm` / `HandleShippingCancel` /
  `HandleShippingCreate` を追加。いずれも Serializable トランザクションで `ShippingDb` を呼ぶ。新規Msgフラグは追加せず既存 `Msg201_Op_Execute` に相乗り。
### 実施内容（クライアント）
- `CvWpfclient/Helpers/ViewModels/BaseShippingConfirmViewModel.cs`（新規）＋ `ShippingConfirmShohinViewModel` /
  `ShippingConfirmTokuiViewModel`（並び順のみ差し替え）。未確定配分の確定/確定取消。有効在庫割れは割れSKUを一覧表示し1件も確定しない。
- `ShippingInputViewModel`（出荷処理入力）: 確定済み配分に実数量を入力（既定=指示数、欠品=指示数−実数量）→ 伝票作成＋引当解除。在庫計上日・入力社員を持つ。
- 一覧は照会画面と同じく既存DBマップ型で取得しクライアントで合成する（`Common.SerializeObject` は `Type` を AQN で送るため、
  クライアント専用POCOはサーバで解決できない）。3View（`ShippingConfirmShohin/Tokui/ShippingInput`）を残完了設定画面のXAMLを土台に実装。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`：成功（0 warnings / 0 errors）。
- `Tests\TestServer\bin\Debug\net10.0\TestServer.exe`：92/92 成功（`ProcessShipping` 新規3件）。`TestLogin`：7/7 成功。
- `SummaryDbTests` に `ProcessShipping` の全量／指示数クランプ／楽観排他競合の3テストを追加（既存の出荷テストヘルパを再利用）。
### 完成度への影響
- 07Haibun の L0 が 17→14、L3 が 3→6。対象画面数131は不変。チェックリスト 3.6/12章、台帳 I2/I3、詳細設計を更新。
- あわせて E8-g の訂正（第2段階で請求データを正とし、月末一括計算の消費税との差は手動の相殺データで金額調整）を台帳へ反映。

## [2026-08-18] I9 配分照会3画面（配分問合わせ／引当問合わせ／有効在庫問合わせ）を実装
### Agent
- Opus 4.8 : Anthropic : Sekiya Sato Claude
### 目的
- 未適用課題 I9 を実装し、旧CV.net【配分】9〜11に相当する商品別照会（read-only）3画面を閉じる。
- あわせて保留仕様 E10/E11/E7/E8-g のユーザー決定を課題台帳へ反映する。
### 判断すべき仕様と決定（ユーザー確定 2026-08-18）
- E10 請求書番号: 得意先Id＋年月日(yyyyMMdd)＋連番2桁でユニーク。再発行は連番で識別。
- E11 その他売上: 区分99を請求一覧用に分離集計（売上本体の畳み込みは維持）。
- E7 親子締日: 不一致なら警告＋「マスタ変更・請求再計算が必要」メッセージ（ブロックしない）。
- E8-g 月末以外の締日の消費税: 仮計算として扱い、第2段階で本計算を再設計。
- 上記は請求・支払計算トラックの前提で、本I9作業には影響しない。台帳2章へ移動済み。
### 詳細設計
- `Doc/spec/2026-08-18_I9_配分照会3画面_詳細設計.md` を新設し、方針・SQL・G/W/T・検証を先に確定した。
- 方針: `ZaikoQueryViewModel`（在庫問合せ）を一般化した共通基底＋3サブクラス。展開する数量だけを差し替える。
- 引当・有効在庫は `SummaryRealStock.ReserveQty`（materialize済み）を読むだけ。配分数のみ `TranHaibun` を集計する。
### 実施内容
- 新規 `CvWpfclient/ViewModels/07Haibun/BaseHaibunInquiryViewModel.cs`。検索条件・商品一覧・倉庫×SKUマトリクスの
  ドリルダウン・CSV出力（`商品CD_数量名.csv`、UTF-8 BOM）・gRPC照会（`Msg101_Op_Query`）を集約。抽象 `LoadDrillRowsAsync`。
- `HaibunQueryViewModel`（配分数＝`TranHaibun` `EndFlag=0`）/ `HikiateQueryViewModel`（`ReserveQty`）/
  `YukoZaikoQueryViewModel`（`Su-ReserveQty`）の3サブクラスを実装。
- `Views/07Haibun/{HaibunQuery,HikiateQuery,YukoZaikoQuery}View.xaml`(+`.xaml.cs`) を実装。
  一覧は 在庫／引当／有効在庫／配分数、ダブルクリックで倉庫×色サイズのマトリクスタブを開く。
- サーバ・スキーマ・Msgの変更なし（読み取りは既存経路、引当は集計テーブル済み）。
### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`：成功（0 warnings / 0 errors）。
- `Tests\TestServer\bin\Debug\net10.0\TestServer.exe`：89/89 成功。`dotnet Tests\TestLogin\...\TestLogin.dll`：7/7 成功（件数不変）。
- 実データは `TranHaibun` 0件のため配分問合わせのマトリクスは空。引当・有効在庫は既存在庫で表示。
### 完成度への影響
- 07Haibun の L0 が 20→17、L3 が 0→3。対象画面数131は不変。
- チェックリスト 3.6 / 11章、課題台帳 I9 / 2章、詳細設計を更新。

## [2026-08-17] MenuData の実装済み画面説明を更新
### Agent
- Codex : GPT-5 : Sekiya Sato Codex
### 目的
- 実装済みのメニュー画面に残っていた「準備中」表示を、実際の機能説明へ修正する。
### 実施内容
- 「仕入伝票印刷」を、条件指定による仕入伝票PDF印刷として説明した。
- 「棚卸日一括メンテナンス」を、店舗別棚卸日・自動補充曜日の一括設定として説明した。
### 確認
- `BestSalesReportView` は XAML が空の Grid、ViewModel も空実装であるため、「準備中」は維持した。
- MenuData 参照と全 View / ViewModel を照合し、未参照分はログイン・設定・スケジューラ編集・メイン画面・Sub 配下の選択/補助画面のみであることを確認した。

## [2026-08-17] 最新コミット反映: 完成度チェックリストと旧CVnet比較の後継課題台帳を更新
### Agent
- Codex : GPT-5 : Sekiya Sato Codex
### 目的
- `899099d` までの実装状況を `2026-08-12_CV10機能完成度チェックリスト.md` へ反映し、旧CVnet比較資料に残る未適用・判断保留・未解決事項を後継文書へ分離する。
### 確認した事実
- `899099d` で発注残完了設定・受注残完了設定が実装され、`EndFlag` の手動完了・解除とSKU単位残数判定を `TestServer` 89件で検証している。
- `09a82eb` で棚卸開始・棚卸確定処理画面、`c9a01e3` で配分出荷・棚卸・完了の業務ロジック、`74b5b2f` で引当集計仕様を実装済み。
- 配分確定・出荷処理・配分照会は View / ViewModel が空実装で、サーバー側ロジックまたは集計だけが先行している。
### 実施内容
- チェックリストのHEAD、画面数、Lv集計、発注・受注残、引当、棚卸の状態を更新。
- `Doc/spec/2026-08-17_旧cvnet比較_未適用・保留課題.md` を新設。旧比較資料は比較根拠と決定履歴として残すことを明記し、後続の実装待ち・判断保留事項だけを台帳化。
### 確認
- 文書差分確認、`git diff --check` 成功。文書変更のみのためビルド・テストは未実施。

## [2026-08-17] 旧CVnet資料と会議資料を突き合わせて優先仕様を確定し、発注・受注の完了フラグと配分の欠品数を追加
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：チェックリストとドラフトから本日の優先課題を洗い出し、旧CV.net仕様と比較して仕様決定の判断材料を列挙する。
- 決定できたものは `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` へ記録し、スキーマ変更は先行実装する。
### 資料の入手と抽出手段
- 当初指定された Google Drive フォルダは接続アカウントから参照できず（`Requested entity was not found`）、ユーザー指示で `refer/cvnet-knowlege/` へ切り替えた。業務フロー18点・マニュアル14点・DB定義書2点・帳票集PDF14点・帳票集xls16点があり、Drive より材料は厚い。
- 追加で `refer/cv10meeting/`（第0回・第1回・修正第1回7者会議・新規機能3者会議×2）を読み、決定の枠組みと優先順位の根拠にした。
- 抽出手段。環境に `pdftotext` も Python も無いため、**PDF は WSL2 の Ghostscript（`gs -sDEVICE=txtwrite`）**、**pptx / docx は zip 展開して XML からテキスト化**した。Excel COM は未登録のため `.xls` 16点だけ未読。帳票集PDF が同一構成なので `帳票別計算式一覧.xls` を除き実質代替できた。
### 事前調査で判明した事実
- **帳票集PDF の「出力内容の説明」に算式が明記されている**。これが残・請求・原価の一次根拠になり、`.xls` が読めない穴をほぼ埋めた。
  - 発注残: 納品数＝仕入データの数量（発注伝票NOとの紐付けが必要）、残数＝発注数−納品数、納期予定＝発注データの納品日。
  - 受注残: 納品数＝受注Noで紐付いた出荷売上数量（受注No未登録は対象外）、残数＝受注数−納品数。
  - 請求一覧表: 繰越金額＝前月残−(現金+振込手数料+手形+相殺+その他)、売上合計＝売上−返品−値引+その他売上+消費税、純売上＝売上合計−消費税。
  - 総平均原価＝(前月在庫×原価+当月仕入金額+TQ)÷(前月在庫+当月仕入数)。`TQ` の定義は帳票上に無い。
- **旧の有効在庫は「実在庫 − 配分入力（初回配分は除く）」**。4スライドで繰り返され、「初回配分は引き当てがかからないので在庫のある商品は必ず在庫配分を選択」という注意書きまである。2026-08-15 実装の「全区分を引当対象」と食い違っていた。
- **旧の配分完了は計算条件だった**。「0未完了＝指示数>出荷数+欠品数 / 1完了済＝指示数<=出荷数+欠品数」。CV10 に欠品数の概念が無かった。
- **旧の棚卸確定は置換**（設定日付時点在庫を棚卸数にする）。再確定が正常な運用手順として想定されている。
- 修正第1回7者会議 **Decision 7** が、引当対象 `EnumHaibun` / 引当解除タイミング / `SummaryRealStock.ReserveQty` を**未決定**と記録していた。2026-08-15 の引当実装はこれに先行しており、第0回 合意事項4（APPROVED 前に独断で確定しない）と整合していなかった。本日の決定で解消した。
- 旧の消費税計算方法は 3値（0:請求単位 / 1:伝票単位 / 2:明細単位）。CV10 の取引先マスタには消費税計算方法も入金率・支払率も無い。
### ユーザー決定
- **G0 / G0'**: 発注・受注の完了は**伝票単位**（旧は明細行単位。CHANGE）。完了は自動で立てるが手動で立てる・解除することも可能。
- **G0-a / b / c**: 「残のみ」は完了除外後に明細残を出す。伝票ごと完了にしたものはSKUに残があっても完了とみなす。自動完了は**明細単位で全SKU充足**を条件とする。
- **G2 / G3**: 完了の取消は可、監査列は持たない。
- **G4**: 受注残 ＝ 受注 − 受注伝票Idで紐付いた出荷（`Tran00Uriage` かつ `TenType` 1:卸先 or 3:売仕店）。集計対象は完了でない受注。配分側の残は「引当数」で見る。
- **4.3**: 発注・受注の状態遷移（未処理 / 一部処理 / 完了(自動) / 完了(強制)）を定義し、**完了判定は新設 `EndFlag` を見る**。
- **I1（重要な仕様変更）**: 引当対象は `EnumHaibun = 0`(初回配分) **以外のすべて**。判定は `Kubun != 0` の一点。
- **5.1.2**: 配分は `Su` をユーザーが入力→倉庫へ送信→倉庫から戻るデータで `JitsuSu` / `ShortSu` が設定され `Su = JitsuSu + ShortSu` が成立。その状態かつ `KakuteiDay` に有効日付があるものを確定、完了は `EndFlag=1`。
- **I4 / I5 / I6**: 出荷売上＝`TenType` 1 or 3、移動＝`TenType` 0 or 6。`TranHaibun` は `DenDay`+`Id_Soko`+`Kubun`+`RelateNo1` 単位でまとめる（仮想ヘッダ）。ハンディは無し。
- **H1 / H4**: 納品予定日は伝票単位で揃える。納期遅れは納品日と完了フラグで判定する。
- **E2**: 締日は第1段階 1得意先1締日、次段階で最大3締日（仕入先も同様）。
- **E2'**: `PayMonth=0` は当月、`PayDay=0` はイレギュラーだが `99`（末日）と同様に扱う。
- **7.0**: 請求・支払計算は旧仕様のデータの持ち方に合わせる。入金予定日 / 支払予定日は追加対象。
- **E8**: 消費税は**月ごとに計算**する（得意先別の方式切替は廃止）。端数は**四捨五入で統一**。税率改定が月途中の場合はイレギュラーとして人手で調整。**内税は廃止**（残す場合も表示のみ）で外税を基本とする。伝票ごとの `Tax` は目安で、確定するのは請求書の税額。差分は `Tran06Nyukin` / `Tran07Shiharai` の `Kubun`（相殺）で ± 調整する。
- **E9**: 入金率・支払率による按分は実装しない。
- **E10 / E11**: 請求書番号、その他売上列は判断保留。
- **F0**: 棚卸確定は `SummaryStock.Su` との差を `AdjustQty` に設定する。在庫数の構成を `Su = InQty + OutQty + AdjustQty` へ変更（仕様変更）。
- **J1 / J2 / J3**: 締日更新は1.0では入れない。修正有効日数・先付有効日数は `MasterSysKanriMente` を参照しワーニングを出す。外部連携は締日更新・有効日数の影響を無視する。
### 実施内容（ドキュメント）
- `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` を新設。第0回 合意事項3 の **KEEP / CHANGE / REPLACE / DROP / LATER** で全論点を分類し、Phase 1〜5 のスケジュールへ割り付けた。修正第1回 Decision 3 に従い、決定済み項目を Given / When / Then へ変換した。
### 実施内容（スキーマ）
- `CvBase/BaseDb2Trans.cs`: `Tran13Hachu.EndFlag` / `Tran12Jyuchu.EndFlag` を追加（`EnumYesNo` の `EnEndFlag` 付き）。自動完了と強制完了の両方を doc コメントへ記載した。
- `CvBase/BaseDbHaibun.cs`: `TranHaibun.ShortSu`（欠品数）を追加。`EndFlag` と `EnumHaibun` のコメントを I1 の判定（`Kubun != 0`）へ更新した。
- `CvBase/UpdateDb.cs`: `26_08_17_01` を1行追加し、3つの `ALTER` を `;` 区切りで記述した。
### 技術決定 Why
- **`TranHaibun.EndFlag` は ALTER に含めなかった**。`26_08_15_01` で追加済みであり、重複させると `duplicate column name` になる。新規列は `Tran13Hachu.EndFlag` / `Tran12Jyuchu.EndFlag` / `TranHaibun.ShortSu` の3つだけ。
- **`EnumHaibun` を2区分へ集約しなかった**。ユーザーの最終指示が「0:初回配分以外が引当数に割り当てられる」だったため、8区分を維持したまま判定式を `Kubun != 0` にした。集約すると「どの画面から登録された配分か」が区分から判別できなくなるが、この方式なら維持できる。既存コードで `EnumHaibun` の値を参照しているのは `HachuHaibunInputViewModel` と `ShopHaibunInputViewModel` の2ファイルのみで、いずれも 0 と 1 しか使っていない。
- **`ShortSu` を追加した理由**: 旧の完了条件「指示数 ≤ 出荷数 + 欠品数」を再現するため。これが無いと出荷されなかった配分の引当を解放する手段が `EndFlag` の手動更新しか無くなる。
- **旧の内部仕様は踏襲しない**方針をユーザーが明示したため、比較は業務能力ベースで行い、列名は機能の存在証明としてのみ引用した。
### 影響範囲
- `CvBase/BaseDb2Trans.cs`、`CvBase/BaseDbHaibun.cs`、`CvBase/UpdateDb.cs`
- `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md`（新規）
- `SummaryDb.CalcHaibun2Reserve()` / `CalcReserveQtyAll()` は**未変更**。現行は `EndFlag=0` のみで絞っており `Kubun != 0` の条件が無いため、I1 の実装時に修正が必要。
### 確認
- `dotnet build`（ソリューション全体）: 成功（警告0、エラー0）。
- `dotnet run --project Tests/TestServer/TestServer.csproj`: 合計72 / 成功72 / 失敗0。`Tests/TestLogin`: 合計7 / 成功7 / 失敗0。
- **UpdateDb の versions 組み込みを実DBスキーマで検証した**。実DB（`server-user163.db`、9.6GB、`DbVersion=26081601`）は容量の都合で丸ごと複製できないため、**read-only で開いて3テーブルの実 DDL を読み出し、同一スキーマの空DBを作って ALTER を適用**した。
  - 適用前の状態を確認: `Tran13Hachu` / `Tran12Jyuchu` に `EndFlag` 無し、`TranHaibun` は `EndFlag` 既存・`ShortSu` 無し。
  - `SubInsertRecordAsync` と同じ分割（`;` 区切り・空要素除去・トリム）で3文に分かれ、3文とも適用成功。
  - 重複適用は `duplicate column name` で失敗するが、`UpdateDb` はエラーをログに残して処理を継続するためDBは壊れない。
  - 新規DB作成時は versions の SQL を実行せず最新版のみ記録する経路のため、テーブル定義側への追加が必須。3テーブルとも `DefineDataTable.cs` の生成対象に含まれることを確認した。
### 残課題
- **実DBへの `26081701` 適用は未実施**。検証は実スキーマの複製に対してのみ行った。
- **チェックリストの更新が必要**。`2026-08-12_CV10機能完成度チェックリスト.md` 5章A-1 が引当を「確定・実装済み」と書いたままで、本日の I1 決定と食い違う。
- **Decision 7 の8項目を APPROVED へ送る手続きが未了**。本日の決定で実質的に埋まったが、7者会議での正式承認は経ていない。
- 人間の判断が残っている主なもの。
  - 実装ブロック: I3（どの画面が `KakuteiDay` / `EndFlag` を立てるか）、I2（`EndFlag` を自動で立てるか手動か）、I7（滞留検出と一括完了）、仕入・出荷の削除や数量減で `EndFlag` を 0 へ戻すか、E8-a（月次税の計算単位）、E8-b（課税標準）、F0'（棚卸開始処理を1.0に入れるか）、F0''（再確定を前提にするか）。
  - 判断保留: E10（請求書番号）、E11（その他売上）、E8-g（締日が月末以外のときの税。第2段階へ進む前の必須決定事項）。
  - LATER 候補（Sales / System / Architect の確認要）: E7 親子締日チェック、I8 自動補充2方式、H5 MDマップ、展示会スワッチ関連。
  - 追認待ち: E1 / E3 / E4 / E5 / E6、H2 / H3、F1〜F4。
- 未取得の資料。`マニュアル/13-配分.pdf`（I2 / I3 を詰めるのに必要）、`業務フロー/06_諸掛・総平均原価更新`（総平均原価の `TQ` の定義）、`帳票集/帳票別計算式一覧.xls`、Google Drive の旧CV.net資料。

---
## [2026-08-16] 消込・掛集計の残論点を確定し実装（入金支払の掛計上日化、元帳の消込マーク、区分別内訳）
### Agent
- Opus 5 : Anthropic : Sekiya Sato Claude
### Editor
- Claude Code
### 目的
- ユーザーからの要望：`Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md` の未決事項を再度洗い出して提示し、決定できたものを新しいドラフトへ確定させて実装する。
- 決定内容の記録先は `Doc/spec/2026-08-16_phase1_業務仕様決定ドラフト.md` を新設し、2026-08-12 版は履歴として残す。
### 事前調査で判明した事実
- 前回ログで「次に判断が必要」とした `IsPay` は、ユーザーが移行済み50,311件を `1` へ一括更新済みだった。`Tran03Shiire` 25件も同じ。推奨案どおり条件を追加しても売掛は消えない。
- 入金明細の `Id_Kin` も 2.1.4 の是正SQLが適用済みで、2,302件すべてが `KIN` マスタへ紐付いていた。
- `MasterTokui.Id_Paysaki` / `MasterShiire.Id_Paysaki` は変化なしで全件 `0` のまま。
- 実DBは WAL モードで、`-wal` に65MBの未チェックポイント分がある。**`.db` だけを複製すると直前の更新が落ちる**。最初の検証はこれに気づかず旧データで走らせ、内訳が全額「その他」に落ちる誤った結果を得た。`-wal` と `-shm` も併せて複製して再検証した。
- 得意先元帳は売上を `DenDay` で拾っており、`KakeDay` で集計する売掛と基準日が違っていた（ドラフト未記載の論点）。
### ユーザー決定
- **A1**: `Id_Paysaki = 0` は親を持たず、その取引先自身に対して請求（支払）を行うとみなす。既存の絞り込み条件をそのまま確定。旧 `Code_Kin = 85` は金額が過大だが一旦 `02` 振込手数料のままとする。
- **A2**: `Tran06Nyukin` / `Tran07Shiharai` の `DenDay` を `KakeDay` へ**列名変更**し、掛計上日とみなす（2026-08-12 版 2.1.2-2 の「`DenDay` を流用し専用列を追加しない」を変更）。
- **A3 / A4 / A5**: 消込済み行の再表示、監査列なし、入金側の対象範囲は推奨どおり承認。
- **C1**: 元帳の `*` は `EndFlag = 1` を条件に、**メモを印字している欄の先頭**へ出す。メモがあれば半角空白1つを挟む。qfm は変更しない。
- **D1〜D6**: 掛集計は推奨どおり。得意先/仕入先単位を維持、区分別内訳を埋める、`IsPay` 条件を追加、対象期間より後の月も再計算、基準日を `KakeDay` に統一、締日単位の税計算は 1.0 対象外。
- 請求・支払計算（2.7）以降は未着手のままとする。
### 実施内容
- `Doc/spec/2026-08-16_phase1_業務仕様決定ドラフト.md`: 新設。決定の前提となった実データの事実、A1〜D6 の決定、実装箇所、未決事項（E〜I）、受入条件と検証結果を記載。
- `Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md`: 冒頭に引き継ぎ注記を追加。後継書で変更された決定（2.1.2-2）を明示。
- `CvBase/BaseDb2Trans.cs`: `TranKinHeader.DenDay` を `KakeDay` へ改名。`Tran06Nyukin` / `Tran07Shiharai` の `KeyDml("nk1")` を追随。
- `CvBase/UpdateDb.cs`: `26_08_16_01` で両テーブルへ `RENAME COLUMN DenDay TO KakeDay`。
- `CvDomainLogic/SummaryDb.cs`: `CalcSummaryUriKake()` / `CalcSummaryKaiKake()` を改訂。共通の `KakeDenWhere` / `KinMeisaiFrom` / `KinBucket()` / `ExtendToMonth()` を新設。
- `CvWpfclient/Helpers/ViewModels/TranMeisaiSql.cs`: `MemoWithKesikomiMark()` を追加。両元帳が同じ式を使う。
- `TokuiLedgerViewModel` / `ShiireLedgerViewModel`: メモ欄の `*` 付与と、基準日の `KakeDay` 化。
- `SeikyuBalanceDetailViewModel` / `ShiharaiBalanceDetailViewModel`: 入金・支払側の期間条件を `KakeDay` へ。同じ問い合わせ内で軸がずれないよう売上・仕入側も揃えた。`SummaryUriSei` / `SummaryKaiShi` の `DenDay`（請求日・支払日）は据え置き。
- `BaseKinInputViewModel` / `BaseMatchingViewModel` / `SelectTranWinViewModel` と、入金・支払の入力/消込 XAML 4件を追随。
- `Tests/TestServer/SummaryKakeDbTests.cs`: 新設（9件）。区分別内訳、`IsPay` 除外、後続月の再計算、`KakeDay` 基準、繰越、再実行の冪等性を固定。
### 技術決定 Why
- **入金の内訳をヘッダ総額と別の枝にした理由**: `json_each` で明細を展開すると1伝票が複数行になり、同じ SELECT で `SUM(KingakuTotal)` を採ると明細数だけ二重計上される。`UNION ALL` の枝を「ヘッダ総額」と「区分別内訳」に分けることで、`TotalIn` はヘッダから、内訳は明細から採れる。
- **バッチSQLで `json_each` を使った理由**: 2.1.2-3 は「JSON関数への依存を増やさない」意図でクライアント側C#展開を採ったが、それは画面照会が `QueryListSqlParam` の既存経路に載るという理由だった。夜間バッチにクライアントは無く、`BaseDbDerived` / `BaseDbJodai` / `TranMeisaiSql` が既に `json_each` を使っているため新規の依存にはならない。
- **内訳を符号付きで持つ理由**: `Uriage + Henpin + Nebiki` が改訂前の `Uriage`（全区分の符号付き合計）と一致し、テストで検証できる不変条件になる。返品・値引は `CalcFlag = -1` なのでマイナスで入る。売上側に「その他」の内訳列が無いため区分 `99` は `Uriage` に含める。
- **未知の `Id_Kin` を `Other` へ寄せる理由**: `Cash + Fee + Densai + Offset + Other = TotalIn` を無条件に成り立たせるため。マスタに無い区分があっても金額が消えない。
- **D4 を「実効終了月を伸ばす」方式にした理由**: 指定範囲を「From 以降すべて」に読み替えると `DateToYyyymm` の意味が失われる。指定はそのまま残し、後続月に集計行または伝票があるときだけ終了月を伸ばす。夜間ジョブは前月・当月しか回さないので通常は挙動が変わらない。
- **`ExtendToMonth` で `KakeDay > @0 || '99'` と書いた理由**: `substr(KakeDay,1,6) > @0` と論理的に同値でありながら、`KakeDay` のインデックスが効く。実DBで件数一致（37,130件）を確認した。
- **元帳の `*` に専用列を作らなかった理由**: 列を増やすと `printform/*.qfm` の座標調整が必要になる。メモ欄へ同居させればレイアウトを維持できる、というユーザー指示。
- **`SelectTranWinViewModel` を列名解決方式にした理由**: このダイアログは `TranAllHeader` 系と `TranKinHeader` 系の両方を扱うが、WHERE句の日付列名を `DenDay` で静的に埋めていた。改名で入金・支払だけ壊れるため、型から `DenDay` / `KakeDay` を解決するようにした。
- **`SummaryUriSei` / `SummaryKaiShi` の `DenDay` を改名しなかった理由**: こちらは請求日・支払日であり、伝票の計上日とは意味が違う。
### 確認
- `dotnet build`: 成功（警告0、エラー0）。
- `TestServer` 72件（新規9件）、`TestLogin` 7件、いずれも全通過。
- 実DBの複製（`.db` / `-wal` / `-shm` を揃えて複製し `26_08_16_01` 適用後）に対し、改訂後の集計SQLを実行して確認した。
  - 売掛18,333行を生成。`Cash + Fee + Densai + Offset + Other <> TotalIn` の行は0件。入金計 10,690,639,150（現金 7,407,676,593 / 手数料 3,146,872,651 / 相殺 120,787,740 / その他 15,302,166）。
  - `Uriage + Henpin + Nebiki` = 伝票側の符号付き合計。売掛 233,574,853、買掛 992,000 で一致。
  - D4 は `202001` 指定で実効終了月が `202205` へ延長され、後続月が無い場合は延長しない。
  - 買掛で繰越の積み上げを確認（`202607` 残 1,091,200 → `202608` 支払 1,055,880 → 残 35,320）。
  - C1 のメモ欄は4パターン（`* メモ` / `*` / `メモ` / 空）とも期待どおり。
- 列名変更の影響監査を両方向で実施。入金・支払テーブルを参照する16ファイルに `DenDay` 参照が残っていないこと、`KakeDay` を使う全SQLの対象テーブルが `KakeDay` を持つことを確認した。`CvServer` に入金・支払テーブルを扱うSQLは無い。`WriteEffectRunner.PartialUpdateDeniedColumns` は両方の列名を含むため変更不要。
- `git diff --check`: 成功。新規2ファイルは UTF-8 BOM なし・CRLF。
### 落とし穴
- **WAL を含めずにDBを複製すると古い状態を検証してしまう**。実DBは WAL モードで、`.db` のみの複製では直近の更新が丸ごと欠ける。検証用に複製するときは `-wal` と `-shm` も揃え、`PRAGMA wal_checkpoint(TRUNCATE)` を通すこと。
- **`substr(t.DenDay,...)` の置換漏れ**。SQL文字列内の列名はコンパイラが検査しないため、ビルド成功では検出できない。最初の一括置換が WHERE 句だけに一致し、`UNION ALL` 側の SELECT リストに `substr(t.DenDay,1,6) AS DenMonth` が残っていた。列名変更では対象テーブルを含む全ファイルを目視で通し、逆方向（新しい列名を持たないテーブルに新列名を使っていないか）も確認する。
- **NPoco の `Insert` は AutoIncrement 主キーに明示した `Id` を捨てる**。テストで `MasterMeisho` の `Id` を固定したかったが `Insert` では反映されず、外部キーの突き合わせが全て外れて内訳が `Other` に寄った。Id を固定したい場合は生SQLで `INSERT` する。
### 残課題
- `26_08_16_01` は実DBへ適用済みだが、実画面での目視確認（入金・支払入力、消込、元帳の印刷プレビュー）は未実施。
- `SummaryUriKake` / `SummaryKaiKake` は実DBでは0件のまま。夜間ジョブでの初回生成は未実施。
- 入金・支払入力画面のラベルは「入金日」「支払日」のままで、列名（掛計上日）と表記が揃っていない。表示語の変更要否は未判断。
- 未着手の決定事項は新ドラフト5章のとおり。請求・支払計算（E1〜E6）、在庫調整・引当の調整数（F1〜F4）、発注・受注残の強制完了（G1〜G3）、納品予定日（H1〜H2）、禁止列リストの保守方針（I1）。
- `Doc/spec/2026-08-12_CV10機能完成度チェックリスト.md` は今回の変更を未反映。

---

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

---
## [2026-08-17] 引当集計を新仕様へ修正し、メニュー構成を1.0スコープへ整理
### Agent
- Claude Code : Opus 5 : Sekiya Sato
### 目的
- 2026-08-17 に確定した業務仕様（`Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md`）のうち、
  引当数の算定規則とメニュー構成をコードへ反映する。
### 実施内容
#### 引当集計（決定 I1 / 5.2.2c）
- `CvDomainLogic/SummaryDb.cs`: 引当の判定を2点変更した。
  - `ReserveTargetWhere` を新設し、対象を `EndFlag = 0 AND Kubun <> 0` とした。
    初回配分(`EnumHaibun.Hatsukai`)は入荷前の振り分けで現物を押さえないため引当対象外になる。
  - `ReserveQtySumExpr` を新設し、積む数量を「未確定(`KakuteiDay` が空)は `Su`、確定済みは `JitsuSu`」へ変更した。
    確定時に `Su = JitsuSu + ShortSu` が成立するため、欠品分は確定と同時に引当から外れる。
  - 上記2つを `CalcHaibun2Reserve()` / `CalcReserveQtyAll()` の4か所すべてで共用し、
    通常更新値と全件Rebuild値が原理的に一致する構造を維持した。
#### メニュー構成（決定 I9 ほか）
- `CvWpfclient/Models/MenuData.cs`:
  - **新設**: 「配分・出荷 > 配分照会」に 配分問合わせ / 引当問合わせ / 有効在庫問合わせ の3画面を追加した。
    旧CV.netの【配分】9〜11に相当し、CV10 に対応画面が無かったため引当数・有効在庫を読む画面が0だった。
    倉庫業務ロールにも「有効在庫問合わせ」を追加した。
  - **削除**: 「発注配分リスト」「配分出荷リスト」の2画面。配分の一覧は「出荷指示一覧印刷」へ集約する。
  - **名称変更**: 「棚卸確定」→「棚卸確定処理」（旧メニュー名に合わせた）。
  - **addInfo 更新**: 本日確定した仕様を反映した。納品予定照会・納品予定表・発注残完了設定・受注残完了設定・
    在庫強制調整入力・請求計算・支払計算・棚卸開始処理・棚卸確定処理は「仕様未確定」ではなくなったため
    確定内容を記載した。1.0対象外に決めたもの（締日更新・自動補充・展示会スワッチ・絵型一覧表）は
    「1.1以降」と明示した。
  - HHT連携は残した。決定 I6「ハンディは無し」は配分→出荷のフローでハンディ読取に依存しない意味であり、
    HHT連携機能そのものの廃止ではない旨をコメントへ記載した。
- 新設3画面の View / ViewModel を追加した。ViewModel には旧マニュアル13-配分から読み取った画面仕様を
  コメントとして残し、実装時の根拠が追える状態にした。
### スキーマ
- 変更なし。`26_08_17_01`（`Tran13Hachu.EndFlag` / `Tran12Jyuchu.EndFlag` / `TranHaibun.ShortSu`）は
  前コミット `967b014` で追加済み。
### 検証
- ソリューション全体のビルド: 成功（警告0、エラー0）。
- `Tests/TestServer`: 合計74 / 成功74 / 失敗0（引当の新規テスト2件を追加した）。
  - `CalcHaibun2Reserve_HatsukaiHaibun_IsNotReserved`: 初回配分は引当されず、在庫配分・取置は引当される。
    通常更新値とRebuild値の一致も確認する。
  - `CalcHaibun2Reserve_AfterKakutei_UsesJitsuSuInsteadOfSu`: 未確定は `Su`、確定済みは `JitsuSu` を積み、
    全量欠品なら引当が消える。
- `Tests/TestLogin`: 合計7 / 成功7 / 失敗0。
- メニュー登録と実体の突き合わせ: 空のView/ViewModelは85組あり、**すべてメニューに登録されている**
  （メニュー未登録の空画面は0件）。指示どおり「メニューに残ったものはそのまま残す」状態になっている。
### 残課題
- 実行時確認が未実施。新設3画面は空Viewのためメニューから開くと空画面が出る。
- 引当の変更は既存の実データに影響しない（`TranHaibun` は0件）。夜間バッチでの再集計は未実施。
- 本日の決定で必要になった実装のうち、以下は未着手である。
  - 発注・受注の自動完了（明細単位で全SKU充足）と残完了設定、完了済み伝票の編集時ワーニング。
  - 配分の確定（`KakuteiDay` と有効在庫割れエラー検証）と出荷処理（伝票作成＋`RelateNo2`＋`EndFlag`）。
  - 棚卸の開始処理・確定処理、在庫恒等式 `Su = InQty + OutQty + AdjustQty` への変更。

---
## [2026-08-17] 残管理・配分出荷・棚卸の業務ロジックを実装
### Agent
- Claude Code : Opus 5 : Sekiya Sato
### 目的
- チェックリスト 10.5 の未着手4件について、業務ロジック層を実装する。
  画面(WPF)は本コミットの範囲外とする。
### 実施内容
#### 1. 発注残・受注残の自動完了
- `CvDomainLogic/CompletionDb.cs`(新規): 紐付く仕入・出荷から完了フラグを自動判定する。
  - 判定は**明細単位で全SKUが充足**すること(決定 G0-c)。伝票合計では判定しない。
  - 発注は `Tran03Shiire.RelateNo1`、受注は `Tran00Uriage.RelateNo1` で紐付ける。
    受注側は出荷先の店種区分が卸先(1)・売仕店(3)のものだけを数える(決定 G4)。
  - 仕入数・出荷数は `CalcFlag` を掛けた符号付きで数えるため、返品を入れると充足が取り消される。
  - いったん立った完了は実績が減っても**自動では戻さない**(決定 4.3.1)。
    `FindCompleted()` を用意し、編集時ワーニングから完了済み伝票を検出できるようにした。
- `CvServer/Services/WriteEffectRunner.cs`: 上記を書き込み後に起動する。
  結果は `WriteEffectResult.Completion` としてログへ出す。
#### 2. 配分の確定・出荷処理
- `CvDomainLogic/ShippingDb.cs`(新規):
  - `ConfirmShipping()`: 有効在庫割れを検証して `KakuteiDay` を立てる。
    旧CV.netと同じく「有効在庫 − 予指示が正の場合のみ確定できる」を守り、
    1SKUでも割れていれば**1件も確定しない**。割れたSKUは呼び出し元へ返す。
  - `CreateShippingSlips()`: 仮想ヘッダ
    (`DenDay + NouhinDay + Id_Soko + Id_Tenpo + Kubun + RelateNo1`、決定 I5)単位で伝票を作る。
    出荷先の店種区分で分岐し、卸先・売仕店は `Tran00Uriage`、倉庫・直営店は `Tran10IdoOut`(決定 I4)。
    数量は確定数 `JitsuSu` を使い、欠品は出荷しない。全量欠品の行は伝票を作らず完了だけ立てる。
    伝票Idを `RelateNo2` へ書き `EndFlag=1` で引当を解除する(決定 I2)。
  - `CancelConfirm()`: 伝票未作成の行に限り確定を取り消す。
#### 3. 棚卸
- `CvBase`: 在庫調整伝票 `Tran61Chosei` を新設した(`ITranSoko`)。区分は `EnumChosei`。
  集計テーブルへ直接書かず伝票にしたのは「通常更新値 = Rebuild値」を守るためである。
- `CvBase`: `TranCalcBase.GetCalcAdjust()` を新設。`Tran61Chosei` だけが調整数へ積む。
- `CvBase`: `SummaryStock.BookQty`(帳簿在庫スナップショット)を追加した。
- `CvDomainLogic/StocktakeDb.cs`(新規):
  - `StartStocktake()`: 対象年月末時点の帳簿在庫を凍結する。棚卸中に伝票が入っても差異が動かない。
  - `FixStocktake()`: 実棚数(`Tran60Tana`)を集計し、帳簿在庫との差を倉庫単位の調整伝票へ起こす。
    **再確定に対応**し、前回この処理が作った調整伝票を取り消してから作り直す(決定 F0'')。
#### 4. 在庫恒等式の変更
- `CvDomainLogic/SummaryDb.cs`: `SummaryStock` の集計へ `AdjustQty` を追加し、
  Rebuild 対象へ `Tran61Chosei` を加えた。`Su = InQty + OutQty + AdjustQty` になる。
  `AdjustQty` は伝票から導出できるようになったため、非Tran列の復元対象から外した。
### スキーマ
- `26_08_17_02`: `SummaryStock.BookQty` を追加。
- `Tran61Chosei` は新規テーブルのため `DefineDataTable` が起動時に作成する(UpdateDb不要)。
### 検証
- ビルド成功(警告0、エラー0)。
- `Tests/TestServer`: 合計85 / 成功85 / 失敗0。新規11件。
  - 完了判定5件: 全SKU充足でのみ完了 / 削除で戻らない / 返品で取り消される /
    店種区分1・3のみ消化 / 紐付けの無い出荷は消化しない。
  - 出荷3件: 有効在庫割れは全件エラー / 店種区分で伝票が分かれ引当が解除される / 全量欠品は伝票なし。
  - 棚卸3件: 開始で帳簿在庫を凍結し確定で調整伝票を起こす / 再確定で作り直す / Rebuildで一致する。
- `Tests/TestLogin`: 合計7 / 成功7 / 失敗0。
- 既存テスト `SummaryAllAsyncStream_Rebuild_PreservesNonTranColumnsForRegeneratedNaturalKey` の
  `AdjustQty` 期待値を更新した。伝票から導出する列になったため、手で入れた値がRebuildで消えるのが正しい。
- テストが `MasterTokui` の Id を明示指定していた箇所を、採番後のIdを使う形へ直した。
  自動採番のため明示指定は反映されず、たまたま一致していただけだった。
### 残課題
- **画面(WPF)が未着手**である。空Viewのまま残っているのは8本。
  発注残完了設定 / 受注残完了設定 / 出荷指示確定(商品) / 出荷指示確定(得意先) / 出荷処理入力 /
  配分問合わせ / 引当問合わせ / 有効在庫問合わせ / 棚卸開始処理 / 棚卸確定処理。
- 完了済み伝票の編集時ワーニングを仕入入力・出荷売上入力へ組み込んでいない。
- `Tran61Chosei` を使う在庫強制調整入力も空Viewのままである。
- 実行時確認は未実施。実DBへの `26_08_17_02` 適用と `Tran61Chosei` の自動作成は未確認。

---
## [2026-08-17] 棚卸開始処理・棚卸確定処理の画面を実装
### Agent
- Claude Code : Opus 5 : Sekiya Sato
### 目的
- 前コミットで実装した `StocktakeDb` を画面から実行できるようにする。
  チェックリスト 10.5.1 の未着手画面のうち、バッチ形式の2本を先に閉じる。
### 設計
- **通信**: 既存の集計バッチ(在庫・掛再更新)と同じストリーミング経路(`QueryMsgStreamAsync`)へ載せる。
  1件ずつgRPCを往復せず、進捗とキャンセルを既存の仕組みで扱えるため。
  - `CvFlag.Msg054_StocktakeStart` / `Msg055_StocktakeFix` を追加した。
  - パラメータ `StocktakeParameter(TanaMonth, DenDay, IdShain, SokoIds)` を `CvBase/Parameters.cs` へ追加した。
  - `QueryMsgStreamService.HandleSummaryStreamAsync` の分岐へ2フラグを足し、`StocktakeDb` を呼ぶ。
- **画面**: 2画面は入力項目がほぼ同じなので基底 `BaseStocktakeViewModel` へ共通化した。
  棚卸年月・対象倉庫・進捗・実行・キャンセルを基底に置き、派生はフラグと処理名だけを与える。
  確定処理だけ「調整伝票日付」と「入力社員」を追加で持つ。
  - 対象倉庫は複数選択ダイアログ(`ShowMultiSelectDialog`)で選ぶ。未選択は全倉庫。
    旧CV.netは得意先をFROM-TOで範囲指定して一覧から1件ずつ選ぶ形だったが、機能としては同じである。
### 実施内容
- `CodeShare/ICoreService.cs`: `Msg054_StocktakeStart` / `Msg055_StocktakeFix` を追加。
- `CvBase/Parameters.cs`: `StocktakeParameter` を追加。
- `CvDomainLogic/StocktakeDb.cs`: `StartAsyncStream()` / `FixAsyncStream()` を追加。
  確定処理は Serializable トランザクションで包み、途中で失敗したら全体を戻す。
- `CvServer/Services/QueryMsgStreamService.cs`: 2フラグの分岐を追加。
- `CvWpfclient/ViewModels/31Monthly/BaseStocktakeViewModel.cs`(新規): 共通部分。
  件数はサーバーが本文へ「件数=N」の形で載せてくるため、完了メッセージから取り出して表示する
  (`StreamMsg.Code` は 0 / -1 のエラー区分にしか使われていない)。
- `CvWpfclient/ViewModels/31Monthly/StockTakeInitiationViewModel.cs`: 棚卸開始処理。
- `CvWpfclient/ViewModels/31Monthly/StockTakeFinalizationViewModel.cs`: 棚卸確定処理。
- `CvWpfclient/Views/31Monthly/StockTakeInitiationView.xaml` / `StockTakeFinalizationView.xaml`:
  在庫・掛再更新のレイアウトに合わせて作成した。空Viewを置き換えた。
- `CvWpfclient/Models/MenuData.cs`: 「月次・更新処理 > 棚卸更新」の2項目の説明を実装済みの内容へ更新した。
### 検証
- ビルド成功(警告0、エラー0)。
- `Tests/TestServer`: 合計87 / 成功87 / 失敗0。新規2件。
  - `Stocktake_AsyncStream_ProducesSameResultAsDirectCall`: 画面が使うストリーミング経路が
    直接呼び出しと同じ結果になる。
  - `Stocktake_WithSokoFilter_TouchesOnlySelectedWarehouse`: 倉庫を指定するとその倉庫だけが対象になる。
- 実DB(`server-user163.db`、`DbVersion=26081701`)で確認した。
  - `SummaryStock`(3,494,240行)に `BookQty` は未追加、`Tran61Chosei` も未作成であり、
    `26_08_17_02` と `DefineDataTable` の対象になる状態である。
  - 実スキーマを複製した空DBへ `26_08_17_02` を適用して成功。棚卸開始処理のUPDATE文も構文確認した。
  - 重複適用は `duplicate column name` で失敗するが、`UpdateDb` はログへ残して処理を継続する。
### 残課題
- 実行時確認が未実施。画面から実際に起動した確認はしていない。
- 実DBへ `26_08_17_02` を適用していない。適用時に `SummaryStock` 349万行への ALTER が走る。
- 棚卸開始処理は `Tran60TanaDate`(棚卸日一括メンテナンス)の棚卸日を参照していない。
  現状は棚卸年月末時点の帳簿在庫を保存する。倉庫ごとに棚卸日が異なる運用へ広げる場合は
  `StartStocktake()` で日付別に集計する必要がある。
- 入力社員は確定処理でのみ選択する。未選択のままでも実行でき、その場合 `Id_Shain=0` で登録される。

---
## [2026-08-17] 発注残完了設定・受注残完了設定を実装
### Agent
- Claude Code : Opus 5 : Sekiya Sato
### 目的
- 前コミットで実装した `CompletionDb` の自動完了に対する例外処理として、
  残っていてもこれ以上入荷・出荷しないと決めた伝票を手で完了にする画面を作る。
### 設計
- **画面構成**: 消込画面(`BaseMatchingViewModel`)と同じ2段構成にした。
  「一覧取得」で伝票を残数付きで並べ、「完了実行」でCheckBoxが変化した伝票だけを書き戻す。
  やることが消込とほぼ同じ(伝票単位のフラグを一覧で立てる)なので、操作を揃えたほうが学習コストが低い。
- **書き戻し**: 消込と同じ `PartialUpdateParam` で `EndFlag` 列だけを更新する。
  行全体を保存する `UpdateParam` だと1件ごとに在庫再集計が走るため。
  楽観排他は行単位で、1件でも競合すればサーバーが全件rollbackする。
- **共通化**: 発注と受注は対象テーブルと取引先が違うだけなので、
  `BaseZanCompletionViewModel<TDen>` へ共通部分を置き、派生はテーブル名と取引先選択だけを与える。
- **残数の定義**: 明細をSKU単位に畳み、実績が足りないぶんだけを `max(不足, 0)` で合計する。
  単純な「伝票数量 − 実績数量」にすると、あるSKUの超過が別のSKUの不足を隠してしまうため。
  サーバー側の自動完了判定(明細単位で全SKU充足)と同じ見方になる。
### 実施内容
- `CvBase/BaseDb2Trans.cs`: `TranCalcBase.ShukkaTenTypes` を追加した。
  受注残を消化する出荷先の店種区分(卸先1・売仕店3)で、サーバーと画面で共用する。
  `CvWpfclient` は `CvDomainLogic` を参照しないため、共有場所を `CvBase` にした。
- `CvDomainLogic/CompletionDb.cs`: 上記定数を使うよう変更した(自前の定数を廃止)。
- `CvWpfclient/Helpers/ViewModels/BaseZanCompletionViewModel.cs`(新規): 共通基底。
  検索条件(日付from-to / 取引先 / 表示区分「残のみ・全て」/ 取得件数上限)、
  一覧(完了Flg・伝票No・日付・取引先・数量・実績数・残数・金額)、
  全チェック・全解除、完了実行を持つ。
  完了済みは残の有無にかかわらず一覧へ出し、初期ONで表示して解除できるようにした。
  残がある伝票を完了にするときは確認ダイアログでその件数を知らせる。
- `CvWpfclient/ViewModels/03Hatchu/HachuZanCompletionSettingViewModel.cs`: 発注側。
- `CvWpfclient/ViewModels/04Juchu/JuchuZanCompletionSettingViewModel.cs`: 受注側。
  出荷は卸先・売仕店へのものだけを数えるため `ActualExtraJoin` で店種区分を絞る。
- `CvWpfclient/Views/03Hatchu/HachuZanCompletionSettingView.xaml`,
  `CvWpfclient/Views/04Juchu/JuchuZanCompletionSettingView.xaml`: 空Viewを置き換えた。
- `CvWpfclient/Models/MenuData.cs`: 2項目の説明を実装済みの内容へ更新した。
### 検証
- ビルド成功(警告0、エラー0)。
- `Tests/TestServer`: 合計89 / 成功89 / 失敗0。新規2件。
  - `AfterPartialUpdate_HachuEndFlag_HasNoSideEffect`: 画面が使う `EndFlag` の部分更新は
    在庫にも引当にも副作用を起こさない。
  - `After_ShiireWrite_RecompletesManuallyClearedHachu`: 手動解除しても実績が充足していれば
    次の書き込みで自動完了へ戻る。完了は片方向の判定なので解除しただけでは戻らない。
- `Tests/TestLogin`: 合計7 / 成功7 / 失敗0。
### 残課題
- 実行時確認が未実施。画面から実際に起動した確認はしていない。
- 実データは発注1件・受注1件しかないため、一覧の残数表示は実データで検証できていない。
- 完了済み伝票の編集時ワーニング(`CompletionDb.FindCompleted()` を使う)は未着手。
  仕入入力・出荷売上入力の保存後処理へ組み込む必要がある。
