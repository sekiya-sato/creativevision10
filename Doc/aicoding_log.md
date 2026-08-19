## [2026-08-19] 請求台帳（発行控え）帳票を新設し qfm スキルを実地検証・追記

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### 目的
- 完成度チェックリスト残タスクから qfm スキル検証に適したものを選び、詳細設計→実装→build→実 PDF 描画まで通す。検証で判明した実手順を `author-printstream-qfm` skill へ追記する。

### 選定
- 請求・支払の主要 6 帳票は qfm+SQL+画面+メニューが全て実装済みで「未実装の qfm」は無かった。唯一のギャップは、請求計算が `SummaryUriSei` に保存する `SeikyuNo`（請求書番号）・`Renban`（再発行世代）・`NyukinYoteiDay`（入金予定日）を**どの帳票も出力していない**こと（`CvWpfclient` で参照ゼロ）。これは P0 Release Gate（§4.1 段階7 の数値突合）の核心で、D-04/D-05 にブロックされない。→ 新帳票「請求台帳（発行控え）」を新設対象に選定。

### 実施内容
- 詳細設計 `Doc/spec/2026-08-19_請求台帳（発行控え）_詳細設計.md` を作成（目的・列定義 item1..item10・SQL・qfm レイアウト・受入 G/W/T）。
- qfm `printform/SeikyuLedgerReport.qfm` を新設。`SeikyuListReport.qfm` をコピーし列（請求書番号/請求日/得意先CD/得意先名/対象期間/売上額/消費税/残高/入金予定日/再発行）・見出し・タイトルを最小差分で差替、Shift_JIS(cp932) で保存。
- `SeikyuLedgerReportViewModel`（`BaseReportViewModel` 派生、`SummaryUriSei` JOIN `MasterTokui` を SELECT 列順=item 順で取得）、`SeikyuLedgerReportView.xaml(.cs)`、`MenuData.cs` に「請求台帳（発行控え）」を追加。
- skill `author-printstream-qfm/SKILL.md` へ実手順を追記: (1)Python 無し環境での validator 代替（iconv/grep + .NET XML）、(2)DB・サーバ不要で実 PDF を描画する検証ハーネス、(3)`BaseReportViewModel` 帳票配線パターン、(4)worked example。ハーネス一式を `tools/qfmprint/`（`PrintPdfService` の PrintContext 構築を最小再現）として同梱。

### 検証
- `CvWpfclient` build 成功（警告0/エラー0）。TestServer 114/114 成功（非回帰）。
- 実 PDF 描画: `tools/qfmprint` ハーネスで `SeikyuLedgerReport.qfm` + 合成 data.txt（正常/負値/大金額/入金予定日空欄の 3 行, cp932）を PrintStream エンジンへ渡し、`IsSuccess=True`・ライセンス全 product 有効・`outfile.pdf` 生成を確認。PDF テキスト層で全 10 列・負値・空欄・日付書式が正しいことを確認。
- qfm validator（Python）はこの環境に Python 実体が無く実行不可のため、構造チェック（encoding/root/path csv/portrait/A4/item・datasrc 数）と .NET XML 整形式チェックで代替した。
- 実 DB での数値突合・Mini-UAT は P1 として未実施（本作業は合成データでの描画確認まで）。ローカルハーネスはフォント埋め込みが本番サーバと異なり、ラスタ画像で一部 CJK グリフが欠けるが、テキスト層は正しい。

## [2026-08-19] PrintStream qfm フォーマット仕様を新設し author-printstream-qfm skill を再構成

### Agent
- Claude Opus 4.8 : Anthropic : Sekiya Sato Claude Code

### 目的
- qfm を AI が新規作成・修正できる粒度のフォーマット仕様を整備し、既存スキルをその仕様を核とする 2 層構成へ刷新する。

### 判断（順次）
- CHM `refer/printdll/PrintStream.chm` は `PrintStream_decompiled/` に展開済みで読める（FormEditor 116 ページ + Javadoc）。
- CHM には raw qfm XML の文法書がない（`datadesc`/`calctype` 等のタグ名で全文検索してもヒット0）。GUI 操作とスクリプト API の解説にとどまる。
- そのため仕様化は「実 qfm コーパスの機械マイニング（文法）＋ CHM の意味論（属性値）」のハイブリッドで実施可能と判断した。

