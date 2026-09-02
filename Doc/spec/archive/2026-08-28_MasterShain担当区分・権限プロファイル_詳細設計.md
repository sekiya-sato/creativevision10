# MasterShain 担当区分・権限プロファイル 詳細設計

対象: `CvBase`（テーブル定義・初期データ・マイグレーション） / 影響確認: `Tests` `CvWpfclient` `CvServer`
状態: **本書スコープ（4章・9章「含む」）は実装完了（commit `e5f59e6`）**。`MasterShain.ResponsibilityScope`/`Id_PermissionProfile`、`SysPermissionProfile`/`SysPermissionProfileDetail`、`EnumResponsibilityScope`/`EnumPermissionType`、マイグレーション`26_08_28_01`、初期データを実装済み。9章「含まない」の4項目（担当区分・プロファイルのメンテ画面UI、権限プロファイルのメンテ画面、権限の実行時判定、`MenuData`への`FunctionId`付与）は本書のスコープ外のまま未着手（D-10により10.0対象外）。
関連: [.omo/plans/2026-08-28_user_operation_log_layer2_plan.md](.omo/plans/2026-08-28_user_operation_log_layer2_plan.md) §1.2 / §2.3 / §2.5

## 1. 背景・目的

ユーザ操作ログ基盤の計画書 §1.2 で「無いもの」として挙げた 3 項目のうち 2 項目を先に確定させる。

- **`ResponsibilityScope`（担当範囲）が無い** … 現状 `MasterShain` は `Id_Tenpo` / `Id_Bumon` の単値しか持たず、「全社担当者か店舗スタッフか」という業務上の立場を表現できない。
- **`PermissionProfileId` が無い** … 権限は `EnumLoginRole`（5種）による `MenuData.AllowedRoles` のメニュー表示制御だけで、機能単位の権限セットが無い。

計画書 §2.5 では「案A（`ScopeKey` を文字列で持つ・マスタ追加ゼロ）で開始」としていたが、本設計では**担当区分を `MasterShain` の enum 列として持ち、システム操作権限は別マスタ（プロファイル）に切り出す**方針を採る。§2.5 の案A/案Cの中間で、担当区分は enum 固定・権限は可変データとする。

概念の分離は次のとおり。

| 概念 | 実体 | 意味 |
| --- | --- | --- |
| 業務上の立場 | `MasterShain.ResponsibilityScope`（enum 固定値） | 全社担当者 / エリアマネージャ / 店舗責任者 / 店舗スタッフ |
| システム操作権限 | `SysPermissionProfile` + `SysPermissionProfileDetail`（データ） | 機能ID × 操作種別 × 許可/禁止 の集合 |

`ResponsibilityScope` は enum なのでプログラムから分岐に使える。権限は運用中に増減するのでデータで持つ。

## 2. 現行規約の実測（この設計が従う根拠）

