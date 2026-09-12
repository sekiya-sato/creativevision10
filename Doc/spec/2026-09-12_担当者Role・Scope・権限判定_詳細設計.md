# 担当者Role・Scope・権限判定 詳細設計

対象: `CvBase`（テーブル定義・列・マイグレーション） / `CvWpfclient`（`MenuData`・メニュー表示・権限判定）
状態: **設計中（未承認）**。本書は [`Doc/spec/2026-09-05_CV10機能完成度チェックリスト.md`](2026-09-05_CV10機能完成度チェックリスト.md) の未決事項 `D-10`（権限判定・メニュー公開状態）への回答。10.0でテーブル定義・基盤を実装し、10.2以降で判定処理を有効化する（0.3節）。
前身: [`Doc/spec/archive/2026-08-28_MasterShain担当区分・権限プロファイル_詳細設計.md`](archive/2026-08-28_MasterShain担当区分・権限プロファイル_詳細設計.md)（以下「前身設計書」）。

---

## 0. 背景と決定

### 0.1 本設計が扱うのは「業務メニュー権限」であって、システムセキュリティではない

`SysLogin`が持つ「CV10にログインできるか」は**システム権限**であり、既存のまま変更しない。本設計が新設するRoleは、**ログイン済みの社員が、どの業務メニューを開けるか／開いた画面で参照のみか編集可か**を決める**業務権限**であり、両者は別物である。

| | システム権限（既存・対象外） | 業務権限（本設計） |
|---|---|---|
| 実体 | `SysLogin`（LoginId/CryptPassword/ExpDate） | `MasterShainResponsibility`/`MasterShainResponsibilityScope`/`SysPermissionProfile`/`SysPermissionProfileDetail` |
| 決めること | CV10に接続できるか | 業務メニューを開けるか、開いた画面が参照のみか編集可か |
| 判定する場所 | サーバ（`LoginService`、JWT発行） | クライアント（`CvWpfclient`） |
| 破られた場合 | 不正アクセス | 正規ログイン済み社員が業務上の担当外を触った、という運用上の問題 |

本設計はサーバ側に強制ポイントを置かない。業務権限はあくまで**業務メニューの出し分けと操作範囲の目安**であり、CV10の認証・アクセス制御（システム権限）を代替・強化するものではない。

### 0.2 前身設計書との関係 — 全面改定の理由

前身設計書（2026-08-28、実装済み）が導入した`MasterShain.ResponsibilityScope`/`Id_PermissionProfile`、`SysPermissionProfile`/`Detail`、`EnumResponsibilityScope`/`EnumPermissionType`は、判定処理が一度も実装されず、業務ロジックからの参照は次の4箇所（定義・登録・入力欄のみ）に限られる。

| 参照箇所 | 内容 |
|---|---|
| [DefineDataTable.cs:27](CvBase/DefineDataTable.cs:27) | `TableTypes`登録 |
| [UpdateDb.cs:46](CvBase/UpdateDb.cs:46) | マイグレーション`26_08_28_01` |
| [MasterShainMenteView.xaml:341](CvWpfclient/Views/01Master/MasterShainMenteView.xaml:341) | 入力欄のみ（判定処理なし） |
| [UpdateDbTests.cs:13](Tests/TestServer/UpdateDbTests.cs:13) | 件数アサートのみ |

`EnumResponsibilityScope`（[BaseEnumClass.cs:231](CvBase/Share/BaseEnumClass.cs:231)）は`CorporateUser`の値変更（4→90、commit `96145dd7`）や外部Role相当の値追加まで経たが、これらも参照ゼロである。よって本設計は**加算的設計ではなく全面改定**とし、旧2列・旧2テーブル・旧2enumの改名・削除・初期データ差し替えを行う。

### 0.3 2段階リリース

- **10.0（本書の実装スコープ）**: Role標準台帳・Scopeテーブル・`SysPermissionProfile`/`Detail`の改定・`MenuData`のFunctionId拡張・`MenuData.AllowedRoles`廃止という**テーブル定義・基盤**を作る。判定処理は呼び出さないため、**10.0では従来どおり全メニュー・全機能が使用可能**。
- **10.2以降**: 判定処理本体（`PermissionEvaluator`、クライアント側）を実装して有効化する。`EnumLoginRole`/`SysLogin.Id_Role`の削除、権限プロファイル保守画面、社員のRole/Scope編集UIもここに含む。

---

## 1. 現行実装の実測

| 実体 | 位置 | 現状 |
|---|---|---|
| `MasterShain.ResponsibilityScope`/`Id_PermissionProfile` | [BaseDb1Master.cs:115](CvBase/BaseDb1Master.cs:115) | 0.2節のとおり参照ゼロ |
| `SysPermissionProfile`/`Detail` | [BaseDb0Login.cs:153](CvBase/BaseDb0Login.cs:153) | 初期データのみ、判定なし |
| `EnumLoginRole` | [BaseEnumClass.cs:198](CvBase/Share/BaseEnumClass.cs:198) | Standard/Shop/Warehouse/Honbu/Keiri |
| `SysLogin.Id_Role` | [BaseDb0Login.cs:27](CvBase/BaseDb0Login.cs:27) | `EnumLoginRole`の数値を保持（[LoginService.cs:92](CvServer/Services/LoginService.cs:92)、[AppGlobal.cs:82](CvWpfclient/AppGlobal.cs:82)） |
| `MenuData.AllowedRoles` | [MenuData.cs:28](CvWpfclient/Models/MenuData.cs:28) | 実設定は[MenuData.cs:101](CvWpfclient/Models/MenuData.cs:101)（店舗業務）と[MenuData.cs:114](CvWpfclient/Models/MenuData.cs:114)（倉庫業務）の2件のみ |

店舗業務・倉庫業務ショートカットは既存の標準業務メニューへのショートカットにすぎず（[MenuData.cs:99](CvWpfclient/Models/MenuData.cs:99)のコメント参照）、標準メニュー側からは同じ画面へ無条件に到達できる。`EnumLoginRole`は実効的なアクセス制御になったことが一度もない。