### 実施内容
- 実 qfm を機械解析する抽出器 `Doc/spec/tools/extract_qfm_grammar.ps1`（PowerShell・cp932 対応）を作成した。要素／属性／enum 値の分布を出力する。
- cv10 `printform/`（108 本）と旧cv.net `C:\gitroot\cv\cvnet_pkg\cvnetpss`（1967 本）の計 **2075 本**を解析し、要素 23 種・属性・enum 値を実測で確定した。
- 仕様本体 `Doc/spec/PrintStream_qfmフォーマット仕様.md` を新設した。要素ツリー、要素×属性×値の文法表、decode 編集文字列チートシート（文字列/数値/日付）、骨格テンプレ、作成ワークフロー、CHM 出所を収録。各項目に出所（`[C]`CHM確定／`[M]`コーパス実測／`[C+M]`両方）を明記した。
- 入力は CSV のみ（`path datatype="csv"` が 2072/2072）というユーザー指示に合わせ、固定長・XML 入力は対象外とした。複数レコード様式のみ `prefix` を任意扱いで記載した。
- `.agents/skills/author-printstream-qfm/SKILL.md` を刷新した。書式詳細を仕様 md へ委譲し、SKILL 側は運用規律（Shift_JIS(cp932)・CSV data.txt・コピー起点・validator・PDF 確認・ロールバック）に集約した。雛形選択を表化し、description を仕様核参照へ更新した。

### 確認
- 仕様 md が参照する雛形 qfm 7 本（MasterShainMente / MasterMeishoMente / MasterSysKanriMente / MasterPrintBarcode002 / ...Code39 / ...Nw7 / ...Sho）の実在を確認した。
- SKILL.md から仕様 md への相対パス `../../../Doc/spec/...` が構成上正しいことを確認した。
- コード・テストの変更はない。ドキュメント 2 新設＋スクリプト 1 新設＋スキル 1 更新のみ。実 PDF 出力によるレイアウト確認は本作業では未実施（帳票を実際に作る際に実施する）。

## [2026-08-18] 請求・支払計算の完成度チェックリストを最終更新

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 独立最終レビュー指摘なしを受け、請求・支払計算の実装済み範囲、Release Gate、残余リスクを最新HEADへ整合させる。

### 実施内容
- `2026-08-18_CV10機能完成度チェックリスト.md` の基準HEADを `6a976d3` へ更新した。DBスキーマ、日次請求/支払残、月次売掛/買掛、gRPCストリーミング、WPF請求/支払/再発行/Rebuild、締日変更ブロックを実装済みとして反映した。
- 月次掛の `Total` 正値源、返品/値引の正値、返品税だけの符号、KIN明細、Other、不正JSON=0、負方向Balance、後続月再計算、冪等性を要約した。Rebuildの `DenDay` / `DayTo` 締日照合、不一致時の手動再計算案内と `Msg051`〜`Msg057`送信0件保証、保存行なしの検出限界を記録した。
- D-02/D-03、UAT-05/UAT-06、旧CV.net対応表、次担当指示を実態に更新し、帳票・月次予定表の数値突合、実DB/Mini-UAT、期首残高/移行、D-04/D-05、D-01/D-07/性能/メニュー公開・権限を優先順として明記した。

### 確認
- 関連テスト30/30、TestServer全114/114、`CvBase` / `TestServer` / `CvWpfclient` build成功、独立最終レビュー指摘なしの記録を反映した。本作業は文書2ファイルだけであり、ソース・テスト・完成度以外の文書は変更していない。
- 実画面操作の自動化、帳票・月次予定表の数値突合、実DB/Mini-UAT、`substr(DenDay, 1, 6)` の性能測定は未実施として残した。

## [2026-08-18] Rebuild締日変更ブロックの独立再々レビューP2を修正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- Rebuild要求計画と実WPF送信経路を同一の共有記述子・送信境界へ統一し、締日変更ブロックの送信0件保証を実経路で検証可能にする。

