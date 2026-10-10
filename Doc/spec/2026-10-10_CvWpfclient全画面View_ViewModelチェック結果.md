# CvWpfclient 全画面 View/ViewModel チェック結果

| 項目 | 内容 |
|---|---|
| 確認日 | 2026-10-10 |
| 調査対象HEAD | feabce1d（作業差分あり。未commit。CvWpfclient 197ファイル変更） |
| 対象 | MenuData の全187画面＋Sub（選択・入力ダイアログ）、MainMenu。View（xaml/xaml.cs）と ViewModel、関係する共通基底 |
| 方法 | 6グループ（G1〜G6）に分けた静的レビュー。加えて UatVm の menulayout シナリオで全画面を自動で開き、崩れを判定し、JPGを目視 |
| 修正方針 | (1) 局所的なデザイン修正と、挙動を大きく変えないロジック修正（条件変更時の旧結果無効化、例外catch、二重実行ガード、null防止など）だけを行った<br>(2) 帳票値・金額・税・在庫の計算結果が変わるもの、サーバ・トランザクション・スキーマ・qfm・仕様解釈が要るものは修正せず「要指示」とした<br>(3) 判断に迷うものは修正していない |
| 検証 | `creativevision10.slnx` のビルドでエラー0・警告0。`git diff --check` で問題なし |

グループの担当範囲:

| グループ | 対象フォルダ |
|---|---|
| G1 | 00System / 01Master |
| G2 | 02Yosan / 03Hatchu / 04Juchu |
| G3 | 05Shiire / 06Uriage |
| G4 | 07Haibun / 08Zaiko（07Haibun のロジック指摘は G4-H-nn） |
| G5 | 20UriageAnalysis / 21OroshiAnalysis / 30HHT |
| G6 | 31Monthly / 32LoyalCustomer / 40Shop / 41Logistics / Sub / MainMenu |

## 1. 結果サマリ

| グループ | 指摘数 | 高 | 中 | 低 | 修正済 | 要指示 | 見送り |
|---|---|---|---|---|---|---|---|
| G1 | 72 | 6 | 36 | 30 | 56 | 11 | 5 |
| G2 | 26 | 6 | 15 | 5 | 15 | 10 | 1 |
| G3 | 29 | 8 | 12 | 9 | 14 | 13 | 2 |
| G4 | 32 | 2 | 16 | 14 | 22 | 10 | 0 |
| G5 | 28 | 3 | 16 | 9 | 10 | 14 | 4 |
| G6 | 24 | 3 | 9 | 12 | 21 | 3 | 0 |
| 計 | 211 | 28 | 104 | 79 | 138 | 61 | 12 |

- 修正済には一部修正を含む（G1-19、G1-20、G1-40、G1-69、G3-16、G3-26、G4-02、G6-16）。残った部分のうち判断が要るものは、6章の残作業に含めた。
- G2-18 と G4-19 の残り（共通基底の修正）は、グループの担当外だったため親で修正した。G2-18 は修正済に数えている。
- 見送りのうち G1-09、G1-18、G1-62 は担当外の共通ファイルの修正が必要なため、6章の残作業に回した。

## 2. 実行時レイアウト検査（UatVm menulayout）

### 2.1 ベースライン（修正前）

| 項目 | 値 |
|---|---|
| 画面数 | 187 |
| 開けなかった画面 | 0 |
| 崩れありと判定された画面 | 52 |

### 2.2 検出の傾向と対応

| 検出の種類 | 判断 | 内容 |
|---|---|---|
| ラベル文字切れ（入力欄の左上付近） | 誤検知 | MaterialDesign の浮動ヒント（0.72倍に縮小した見出し）を測っていた。JPGではフォームのラベルは全文表示されている |
| ウィンドウ外の TextBox/Button | 誤検知 | ScrollViewer 内でスクロール外にある項目を検出していた |
| 列が横スクロール外 | 誤検知 | DataGrid を横スクロールすれば到達できる |
| 検索ボタンが「枠で切れ」 | 誤検知 | MenteSearchTextBox 内の検索ボタン。JPGでは正常に表示 |
| 幅と必要幅が同じボタン（印刷実行 80/80 など） | 誤検知 | JPGでは切れていない |

実際に直した崩れ:

| 画面 | 修正 |
|---|---|
| ImportTemplateCreateView | 列見出し「降順？」の列幅を 54→64 |
| UriageCashTypeReportView | 店舗名の列を `Width="*" MinWidth="140"` にし、標準幅で「差額」列まで収めた |
| StockIdoInputView / StockForceInputView / StockInputListView | 列見出し「ｻｲｽﾞCD」の列幅を 70→80 |
| HhtErrorDataInputView | 日付・店舗の列幅を 90→110、伝票No・掛率の列幅を 120→170 |
| 31Monthly（諸掛確認・最終仕入原価更新・総平均原価更新・評価替・積送中クリア） | 切れていた列見出しの列幅を拡大 |
| IntegrationDataManualTransmitView | 最小幅で「対象取得」がウィンドウ外に出るため、MinWidth を 1010 に変更 |

### 2.3 修正後の再検査結果

| 項目 | ベースライン | 修正後 |
|---|---|---|
| 画面数 / 開けなかった画面 | 187 / 0 | 187 / 0 |
| 崩れありと判定された画面 | 52 | 104 |
| うち悪化と判定された画面 | - | 61（すべて下記の誤検知・既知事項） |
| 改善した画面 | - | 9（InTransitClear、SundryChargesUpdate、CostRevaluation、StockIdo/StockForce/StockInputList など） |