認証はJWT（`ClaimTypes.SerialNumber = SysLogin.Id`）で行われ、これは0.1節の「システム権限」であり本設計の対象外。

---

## 2. 現行規約の実測（本書が従う根拠）

| 規約 | 実測 |
|---|---|
| PKは`long Id`（AutoIncrement）、業務コードは`Code`列 | [BaseDbDefinition.cs:10](CvBase/Share/BaseDbDefinition.cs:10) |
| enumは数値列+`En*`ラッパ | [BaseDb1Master.cs:117](CvBase/BaseDb1Master.cs:117) |
| `Master*`＝業務マスタ、`Sys*`＝システム管理 | [DefineDataTable.cs:104](CvBase/DefineDataTable.cs:104) |
| 1:N子テーブルは「親名＋Detail」 | `SysPermissionProfileDetail`（[BaseDb0Login.cs:259](CvBase/BaseDb0Login.cs:259)） |
| 新規テーブルは`TableTypes`へ登録、初期データは`CreateDefaultData` | [DefineDataTable.cs:27](CvBase/DefineDataTable.cs:27) |
| 列追加/削除/改名は`UpdateDb.versions`に8桁バージョンで追記、SQLは1本の文字列 | 最新`26_09_10_01`（[UpdateDb.cs:61](CvBase/UpdateDb.cs:61)）。`DROP COLUMN`/`RENAME COLUMN`前例あり |
| `MasterMeisho.Kubun`は`const string`定数、区分追加は低コスト | [BaseDb0System.cs:183-252](CvBase/BaseDb0System.cs:183)。`CHR`/`KIJ`/`C30`〜`C32`の追加前例あり |

---

## 3. 命名・構造変更の対応表

| 前身設計書の実体 | 本書の扱い |
|---|---|
| `MasterShain.ResponsibilityScope`（int列） | **廃止（DROP COLUMN）**。データは`MasterShainResponsibility`+`Scope`へ移行 |
| `MasterShain.Id_PermissionProfile`（long FK） | **維持** |
| `EnumResponsibilityScope`/`EnumResponsibilityExternalScope` | **廃止**。列挙値はRole標準台帳（5章）へ吸収 |
| `SysPermissionProfile.ResponsibilityScope`/`IsDefault` | **廃止（DROP COLUMN）** |
| `EnumPermissionType`（8種） | **View/Edit の2種へ縮小**（8章） |
| `MenuData.AllowedRoles` | **10.0で廃止**しFunctionIdベースへ一本化 |
| `EnumLoginRole`/`SysLogin.Id_Role` | **10.2以降で廃止**（`LoginService`/gRPC改修と同時、9.2節） |

---

## 4. 概念モデル

```text
MasterShain（社員）
 └─ MasterShainResponsibility（責任Role割当、1人にN行＝兼務）
     ├─ Id_ResponsibilityRole → MasterResponsibilityRole（P01〜P22相当）
     └─ MasterShainResponsibilityScope（割当ごとにN行）
         ├─ ScopeKubun（Brand/Area/Store/Warehouse/Customer/CustomerGroup/Supplier/Bumon/ProductCategory/Season/All）
         └─ TargetId

MasterShain.Id_PermissionProfile（単一FK）
 └─ SysPermissionProfile → SysPermissionProfileDetail（FunctionId × View/Edit × IsAllowed）

MenuData（CvWpfclient）
 └─ FunctionIdを機械導出 → SysPermissionProfileDetail.FunctionIdと文字列一致で判定（クライアント内で完結）
```

Role（何を担当するか）・Scope（どこまで担当するか）・Permission（メニューを開けるか/編集できるか）は独立して持ち、RoleからPermissionを自動導出しない（兼務時の過剰権限化を防ぐ）。判定はすべて**クライアント側**で行い、判定に使うデータ（Role/Scope/Permission）はログイン時にサーバから読み込む（8章）。サーバは値の保管庫であり、判定ロジックを持たない。

---

## 5. テーブル・列定義

### 5.1 `MasterResponsibilityRole`（Role標準台帳）

```csharp
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Code))]
[Comment("マスター：責任Role標準台帳 何を担当するかを表す静的分類。権限を持たない")]
public sealed partial class MasterResponsibilityRole : BaseDbClass, IBaseCodeName {
	[ObservableProperty][ColumnSizeDml(12)][Comment("コード 例 P03")]
	public partial string Code { get; set; } = string.Empty;
	[ObservableProperty][ColumnSizeDml(60)][Comment("名前 例 Merchandiser")]
	public partial string Name { get; set; } = string.Empty;
	[ObservableProperty][ColumnSizeDml(100)][Comment("日本語名(既存Kana列を転用)")]
	public partial string Kana { get; set; } = string.Empty;
	[ObservableProperty][NotifyPropertyChangedFor(nameof(EnRoleCategory))]
	[Comment("Role区分 0=Core 1=BusinessModel")]
	public partial int RoleCategory { get; set; }
	[Ignore][JsonIgnore]
	public EnumResponsibilityRoleCategory EnRoleCategory {
		get => (EnumResponsibilityRoleCategory)RoleCategory;
		set => RoleCategory = (int)value;
	}
	[ObservableProperty][Comment("表示順")]
	public partial int Odr { get; set; }
	[ObservableProperty][Comment("使用可能か")]
	public partial bool IsActive { get; set; } = true;
}
```

### 5.2 `MasterShainResponsibility`（社員×Role割当、兼務対応）

```csharp
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Id_Shain), nameof(Id_ResponsibilityRole))]
[Comment("マスター：社員の責任Role割当 兼務は複数行で表現する")]
public sealed partial class MasterShainResponsibility : BaseDbClass {
	[ObservableProperty][ForeignKey(nameof(MasterShain))][Comment("社員Id")]
	public partial long Id_Shain { get; set; }
	[ObservableProperty][ForeignKey(nameof(MasterResponsibilityRole))][Comment("責任RoleId")]
	public partial long Id_ResponsibilityRole { get; set; }
	[ObservableProperty][Comment("使用可能か(異動時にfalseへ。行は残す)")]
	public partial bool IsActive { get; set; } = true;
}
```