### 実施内容
- `CvBase` の共有Plannerを、`CvFlag`、対象年月範囲、対象月、締日まで展開済みの順序付き要求記述子へ変更した。旧テスト専用のフラグ計画は削除し、在庫、売掛、買掛、請求残、支払残の順序を実行記述子だけで表す。締日0件では売掛・買掛の記述子は残し、該当する請求残・支払残だけを0件とする。
- 締日照会、不一致判定、記述子生成、要求変換、送信コールバックを同じ汎用非同期送信境界に集約した。不一致、照会例外、照会取消では、記述子生成・要求変換・送信コールバックへ進まない。
- `StockKakeUpdateViewModel` は共有記述子を一対一で表示名・`CvMsg`・パラメータへ変換し、実際の `QueryMsgStreamAsync` を共有送信境界のコールバック内へ移動した。画面側に対象別の要求回数展開は残さない。
- 詳細設計9.2、G/W/T-10、テスト方針を、共有記述子と送信境界・締日0件の受入条件へ更新した。

### 確認
- `SummaryKakeDbTests` に、展開済み記述子の完全な順序・年月・締日、締日0件の対象4種、正常時の要求変換・送信順序と件数、不一致・照会例外・照会取消での記述子生成／要求変換／送信0件を追加した。
- `CvBase/CvBase.csproj`、`Tests/TestServer/TestServer.csproj`、`CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。TestServer全114件は成功した。XAMLは未変更。
- WPF実装の `QueryMsgStreamAsync` は `SummaryRebuildRequestDispatchGate.ExecuteAsync` の送信コールバック内だけにあり、共有テストが検証する同一の送信境界を通ることをセルフレビューした。実画面のダイアログ操作・通信取消は自動化用プロジェクトがないため未実施。
- 区分Cの金額集計とRebuild安全策を含むため、独立最終レビュー待ちとする。

## [2026-08-18] Rebuild締日変更ブロックの最終レビュー指摘を修正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 空・不正な保存締日が照会範囲から脱落する問題と、対象別の要求計画・確認文・送信境界の受入不足を解消する。

### 実施内容
- `SummaryUriSei`／`SummaryKaiShi` の物理スキーマ、`DenDay` 索引、生成SQLの `DenDay=DayTo=締日` を確認し、締日照会の対象月を `DayTo` ではなく `DenDay` で絞る共有SQLへ変更した。`DayTo` は nullable DTO として受け、NULL・空・8桁以外・不正年月を安全に不一致化する。
- `CvBase` に要求計画・確認追加文・送信境界を追加し、WPFは共有計画順（在庫、売掛、買掛、請求残、支払残）で要求を組み立てるようにした。締日照会の不一致・例外・取消では要求作成へ進まない。
- 確認文を全て＝請求残・支払残、売掛のみ＝請求残、買掛のみ＝支払残、在庫のみ＝追加文なしへ確定した。
- 詳細設計9.1、G/W/T-10、テスト方針を `DenDay` 範囲選択とNULL・空・不正 `DayTo` の不一致化へ整合させた。

### 確認
- `SummaryKakeDbTests` 29件が成功。`DenDay` 月範囲で空・不正 `DayTo` を両側とも取得すること、範囲外を除外すること、物理 `DayTo` のNULL更新が拒否されること、DTOのNULL判定、4対象の正確なCvFlag順序、確認文、照会不一致・例外・取消時の要求作成0件を確認した。
- `Tests/TestServer/TestServer.csproj` と `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。XAMLは未変更。
- WPFのストリーム送信は共有送信境界が要求を返した後にだけ存在するため、照会中の不一致・例外・取消で送信へ進まないことをテストと構造で確認した。実画面のダイアログ操作・通信取消は未自動化。
- 区分Cのため、独立再々レビュー待ちとする。

## [2026-08-18] Rebuild締日変更ブロックの独立レビュー指摘を修正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- Rebuild締日変更ブロックのサーバ型解決、入力競合、受入テスト不足を解消する。