| 規約 | 実測 | 位置 |
| --- | --- | --- |
| 主キーは `long Id`（AutoIncrement）。業務コードは `Code` 列 | 全テーブルが `BaseDbClass`（`Id`/`Vdc`/`Vdu`/`Disp0`）継承 + `[PrimaryKey(nameof(Id), AutoIncrement = true)]` | [BaseDbDefinition.cs:10](CvBase/Share/BaseDbDefinition.cs:10) |
| 外部キーは `Id_<参照先>`（`long`）+ `[ForeignKey]` | `MasterShain.Id_Tenpo` / `Id_Bumon`、`SysLogin.Id_Shain` | [BaseDb1Master.cs](CvBase/BaseDb1Master.cs) / [BaseDb0Login.cs:21](CvBase/BaseDb0Login.cs:21) |
| enum は **数値列 + `[Ignore][JsonIgnore]` の `En*` ラッパ**（DB は数値、C# は enum） | `Gender` + `EnGender`、`Shime1` + `EnShime1`、`ShimeBi` + `EnShimeBi` | [BaseDb1Master.cs:203](CvBase/BaseDb1Master.cs:203) / [BaseDb0System.cs:37](CvBase/BaseDb0System.cs:37) |
| enum 型名は `Enum*`、`CvBase/Share/BaseEnumClass.cs` に集約 | `EnumGender` / `EnumShime` / `EnumTokui` / `EnumLoginRole` / `EnumYesNo` | [BaseEnumClass.cs](CvBase/Share/BaseEnumClass.cs) |
| `Id_*` に必ず `V*`（`CodeNameView`）が付くわけではない | `MasterMaterial.Id_Tax` は `V*` 無しで運用中 | [UpdateDb.cs](CvBase/UpdateDb.cs) の `26_08_27_02` |
| `bool` 列は 3DB とも生成可（SQLite `NUMBER not null default 0` / PostgreSQL `boolean NOT NULL DEFAULT FALSE`） | `MasterShipping.TrackingSupported` / `IsActive` | [BaseDb1Master.cs:895](CvBase/BaseDb1Master.cs:895) |
| 説明文の列名は `Memo`（CvBase 内 18 箇所）。`MasterShipping` のみ `Notes` | `MasterEndCustomer.Memo` ほか | [BaseDb1Master.cs:200](CvBase/BaseDb1Master.cs:200) |
| 有効フラグは `IsActive` | `MasterShipping.IsActive` | [BaseDb1Master.cs:904](CvBase/BaseDb1Master.cs:904) |
| 新規テーブルは `DefineDataTable.TableTypes` へ登録。初期データは `<型>.CreateDefaultData(db)`（件数0のときだけ投入） | `MasterShipping.CreateDefaultData` | [DefineDataTable.cs:143](CvBase/DefineDataTable.cs:143) / [BaseDb1Master.cs:981](CvBase/BaseDb1Master.cs:981) |
| 既存DBへの列追加は `UpdateDb.versions` に 8 桁バージョンで追記（新規テーブルは `CreateTable IF NOT EXISTS` が作るので追記不要） | `26_08_27_02` など | [UpdateDb.cs:16](CvBase/UpdateDb.cs:16) |
| `Sys*` 系（システム全体に関わるテーブル）は `BaseDb0*.cs` に定義 | `SysLogin` / `SysHistJwt` は `BaseDb0Login.cs` | [BaseDb0Login.cs](CvBase/BaseDb0Login.cs) |

## 3. 提示案からの命名変更（再検討結果）

ご提示の案は概念設計としてはそのまま採用し、CV10 の実装規約に合わせて以下を変更する。**変更しない項目は表に載せていない。**

| ご提示 | 採用 | 理由 |
| --- | --- | --- |
| `SysPermissionProfile.PermissionProfileId`（string PK, 例 `CorporateUserDefault`） | `Id`(long, PK) + `Code`(string, 値は `CorporateUserDefault`) | CV10 は全テーブルが `long Id` の AutoIncrement PK。文字列PKは NPoco の `[PrimaryKey]`・`Insert` 戻り値・`Id_*` 参照のすべてで前提が崩れる。**識別子の値はご提示のまま `Code` に入る**ので運用上の見え方は変わらない |
| `PermissionProfileName` | `Name` | `Code`/`Name` が全マスタ共通の列名 |
| `MasterShain.PermissionProfileId` | `MasterShain.Id_PermissionProfile`（long, `[ForeignKey(nameof(SysPermissionProfile))]`） | 外部キーは `Id_*` 規約。文字列参照だとプロファイル改名時に不整合になる |
| `SysPermissionProfile.UserResponsibilityRole` | `ResponsibilityScope` | 同じ enum を指すのに 2 つの列名があると `nameof` での突き合わせができない。**両テーブルで `ResponsibilityScope` に統一** |
| `Description` | `Memo` | CvBase 内の説明列は `Memo`（18 箇所）。`Description` は 0 箇所 |
| `IsEnabled` | `IsActive` | 有効フラグの既存列名（`MasterShipping.IsActive`） |
| `Version` | `ProfileVersion` | `SysUpdateDb.DbVersion`・`InfoServer` のバージョン概念と紛れる。用途を列名に含める |
| `SysPermissionProfileDetail.PermissionProfileId` | `Id_PermissionProfile`（long） | 同上 |
| `FunctionId` の値（`SalesEntry` / `InventoryAdjustment` など） | `06Uriage.ShopUriageInput` 形式 | 操作ログ計画 §2.3 が `<領域2桁><領域名>.<機能名>.<動作>` を定めている。権限側は**動作を `PermissionType` が持つ**ので、`FunctionId` は前 2 節（`<領域><機能名>`）とし、ログ側は `FunctionId + "." + ActionType` で組み立てる。台帳を 1 本にできる（§5） |
| 列型 `enum` | 数値列 + `En*` ラッパ | §2 の実測どおり。DB は数値、C# は enum |