### 5.3 `MasterShainResponsibilityScope`（割当ごとのScope行）

```csharp
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Id_ShainResponsibility), nameof(ScopeKubun), nameof(TargetId))]
[Comment("マスター：責任Role割当ごとのScope行 どこまで担当するかを表す")]
public sealed partial class MasterShainResponsibilityScope : BaseDbClass {
	[ObservableProperty][ForeignKey(nameof(MasterShainResponsibility))][Comment("責任Role割当Id")]
	public partial long Id_ShainResponsibility { get; set; }
	[ObservableProperty][NotifyPropertyChangedFor(nameof(EnScopeKubun))]
	[Comment("Scope区分 1=Brand 2=Area 3=Store 4=Warehouse 5=Customer 6=CustomerGroup 7=Supplier 8=Bumon 9=ProductCategory 10=Season 99=All")]
	public partial int ScopeKubun { get; set; }
	[Ignore][JsonIgnore]
	public EnumScopeKubun EnScopeKubun {
		get => (EnumScopeKubun)ScopeKubun;
		set => ScopeKubun = (int)value;
	}
	[ObservableProperty][Comment("対象Id。ScopeKubun=All(99)のときは0固定で未使用")]
	public partial long TargetId { get; set; }
}
```

`TargetId`はポリモーフィックFKのため`V*`列を持たない。10.0では判定に`TargetId`の一致は使わず、ScopeKubunの有無だけで判定する（8.4節、簡略化の理由も同節）。`TargetId`は将来の精緻化（対象値との突合）に備えて保持するのみ。

`EnumScopeKubun`（値0は予約し使わない）:

```csharp
public enum EnumScopeKubun : int {
	Brand = 1, Area = 2, Store = 3, Warehouse = 4, Customer = 5,
	CustomerGroup = 6, Supplier = 7, Bumon = 8, ProductCategory = 9, Season = 10,
	All = 99,
}
```

### 5.4 `SysPermissionProfile`/`SysPermissionProfileDetail`の改定

`SysPermissionProfile`（[BaseDb0Login.cs:153](CvBase/BaseDb0Login.cs:153)）から`ResponsibilityScope`列と`IsDefault`列を削除する。残す列はCode/Name/Memo/IsActive/ProfileVersion。

`SysPermissionProfileDetail`の構造（FunctionId/PermissionType/IsAllowed）は維持するが、`EnumPermissionType`を次の2値へ縮小する（値0は予約し使わない）。

```csharp
public enum EnumPermissionType : int {
	View = 1,
	Edit = 2,
}
```

`View`＝その画面を開ける（メニュー起動可）。`Edit`＝開いた画面で修正・追加・削除ができる（Scopeとの掛け合わせは8.3節・8.4節）。

### 5.5 `DefineDataTable.TableTypes`への登録

[DefineDataTable.cs:35](CvBase/DefineDataTable.cs:35)の`MasterShain`直後へ`MasterResponsibilityRole`/`MasterShainResponsibility`/`MasterShainResponsibilityScope`を追加する。初期データ投入（[DefineDataTable.cs:151](CvBase/DefineDataTable.cs:151)付近）へ`MasterResponsibilityRole.CreateDefaultData(db);`を追記する。

### 5.6 `MasterSysman`への追加列（未登録FunctionIdの既定ポリシー）

未登録FunctionIdの既定ポリシー（決定事項C-9）は`MasterSysman`（[BaseDb0System.cs:16](CvBase/BaseDb0System.cs:16)、[DefineDataTable.cs:31](CvBase/DefineDataTable.cs:31)でTableTypes登録済み）へ列を追加して持つ。`MasterConfig`はシステム設定の一元化という別用途で使われているため、業務権限の既定ポリシーはそちらに混ぜず`MasterSysman`（会社全体で1件しか無い設定行）に置く。列名・enumラッパは既存の`CostMethod`/`EnumCostMethod`（[BaseDb0System.cs:133](CvBase/BaseDb0System.cs:133)）と同じ流儀に揃える。

```csharp
// MasterSysman へ追加(CvBase/BaseDb0System.cs)
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(EnPermissionDefaultMode))]
[Comment("業務権限の既定ポリシー 0=Audit 1=Warn 2=Deny")]
public partial int PermissionDefaultMode { get; set; }
[Ignore][JsonIgnore]
public EnumPermissionDefaultMode EnPermissionDefaultMode {
	get => (EnumPermissionDefaultMode)PermissionDefaultMode;
	set => PermissionDefaultMode = (int)value;
}
```

`EnumPermissionDefaultMode`は他のenumと同じく`CvBase/Share/BaseEnumClass.cs`に置く（`CvWpfclient`はここを参照して使う）。

```csharp
public enum EnumPermissionDefaultMode : int { Audit = 0, Warn = 1, Deny = 2 }
```

初期データ（[DefineDataTable.cs:201](CvBase/DefineDataTable.cs:201)付近の`MasterSysman`生成）は変更不要。`int`の既定値0がそのまま`Audit`になるため、明示的な設定は不要。

---

## 6. Role標準台帳

ペルソナ仕様（ユーザー提示の概念仕様）5章のP01〜P22をそのまま採用する。