- 増加の大半（55画面）は「ボタン文字切れ」。帳票条件画面を ScrollViewer で包んだことで「印刷実行」等の必要幅と表示幅が同値（80/80 等）の境界判定になったもの。JPGでは全文表示されており誤検知。
- 残りは「列が横スクロール外」（StockTake 開始/確定の「状態」、IntegrationDataManualTransmit の「メモ」など）で、横スクロールで到達できる。
- 1回目の再検査で、追加した ScrollViewer の横スクロールが `Auto` のため注記の折り返しが効かず右端で切れる回帰を9画面で検出した。追加した58箇所を `HorizontalScrollBarVisibility="Disabled"` に変更し、2回目の再検査とJPG目視で折り返し・入力欄位置がベースライン相当に戻ったことを確認した。
- 棚卸開始/確定のラベル「棚卸日未設定店舗の対象月」の末尾切れ（修正前から存在）はラベル列 160→230 で解消（フィルタ再撮影で目視確認）。BalanceRegistration の最小幅での「備考」見出し切れは MinWidth=60 で解消。

## 3. 修正内容

### 3.1 G1（00System / 01Master）

| ID | 画面 | 修正内容 |
|---|---|---|
| G1-02 | SysGeneralMente | テーブルの再選択時に旧テーブルの行・選択・件数を破棄し、旧行を新しい型で保存できないようにした |
| G1-06 | ExternalCsvImport | 取込に成功したら取込データをクリアし、同じCSVを二重に登録できないようにした |
| G1-07 | ConvertDb / ConvertSelected / StockKakeUpdate | 実行中は［戻る］［キャンセル］とヘッダの←を無効にした |
| G1-08 | Login | 稼働中のNICがないときのIPアドレス null を防止し、情報取得を try の中へ移した |
| G1-10 | SysLogin | ［パス暗号化］ボタンの文字切れを解消 |
| G1-11 | SysExecMisc | 「マニュアル排他制御クリア」ボタンを MinWidth にして文字切れを解消 |
| G1-12 | StockKakeUpdate | 注記を Grid の別の行に移して折り返すようにした |
| G1-13 | SysSchedulerCronEdit | プリセットボタンと書式説明の行を分け、重なりを解消 |
| G1-14 | SysUpgrade | 状態表示とメッセージを折り返し、ボタンが切れないようにした |
| G1-15 | ConvertSelected | プログラム名の列に省略表示とツールチップを付けた |
| G1-16 | MasterShainMente / MasterEndCustomerMente | 店舗・部門の選択をキャンセルしたとき、既存の値を消さないようにした |
| G1-17 | Shain / Shohin / Tokui / Shiire / EndCustomer の各マスタ | 名称リスト（Jsub）を選び直したとき、Sid も更新するようにした |
| G1-19 | MasterSysKanriMente | 高さ30固定の TextBox を折り返しなしにした（一部。列幅の見直しと共通スタイル化は見送り） |
| G1-20 | ExternalCsvUpdate | 中断・例外時に更新済み件数を表示し、エラー行の行番号を保持し、送信後の再実行を防止した（一部。一括の原子性は残作業） |
| G1-21 | ImportTemplateCreate | 対象日付・並び順を変えたら取得済みデータを破棄するようにした |
| G1-22 | MasterJouDaiBulkChange | 処理中は各コマンド（Fキーを含む）を実行できないようにした |
| G1-23 | MasterJouDaiBulkChange | 伝票の組立・設定取得を try と StartBusy の内側へ移した |
| G1-24 | MasterJouDaiBulkChange | 競合チェックに失敗したら確定を中止するようにした |
| G1-27 | MasterJouDaiBulkChange | 一括操作の値にカンマを許可し、不正値なら中止するようにした |
| G1-28 | MasterJouDaiBulkChange | ②適用範囲タブで対象店舗の一覧に最小の高さを確保した |
| G1-29 | MasterJouDaiBulkChange | ④確認タブの Timeline をスクロール可能にした |
| G1-30 | MasterJouDaiBulkChange | 説明文と操作ボタンの列を分け、説明文に省略表示を付けた |
| G1-32 | Shohin / Tokui / Shiire / EndCustomer / Shain の各マスタ | Jdetail が null のレコードでも振込先・予備項目を入力できるようにした |
| G1-34 | MasterShohinMente | 原価履歴の取得中に商品を切り替えたら、古い結果を破棄するようにした |
| G1-36 | Shohin / Tokui / Shiire / EndCustomer の各マスタ | 行内の検索ボタンが、押した行へ直接書き込むようにした |
| G1-37 | MasterEndCustomerMente | 都道府県・市区町村を1行1項目にして、ラベルと入力欄を対応させた |
| G1-38 | Tokui / Shiire / EndCustomer の各マスタ | ラベル列を広げ、7文字のラベルの切れを解消 |
| G1-39 | MasterJouDaiBulkChange | 対象区分の変更・明細取得・Scope増減のときにプレビュー・競合一覧・Timeline の候補を作り直すようにした（旧系統を指す Scope の自動削除はしない） |
| G1-40 | SysGeneralMente | 未保存の新規行があるときは再読込の前に確認するようにした（一部。既存行の未保存編集は検出しない） |
| G1-41 | SysSchedulerJobMente | 処理中は起動時間変更・実行切替・メール切替を無効にした |
| G1-42 | SysUpgrade | 「今すぐ更新」ボタンに MaterialDesign のスタイルを適用 |
| G1-43 | Login | リフレッシュタブのキャンセルボタンを IsCancel にした |
| G1-44 | Login / SysSetConfig | 未定義のスタイルキー MaterialDesignBody2 を MaterialDesignBody に置換 |
| G1-45 | SysAutoExecMailConfig / SysSchedulerJobMente | ×で閉じたときも専用の GrpcChannel を Dispose するようにした |
| G1-46 | SysAutoExecMailConfig | 処理中は F5 の再読込を無効にした |
| G1-47 | SysExecMisc | Test01 に例外の catch を追加 |
| G1-48 | ConvertDb / ConvertSelected | キャンセル以外の例外をログ欄に出すようにした |
| G1-49 | SysLoginHistory | 一覧取得を連続したとき、古い応答を破棄するようにした |
| G1-50 | SysLoginHistory / SysAutoExecHistory | 重複していた GridSplitter を削除 |
| G1-51 | SysTableSpec | 共通スタイルを上書きしていたローカルの FormLabel を削除 |
| G1-52 | SysAutoExecMailConfig / SysSchedulerJobMente | 円形プログレスの崩れを修正 |
| G1-53 | StockKakeUpdate / SysSetConfig | 「キャンセル」ボタンを MinWidth にした |
| G1-54 | SysGeneralMente | 一覧と編集ヘッダのタイトルに省略表示とツールチップを付けた |
| G1-55 | MasterMaterialMente / MasterShohinMente | 税区分の取得に失敗しても一覧取得を続けるようにした |
| G1-56 | PrintMasterShainCard | 社員Codeの選択をキャンセルしたとき、入力済みの値を消さないようにした |
| G1-57 | MasterPrintBarcode | LIKE 条件の値をエスケープした |
| G1-60 | ExternalCsvImport / ExternalCsvUpdate | エラー詳細の列を折り返し・ツールチップ付きにした |
| G1-61 | PrintMasterShainCard | バーコード種類の欄の位置をラベル行に揃えた |
| G1-63 | MasterJouDaiBulkChange | IME 無効の指定を TextBlock から TextBox へ移した |
| G1-64 | MasterJouDaiBulkChange | Scope・店舗の日付が yyyyMMdd として正しいか検査するようにした |
| G1-65 | MasterJouDaiBulkChange | 伝票の読込をすべて終えてから一括で反映し、失敗時に半端な状態を残さないようにした |
| G1-67 | MasterShohinMente | 固定幅の内側 Grid が切れないよう、横スクロールを付けた |
| G1-68 | Shohin / Tokui / Shiire / EndCustomer / Shain の各マスタ | 行内の検索ボタンの直書きの白を DynamicResource に置換 |
| G1-69 | MasterShiireMente | FontSize・タブアイコン・ラベル列幅を商品マスタに合わせた（一部。ウィンドウの標準サイズは基準が未確定のため見送り） |
| G1-70 | MasterEndCustomerMente | 名称リストのラベルとボタンの重なりを解消し、ScrollViewer を付けた |
| G1-71 | MasterTokuiMente / MasterShiireMente | 種別を名称で表示し、数値欄を右寄せの NumericFormTextBox にした |

