# CvBase データベース定義ドキュメント

## 目的

CvBase プロジェクトで定義されているテーブル候補を、`[PrimaryKey]` または `[Comment]` 属性を持つクラスを起点に整理する。DB 初期化対象、派生テーブル、属性定義のみのテーブルを分け、実装上の共通列とテーブル別の主な列・キーを確認できるようにする。

## 抽出条件

- 対象範囲: `CvBase/**/*.cs`
- 対象クラス: クラス属性として `[PrimaryKey]` または `[Comment]` を持つもの
- 除外: ブロックコメント内の ToDo 定義、`NoUseClass.cs` 内のコメントアウト済み定義
- テーブル名: `TableNameAttribute` が見つからないため、現状はクラス名をテーブル名として扱う
- 物理列: `ExDatabase.GetSqlColumns()` の方針に合わせ、`[Ignore]` / `[ComputedColumn]` / `[ResultColumn]` は除外対象とする
- 文字列長: `[ColumnSizeDml(n)]` がある場合は `varchar(n)`、指定なしの `string` は `varchar(255)` 相当
- JSON 格納: `[SerializedColumn]` は NPoco の JSON シリアライズ列として扱う。多くは `[ColumnSizeDml]` により `varchar` として定義される

## 全体サマリ

| 区分 | 件数 | 内容 |
| --- | ---: | --- |
| システム | 5 | DB 更新履歴、連番、ログイン、ログイン履歴、マスター操作履歴 |
| マスター | 8 | システム管理、名称、社員、顧客、商品、設定、得意先、仕入先 |
| トランザクション | 14 | 売上、仕入、移動、入金、支払、棚卸、受発注、HHT 取込、上代一括変更 |
| 集計 | 2 | 現在庫、年月在庫 |
| 派生 | 2 | 商品マスタの色サイズ展開、適用上代 |
| 合計 | 31 | `[PrimaryKey]` または `[Comment]` 付きクラス |

## 作成状態

| 状態 | 対象 |
| --- | --- |
| `DefineDataTable.Initialize()` の `CreateTable` 対象 | 27 テーブル |
| `CreateDerivedTable<T>()` 対象 | `DerivedShohinColSiz` |
| 属性定義のみで初期作成リスト外 | `SysHistryMaster` |

## 共通列

### BaseDbClass

多くの実テーブルが継承する基底列。

| 列 | 型の目安 | 内容 |
| --- | --- | --- |
| `Id` | `bigint auto_increment` | ユニークキー。SQLite では `INTEGER PRIMARY KEY AUTOINCREMENT` |
| `Vdc` | `bigint` | 作成日 UTC ticks |
| `Vdu` | `bigint` | 修正日 UTC ticks |
| `Disp0` | DB列対象外 | `[ResultColumn]` の表示専用項目 |

### BaseDbHasAddress

`BaseDbClass` に住所・連絡先を追加する。

| 列 | 型の目安 | 内容 |
| --- | --- | --- |
| `PostalCode` | `varchar(30)` | 郵便番号 |
| `Address1` | `varchar(60)` | 住所1 都道府県 |
| `Address2` | `varchar(60)` | 住所2 市区町村 |
| `Address3` | `varchar(60)` | 住所3 番地 |
| `Tel` | `varchar(20)` | 電話番号 |
| `Mail` | `varchar(120)` | メールアドレス |

### MasterTorihiki

得意先・仕入先の共通基底。`BaseDbHasAddress` も継承する。

| 列 | 内容 |
| --- | --- |
| `Code`, `Name`, `Ryaku`, `Kana` | 取引先コード・名称・略称・カナ |
| `Id_Shain`, `VShain` | 担当社員。`VShain` は JSON シリアライズ列 |
| `RateProper`, `RateSale` | 掛率、セール掛率 |
| `Shime1`, `Shime2`, `Shime3` | 締日 |
| `PayMonth`, `PayDay` | 入金・支払月日 |
| `Id_PayMethod`, `VPayMethod` | 入金・支払方法。`VPayMethod` は JSON シリアライズ列 |
| `IsPay`, `Id_Paysaki`, `VPaysaki` | 請求・支払管理と請求先/支払先 |
| `Jdetail` | 取引先詳細 JSON |