### 実施内容
- `SummaryClosingCheckRow` と締日判定規則を `CvBase` の公開共有型へ追加し、`StockKakeUpdate` と請求計算／支払計算の締日取得が WPF private 型を `QueryListSqlParam.ItemType` に渡さないよう統一した。
- Rebuild実行は確認直後に対象・年月・対象月をスナップショット化し、締日照会から要求生成・完了表示まで同じ値を使用する。`IsProcessing` は照会前に設定し、照会・送信を単一の `try` / `finally` で保護した。不一致、キャンセル、例外では要求列生成・`Msg051`〜`Msg057`の送信へ進まない。
- 請求残／支払残を含む確認文へ、保存済み集計行がない場合は締日変更を検出できない制約を明示した。
- 既存 TestServer に、共有DTOの実サーバ `Msg101_Op_Query` 型解決、対象4種、1日／31日月末丸め、99月末、保存行なし、不一致、最大5件＋残件数、送信可否ゲートを追加した。月次掛集計には19／29／39／89境界と90／99除外を売掛・買掛対称に追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` と `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- `SummaryKakeDbTests` は25件すべて成功し、共有DTOの実サーバ型解決テストも1件成功した。
- 締日照会の `await` より後にだけ要求列生成・ストリーム送信が存在することを静的に確認した。WPFの確認ダイアログ操作、通信取消、実画面警告は自動化用プロジェクトがないため未実施。
- 区分Cの金額集計とRebuild安全策を含むため、独立再レビュー待ちとする。

## [2026-08-18] Rebuildの締日変更ブロックを追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 締日マスタ変更後の保存済み請求残・支払残を、在庫・掛再更新で無条件に削除・再作成しないようにする。

### 実施内容
- `StockKakeUpdate` は利用者確認後かつ要求列生成前に、対象側の `SummaryUriSei`／`SummaryKaiShi` と現在マスタ締日をパラメータ化照会で照合するようにした。
- 不一致時は最大5件と残件数、手動再計算の案内を警告し、`Msg051`〜`Msg057`を送信せず処理を開始しない。
- 全ては両側、売掛のみ／買掛のみは該当側、在庫のみは検査なしとし、照会・期待締日判定・警告組立を小メソッドへ分離した。

### 確認
- `CvWpfclient/CvWpfclient.csproj` と `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- `SummaryKakeDbTests` を再実行し、21件すべて成功した。
- XAMLは未変更。既存バインディングとApp共通リソースを確認した。WPFの実サーバ照会・警告表示は自動化用プロジェクトがないため未確認。
- 区分C作業のため、独立レビュー待ちとする。

## [2026-08-18] 月次売掛・買掛の確定集計ルールを実装

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 月次売掛・買掛を請求残・支払残と同じ金額列・内訳・残高符号へ統一する。

### 実施内容
- `CalcSummaryUriKake`／`CalcSummaryKaiKake` を、`Total`の正値内訳、返品税だけの符号化、内訳からの合計算出、`TotalIn/TotalOut - TotalSales/TotalShiire`方向の残高へ対称に更新した。
- 入金・支払は有効JSONの明細だけを集計し、ヘッダ`KingakuTotal`を正値源から除外した。05・未知KINはOther、不正JSONは空明細・0として処理を継続する。
- `SummaryKakeDbTests` を売掛・買掛対称に拡張し、区分範囲、99除外、税、残高、後続月、冪等性、KINフォールバック、不正JSONを固定した。テストヘルパーは`Total`／`KingakuTotal`と明細／ヘッダを意図的に異ならせる。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、21件すべて成功した。
- 金額結果を広範囲に変更する区分C作業のため、独立レビュー待ちとする。

## [2026-08-18] 請求・支払計算の不正JSON時の明細集計設計を訂正

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 不正JSONを空配列へ防御する場合に明細金額を復元できないことを、KIN Otherフォールバックと矛盾しない設計へ訂正する。

### 実施内容
- Otherフォールバックを有効JSON内の `Id_Kin=0`、未知・未登録・空・01〜05以外のKINコードに限定した。
- 不正JSONは例外にせず空明細として扱い、その伝票の `TotalIn` / `TotalOut` を0とすること、不正JSONの検知・補正は別のデータ品質課題であることを明記した。
- G/W/T-8とテスト方針を、有効JSONの未知KIN→Other、不正JSON→例外なし・0へ分離した。`KingakuTotal`を正値源にしない方針は維持した。

### 確認
- 詳細設計のSQL集計、請求計算、支払計算、受入、テスト方針の記述を同じ期待値へ統一した。
- 今回は設計文書のみを変更し、ソース実装・テスト・完成度チェックリストは変更していない。

## [2026-08-18] 請求・支払計算のRebuild安全策と月次掛集計ルールを詳細設計へ反映

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 締日変更済みマスタでのRebuild誤更新を防ぎ、月次売掛・買掛の金額列・残高符号を請求残・支払残と整合させる次段階実装の詳細設計を確定する。

### 実施内容
- `StockKakeUpdate` の対象別締日変更ブロック、パラメータ化照会、送信0件保証、警告内容、既存残高行がない場合の検出限界を設計化した。
- `CalcSummaryUriKake`／`CalcSummaryKaiKake` の `Total` 正値集計、正値の返品・値引内訳、税符号、明細だけを正値源とするKIN集計、未知KINのOtherフォールバック、残高符号、JSON防御を具体化した。
- テストデータの `Total`／`KingakuTotal` 分離、Rebuildブロック受入条件、区分Cの独立確認必須を追記した。
- Renbanのmigration既定0と計算生成時の業務既定1を区別し、月別予定表・帳票qfmはスコープ外のままとした。

### 確認
- 文書差分を現行 `SummaryDb`、`SummaryKakeDbTests`、`StockKakeUpdateViewModel` と照合した。
- 今回は設計文書のみを変更し、ソース実装・テスト・完成度チェックリストは変更していない。

## [2026-08-18] 在庫・掛再更新へ請求残・支払残のRebuildを追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 在庫・掛再更新の売掛・買掛再集計後に、同じ対象年月の請求残・支払残も通常再計算で再作成できるようにする。

### 実施内容
- `StockKakeUpdate` が売掛・買掛再集計の完了後、得意先／仕入先マスタに実在する有効締日（1～31日・末日）を取得し、対象年月×締日で `Msg056`／`Msg057` を順次実行するようにした。
- Rebuildから渡す `BillingParameter.IsReissue` は常に `false` とし、既存の請求書番号・連番を保持する通常再計算を使用する。
- 実行確認時に請求残・支払残も再作成することを表示するようにした。

### 確認
- `CvServer/CvServer.csproj`、`CvWpfclient/CvWpfclient.csproj`、`Tests/TestServer/TestServer.csproj` を Development 環境で直列ビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、15件すべて成功した。

## [2026-08-18] 請求書の明示的再発行を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 通常再計算の採番冪等性を維持しつつ、明示的な請求書再発行時だけ請求書連番を更新できるようにする。

### 実施内容
- `BillingParameter.IsReissue` を追加し、請求計算のgRPCストリーミングへ伝播した。
- 請求残の再作成時、通常実行は既存 `SeikyuNo`／`Renban` を維持し、再発行指定時は `Renban` を+1して請求書番号を再採番するようにした。
- 請求計算画面に再発行チェックボックスを追加した。
- 採番維持と再発行連番のテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、15件すべて成功した。
- `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。