`SysPermissionProfile` は `IBaseCodeName` を**実装しない**（`Ryaku`/`Kana` が業務上不要で、`WriteEffectRunner` の `IBaseCodeName` 付随処理の対象にしたくないため）。

## 4. テーブル・列定義

### 4.1 `MasterShain` 追加 2 列（[CvBase/BaseDb1Master.cs](CvBase/BaseDb1Master.cs) / `ExpireDate` の直後）

```csharp
	/// <summary>
	/// 担当区分 0=未設定 1=店舗スタッフ 2=店舗責任者 3=エリアマネージャ 4=全社担当者
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnResponsibilityScope))]
	[Comment("担当区分 0=未設定 1=店舗スタッフ 2=店舗責任者 3=エリアマネージャ 4=全社担当者")]
	public partial int ResponsibilityScope { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumResponsibilityScope EnResponsibilityScope {
		get => (EnumResponsibilityScope)ResponsibilityScope;
		set => ResponsibilityScope = (int)value;
	}
	/// <summary>
	/// 権限プロファイルId
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(SysPermissionProfile))]
	[Comment("権限プロファイルId")]
	public partial long Id_PermissionProfile { get; set; }
```

- `V*`（`CodeNameView`）列は**付けない**。プロファイル名を一覧に出す必要が出たら `SysPermissionProfile` を JOIN する（`MasterMaterial.Id_Tax` と同じ扱い）。`V*` を持たないので `MasterCascadeDb.VRules` への追記も不要（`MasterCascadeDbTests.VRules_CoverAllMasterVColumns` は `CodeNameView` 型の列だけを見る）。
- 既存社員は移行時に両列とも `0`（未設定）になる。

### 4.2 `EnumResponsibilityScope`（[CvBase/Share/BaseEnumClass.cs](CvBase/Share/BaseEnumClass.cs) へ追記）

```csharp
/// <summary>
/// 担当区分（人の業務上の立場）。MasterShain.ResponsibilityScope / SysPermissionProfile.ResponsibilityScope
/// 値は担当範囲の広い順に大きくなる（比較で「これ以上の立場か」を判定できる）
/// </summary>
public enum EnumResponsibilityScope : int {
	/// <summary>未設定（移行直後の既定値）</summary>
	Unset = 0,
	/// <summary>店舗スタッフ</summary>
	StoreStaff = 1,
	/// <summary>店舗責任者</summary>
	StoreManager = 2,
	/// <summary>エリアマネージャ</summary>
	AreaManager = 3,
	/// <summary>全社担当者</summary>
	CorporateUser = 4,
}
```

`0 = Unset` を置く理由は 2 つ。`ALTER TABLE ... default 0` で入る既存行を「未設定」と識別できること、`EnumGender.Unknown = 0` と同じ流儀であること。値の並びはご提示（全社担当者が先頭）と逆順で、**担当範囲の広い順に昇順**とした。`>= EnumResponsibilityScope.StoreManager` のような比較が書けるためで、この点は §10 の決定事項に含める。