| Code | Name | 日本語名 | Category |
|---|---|---|---|
| P01 | Executive | 経営責任者 | Core |
| P02 | BusinessController | 事業統括 | Core |
| P03 | Merchandiser | MD | Core |
| P04 | Buyer | バイヤー | Core |
| P05 | ProductPlanner | 商品企画 | BusinessModel |
| P06 | ProductionManager | 生産管理 | BusinessModel |
| P07 | Procurement | 調達 | BusinessModel |
| P08 | WholesaleSales | 卸営業 | BusinessModel |
| P09 | WholesaleManager | 卸営業責任者 | BusinessModel |
| P10 | SalesAdministration | 営業事務 | Core |
| P11 | InventoryController | 在庫管理 | Core |
| P12 | Distributor | 店舗配分担当 | BusinessModel |
| P13 | WarehouseManager | 倉庫責任者 | BusinessModel |
| P14 | WarehouseOperator | 倉庫作業担当 | BusinessModel |
| P15 | AreaManager | エリアマネージャ | BusinessModel |
| P16 | StoreManager | 店舗責任者 | BusinessModel |
| P17 | StoreStaff | 店舗スタッフ | BusinessModel |
| P18 | ECManager | EC責任者 | BusinessModel |
| P19 | ECOperations | EC運用担当 | BusinessModel |
| P20 | Accounting | 経理 | Core |
| P21 | SystemAdministrator | システム管理 | Core |
| P22 | MasterDataAdministrator | マスター管理 | Core |

旧`EnumResponsibilityScope`の吸収先: `StoreStaff`(1)→P17、`StoreManager`(2)→P16、`AreaManager`(3)→P15、`CorporateUser`(90)→**P02（暫定、決定事項C-1）**。

---

## 7. Scope定義と実マスタ対応表

| Scope | 対応するCV10実マスタ |
|---|---|
| Brand | `MasterMeisho`（`Kubun='BRD'`） |
| Area | `MasterMeisho`（新設`Kubun='SCA'`。上代用`C31`とは別物、7.1節） |
| Store | `MasterTokui`（`TenType=6`直営店） |
| Warehouse | `MasterTokui`（`TenType=0`倉庫） |
| Customer | `MasterTokui`（`TenType=1`卸先。売仕店`TenType=3`を含めるかは21章） |
| CustomerGroup | `MasterMeisho`（新設`Kubun='SCG'`。上代用`C30`とは別物） |
| Supplier | `MasterShiire` |
| Bumon | `MasterMeisho`（`Kubun='BMN'`）。既存`Id_Bumon`の移行元 |
| ProductCategory | `MasterMeisho`（`Kubun='ITM'`） |
| Season | `MasterMeisho`（`Kubun='SZN'`） |

不採用: Channel（対応するマスタが業務チャネル区分と一致しない）、Company/BusinessUnit（CV10は1DB=1社運用）、Department（Bumonへ統合）。

### 7.1 `SCA`/`SCG`の新設（決定事項C-4）

Area・CustomerGroupは、上代一括変更用の既存区分（`C31`地域・`C30`価格グループ）を流用せず、担当範囲専用の区分を新設する。価格区分と担当範囲は業務上一致するとは限らず、混同すると上代側の運用変更がScopeへ波及するため。`MasterMeisho.Kubun`は単純な`const string`定数（[BaseDb0System.cs:183](CvBase/BaseDb0System.cs:183)）で、`MasterMeishoMenteViewModel`はKubun一覧を`Kubun='IDX'`行から動的読み込みするため追加コストは低い。

```csharp
public const string KubunScopeArea = "SCA";
public const string KubunScopeCustomerGroup = "SCG";
```

**注意**: `SCA`/`SCG`は`C30`/`C31`（上代の価格グループ・地域）とは別物であり、混同しないこと。

「全社（無制限）」は`EnumScopeKubun.All`(99)を持つScope行1件で表現する（8.4節）。

---

## 8. 権限評価方式

判定は**すべてクライアント（`CvWpfclient`）側**で行う。サーバは`SysPermissionProfile`/`Detail`・Role/Scopeのデータを保管するだけで、判定ロジックは持たない。

### 8.1 単一プロファイル（決定済みC-2）

`MasterShain.Id_PermissionProfile`の単一FKを維持する。兼務してもプロファイルは1個のまま。複数プロファイルの合成は行わない（兼務時の権限過剰合成を避けるため）。

### 8.2 メニュー起動判定・編集可否判定（Permission）

`FunctionId × PermissionType`（View or Edit）ごとに、`SysPermissionProfileDetail`を次の順で見る。

1. 明示`IsAllowed=false`の行があれば**不可**。
2. 明示`IsAllowed=true`の行があれば**可**。
3. 該当行が無ければ`MasterSysman.PermissionDefaultMode`の既定ポリシー（Audit=可・記録のみ／Warn=可・警告ログ／Deny=不可。決定事項C-9、5.6節）に従う。

`View`が不可ならメニュー非表示・起動拒否。`Edit`が可でも、次節のScope判定を満たさなければ参照のみになる。

### 8.3 Scope判定の対象画面（マスターメンテ系・入力系のみ）

Scope判定は**マスターメンテ系（`*MenteView`）と入力系（`*InputView`）の画面だけ**を対象にする。照会系・帳票系・一覧系・設定系（`*QueryView`/`*ReportView`/`*ListView`/`*PrintView`/`*SettingView`等）はそもそも編集という概念が無いか、あっても業務の担当範囲とは無関係なため、Scope判定を**行わない**（可否はPermissionのView/Editのみで決まる）。

対象かどうかは`MenuData`に別途宣言を持たせず、`ViewType`の型名が`MenteView`または`InputView`で終わるかどうかから機械的に導出する（10.3節）。FunctionId自体も`ViewType`から導出しており（決定済みC-7、二重定義をしない）、対象判定も同じ考え方に揃える。

CV10の`*View.xaml`（215件）を接尾辞で数えると次のとおりで、対象は`Mente`19件＋`Input`20件＝**39画面**、残り176画面は対象外である。

| 接尾辞 | 件数 | 分類 | Scope判定 |
|---|---:|---|---|
| `Mente` | 19 | マスターメンテ系 | 対象 |
| `Input` | 20 | 入力系 | 対象 |
| `Report` | 34 | 帳票系 | 対象外 |
| `List` | 11 | 一覧系 | 対象外 |
| `Query` | 8 | 照会系 | 対象外 |
| `Print` | 6 | 印刷系 | 対象外 |
| `Setting` | 5 | 設定系 | 対象外 |

`MasterJouDaiBulkChange`/`StockKakeUpdate`等、接尾辞がこの規約に当てはまらない編集系画面も存在するが、これらはScopeによる参照のみ/編集可の制御を行わず、メニュー起動権限（`FunctionId × View/Edit`）だけで制御する。39画面以外はすべてこの扱いであり、取りこぼしを気にする必要はない。