### 3.2 G2（02Yosan / 03Hatchu / 04Juchu）

| ID | 画面 | 修正内容 |
|---|---|---|
| G2-04 | ShopBrandBudgetMaster / SalesStaffBudgetMaster | 作成・読込時の対象キーを保持し、保存時にキーが一致しなければ警告して中止するようにした |
| G2-05 | ShopBrandBudgetMaster / SalesStaffBudgetMaster | コードを手入力したら Id と名称をクリアし、再選択を必須にした |
| G2-10 | ShopBrandBudgetMaster / SalesStaffBudgetMaster | 処理中は各コマンドを実行できないようにした |
| G2-11 | HachuHaibunInput | 上代の取得を try と StartBusy の内側へ移した |
| G2-12 | HachuHaibunInput | 登録後の再読込の失敗は「登録済み・再表示失敗」と表示し、古い Vdu での再登録を防ぐため対象をクリアするようにした |
| G2-13 | DailyShopBudgetQuery | 年月の誤りと、条件パネル表示中のデータなしをダイアログで通知するようにした |
| G2-14 | DailyShopBudgetQuery | データ0件でも条件サマリを更新し、CSVのファイル名に表示中の結果の年月を使うようにした |
| G2-15 | DailyShopBudgetQuery | 固定の文字色を背景色付きの列だけに限定し、合計グリッドの見出しを共通スタイルにした |
| G2-16 | DailyShopBudgetQuery | 店舗列をインデクサ形式のバインディングにし、名称に「. / ( [」を含んでも表示されるようにした |
| G2-17 | ShopBrandBudgetMaster / SalesStaffBudgetMaster | 名称に省略表示とツールチップを付けた |
| G2-18 | 帳票画面12画面（BaseReportViewModel） | 必須の日付が空欄のとき警告を出すようにした（親で修正） |
| G2-20 | HachuInput | リードタイムなどの非同期取得の結果を、取得元の伝票と一致するときだけ反映するようにした |
| G2-21 | HachuHaibunInput | 全角数字を正規化し、不正値・負数は受け付けず直前の値に戻すようにした |
| G2-23 | DeliveryScheduleInquiry / NouhinYoteiTable / HachuZanCompletionSetting / JuchuZanCompletionSetting | 列見出しを共通の MenteDataGridColumnHeader にした |
| G2-24 | HachuForm / HachuHaibunInput | 伝票Noが数値でなければ警告して中止するようにした |

### 3.3 G3（05Shiire / 06Uriage）

| ID | 画面 | 修正内容 |
|---|---|---|
| G3-07 | HenpinInput | 仕入先・倉庫の選択を変えたら一覧・合計・登録済みNoをクリアするようにした |
| G3-13 | ShopUriageInput / ShukkaUriageInput | 明細の RowDetails の金額を、保存値 Kingaku の表示にした |
| G3-16 | ShiireInput / MaterialInput / ShopUriageInput / ShukkaUriageInput | 投げっぱなしの税再計算の例外を捕捉し、Message に出すようにした（一部。保存前の await と税率キャッシュの直列化は残作業） |
| G3-17 | HenpinInput | 登録済みの一覧で再実行するときに確認するようにした |
| G3-18 | UriageCashTypeReport | 集計行と合計を long にした |
| G3-19 | HenpinInput | 仕入先が引けないとき、掛率と税計算単位を既定値に戻すようにした |
| G3-21 | PosDailySeisanInput | 検索条件の店舗に「解除」ボタンを追加した |
| G3-22 | ShiireSlipPrint | json_each に json_valid のガードを付け、期間の逆転を警告するようにした |
| G3-23 | ShiharaiInput / NyukinInput | ヘッダの Grid をラベル列（Auto）と入力列で組み直し、Margin による重ね合わせと固定の高さをやめた |
| G3-24 | HenpinInput | 操作ボタン行を左右の列に分け、注意書きがボタンの下に潜らないようにした |
| G3-25 | 05Shiire・06Uriage の帳票22画面 | 条件 Grid を ScrollViewer（縦 Auto）で包んだ |
| G3-26 | ShiharaiMatching / NyukinMatching | DataGrid に共通スタイル・見出し・右寄せを適用し、掛計上日を yyyy/MM/dd 表示にした（一部。ラベルの共通化と条件領域のスクロール化は見送り） |
| G3-28 | ShopUriageInput | 単価・上代・下代の列幅を100に広げ、金額の途中省略を防いだ |
| G3-29 | ShiharaiInput / NyukinInput | 日付列の幅を120にし、金額列を編集中も右寄せにした |
| 実行時検出 | UriageCashTypeReport | 店舗名の列を伸縮可能にし、標準幅で「差額」列まで収めた |

### 3.4 G4（07Haibun / 08Zaiko）

| ID | 画面 | 修正内容 |
|---|---|---|
| G4-02 | CustomerReservationAllocationInput | POS注意バナーを DockPanel にして折り返すようにした（一部。固定色は残作業） |
| G4-03 | InventoryAllocationInput / PurchaseReceiptAllocationInput / SalesOrderAllocationInput | 在庫不足の強調色を NegativeForegroundBrush に置換 |
| G4-04 | InventoryAllocationInput / PurchaseReceiptAllocationInput | ボタン行を WrapPanel にした |
| G4-05 | StockForceHistory | 条件行を WrapPanel にし、［取消］が切れないようにした |
| G4-06 | HaibunCommit | 条件・ボタン行を WrapPanel にした |
| G4-07 | ShippingConfirmDetailPrint と 08Zaiko の帳票10画面 | フォーム Grid を ScrollViewer で包んだ |
| G4-08 | 配分・照会など7画面 | ヘッダを DockPanel にし、説明文に省略表示とツールチップを付けた |
| G4-11 | StockInputList / StockIdoInput / StockForceInput（BaseStockSheetInputViewModel） | 倉庫コードを変えたら一覧・IdSoko・合計を破棄するようにした |
| G4-12 | StockInputList / StockIdoInput / StockForceInput | 登録済みの一覧で再登録するときに確認するようにした（基底に ConfirmReRegisterIfRegistered を追加） |
| G4-15 | ShohinHistoryQuery | 色・サイズ名を (伝票Id, SKU) で引くようにした |
| G4-19 | StockInput / BaseIdoInputViewModel | 明細の PropertyChanged の二重購読を防止した（基底側は親で修正） |
| G4-20 | ZaikoQuery | 検索の実行時に在庫明細タブを閉じるようにした |
| G4-H-01 | InventoryAllocationInput | 倉庫コードの変更時・別倉庫での再検索時・読込失敗時に配分入力の状態を破棄し、登録は配分入力タブの表示中だけにした |
| G4-H-02 | SalesOrderAllocationInput / PurchaseReceiptAllocationInput | 倉庫・商品コードを変えたらマトリクス・対象Id・既存配分を破棄するようにした |
| G4-H-03 | SalesOrder / Purchase / JuchuHaibun / Inventory の配分入力 | 読込が例外で中断したら状態を破棄するようにした |
| G4-H-06 | InventoryAllocationInput / PurchaseReceiptAllocationInput | 画面に表示できない既存配分があるとき警告するようにした |
| G4-H-07 | JuchuHaibunInput | 数量入力と同数展開で負数を0にした |
| G4-H-08 | ShippingConfirmList | 表示種別・倉庫コードを変えたら一覧を破棄するようにした |
| G4-H-09 | 配分入力5画面 | 保存後の再読込を別の try に分け、失敗時は状態を破棄して再登録させないようにした |
| G4-H-10 | CustomerReservationAllocationInput / ShippingConfirmDetailPrint | 例外の catch を追加 |
| G4-H-11 | 配分照会（BaseHaibunInquiryViewModel） | 検索時にドリルタブを閉じるようにした |
| G4-H-12 | Inventory / Purchase / SalesOrder の配分入力 | 処理中は検索を実行しないようにした |
| 実行時検出 | StockIdoInput / StockForceInput / StockInputList | 列見出し「ｻｲｽﾞCD」の列幅を80にした |

### 3.5 G5（20UriageAnalysis / 21OroshiAnalysis / 30HHT）

| ID | 画面 | 修正内容 |
|---|---|---|
| G5-04 | IdoDetailBookPrint / IdoSokuDetailBookPrint | 伝票NOが数値でなければ警告して中止するようにした |
| G5-09 | HhtDataUpdate | 完了後の件数を ErrorMsg 条件なしで数え直し、エラー件数を正しく出すようにした |
| G5-10 | HhtDataUpdate | 条件を変えたら対象件数を「未集計」の表示にした |
| G5-11 | HhtErrorDataInput | 日付が不正なら警告して読込を中止するようにした |
| G5-14 | HhtManualDataReceive | ファイル移動を別の try に分け、「登録済み・移動失敗」と再受信しないことを案内するようにした |
| G5-15 | HhtMasterDataCreate | 一時ファイルに書いてから置き換えるようにし、失敗時に壊れたマスタを残さないようにした |
| G5-18 | SalesQuickReport（40Shop の派生を含む） | 集計対象を必要な期間に限定した（結果は変わらない） |
| G5-20 | 20UriageAnalysis・21OroshiAnalysis・30HHT の帳票25画面 | 条件 Grid を ScrollViewer で包んだ |
| G5-21 | HhtErrorDataInput | 数量・単価の列を右寄せにした |
| G5-28 | HhtErrorDataInput | 日付・店舗・伝票No・掛率の列幅を広げ、セルと見出しの切れを解消した |

### 3.6 G6（31Monthly / 32LoyalCustomer / 40Shop / 41Logistics / Sub / MainMenu）

| ID | 画面 | 修正内容 |
|---|---|---|
| G6-01 | StockTakeInitiation / StockTakeFinalization | 対象月・倉庫を変えたら一覧を破棄し、実行時に取得時の条件と一致するか検査し、確認ダイアログに対象月を出すようにした |
| G6-02 | CostRevaluation | 掛率・固定額・端数・抽出条件を変えたら確認済みの状態を破棄するようにした |
| G6-03 | SelectTranWin | 金額列を long で読み、常に0になる不具合を修正 |
| G6-04 | 原価更新・請求/支払計算・棚卸の7画面 | 年月入力を UpdateSourceTrigger=PropertyChanged にし、Enter で旧い月のまま実行されないようにした |
| G6-05 | BillingCalculation / PaymentCalculation | 実行前の締日不一致の照会を try の中へ移した |
| G6-06 | PointSummary | 再計算と失効を相互に排他し、キャンセルを実行中のコマンドへ振り分けるようにした |
| G6-08 | SelectWin / SelectKubun | 0件のときに空の MasterMeisho を返さないようにした |
| G6-09 | WebPdf | 閉じた後の CoreWebView2 参照で例外が出ないようにした |
| G6-10 | BalanceRegistration | 締日を変えたら検証結果を破棄するようにした |
| G6-11 | StockTakeInitiation / StockTakeFinalization | リサイズ可能にし、MinWidth 1000・MinHeight 700 を設定した |
| G6-12 | InTransitClear | 「積送中クリア実行」ボタンを MinWidth にし、列見出しの切れも解消した |
| G6-13 | 請求/支払計算・棚卸・ポイント再計算/失効 | ストリームが完了通知なしで終わったとき「完了」と表示しないようにした |
| G6-14 | 原価更新系（BaseCostUpdateViewModel） | 処理中は状態更新・確認・更新を相互に実行できないようにした |
| G6-15 | CostRevaluation | 適用時点・対象月を変えたら対象期間の表示を更新するようにした |
| G6-16 | SelectMultiWin | エラー応答を検査し、中止をエラー扱いしないようにした（一部。(3)は残作業） |
| G6-17 | PointMasterSearchParam | 呼出元の条件を複製して渡し、キャンセル時に条件が書き換わらないようにした |
| G6-18 | SelectKubun | 送信する型を DataType の QueryListSimpleParam に揃えた |
| G6-19 | 棚卸（BaseStocktakeViewModel） | 店舗名の照会でエラー応答を検査するようにした |
| G6-20 | InputBarcode | 中止時に例外を再送出しないようにした |
| G6-21 | BalanceRegistration | キー日付の入力遅延（Delay）をやめた |
| G6-22 | BalanceRegistration | MinWidth 1000・MinHeight 680 を設定した |
| 実行時検出 | 31Monthly の5画面 / IntegrationDataManualTransmit | 列見出しの列幅を拡大し、IntegrationDataManualTransmit の MinWidth を 1010 にした |

### 3.7 親（全体）で行った修正

| 対象 | 修正内容 |
|---|---|
| Helpers/ViewModels/BaseReportViewModel.cs | G2-18: 必須の日付が空欄のとき警告する |
| Helpers/ViewModels/BaseIdoInputViewModel.cs | G4-19の残り: 明細の PropertyChanged の二重購読を防止 |
| ViewModels/07Haibun/PurchaseReceiptAllocationInputViewModel.cs | 文字列リテラルの構文エラーを修正 |
| ViewModels/31Monthly/CostRevaluationViewModel.cs | 部分メソッドのシグネチャの警告を修正 |
| 変更ファイル全体 | 改行を CRLF に統一 |
| 帳票条件画面（追加した ScrollViewer 58箇所） | 再検査で見つかった注記の折り返し切れの回帰を、横スクロール Disabled で修正 |
| StockTakeInitiation/FinalizationView、BalanceRegistrationView | ラベル列 160→230、備考列 MinWidth=60 |
| Doc/test/UatVm（menulayout シナリオ追加） | MenuData 全画面を開き、標準/最小サイズでJPG保存と表示崩れ判定を行う。`--out` `--filter` 対応 |

## 4. 見送り

| ID | 画面 | 理由 |
|---|---|---|
| G1-09 | SysLogin（有効期限） | 共通の DateTimeYmdHmsConverter の変更が必要で担当外。日付だけを入れたときの時刻は仕様判断になるため残作業へ |
| G1-18 | MasterMaterialMente ほか | カンマを許す共通コンバータの追加が必要で担当外。残作業へ |
| G1-35 | MasterShohinMente（掛率） | LostFocus にすると、Fキーで保存したとき入力中の値が反映されないリスクがある。基底の確定処理か変換器の設計変更が必要 |
| G1-62 | MasterJouDaiBulkChange（直書きの色） | 置き換え先の DynamicResource がない。新しいリソースの追加は担当外のため残作業へ |
| G1-66 | MasterJouDaiBulkChange（SKU数） | 件数SQLの作り直しが必要（変数の上限への対策は設計判断） |
| G2-22 | 02Yosan/03Hatchu/04Juchu の帳票 | 最小高480でも削られるのは下の余白だけで、操作ボタンは隠れない見込み。実行時検出でも崩れ0。全行の再インデントを伴う |
| G3-12 | ShukkaUriageInput | 誤検知。軽量列の次の行で Tax1-3・Total を取得済み |
| G3-27 | ShopUriageInput / ShukkaUriageInput | 背景の PrimaryButtonColor は Light/Dark とも濃色なので白文字が適正。置き換えるとかえってコントラストが下がる |
| G5-16 | HhtMasterDataCreate | 誤検知。実画面で文字切れなし |
| G5-17 | HhtManualDataReceive | 誤検知。実画面で文字切れなし |
| G5-25 | CorporateInOutReport | 性能だけの低重要度の指摘で、SumMonth の型・書式が未確認のままSQLを変えるため |
| G5-27 | HhtManualDataReceive | DB の BackupFileName と名前を一致させる必要があり、登録前の名前決定まで変更が及ぶため |
| 実行時検出 | 2.2 の誤検知 | 浮動ヒント・ScrollViewer 内・DataGrid の横スクロール・検索ボタンの検出は誤検知 |

## 5. 未実施・残余リスク

- 棚卸確定（StockTakeFinalization）は最小サイズ 1000x700 で一覧領域がほぼ無くなる（固定サイズからリサイズ可能に変更したため新たに測定された）。最大化すれば使用可。最小高さを上げるかは小さい画面（768）との兼ね合いで要判断。
- ScrollViewer 化した帳票条件画面の「ボタン文字切れ」判定（境界値の誤検知）は検出器側の閾値調整で解消できるが、今回は未対応。

- 個別の修正について、UatVm での操作確認（gRPC 経由の保存・登録）は行っていない。確認したのはビルド・静的確認・menulayout だけ。
- G4-H 以外の 08Zaiko のロジック指摘は、エージェントの報告をそのまま転記したもので、レビューの作成者は再検証していない。

## 6. 残作業（指示が必要な修正）

重要度の高い順に並べた。同じ種類の指摘は1つにまとめた。

| No | 重要度 | ID | 対象 | 内容 | 判断が必要な点 | 推奨案 |
|---|---|---|---|---|---|---|
| 1 | 高 | G2-02, G3-01〜05, G3-15, G5-01, G5-02 | 予算実績3画面、仕入先元帳、得意先元帳、得意先別推移表、品番別売上チェックリスト、支払明細書、売上チェックリスト、納品書未発行チェック、専用伝票納品書、20UriageAnalysis の全帳票（40Shop の派生を含む）、店舗別売上日計表 | CalcFlag を掛けていないため、返品（正値・CalcFlag=-1）が売上・仕入に加算される。元帳では残高が「返品額×2」ずれ、支払明細書では明細とヘッダの合計が一致しない。21OroshiAnalysis・ShiireTrend・HinbanShiireCheckList・SeikyuBalanceDetail・NouhinBookPrintR2 は CalcFlag を掛けており、帳票間で値が食い違う | 売上実績の対象区分（10〜29 に揃えるか）。客数・客単価で返品伝票をどう数えるか（除外か −1件か）。元帳で返品を借方のマイナスにするか貸方に振り替えるか。日計表の返品額の表示符号。qfm 側で合計しているか（G3-15） | 全SQLで数量・金額・税に `h.CalcFlag *` を掛け、既存の正しい画面に揃える。件数は返品伝票を除外する。修正後は明細・ヘッダの合計を独立に集計して照合する |
| 2 | 高 | G2-01, G3-06 | 店ブランド予算実績表、売上金種Viewer | 粗利実績が `kingaku - gedai` で、数量2以上の明細で原価を1個分しか引いていない。金種Viewerは税抜の KingakuTotal を税込の金種計と比べるため、全伝票で差額＝消費税となり「金種未設定」と誤表示する | 返品時の POS 金種の符号の仕様 | No1 と同時に直す。粗利は `kingaku - su * gedai`、金種の売上は `CalcFlag * Total`（Total=0 なら KingakuTotal＋税）にする |
| 3 | 高 | G1-03, G1-31 | 顧客マスタメンテ（印刷F6、販売実績欄） | migration で DROP 済みの Point・SalesCount・SalesKingaku を参照しているため、印刷は必ず失敗する。画面の欄は Binding エラーで常に空 | 購買回数・購買金額を MasterEndCustomerAccount のどの列に対応させるか | 印刷SQLは MasterEndCustomerAccount を LEFT JOIN し、qfm の item14〜16 の位置を維持する。画面は読み取り専用で表示する |
| 4 | 高 | G1-01 | ログイン管理（SysLogin） | クライアント側の Vdc を鍵にパスワードを暗号化するが、サーバは Insert 時に Vdc を採番し直す。そのため新規に作ったユーザはログインできない | サーバで暗号化するか、運用で回避するか | 当面は Id=0 のとき［パス暗号化］を無効にする（追加後に再選択して設定する）。恒久対応は、平文を送ってサーバが採番後の Vdc で暗号化する方式 |
| 5 | 高 | G2-03, G2-26, G4-10, G3-11, G3-10(2), G5-12, G1-20 | 月一括予算（店ブランド・販売員）、予算メンテ、移動受入力、POS日次精算入力、専用伝票納品書、HHTエラーデータ修正入力、外部CSV更新 | 二重登録・部分適用の問題（AGENTS 7.4）。予算は削除と登録が別のトランザクションで、登録に失敗すると既存の予算が消える。予算に一意制約がなく重複登録できる。移動受は同じ出庫に受入伝票を二重に登録できる。精算回数は画面の一覧だけで採番する。発行済の更新・HHTの保存・CSVの更新は1行ずつ別のリクエストで、途中で失敗すると一部だけ反映される | サーバAPIの新設。一意制約（スキーマ移行）。移動受で分納を認めるか。CSV更新を原子的に処理するか | 予算は配分の SaveHaibunAsync と同じく、サーバで洗い替えを1トランザクションにするAPIを作り、(日付, 店舗, ブランド/販売員) で重複を検査する。移動受はサーバで受入数の合計が出庫数以下かを検査する。精算回数はサーバで MAX+1 を採番する。部分更新系は変更行だけを一括（1トランザクション）で送る |
| 6 | 高 | G5-03, G3-09, G3-10(1)(3) | 移動明細書（通常・即時）、納品書（R2）、専用伝票納品書 | PDFの生成に失敗・キャンセルしても伝票が発行済（IsPrint=1）になり、通常発行の対象から消える。共通の PrintPdfHelper.RunPrintPdfAsync が失敗を握りつぶして正常に戻る。専用伝票は印刷とは別に再抽出するため、印刷後に追加された伝票まで発行済になる。通信エラーを「対象なし」と表示する | 共通ヘルパーのシグネチャ変更（担当外）と、全呼出元への影響 | RunPrintPdfAsync が成否を `Task<bool>` で返すようにし、成功したときだけ IsPrint を更新する。専用伝票は印刷時の対象Idを保持し、IsPrint 列だけを Vdu 照合付きで一括更新する |
| 7 | 高 | G2-07, G3-08, G1-58 | 伝票入力（発注・受注・仕入・資材・支払・入金・店舗売上・出荷売上・POS精算）、予算メンテ、社員マスタ | ヘッダの Id 欄は手入力でき、Id だけが変わる。V*（伝票時点の監査値）、掛率、税設定、リードタイムは更新されず、「Id は A社、名称は B社」の伝票を保存できる。サーバにも V* を再解決する処理はない | 入力方式の横断的な方針（読み取り専用にして選択を必須にするか、Id からマスタを引き直すか） | Id 欄を読み取り専用にし、検索ボタンでだけ選ばせる（変更が小さく、V* の整合が保証される） |
| 8 | 高 | G2-06 | 発注残管理表、受注残管理表、得意先売上予定表、入荷待ちリスト | 「残のみ」は残数だけで判定し、EndFlag を見ていない。手動で完了にした伝票も残として出続ける（完了設定画面の案内「残管理表に出なくなります」と食い違う） | 完了の意味。売上予定・入荷待ちにも適用するか | 「残のみ」の条件に `endFlag = 0` を加える。残数の算出は変えない |
| 9 | 高 | G1-04, G1-05, G1-25, G1-26 | 上代一括変更 | Scope を削除して No を振り直すと、セル・店舗別期間が別の Scope に付いて保存される。対象店舗グリッドの期間は保存に使われないが、ツールチップは「保存時に維持」と表示する。保存のたびに DenDay・FixDay が今日で上書きされ、確定・取消済みも再保存できる。既定 Scope の期間がヘッダの期間に追従しない | 店舗期間と Scope 期間の優先順位。確定済み伝票の再保存の可否。送信済を SendFlg だけ更新する経路にするか | 削除時に No を振り直さない（欠番を許す）。店舗期間の列は読み取り専用にする。DenDay・FixDay は新規時・確定時だけ設定する。ヘッダと Scope の期間が食い違うときは保存前に警告する |
| 10 | 中 | G3-16の残り, G1-33, G4-14 | 共通基底（BaseTranInputViewModel、BaseLightMenteViewModel 系の全マスタ・在庫/移動入力） | 保存前に税の再計算を待たないため、税率を取得できないと税額0のまま保存できる。税率キャッシュの更新が並行に走り、日付が混ざる。軽量一覧で選んだ直後の詳細読込が完了・成功する前に修正でき、詳細の項目が既定値や空で上書きされる | 基底クラスの変更の影響範囲 | 保存の前段で税の再計算を await し、キャッシュの更新を SemaphoreSlim で直列化する。基底に詳細読込の完了フラグを持たせ、修正・削除の CanExecute に反映する |
| 11 | 中 | G4-16, G4-17, G4-13, G4-18 | 商品履歴照会、倉庫受払表、商品受払表、棚卸入力（一覧）、棚卸入力 | 移動の入出の向きと倉庫の条件が在庫計算と一致せず、Tran61Chosei も対象外。前月残・当月残を CumulativeSu から取るため、動きのない月で残高が欠ける。実棚0の行が明細にならない。棚卸の単価が画面によって上代と原価に分かれる | 在庫計算（TranCalcBase）との定義の一致。棚卸で0行を記録するか。単価をどちらに統一するか | 移動は VSoko を出・VIdo を入にし、Chosei を加える。残高は年月以下の SUM(Su)（期首を含む）で計算する。棚卸は理論在庫がある行と変更した行を0でも明細に含める。修正後は全件再集計の結果と倉庫×商品×色×サイズで照合する |
| 12 | 中 | G4-H-04, G4-H-05 | 配分出荷リスト、受注配分入力 | 同じ受注×SKUに配分行が複数あると、受注数計が重複して加算される。受注返品（区分20）・値引（30）も配分できてしまい、商品別画面と食い違う | 帳票の受注数の定義。返品・値引を配分の対象にするか | 受注数は受注×SKUで一度だけ数える。返品・値引は配分の対象外にする |
| 13 | 中 | G2-19, G3-14 | 発注残管理表、納品予定表、入荷待ちリスト、仕入伝票印刷 | 発注と入荷を RelateNo1 だけで突合し、移行した旧仕入の偶然の一致を除外していない。仕入伝票の総合計が明細金額の合計だけで、伝票単位の税を含まない | 完了判定（CompletionDb）との揃え方。請求単位の税の扱いと qfm | 受注側と同じく `Id_Shiire` の一致と `DenDay >= 発注日` を加える。総合計は伝票単位の場合 `h.Total` を出す |
| 14 | 中 | G5-05, G5-06, G5-07, G5-08, G5-19, G5-22, G5-23, G5-24, G2-25 | 売上週報・月報、ベスト表ほか分析帳票、担当別半期報、投入売上在庫表、売上予算構成比、消化率系、店舗予算表（全店） | 集計の定義の問題。全店合計の予算が店舗範囲で絞られない。先頭期間に開始日より前の売上が入る。伝票時点の名称を GROUP BY に含めるため、名称変更で行が分かれる。半期の得意先数が月別の最大値になる。同順位の並びが不定。「全て」が全商品にならない。ブランド未設定の売上が除外される。消化率の在庫が全倉庫。千円予算を店舗ごとに切り捨ててから合算する | 各帳票の数値の定義。旧帳票との一致 | 予算は店舗範囲で絞る。当期は指定期間に限る。GROUP BY はコードだけにし、名称は MAX で取る。得意先数は期間全体で DISTINCT に数える。ORDER BY にコードを加える。それ以外は仕様を確認してから直す |
| 15 | 中 | G2-08, G2-09 | 月一括予算（店ブランド・販売員） | 読込時に千円単位へ切り捨てるため、再保存で円単位の端数が消える。読込の直後に前回の休業日が適用され、登録済みの予算が0になる | 端数の扱い。休業日をDBの値から再現するか | 端数があれば警告する。読込の前に休業日欄をクリアする |
| 16 | 中 | G1-09, G1-18 | 共通コンバータ（Helpers/Converters） | 有効期限の入力で日付だけを入れると値が更新されず、旧い有効期限のまま保存される。N0書式の int を双方向バインドすると、カンマ付きの値を戻せず旧値のまま保存される（資材・商品マスタなど） | 日付だけ入力したときの時刻（000000 か 235959 か） | DateTimeYmdHmsConverter で yyyy/MM/dd と yyyyMMdd も受け付ける。カンマを許す数値コンバータを共通に追加し、該当の欄で使う |
| 17 | 中 | G1-62, G4-01, G4-02の残り | 上代一括変更、取置配分入力、受注配分入力 | 行・セルの背景、警告バナーの色が固定の淡色で、ダークテーマでは文字が読めない | 警告・注意・無効の色の定義 | Resources/UIColors.xaml と UIColors.Dark.xaml に背景用のブラシを追加し、DynamicResource で参照する |
| 18 | 中〜低 | G6-07, G5-13, G6-16(3), G3-20, G1-59, G4-09, G5-26, G6-23, G6-24, G1-72 | バーコード連続読取、HHTエラーデータ修正入力、複数選択ダイアログ、店舗売上入力、システム管理マスタ、日付条件の入力、出荷指示明細書、自動発注・補充、メイン画面、マスタ画面のヘッダ | 操作仕様の問題。照会中に読んだ次のバーコードが消える。未保存の修正が「更新実行」「検索」で黙って捨てられる。条件外になった選択が黙って外れる。店舗の選択条件（TenType）が画面ごとに違う。税率の Jsub が3件ないと入力できない。日付条件が自由入力。JAN引当のSQLが遅い。ボタンの配置が同種画面と違う。タスクバーが左・上にあると位置がずれる。最小幅でヘッダが切れる | 入力方式・操作フロー・見た目の変更の可否。店舗の定義（6だけか 3,6 か）。Jsub のデータ契約 | バーコードは読んだ値をキューに積む。未保存の変更があれば確認する。条件外の選択は件数差を警告する。店舗の定義を決めて揃える。Jsub は読込時に3件に補う。それ以外は優先度を下げて個別に判断する |