### 4.3 `SysPermissionProfile`（[CvBase/BaseDb0Login.cs](CvBase/BaseDb0Login.cs) へ追加）

```csharp
/// <summary>
/// システム：権限プロファイルマスタ（システム操作権限セットの親）
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：権限プロファイルマスタ 機能単位の操作権限セット")]
[KeyDml("uq1", true, nameof(Code))]
[KeyDml("nk2", false, nameof(ResponsibilityScope))]
public sealed partial class SysPermissionProfile : BaseDbClass {
	[ObservableProperty][ColumnSizeDml(30)][Comment("権限プロファイルコード 例 CorporateUserDefault")]
	public partial string Code { get; set; } = string.Empty;
	[ObservableProperty][ColumnSizeDml(60)][Comment("表示名称 例 全社担当者 標準権限")]
	public partial string Name { get; set; } = string.Empty;
	[ObservableProperty][NotifyPropertyChangedFor(nameof(EnResponsibilityScope))]
	[Comment("主に想定する担当区分 0=未設定 1=店舗スタッフ 2=店舗責任者 3=エリアマネージャ 4=全社担当者")]
	public partial int ResponsibilityScope { get; set; }
	[Ignore][JsonIgnore]
	public EnumResponsibilityScope EnResponsibilityScope {
		get => (EnumResponsibilityScope)ResponsibilityScope;
		set => ResponsibilityScope = (int)value;
	}
	[ObservableProperty][ColumnSizeDml(120)][Comment("説明")]
	public partial string Memo { get; set; } = string.Empty;
	[ObservableProperty][Comment("担当区分の標準プロファイルか")]
	public partial bool IsDefault { get; set; }
	[ObservableProperty][Comment("使用可能か")]
	public partial bool IsActive { get; set; }
	[ObservableProperty][Comment("権限定義バージョン")]
	public partial int ProfileVersion { get; set; } = 1;
}
```

`IsDefault` は「担当区分ごとに 1 件」を意図するが、`(ResponsibilityScope, IsDefault)` のユニーク制約は **張らない**（部分ユニークインデックスは 3DB で書き方が割れるため）。既定プロファイルの解決は `where ResponsibilityScope=? and IsDefault=1 and IsActive=1 order by Id limit 1` とし、重複時は Id 昇順の先頭を採る。

### 4.4 `SysPermissionProfileDetail`（同ファイル、親の直後）

```csharp
/// <summary>
/// システム：権限プロファイル明細（SysPermissionProfile と 1:N）
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("システム：権限プロファイル明細 機能ID×操作種別の許可/禁止")]
[KeyDml("uq1", true, nameof(Id_PermissionProfile), nameof(FunctionId), nameof(PermissionType))]
[KeyDml("nk2", false, nameof(FunctionId))]
public sealed partial class SysPermissionProfileDetail : BaseDbClass {
	[ObservableProperty][ForeignKey(nameof(SysPermissionProfile))][Comment("親プロファイルId")]
	public partial long Id_PermissionProfile { get; set; }
	[ObservableProperty][ColumnSizeDml(60)][Comment("CV10の機能ID 例 06Uriage.ShopUriageInput")]
	public partial string FunctionId { get; set; } = string.Empty;
	[ObservableProperty][NotifyPropertyChangedFor(nameof(EnPermissionType))]
	[Comment("操作種別 1=View 2=Create 3=Update 4=Delete 5=Execute 6=Approve 7=Export 8=Configure")]
	public partial int PermissionType { get; set; }
	[Ignore][JsonIgnore]
	public EnumPermissionType EnPermissionType {
		get => (EnumPermissionType)PermissionType;
		set => PermissionType = (int)value;
	}
	[ObservableProperty][Comment("許可/禁止")]
	public partial bool IsAllowed { get; set; }
}
```