## [2026-08-18] 請求計算・支払計算の実行画面を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 得意先／仕入先の締日、計算月、コード範囲を指定して請求残・支払残をストリーミング実行できる画面を提供する。

### 実施内容
- `BillingCalculationView`／`PaymentCalculationView` と共通ViewModelを追加し、実データから動的に取得した締日、計算月、コード範囲、進捗、実行・キャンセルを実装した。
- 請求計算は親子の締日不一致を事前検出し、「マスタ変更および請求再計算が必要」と警告するが、処理はブロックしない。
- 掛管理メニューの請求計算・支払計算を実装済み表示へ更新した。

### 確認
- 追加XAMLのXML構文、名前空間、App共通リソース、ViewModelバインディングを確認した。
- `CvWpfclient/CvWpfclient.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。

## [2026-08-18] 請求・支払計算のgRPCストリーミングを結線

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 請求残・支払残の計算を既存の掛再更新と同じgRPCストリーミング経路から実行できるようにする。

### 実施内容
- `BillingParameter`、`Msg056_SummaryUriSei`、`Msg057_SummaryKaiShi` を追加した。
- `SummaryDb` に請求残・支払残のストリーミング入口を追加し、`QueryMsgStreamService` から結線した。
- 両ストリーミング入口がエラーなく完了通知を返すテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、15件すべて成功した。

## [2026-08-18] 支払残の計算処理を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 指定締日・支払月・仕入先コード範囲から支払残を冪等に作成し、支払予定日を保持する。

### 実施内容
- `SummaryDb.CalcSummaryKaiShi` を追加し、仕入先締日から支払期間を算出して対象仕入先ごとに支払残をDELETE→再作成するようにした。
- 仕入／返品／値引、返品税の符号、支払内訳、累計残、`PayMonth`／`PayDay` による支払予定日を実装した。
- 支払残の期間・内訳・累計残・冪等性・月末予定日を検証するテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、13件すべて成功した。

## [2026-08-18] 請求残の計算処理を追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 指定締日・請求月・得意先コード範囲から請求残を冪等に作成し、請求書番号と入金予定日を保持する。

### 実施内容
- `SummaryDb.CalcSummaryUriSei` を追加し、締日から請求期間を算出して対象得意先ごとに請求残をDELETE→再作成するようにした。
- 売上／返品／値引、返品税の符号、入金内訳、累計残、`PayMonth`／`PayDay` による入金予定日を実装した。
- 通常再計算では既存の `SeikyuNo`／`Renban` を維持し、未採番時は連番1で採番するようにした。
- 請求残の期間・内訳・累計残・採番維持・予定日を検証するテストを追加した。

### 確認
- `Tests/TestServer/TestServer.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。
- Microsoft.Testing.Platform の実行形式から `SummaryKakeDbTests` を実行し、11件すべて成功した。