### 8.4 Scope判定の規則（すべてOR、画面単位）

対象画面（上記39画面相当）でのScope判定は次のとおり。

1. 社員が兼務するすべてのRole割当のScope行を、Role横断でフラットに集める。判定はこの集合に対して行い、**同一種別内も異種別間もすべてOR**とする（前身の「同一種別内OR・異種別間AND」は廃止、決定済みC-3）。
2. 集合の中に`All`(99)が1件でもあれば無条件でScope一致。
3. `MenuData`に画面ごと宣言する`EditScopeKubuns`（その画面の編集可否に関係するScope種別の集合）が**未設定（null/空）ならScope判定をスキップし、一致とみなす**。対象39画面であっても宣言が済むまでは従来どおり編集できる。10.2で判定を有効化した瞬間に未宣言の画面が一斉に参照のみへ落ちる事故を防ぐためであり、0.3節・9.4節と同じ「有効化しても業務が止まらない」方針に揃える。
4. `EditScopeKubuns`が設定されていれば、社員のScope行がそのいずれかの種別を1件でも持っていればScope一致。
5. いずれも無ければ不一致（参照のみ）。

`TargetId`（店舗・ブランド等の具体的な対象）は10.0では判定に使わない。「対象値を実行時に確定できる画面は値と突合する」というより精緻な案は複雑さに見合わないと判断し不採用とした（複雑案は決定事項C-3参照）。

**編集可否のまとめ**:

| Edit（8.2） | 画面がScope対象か | Scope一致 | 結果 |
|---|---|---|---|
| 不可 | — | — | 参照のみ |
| 可 | 対象外（照会・帳票等） | — | 修正追加削除可（Permissionのみで決定） |
| 可 | 対象（Mente/Input）だが`EditScopeKubuns`未宣言 | 判定しない | 修正追加削除可 |
| 可 | 対象（Mente/Input） | 不一致 | 参照のみ |
| 可 | 対象（Mente/Input） | 一致 | 修正追加削除可 |

### 8.5 配置とタイミング

`PermissionEvaluator`は**`CvWpfclient`側**（`CvWpfclient/Services/PermissionEvaluator.cs`）に置く。`CvServer`はこのクラスを参照しない（`CvServer`の`CvWpfclient`参照は無く、技術的にも不可能）。`CvDomainLogic`は`CvServer`から使われるため配置先として不適切と判断した。

```csharp
namespace CvWpfclient.Services;
// EnumPermissionDefaultMode は CvBase/Share/BaseEnumClass.cs 側の定義を使う(5.6節)

public sealed class PermissionEvaluator {
	public bool CanOpen(string functionId);
	public bool CanEdit(string functionId);
}
```

Role/Scope/権限プロファイルは**ログイン時に1回読み込む**。TTLキャッシュや即時失効の仕組みは持たない。ログイン中に管理者が権限を変更しても、その社員には**次回ログインまで反映されない**（仕様として明記する）。

---

## 9. `EnumLoginRole`からの一本化移行（決定済みC-6）

10.0で`MenuData.AllowedRoles`のみ廃止し、`EnumLoginRole`/`SysLogin.Id_Role`は10.2以降`LoginService`/gRPC契約の改修と同時に廃止する（列を先に消すと`LoginService`のビルドが壊れるため）。

### 9.1 `MenuData.cs:101,114`の移行

10.0では`AllowedRoles`プロパティごと削除し、代替フィルタは入れない（両ショートカットは誰にでも無条件表示のまま）。10.2以降で、配下画面のFunctionIdに対する`View`権限の有無からフォルダの可視性を決めるフィルタを実装する。

### 9.2 `SysLogin.Id_Role`

10.0では**残置・未使用化**（列は残すが`MenuData`側では参照しなくなる）。10.2で`LoginService.cs:92,97,141`・`LoginReply.Role`の改修と同時に`DROP COLUMN`する。

### 9.3 未登録FunctionIdの既定ポリシー

`MasterSysman.PermissionDefaultMode`（5.6節）を使う。運用開始は`Audit`とし、運用側の判断で`Warn`→`Deny`へ手動で切り替える（決定済みC-9）。ロックアウトした場合の緊急復旧も、この列を`Audit`へ書き戻すだけでよい。

### 9.4 ロックアウト防止（決定済みC-15）

`PermissionDefaultMode`を`Deny`にした瞬間、権限を直す画面自体が拒否されて誰も直せなくなる詰みを防ぐため、`CvWpfclient`の`PermissionEvaluator`実装内に、DBの状態に関わらず常時許可する固定FunctionId一覧を持つ。

| FunctionId（想定） | 画面 |
|---|---|
| `01Master.MasterShainMente` | 社員マスタメンテ |
| `00System.SysPermissionProfileMente`（10.2新設） | 権限プロファイル保守画面 |
| `00System.SysGeneralMente` | 汎用マスタメンテ |

この3画面に限定する（権限設定を直す手段以外を含めると業務上の抜け道になるため）。

---

## 10. `MenuData`拡張とFunctionId自動導出

### 10.1 方針（決定済みC-7）

FunctionId台帳を独立テーブルとして新設しない。唯一の定義元は`MenuData.CreateAll()`（[MenuData.cs:86](CvWpfclient/Models/MenuData.cs:86)）とし、`SysPermissionProfileDetail`は例外行（明示Allow/Deny）だけを持つ。

### 10.2 導出式

```text
namespace: CvWpfclient.Views._06Uriage → 先頭の"_"を除去 → "06Uriage"
type name: ShopUriageInputView → 末尾の"View"を除去 → "ShopUriageInput"
FunctionId = "06Uriage.ShopUriageInput"
```

`Views/Sub`配下・`MainMenuView`は領域プレフィックスを持たないため導出対象外とする。

### 10.3 `MenuData`への追加プロパティ