### TranAllHeader

売上・仕入・移動・受発注・棚卸系の共通ヘッダ。`BaseDbClass` を継承する。

| 列 | 内容 |
| --- | --- |
| `DenDay` | 計上日 `yyyyMMdd` |
| `Id_Shain`, `VShain` | 社員と社員 JSON |
| `Id_Soko`, `VSoko` | 倉庫と倉庫 JSON |
| `CalcFlag` | 在庫計算フラグ |
| `SuTotal`, `KingakuTotal`, `JodaiTotal`, `GedaiTotal` | 数量・金額・上代・下代合計 |
| `Nebiki00Total`, `Nebiki01Meisai` | 値引関連 |
| `Memo` | ヘッダメモ |
| `Jdetail` | 詳細 JSON |
| `Jmeisai` | 明細リスト JSON |

### TranKinHeader

入金・支払系の共通ヘッダ。`BaseDbClass` を継承する。

| 列 | 内容 |
| --- | --- |
| `DenDay` | 計上日 `yyyyMMdd` |
| `Id_Shain`, `VShain` | 社員と社員 JSON |
| `Id_Torisaki`, `VTori` | 取引先と取引先 JSON |
| `KingakuTotal` | 金額合計 |
| `ManualNo` | 手入力 No |
| `Memo` | ヘッダメモ |
| `Jmeisai` | 入金・支払明細 JSON |

## テーブル一覧

### システム

| テーブル | 作成 | 主キー | キー | 概要 | 主な固有列 | 定義元 |
| --- | --- | --- | --- | --- | --- | --- |
| `SysUpdateDb` | CreateTable | `Id` | `uq1(DbVersion)` | DB 定義更新管理 | `DbVersion`, `DateStart`, `NewVersion`, `Sql`, `Memo` | `CvBase/BaseDb0Config.cs:14` |
| `SysSequence` | CreateTable | `Id` | なし | BaseDbClass.Id 以外の連番管理 | `TableName`, `ColumnName`, `SeqNo`, `Memo` | `CvBase/BaseDb0Config.cs:49` |
| `SysHistryMaster` | 初期作成リスト外 | `Id` | `nk1(Vdc)`, `nk2(TableName)` | マスター系操作履歴 | `TableName`, `Id_Table`, `OperationType`, `TableType`, `ItemBefore`, `ItemAfter` | `CvBase/BaseDb0Config.cs:84` |
| `SysLogin` | CreateTable | `Id` | `uq1(LoginId)`, `nk2(Id_Shain)`, `nk3(Id_Role)` | ログイン ID 管理 | `Id_Shain`, `Id_Role`, `LoginId`, `CryptPassword`, `ExpDate`, `LastDate`, `VShain` | `CvBase/BaseDb0Login.cs:16` |
| `SysHistJwt` | CreateTable | `Id` | `nk1(Id_Login)`, `nk2(JwtUnixTime)` | ログイン履歴 | `Id_Login`, `JwtUnixTime`, `Jsub`, `ExpDate`, `Ip`, `Op` | `CvBase/BaseDb0Login.cs:71` |

### マスター