## [2026-08-18] 請求・支払計算の請求残／支払残スキーマを追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 請求計算・支払計算の詳細設計に基づき、請求書番号・連番・入金予定日・支払予定日を保持できるようにする。

### 実施内容
- `SummaryUriSei` に `SeikyuNo`、`Renban`、`NyukinYoteiDay` を追加した。
- `SummaryKaiShi` に `ShiharaiYoteiDay` を追加した。
- `UpdateDb` の `26_08_18_02` で既存DBへ同列を追加し、既存行は空文字または0で初期化する。

### 確認
- `CvBase/CvBase.csproj` を Development 環境でビルドし、警告・エラーなしを確認した。

## [2026-08-18] 請求・支払計算の集計・締日ルールを詳細設計へ反映

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 請求・支払計算と掛月次集計について、締日、対象期間、区分別内訳、累計残高、税率・丸めのユーザー決定を詳細設計へ反映する。

### 実施内容
- 得意先・仕入先の締日をそれぞれ `MasterTokui.Shime1` / `MasterShiire.Shime1` と明記し、`PayMonth`/`PayDay` は予定日の算出専用とした。
- `SummaryUriKake` / `SummaryKaiKake`、`SummaryUriSei` / `SummaryKaiShi` の区分別集計、合計式、対象期間分のみを保持する内訳、対象期間までの累計 `Balance` を明文化した。
- 税は取引の `Tax` を集計し、返品は `CalcFlag` により負値とすること、1.0の新規税額算出は `MasterSysMan` の `No=1` の `Tax` と四捨五入を使うことを記載した。
- 通常再計算・Rebuild時の採番維持と、明示的再発行時だけの `Renban` 増加を明記した。

### 確認
- Markdownの見出し・表・用語を確認し、`git diff --check` を実行する。
- 文書のみの変更のため .NET build/test は省略する。

## [2026-08-18] CV10機能完成度チェックリストを現行メニュー基準で再作成

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- 2026-08-12版の機能完成度チェックリストを履歴として保存し、最新コミットと `CvWpfclient/Models/MenuData.cs` を基準に2026-08-18版を新設する。
- 実装済み機能の詳細列挙から、1.0/1.1/1.2以降の計画、必要な仕様決定、最小UAT、旧CV.net機能の継承計画を中心とする文書へ改める。
- 後続担当（Luna等）が、請求・支払計算から設計・実装・検証へ着手できる引継ぎ粒度にする。

### 調査根拠
- HEAD `c0dc00c`（請求計算・支払計算の詳細設計。実装未着手）。
- `CvWpfclient/Models/MenuData.cs` の16大メニュー、232表示参照、重複を除く212 View。
- `Doc/spec/2026-08-18_請求計算・支払計算_詳細設計.md` と2026-08-18付の各詳細設計。
- `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md`、未適用・保留課題台帳。
- `refer/cvnet-knowlege/` のマニュアル、業務フロー、帳票集、DB定義の既存調査結果。