```csharp
public partial class MenuData : ObservableObject {
	// AllowedRolesは削除（9.1節）
	public string? FunctionId { get; init; } // 明示指定。nullなら自動導出
	public IReadOnlyList<EnumScopeKubun>? EditScopeKubuns { get; init; } // Scope対象画面がどの種別を見るかの宣言(8.4節)
	public static string? DeriveFunctionId(Type viewType);
	public static bool IsScopeTargetView(Type viewType); // 型名が MenteView/InputView で終わるかどうか(8.3節)
}
```

`EditScopeKubuns`の役割は「その画面でどのScope種別を見るか」の宣言だけであり、「その画面がScope対象かどうか」は`IsScopeTargetView`が`ViewType`の型名接尾辞から機械的に決める（`MenuData`に対象かどうかを宣言するプロパティは持たせない）。対象は`*MenteView`19画面と`*InputView`20画面の計39画面であり、それ以外の画面（命名規約から外れる編集系画面を含む）はメニュー起動権限（`FunctionId × View/Edit`）だけで統制する（8.3節）。

`Views/Sub`配下の画面（呼び出し元画面から開くダイアログ）は独自のFunctionIdを持たず、呼び出し元画面のFunctionIdをそのまま使う。

### 10.4 画面台帳との整合性確認

`SysFunctionLedger`のような専用テーブルは置かない（決定事項C-16）。`MenuData`から導出したFunctionId一覧と、`SysPermissionProfileDetail`に登録されているFunctionId一覧は、クライアント起動時に両方を手元（`MenuData.CreateAll()`と既存の汎用クエリ経路での`SELECT DISTINCT FunctionId`）で突き合わせ、食い違い（権限設定はあるが画面が無い等）を自己診断ログに出すだけで足りる。保守画面は10.0では作らない。

---

## 11. 担当者別ホーム画面 I/F

Role固定Dashboardにしない。Role×Scope×現在の状態から表示項目を動的に決める（Taskは10.0/10.2で永続化せず動的算出のみ）。

```csharp
public sealed record HomeCard(string ResponsibilityRoleCode, string Title, int Count, string DrillDownFunctionId);
public interface IHomeCardProvider {
	string ResponsibilityRoleCode { get; }
	IReadOnlyList<HomeCard> BuildCards(long idShain, IReadOnlyList<MasterShainResponsibilityScope> scopes);
}
```

兼務時は各Roleのカードを集め、`DrillDownFunctionId`が同じものは合算する。表示順は`MasterResponsibilityRole.Odr`に従う。

---

## 12. 操作ログへの付与（将来、10.3以降）

操作ログ計画書（`.omo/plans/2026-08-28_user_operation_log_layer2_plan.md`）が挙げた`ScopeKey`/`PermissionProfileId`は、本書のRole/Scope構造でそのまま埋められる（決定済みC-14）。`SysOpEvent`本体・`SysHistMaster`の実装は本書のスコープ外。

---

## 13. AI分析Context I/F（10.2以降）

`CvDomainLogic`は`CvBase`のみを参照し`CodeShare`を参照しないため、AI Context DTOの配置先は`CvDomainLogic`が妥当（`CvServer`から利用される想定のため）。

```csharp
namespace CvDomainLogic;
public sealed record AiAnalysisContext(
	long IdShain, IReadOnlyList<string> ResponsibilityRoleCodes,
	IReadOnlyList<MasterShainResponsibilityScope> Scopes, long IdPermissionProfile);
```

AIはRecommendationに限定し、直接更新経路は持たせない。

---

## 14. System Actor（設計のみ・実装スコープ外）

POS/EC/倉庫等の外部システム連携主体は人間のRoleと分離する方針だけを引き継ぎ、10.0/10.2では実装しない。人間のRole標準台帳に外部Actor相当の値を混入させない（0.2節の反省を踏まえる）。

---

## 15. 初期データ

### 15.1 `MasterResponsibilityRole`

6章の22件をそのまま`CreateDefaultData`の初期データとする。

### 15.2 `SysPermissionProfile`/`SysPermissionProfileDetail`

旧4プロファイル・11明細は破棄し作り直す（Role非依存の名称へ）。

| Id | Code | Name |
|---|---|---|
| 1 | FullAccess | 全機能標準権限 |
| 2 | StoreOperationStandard | 店舗業務標準権限 |
| 3 | WarehouseOperationStandard | 倉庫業務標準権限 |
| 4 | ReadOnlyStandard | 参照専用標準権限 |

| Id_PermissionProfile | FunctionId | PermissionType | IsAllowed |
|---|---|---|---|
| 2 | `06Uriage.ShopUriageInput` | View | true |
| 2 | `06Uriage.ShopUriageInput` | Edit | true |
| 2 | `08Zaiko.ZaikoQuery` | View | true |
| 2 | `08Zaiko.StockForceInput` | View | true |
| 2 | `08Zaiko.StockForceInput` | Edit | **false** |
| 3 | `08Zaiko.IdoInputSoku` | View | true |
| 3 | `08Zaiko.IdoInputSoku` | Edit | true |
| 3 | `08Zaiko.ZaikoQuery` | View | true |
| 1 | `06Uriage.ShopUriageInput` | View | true |
| 1 | `06Uriage.ShopUriageInput` | Edit | true |
| 1 | `08Zaiko.StockForceInput` | View | true |
| 1 | `08Zaiko.StockForceInput` | Edit | true |
| 1 | `20UriageAnalysis.SalesQuickReport` | View | true |
| 1 | `07Haibun.ShopHaibunInput` | View | true |
| 1 | `07Haibun.ShopHaibunInput` | Edit | true |
| 4 | `08Zaiko.ZaikoQuery` | View | true |
| 4 | `20UriageAnalysis.SalesQuickReport` | View | true |

4プロファイル・17明細。内容は一例であり実装時に業務側の確認を仰ぐ。

### 15.3 `MasterSysman.PermissionDefaultMode`

5.6節のとおり列を追加するのみで、初期データ生成コード（[DefineDataTable.cs:201](CvBase/DefineDataTable.cs:201)）の変更は不要（`int`既定値0がそのまま`Audit`になる）。