```csharp
/// <summary>
/// 権限の操作種別。SysPermissionProfileDetail.PermissionType
/// 操作ログの ActionType（計画書 §2.4）のうち、権限判定に使う 8 種に絞った部分集合
/// </summary>
public enum EnumPermissionType : int {
	View = 1,
	Create = 2,
	Update = 3,
	Delete = 4,
	Execute = 5,
	Approve = 6,
	Export = 7,
	Configure = 8,
}
```

`0` は未設定＝不正値とし、`Enum.IsDefined` で弾く（ご提示の `View` を 0 にすると、`default(int)` の行が「参照許可」として通ってしまうため 1 始まりにした）。

**明細を JSON 列（`Jdetail`）にしない理由**: 明細は「この機能を許可しているプロファイル一覧」を機能ID側から引く用途があり、SQL の `where FunctionId=? and PermissionType=?` が効く実テーブルの方が適する。1 プロファイルあたり数十〜数百行を見込むため、`[SerializedColumn]` の想定サイズにも収まらない。

## 5. `FunctionId` の値と初期台帳

書式は操作ログ計画 §2.3 に合わせ `<領域2桁><領域名>.<機能名>`（`<機能名>` は View 型名から `View` を除いたもの）。ご提示の概念名との対応は次のとおり。

| ご提示 | 採用 `FunctionId` | 対応する画面 |
| --- | --- | --- |
| `SalesEntry` | `06Uriage.ShopUriageInput` | [ShopUriageInputView.xaml](CvWpfclient/Views/06Uriage/ShopUriageInputView.xaml) |
| `StockInquiry` | `08Zaiko.ZaikoQuery` | [ZaikoQueryView.xaml](CvWpfclient/Views/08Zaiko/ZaikoQueryView.xaml) |
| `InventoryAdjustment` | `08Zaiko.StockForceInput` | [StockForceInputView.xaml](CvWpfclient/Views/08Zaiko/StockForceInputView.xaml)（在庫強制調整＝`Tran61Chosei`） |
| `SalesCorrection` | `06Uriage.ShopUriageInput` + `Approve` | 専用画面が無く、店舗売上入力の修正操作に相当（§10 の決定事項） |
| `StoreTransfer` | `08Zaiko.IdoInputSoku` | [IdoInputSokuView.xaml](CvWpfclient/Views/08Zaiko/IdoInputSokuView.xaml)（積送は `08Zaiko.IdoInputOut` を別途） |
| `CompanySalesAnalysis` | `20UriageAnalysis.SalesQuickReport` | [SalesQuickReportView.xaml](CvWpfclient/Views/20UriageAnalysis/SalesQuickReportView.xaml) |
| `Allocation` | `07Haibun.ShopHaibunInput` | [ShopHaibunInputView.xaml](CvWpfclient/Views/07Haibun/ShopHaibunInputView.xaml) |

本設計で `MenuData` への `FunctionId` 追加は**行わない**（操作ログ計画 Phase 1 の担当範囲）。初期データはこの 7 機能のみを持ち、台帳の全画面展開は操作ログ側と同時に行う。

## 6. 初期データ

`MasterShipping.CreateDefaultData` と同形で、`SysPermissionProfile.CreateDefaultData(db)` を [DefineDataTable.cs:143](CvBase/DefineDataTable.cs:143) の直後に 1 行追加する（親・明細をまとめて投入。件数 0 のときだけ動くので既存DBでは何もしない）。

### プロファイル（`Id` は明細から参照するため固定値）

| Id | Code | Name | ResponsibilityScope | IsDefault | IsActive | ProfileVersion |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `CorporateUserDefault` | 全社担当者 標準権限 | 4 (CorporateUser) | true | true | 1 |
| 2 | `AreaManagerDefault` | エリアマネージャ 標準権限 | 3 (AreaManager) | true | true | 1 |
| 3 | `StoreManagerDefault` | 店舗責任者 標準権限 | 2 (StoreManager) | true | true | 1 |
| 4 | `StoreStaffDefault` | 店舗スタッフ 標準権限 | 1 (StoreStaff) | true | true | 1 |