| テーブル | 作成 | 主キー | キー | 概要 | 主な固有列 | 定義元 |
| --- | --- | --- | --- | --- | --- | --- |
| `MasterSysman` | CreateTable | `Id` | なし | システム管理。会社名、消費税設定など | `Name`, `Hp`, `ShimeBi`, `ModifyDaysEx`, `ModifyDaysPre`, `BankAccount1-3`, `FiscalStartDate`, `Jsub`。住所列は `BaseDbHasAddress` | `CvBase/BaseDb0System.cs:14` |
| `MasterMeisho` | CreateTable | `Id` | `uq1(Kubun, Code)`, `nk2(Kubun, Odr, Code)` | 汎用名称。区分 + 名称コード | `Kubun`, `KubunName`, `Code`, `Name`, `Ryaku`, `Kana`, `Odr` | `CvBase/BaseDb0System.cs:122` |
| `MasterShain` | CreateTable | `Id` | `uq1(Code)` | 社員 | `Code`, `Name`, `Ryaku`, `Kana`, `Mail`, `Id_Tenpo`, `VTenpo`, `Id_Bumon`, `VBumon`, `Jsub`, `Jdetail` | `CvBase/BaseDb1Master.cs:14` |
| `MasterEndCustomer` | CreateTable | `Id` | `uq1(Code)` | 店頭顧客または EC 顧客 | `Code`, `Name`, `Ryaku`, `Kana`, `Rank`, `Id_Tenpo`, `VTenpo`, `Birthday`, `BirthNoyear`, `Memo`, `Gendar`, `Point`, `SalesCount`, `SalesKingaku`, `Jsub`, `Jdetail`。住所列は `BaseDbHasAddress` | `CvBase/BaseDb1Master.cs:96` |
| `MasterShohin` | CreateTable | `Id` | `uq1(Code)` | 商品。`Jcolsiz` に色 CD、サイズ CD、JAN を格納 | `Code`, `Name`, `Ryaku`, `Kana`, `Id_Brand`, `VBrand`, `Id_Item`, `VItem`, `Id_Tenji`, `VTenji`, `Id_Maker`, `VMaker`, `Id_Season`, `VSeason`, `Id_Material`, `VMaterial`, `Id_Country`, `VCountry`, `TankaJodaiOrg`, `TankaJodai`, `TankaGenka`, `TankaShiire`, `DayShukka`, `DayNohin`, `DayTento`, `Id_Tax`, `IsZaiko`, `MakerHin`, `SizeKu`, `Id_Soko`, `VSoko`, `Memo`, `Jgenka`, `Jcolsiz`, `Jgrade`, `Jsub`, `Jdetail` | `CvBase/BaseDb1Master.cs:219` |
| `MasterConfig` | CreateTable | `Id` | `uq1(Name)` | 設定フラグ。`Name` と `Val` の組 | `Category`, `Name`, `Val`, `Example`, `Memo` | `CvBase/BaseDb1Master.cs:577` |
| `MasterTokui` | CreateTable | `Id` | `uq1(Code)` | 得意先。`TenType` は 0=倉庫、1=卸先、3=売仕店、6=直営店 | `TenType`, `IsZaiko`, `Jsub`。取引先共通列は `MasterTorihiki` | `CvBase/BaseDb1MasterTorihiki.cs:202` |
| `MasterShiire` | CreateTable | `Id` | `uq1(Code)` | 仕入先 | `Jsub`。取引先共通列は `MasterTorihiki` | `CvBase/BaseDb1MasterTorihiki.cs:244` |

### トランザクション