---

## 16. マイグレーションと影響範囲

`26_09_12_01`から追記する（[UpdateDb.cs:61](CvBase/UpdateDb.cs:61)の直後）。新規テーブルは`DefineDataTable.InitializeAsync`のCREATE TABLE工程が`WriteVersionInfoAsync`より先に作る。

```csharp
new (26_09_12_01,
  "INSERT INTO MasterShainResponsibility (Vdc,Vdu,Id_Shain,Id_ResponsibilityRole,IsActive) " +
  "SELECT s.Vdu, s.Vdu, s.Id, r.Id, 1 FROM MasterShain s JOIN MasterResponsibilityRole r ON r.Code = " +
  "CASE s.ResponsibilityScope WHEN 1 THEN 'P17' WHEN 2 THEN 'P16' WHEN 3 THEN 'P15' WHEN 90 THEN 'P02' END " +
  "WHERE s.ResponsibilityScope IN (1,2,3,90);",
  "MasterShain.ResponsibilityScope からMasterShainResponsibilityへ移行(決定事項C-1)"),
new (26_09_12_02,
  "INSERT INTO MasterShainResponsibilityScope (Vdc,Vdu,Id_ShainResponsibility,ScopeKubun,TargetId) " +
  "SELECT msr.Vdu, msr.Vdu, msr.Id, 3, s.Id_Tenpo FROM MasterShain s JOIN MasterShainResponsibility msr ON msr.Id_Shain=s.Id WHERE s.Id_Tenpo<>0;" +
  "INSERT INTO MasterShainResponsibilityScope (Vdc,Vdu,Id_ShainResponsibility,ScopeKubun,TargetId) " +
  "SELECT msr.Vdu, msr.Vdu, msr.Id, 8, s.Id_Bumon FROM MasterShain s JOIN MasterShainResponsibility msr ON msr.Id_Shain=s.Id WHERE s.Id_Bumon<>0;",
  "既存Id_Tenpo・Id_BumonをScope行へ複製 列自体は残す"),
new (26_09_12_03,
  "DROP INDEX IF EXISTS SysPermissionProfile_nk2;" +
  "ALTER TABLE MasterShain DROP COLUMN ResponsibilityScope;" +
  "ALTER TABLE SysPermissionProfile DROP COLUMN ResponsibilityScope;" +
  "ALTER TABLE SysPermissionProfile DROP COLUMN IsDefault;" +
  "DELETE FROM SysPermissionProfileDetail WHERE Id_PermissionProfile IN (SELECT Id FROM SysPermissionProfile WHERE Code IN ('CorporateUserDefault','AreaManagerDefault','StoreManagerDefault','StoreStaffDefault'));" +
  "DELETE FROM SysPermissionProfile WHERE Code IN ('CorporateUserDefault','AreaManagerDefault','StoreManagerDefault','StoreStaffDefault');" +
  "INSERT INTO SysPermissionProfile (Id,Vdc,Vdu,Code,Name,Memo,IsActive,ProfileVersion) VALUES (1,strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'FullAccess','全機能標準権限','',1,1),(2,...,'StoreOperationStandard','店舗業務標準権限','',1,1),(3,...,'WarehouseOperationStandard','倉庫業務標準権限','',1,1),(4,...,'ReadOnlyStandard','参照専用標準権限','',1,1);" +
  "INSERT INTO SysPermissionProfileDetail (Vdc,Vdu,Id_PermissionProfile,FunctionId,PermissionType,IsAllowed) VALUES (...,2,'06Uriage.ShopUriageInput',1,1), /* 以下15.2節の17行 */ ...;",
  "旧SysPermissionProfileクラスのKeyDml(\"nk2\",false,nameof(ResponsibilityScope))由来のSysPermissionProfile_nk2インデックスが現行クラス定義になく実DBに残ったままのため、DROP COLUMN ResponsibilityScopeが失敗しないよう先に落とす(26_09_08_04と同様の前例)。旧列削除、旧4プロファイル・11明細を破棄。新4プロファイル・17明細(15.2節)のINSERTもここに含める(実装時のOpus決定、下記注参照)"),
new (26_09_12_04,
  "INSERT INTO MasterMeisho (Vdc,Vdu,Kubun,KubunName,Code,Name,Ryaku,Kana,Odr) VALUES " +
  "(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'IDX','名称区分','SCA','担当エリア区分','','',40)," +
  "(strftime('%s','now')*10000000+621355968000000000,strftime('%s','now')*10000000+621355968000000000,'IDX','名称区分','SCG','担当得意先グループ区分','','',41);",
  "SCA/SCGのIDX行を追加(7.1節)"),
new (26_09_12_05,
  "ALTER TABLE MasterSysman ADD COLUMN PermissionDefaultMode NUMBER not null default 0;",
  "業務権限の既定ポリシー列を追加(5.6節、決定事項C-9) MasterSysmanは1行のみのためdefault 0(Audit)がそのまま既存行に入る"),
```

**`26_09_12_03`が新データのINSERTを兼ねる理由（実装時の決定）**: `WriteVersionInfoAsync`は`latestDb == null`（新規DB）のとき最新バージョンを記録するだけでマイグレーションSQLを実行しない。一方`CreateDefaultData`はマイグレーションより先に走り「件数0のときだけ投入」する。したがって新規DBでは`CreateDefaultData`が新4プロファイル・17明細を投入しマイグレーションSQLは実行されない（重複しない）が、既存DBでは`CreateDefaultData`が件数≠0でスキップされるため、`26_09_12_03`のSQL末尾に新4プロファイル・17明細の`INSERT`を直接含めて、既存DBだけに投入させる。

`SysLogin.Id_Role`の`DROP COLUMN`はここに含めない。`LoginService`/gRPC契約の改修と同時に、10.2着手時にバージョン採番する（9.2節）。

### 16.1 影響範囲