### 実施内容
- `Doc/spec/2026-08-18_CV10機能完成度チェックリスト.md` を新設した。
- 2章を現行メニューの規模、1.0完成までの断絶、期別ロードマップ、優先順位へ変更した。
- 3章を16大メニュー別に再構成し、各領域を「現在地 / 1.0実装予定 / 1.1予定 / 1.2以降・決定」で整理した。
- 4章以降を大幅に再構成し、請求・支払詳細設計の7段階、月次・原価・残高登録、15件の仕様決定、最小UAT 9シナリオ、旧CV.net資料別の期別計画、後続担当の開始/停止条件、既知リスクを記載した。
- 請求のDELETE→再作成による冪等性と、再実行時の`Renban+1`が競合する点をD-03として明示し、通常再計算と明示的再発行の分離を推奨した。
- ユーザー指示により、LCVの全9画面・移行・セキュリティ審査・UATを1.1へ配置した。
- ユーザー指示により、最大3締日と月末以外締日の税対応を1.2以降へ配置し、1.1の締日更新は1得意先1締日の運用制御として残した。
- 2026-08-12版チェックリストは変更せず履歴として保持した。

### ログアーカイブ
- 旧 `Doc/aicoding_log.md`（945行）を `Doc/aicoding_log_013.md` へ移動した。
- アーカイブ作成後、今回分だけを記載した新しい `Doc/aicoding_log.md` を作成した。

### 確認
- 文書内の大メニュー名と表示参照数を `MenuData.cs` から再集計した。
- LCVが1.1、最大3締日が1.2以降で一貫していることを検索確認した。
- Markdown見出し、表、相対リンク、UTF-8、CRLF、`git diff --check` を確認した。
- 文書とログのみの変更であるため、.NET build/testは省略する。

### 残る重要判断
- D-01: 1.0の月次・原価範囲。
- D-03: 通常再計算と請求再発行の採番・冪等性。
- D-05: 適格請求書要件。
- D-06/D-07: 総平均原価の`TQ`と月次処理順。
- D-08/D-09: 期首残高移行とMini-UAT責任者。
- D-14: 1.1 LCVの個人情報、ポイント会計、返品・失効、移行責務。
## [2026-08-18] McpOracleをOracle接続用MCPサーバとして追加

### Agent
- GPT-5 : OpenAI : Sekiya Sato Codex

### 目的
- `McpSql` と同じ stdio MCP サーバ構成で、Oracle 接続文字列を引数または環境変数から受け取り、スキーマ参照・照会・任意時の更新を行える `McpOracle` プロジェクトを追加する。

### 設計・実装
- `McpOracle/McpOracle.csproj` を新設し、既存の中央パッケージ管理から `ModelContextProtocol.Core` と `Oracle.ManagedDataAccess.Core` を参照する。
- 起動引数の第1非オプション引数、または `MCPORACLE_CONNECTION_STRING` を接続文字列として利用する。`--allow-write` を指定しない既定時には更新ツールを MCP に公開しない。
- `list_tables` / `describe_table` / `list_indexes` は `USER_OBJECTS`、`USER_TAB_COLUMNS`、`USER_CONSTRAINTS`、`USER_INDEXES` などの `USER_*` データディクショナリを使用し、接続ユーザー所有のオブジェクトに限定する。
- `query` は単文の `SELECT` または `WITH ... SELECT` に限定し、行数・応答サイズ・セルサイズを上限管理する。値は `:p0`、`:p1` 形式でバインドする。
- `explain` は `EXPLAIN PLAN FOR` と `DBMS_XPLAN.DISPLAY()` を使用する。DDL は `DBMS_METADATA.GET_DDL` が許可されない環境でも、列・制約情報を返せるようにする。
- 接続文字列・パスワードをログや応答へ出力しない。読取り時の SQL 検証に加え、Oracle アカウント権限を最終的なアクセス制御境界とする。
- `creativevision10.slnx` に `McpOracle` を追加した。

### 確認
- `C:\gitroot\UT\vscmd.bat dotnet build McpOracle\McpOracle.csproj` 成功（警告 0、エラー 0）。
- 引数なし起動で、接続文字列の指定方法を stderr に表示して終了することを確認した。
- `git diff --check` を実行した。

### 使用例
- `McpOracle.exe "Data Source=192.168.9.243/cvnet;User Id=CV00PKG;Password=CV00PKG;"`
- 環境変数: `MCPORACLE_CONNECTION_STRING` に同じ接続文字列を設定して `McpOracle.exe` を起動する。
- 更新を許可する場合: 上記に `--allow-write` を追加する。