| テーブル | 作成 | 主キー | キー | 概要 | 主な固有列 | 定義元 |
| --- | --- | --- | --- | --- | --- | --- |
| `Tran06Nyukin` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(Id_Torisaki)` | 入金。売掛に対する入金 | 固有列なし。共通列は `TranKinHeader` | `CvBase/BaseDb2Trans.cs:451` |
| `Tran07Shiharai` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(Id_Torisaki)` | 支払。買掛に対する支払 | 固有列なし。共通列は `TranKinHeader` | `CvBase/BaseDb2Trans.cs:460` |
| `Tran60Tana` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(Id_Soko)` | 棚卸。月末または特定日の倉庫現在値 | `TanaNo`。共通列は `TranAllHeader` | `CvBase/BaseDb2Trans.cs:470` |
| `Tran00Uriage` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(KakeDay)`, `nk3(Id_Soko)`, `nk4(Id_Tokui)` | 本部売上。売掛計上と倉庫出庫 | `KakeDay`, `Id_Tokui`, `VTokui`, `IsPay`, `Kubun`, `ManualNo`, `RelateNo1`, `RelateNo2`, `Rate` | `CvBase/BaseDb2Trans.cs:489` |
| `Tran01Tenuri` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(Id_Soko)`, `nk3(Id_Tenpo)`, `nk4(Id_Customer)` | 店舗売上。店舗売上と店舗/倉庫出庫 | `Id_Tenpo`, `VTenpo`, `Id_Customer`, `VCustomer`, `Code_Customer`, `Kubun`, `RelateNo1`, `Rate` | `CvBase/BaseDb2Trans.cs:576` |
| `Tran03Shiire` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(KakeDay)`, `nk3(Id_Soko)`, `nk4(Id_Shiire)` | 仕入。買掛計上と倉庫入庫 | `KakeDay`, `Id_Shiire`, `VShiire`, `IsPay`, `Kubun`, `ManualNo`, `RelateNo1`, `Rate` | `CvBase/BaseDb2Trans.cs:650` |
| `Tran05Ido` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(Id_Soko)`, `nk3(Id_Ido)` | 即時移動。倉庫出庫と移動先入庫 | `Id_Ido`, `VIdo`, `RelateNo1`, `ManualNo` | `CvBase/BaseDb2Trans.cs:728` |
| `Tran10IdoOut` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(Id_Soko)`, `nk3(Id_Ido)` | 積送出庫。積送中在庫への出庫 | `Id_Ido`, `VIdo`, `RelateNo1`, `ManualNo` | `CvBase/BaseDb2Trans.cs:763` |
| `Tran11IdoIn` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(Id_Soko)`, `nk3(Id_Ido)` | 積送入庫。積送中在庫から移動先への入庫 | `Id_Ido`, `VIdo`, `RelateNo1`, `ManualNo` | `CvBase/BaseDb2Trans.cs:797` |
| `Tran12Jyuchu` | CreateTable | `Id` | `nk1(DenDay)`, `nk3(Id_Soko)`, `nk4(Id_Tokui)` | 受注。本部売上化時は売上側 `RelateNo1` に受注 `Id` を設定 | `Id_Tokui`, `VTokui`, `Kubun`, `RelateNo1`, `Rate` | `CvBase/BaseDb2Trans.cs:831` |
| `Tran13Hachu` | CreateTable | `Id` | `nk1(DenDay)`, `nk3(Id_Soko)`, `nk4(Id_Shiire)` | 発注。仕入化時は仕入側 `RelateNo1` に発注 `Id` を設定 | `Id_Shiire`, `VShiire`, `Kubun`, `RelateNo1`, `Rate` | `CvBase/BaseDb2Trans.cs:876` |
| `TranHhtData` | CreateTable | `Id` | `nk1(DenDay)` | ハンディターミナル取込データ | `Store`, `DenDay`, `Kubun`, `DenNo`, `Tanto`, `Tori`, `Hinban`, `Color`, `Size`, `MotoJodai`, `Jodai`, `Gedai`, `Su`, `Store2`, `SaleFlg`, `TanaNo`, `RelateDenNo`, `Kakeritsu`, `NouhinDay`, `Yobi03-12`, `FileName`, `LineNo`, `VdCnvDate` | `CvBase/BaseDb2Trans.cs:920` |
| `TranVulcanHht` | CreateTable | `Id` | `nk1(BackupFileName)`, `nk2(VdCnvDate)` | VULCAN データレイアウト HHT 取込データ | `Type0`, `HhtNo`, `Serial`, `DenDay`, `Store`, `Tanto`, `HanKubun`, `DenNo`, `Jan1`, `Jan2`, `Su`, `Tanka`, `ToriSaki`, `KakeRitsu`, `TotalCnt`, `Filler`, `BackupFileName`, `LineNo`, `ComputerName`, `UserName`, `VdCnvDate`, `TargetTableName`, `TargetId`, `ErrorMsg` | `CvBase/BaseDb2Trans.cs:1135` |
| `TranJodai` | CreateTable | `Id` | `nk1(DenDay)`, `nk2(Id_Sale)` | 上代一括変更の伝票。対象店舗・対象明細・抽出条件を JSON 配列で保持し、物理テーブルはこの1表のみ。確定すると `DerivedJodai` へ展開される | `DenDay`, `Kubun`(P/S), `TaishoType`(店舗用/本部売上用), `Id_Sale`, `VSale`, `Title`, `Id_Shain`, `VShain`, `DayFrom`, `DayTo`, `CalcType`, `CalcRate`, `CalcValue`, `RoundUnit`, `RoundType`, `Status`, `FixDay`, `SendFlg`, `ShopCnt`, `MeisaiCnt`, `ExpandCnt`, `Jcond`, `Jshop`, `Jmeisai`, `Memo` | `CvBase/BaseDbJodai.cs:59` |

### 集計

| テーブル | 作成 | 主キー | キー | 概要 | 主な固有列 | 定義元 |
| --- | --- | --- | --- | --- | --- | --- |
| `SummaryRealStock` | CreateTable | `Id` | `unq1(Id_Soko, Id_Shohin, Id_Col, Id_Siz)`, `nk1(Id_Soko)`, `nk2(Id_Shohin)` | 現在庫。倉庫、商品、色、サイズで集計 | `Id_Soko`, `Id_Shohin`, `Id_Col`, `Id_Siz`, `Su` | `CvBase/BaseDb3Summary.cs:15` |
| `SummaryStock` | CreateTable | `Id` | `unq1(SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz)`, `nk1(Id_Soko)`, `nk2(Id_Shohin)` | 年月在庫。`Su` は当月、`CumulativeSu` は累計 | `SumMonth`, `CumulativeSu`, `InQty`, `OutQty`, `TransitQty`, `AdjustQty`, `StocktakeDdate`, `ActualQty`。現在庫軸は `SummaryRealStock` | `CvBase/BaseDb3Summary.cs:50` |

### 派生

| テーブル | 作成 | 主キー | キー | 概要 | 主な固有列 | 定義元 |
| --- | --- | --- | --- | --- | --- | --- |
| `DerivedShohinColSiz` | CreateDerivedTable | `Id` | `unq1(Id_Shohin, Id_Col, Id_Siz)`, `n1(Id_Shohin)`, `n2(Code)`, `njan1(Jan1)`, `njan2(Jan2)`, `njan3(Jan3)` | 商品マスタ `MasterShohin` から商品・色・サイズに展開した派生マスタ | `Id`, `Id_Shohin`, `RowIdx`, `Code`, `Id_Col`, `Code_Col`, `Mei_Col`, `Id_Siz`, `Code_Siz`, `Mei_Siz`, `Jan1`, `Jan2`, `Jan3` | `CvBase/BaseDbDerived.cs:160` |
| `DerivedJodai` | CreateTable | `Id` | `uk1(Id_Tran, TaishoType, Id_Tenpo, Id_Shohin)`, `nk1(Id_Shohin, TaishoType, Id_Tenpo, DayFrom, DayTo)`, `nk2(Id_Tran)`, `nk3(DayTo)` | 適用上代。`TranJodai`(確定分)を「対象 × 商品 × 期間」へ展開したもの。該当行が無ければ `MasterShohin.TankaJodai` を使う。V*列は持たない（JOIN 前提） | `TaishoType`, `Id_Tenpo`(0=全件), `Id_Shohin`, `DayFrom`, `DayTo`, `Kubun`, `Jodai`, `RateOff`, `Id_Tran`, `No`, `Priority` | `CvBase/BaseDbJodai.cs:484` |

## 補足

- `MasterShohin` の `Jcolsiz`、`Jgenka`、`Jgrade` などの `J*` 列は、サブ構造を JSON として持つ前提の列である。
- `V*` 列の多くは `CodeNameView` を JSON として保持する表示・参照用のスナップショット列である。
- `SysHistryMaster` は属性上は実テーブル候補だが、現時点の `DefineDataTable.Initialize()` には含まれていない。
- `BaseDb2Trans.cs` 末尾の原価・予算・配分・補充・売掛/買掛・ポイント系 ToDo テーブルはブロックコメント内のため、このドキュメントでは対象外とした。
- `TranJodai` の `Jcond` / `Jshop` / `Jmeisai` は `[ColumnSizeDml(ColumnType.Json)]` 指定の JSON 配列列である。
  SQLite では `TEXT`（サイズ制限なし）、MariaDB では `JSON` 型になる。`List<>` 型に `[ColumnSizeDml]` を付け忘れると
  MariaDB / Oracle 側で**列自体が生成されない**（`ExDatabase.GetSqlColumns` が `continue` する）ので注意する。
- `DerivedJodai` は `TranJodai` の `IDerivedOrigin` 実装により、`CvServer/Services/HandlerDerived` が
  Insert / Update / Delete 時に自動で再展開・削除する（`DerivedShohinColSiz` と同じ仕組み）。
  手動修復は `CvDomainLogic/JodaiDb.Rebuild()` / `RebuildAll()`。設計は `.omo/20260811_jodai_table_design_plan.md`。