| 対象 | 影響 |
|---|---|
| `Tests/TestSqlDialect`の`DdlSnapshotTests` | 新規3テーブルが自動的に対象になる |
| `Tests/TestServer/UpdateDbTests.cs` | `SysPermissionProfileDefaultDataTests`のアサートを4件・17件へ更新 |
| `CvServer` | **変更なし**（本設計はクライアント側のみで完結する） |
| `CvWpfclient` | `MenuData`拡張、社員マスタメンテのUI変更（10.2以降） |
| `printform/MasterShainMente.qfm` | `ResponsibilityScope`列を印字に使っていないか要確認（21章） |

---

## 17. 本設計のスコープ

**含む（10.0）**: Role標準台帳・Scopeテーブルの新設と初期データ、`SysPermissionProfile`/`Detail`の改定、`MenuData`のFunctionId拡張、`MenuData.AllowedRoles`廃止（全メニュー使用可能のまま）、判定ロジックのシグネチャ定義。

**含まない（10.2以降）**: `PermissionEvaluator`の実装本体・画面への組み込み、社員マスタメンテのRole/Scope編集UI、権限プロファイル保守画面、`EnumLoginRole`/`SysLogin.Id_Role`の削除と`LoginService`改修、操作ログ本体、永続Task、AI Recommendation本体、System Actorの実装。

---

## 18. リスクと対策

| リスク | 対策 |
|---|---|
| 担当替え（異動）の反映漏れ | `MasterShainResponsibility`/`Scope`の更新を人事異動フローの一部として運用に組み込む（本書は仕組みのみ提供） |
| ロックアウト（権限設定画面自体が見えなくなる） | 9.4節の固定許可リスト |
| 一本化でメニューが見えなくなる | `Deny`段階へ進める前に、店舗業務・倉庫業務ショートカット配下18画面のFunctionIdへ明示Allow行を用意する |
| 兼務でScopeが広がりすぎる | 全てORのため兼務するほど編集可能範囲は広がる。業務上の担当外を触れる可能性はあるが、本設計はセキュリティ境界ではなく業務メニュー権限であるため許容する（決定済みC-3） |

---

## 19. 決定事項

| No | 論点 | 状態 |
|---|---|---|
| C-1 | 旧`CorporateUser`(90)の移行先 | 暫定でP02に割り当て、運用で個別修正 |
| C-2 | 権限プロファイルは単一 | 決定済み（ユーザー承認済み） |
| C-3 | Scope評価は全てOR、画面単位、`TargetId`は10.0では未使用 | 決定済み（ユーザー回答済み）。代替: `TargetId`まで突合する精緻な案（前版で検討）は複雑さに見合わないため不採用 |
| C-4 | Area/CustomerGroup用に`SCA`/`SCG`を新設 | 決定済み（ユーザー承認済み） |
| C-5 | `SysPermissionProfile.IsDefault`削除 | 決定済み（ユーザー承認済み） |
| C-6 | `EnumLoginRole`一本化。`AllowedRoles`は10.0、`Id_Role`は10.2 | 決定済み（ユーザー承認済み） |
| C-7 | FunctionId台帳は`MenuData`から自動導出、独立テーブルを持たない | 決定済み（ユーザー承認済み） |
| C-8 | FunctionId導出結果のサーバ連携方式 | **論点消滅**。サーバが業務権限に関与しない方式（0.1節）になり、導出結果をサーバへ報告する必要が無くなった（C-16へ収斂） |
| C-9 | 未登録FunctionIdの既定はフラグの手動切替 | 決定済み（ユーザー承認済み）。**保持先は`MasterConfig`ではなく`MasterSysman.PermissionDefaultMode`列**（5.6節。`MasterConfig`は別用途のため混ぜない） |
| C-10 | 権限・Scopeキャッシュの失効方式 | **論点消滅**。ログイン時読込・次回ログインまで反映しない方式（8.5節）に置き換わり、TTLキャッシュという論点自体が無くなった |
| C-11 | Scope強制の適用範囲（サーバ書込系/参照系） | **論点消滅**。サーバ側強制という前提自体が無くなった（0.1節・8章） |
| C-12 | Role・Scopeは3テーブル構成 | 決定済み（ユーザー承認済み） |
| C-13 | `MasterResponsibilityRole`の日本語名は`Kana`列を転用 | 決定済み（ユーザー承認済み） |
| C-14 | 操作ログ計画書D-4の決着（`MasterShainResponsibilityScope`で表現） | 決定済み（ユーザー承認済み） |
| C-15 | ロックアウト防止はクライアント側固定許可リスト | 決定済み（ユーザー承認済み） |
| C-16 | `SysFunctionLedger`/`FunctionLedgerEntry`を置かず、クライアント内自己診断のみとする | 決定済み（ユーザー承認済み） |

---

## 20. 実装後の検証手順

1. `C:\gitroot\UT\vscmd.bat dotnet build creativevision10.slnx`
2. `Tests/TestSqlDialect`の`DdlSnapshotTests`を実行する。
3. `Tests/TestServer/UpdateDbTests.cs`のアサートを4件・17件へ更新して実行する。
4. 新規SQLiteファイルで初期化し、新規3テーブル・`SCA`/`SCG`のIDX行・`MasterSysman.PermissionDefaultMode`列(既定0)・`ResponsibilityScope`列消滅を確認する。
5. 既存DB複製で起動し、マイグレーション適用と`Id_Role`列が残っていることを確認する。
6. 全メニュー・全機能が引き続き使用可能であることを確認する（10.0の必須条件）。
7. （10.2以降）`PermissionEvaluator`の単体テストをクライアント側に追加する（配置先は実装時に確認）。

---

## 21. 未確認事項

1. Customer Scopeに売仕店（`TenType=3`）を含めるか。
2. ロックアウト防止のブートストラップ許可範囲（9.4節の3画面）が実運用で十分か。
3. Scope対象の39画面（`*MenteView`19・`*InputView`20）それぞれに`MenuData.EditScopeKubuns`としてどのScope種別を宣言するか。10.2で判定を有効化する前に決める必要がある。
4. `TargetId`を将来使う精緻化（対象値との突合）が実際に必要になるか。