### 明細

| Id_PermissionProfile | FunctionId | PermissionType | IsAllowed |
| --- | --- | --- | --- |
| 4 (StoreStaffDefault) | `06Uriage.ShopUriageInput` | 5 Execute | true |
| 4 | `08Zaiko.ZaikoQuery` | 1 View | true |
| 4 | `08Zaiko.StockForceInput` | 5 Execute | **false** |
| 3 (StoreManagerDefault) | `06Uriage.ShopUriageInput` | 5 Execute | true |
| 3 | `06Uriage.ShopUriageInput` | 6 Approve | true |
| 3 | `08Zaiko.ZaikoQuery` | 1 View | true |
| 3 | `08Zaiko.StockForceInput` | 5 Execute | true |
| 2 (AreaManagerDefault) | `08Zaiko.IdoInputSoku` | 5 Execute | true |
| 2 | `08Zaiko.IdoInputSoku` | 6 Approve | true |
| 1 (CorporateUserDefault) | `20UriageAnalysis.SalesQuickReport` | 1 View | true |
| 1 | `07Haibun.ShopHaibunInput` | 5 Execute | true |

`IsAllowed=false` の行（店舗スタッフの在庫強制調整）を明示的に持つのは、「未登録」と「明示的な禁止」を区別できるようにするため。判定順序は **明示 `false` ＞ 明示 `true` ＞ 未登録（§10 の決定事項）** とする。

## 7. マイグレーション

既存DB（`server-user163.db` 等）向けに [CvBase/UpdateDb.cs](CvBase/UpdateDb.cs) の `versions` 末尾へ 1 件追記する。

```csharp
new (26_08_28_01,"ALTER TABLE MasterShain ADD COLUMN ResponsibilityScope NUMBER not null default 0;ALTER TABLE MasterShain ADD COLUMN Id_PermissionProfile NUMBER not null default 0;","MasterShain 担当区分・権限プロファイル列を追加 既存社員は未設定(0) 権限プロファイル2テーブルはDefineDataTableが作成し初期データを投入する"),
```

新規 2 テーブルは `DefineDataTable.InitializeAsync` の `CreateTable`（`CREATE TABLE IF NOT EXISTS`）が作るため `UpdateDb` への SQL は不要。`TableTypes` にはシステムテーブルのブロック（`SysHistAutoexec` の直後）へ 2 行追加する。

## 8. 影響範囲

| 対象 | 影響 | 対応 |
| --- | --- | --- |
| `Tests/TestSqlDialect/DdlSnapshotTests` | `TableTypes` を全走査し 3DB で列集合一致を検証。新規 2 テーブルが自動的に対象になる | 追加の記述不要。**実行して緑を確認する**（`bool` 列は SQLite/MariaDB/PostgreSQL すべて対応済み） |
| `Tests/TestServer/MasterCascadeDbTests` | `V*` 列を追加しないため `VRules` 追記不要 | 影響なし（回帰確認のみ） |
| `CvServer` | Insert/Update/Delete は `InsertParam` 等の型情報から汎用処理する（[HandlerClass.cs:213](CvServer/Services/HandlerClass.cs:213)）ため、テーブル個別の配線は不要 | 変更なし |
| `CvWpfclient` 社員マスタメンテ | 一覧は `AdditionalLightweightColumns`（[MasterShainMenteViewModel.cs:28](CvWpfclient/ViewModels/01Master/MasterShainMenteViewModel.cs:28)）で列を絞るため、追加列は**指定しない限り一覧に出ない**。詳細タブの入力欄も追加されない | 本設計のスコープ外（§9） |
| `printform/MasterShainMente.qfm` | 印刷SQLは列を明示列挙（`select Id, ..., Code, Name, ...`）で `select *` ではない | 影響なし |
| 既存の `EnumLoginRole` / `SysLogin.Id_Role` / `MenuData.AllowedRoles` | 権限の概念が二重になる | 当面併存。整理方針は §10 |

## 9. 本設計のスコープ

**含む**: `MasterShain` 2 列追加、`EnumResponsibilityScope` / `EnumPermissionType` 追加、`SysPermissionProfile` / `SysPermissionProfileDetail` の定義・登録・初期データ・マイグレーション。

**含まない（後続で別途設計）**:

1. 社員マスタメンテ画面での担当区分・プロファイルの選択UI（`ComboBox` は `MasterEndCustomerMenteView.xaml:294` の `SelectedIndex` バインド方式に倣う想定）。
2. 権限プロファイルのメンテナンス画面（`00System` 配下）。
3. **権限の実行時判定**（メニュー表示制御・機能起動時チェック・サーバ側の強制）。本設計はデータ構造のみで、判定処理を入れないため**既存の動作は一切変わらない**。
4. `MenuData` への `FunctionId` 付与と全画面の台帳化（操作ログ計画 Phase 1）。

## 10. 決定事項（承認をお願いしたい項目）

| No | 論点 | 推奨 | 代替 |
| --- | --- | --- | --- |
| B-1 | `EnumResponsibilityScope` の値の並び | **担当範囲の広い順に昇順**（1=店舗スタッフ … 4=全社担当者）、0=未設定 | ご提示順（0=全社担当者 … 3=店舗スタッフ）。この場合 0 が最上位権限になり、移行時の既定値 0 が全社担当者になってしまうため非推奨 |
| B-2 | プロファイルの保持先 | **`MasterShain.Id_PermissionProfile`**（ご提示どおり） | `SysLogin` に持たせる（権限はログイン単位という考え方）。1社員1ログイン前提が崩れる場合は再検討が必要 |
| B-3 | 明細に無い機能の既定 | **未登録＝許可**（現行動作を変えない。段階導入向け） | 未登録＝禁止（fail-closed）。安全側だが、台帳が全画面分揃うまで業務が止まる |
| B-4 | `SalesCorrection` の機能ID | **`06Uriage.ShopUriageInput` + `Approve`**（専用画面が無いため） | 売上修正の専用機能IDを新設する（画面新設が前提） |
| B-5 | `StoreTransfer` の範囲 | **即時移動（`08Zaiko.IdoInputSoku`）のみ**を初期データに入れる | 積送（`08Zaiko.IdoInputOut`）・移動受（`08Zaiko.IdoInputUke`）も同時に許可する |
| B-6 | `EnumLoginRole` との関係 | **当面併存**（`Id_Role`＝メニュー表示、`ResponsibilityScope`＝業務上の立場） | 今回 `EnumLoginRole` を廃止して一本化する。メニュー定義・ログイン画面・コンバータの改修が伴うため別作業を推奨 |

## 11. 実装後の検証手順

1. `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`
2. `Tests/TestSqlDialect` の `DdlSnapshotTests`（3DB の列集合一致・照合順序）を実行。
3. `Tests/TestServer` の `MasterCascadeDbTests` を実行（`V*` 未追加の回帰確認）。
4. 新規SQLiteファイルで初期化 → `SysPermissionProfile` 4 件、`SysPermissionProfileDetail` 11 件、`MasterShain` の 2 列が存在することを確認。
5. 既存DB（`server-user163.db` の複製）で起動 → `SysUpdateDb` に `26_08_28_01` が記録され、`MasterShain` の 2 列が `0` で追加され、新規 2 テーブルが作成・初期データ投入されることを確認。
6. 社員マスタメンテ画面を開き、追加・修正・削除が従来どおり動くことを確認（UI 未変更のため表示は変わらない）。
7. `git diff --check`
