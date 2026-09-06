using CommunityToolkit.Mvvm.ComponentModel;
using CvAsset;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;

/// <summary>
/// 社員テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Code))]
[Comment("マスター：社員テーブル 店舗、部門などのマスタと紐づく社員情報")]
[OldTableCommentAttr("HC$MASTER_SHAIN")]
public sealed partial class MasterShain : BaseDbClass, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(12)]
	[OldTableCommentAttr("社員CD")]
	[Comment("コード")]
	public partial string Code { get; set; } = string.Empty;
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(80)]
	[OldTableCommentAttr("名前")]
	[Comment("名前")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("略称")]
	public partial string Ryaku { get; set; } = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("フリガナ")]
	[Comment("カナ")]
	public partial string Kana { get; set; } = string.Empty;
	/// <summary>
	/// メールアドレス
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[OldTableCommentAttr("メール")]
	[Comment("メールアドレス")]
	public partial string Mail { get; set; } = string.Empty;
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("店舗CD")]
	[ForeignKey(nameof(MasterTokui), tenType: 6)]
	[Comment("店舗Id")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("店舗データ")]
	public partial CodeNameView VTenpo { get; set; } = new();
	/// <summary>
	/// 部門Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("部門")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunBumon)]
	[Comment("部門Id")]
	public partial long Id_Bumon { get; set; }
	/// <summary>
	/// 部門データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("部門データ")]
	public partial CodeNameView VBumon { get; set; } = new();
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[OldTableCommentAttr("名称CD01 - 名称CD05")]
	[ForeignKey(nameof(MasterMeisho), meishoListKubunTop: MasterMeisho.KubunTopShain)]
	[Comment("名称リスト")]
	public partial List<MasterGeneralMeisho>? Jsub { get; set; }
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[Comment("詳細内容")]
	public partial BaseDetailClass? Jdetail { get; set; }
	/// <summary>
	/// 有効期限 yyyyMMdd (この期限を過ぎた場合はログイン無効) ただし今のところは未使用
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("有効期限")]
	[Comment("有効期限 yyyyMMdd (この期限を過ぎた場合はログイン無効) ただし今のところは未使用")]
	public partial string ExpireDate { get; set; } = string.Empty;
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
}

/// <summary>
/// 顧客テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Code))]
[Comment("マスター：顧客テーブル 店頭顧客あるいはEC顧客")]
[OldTableCommentAttr("HC$MASTER_KOKYAKU")]
public sealed partial class MasterEndCustomer : BaseDbHasAddress, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(12)]
	[OldTableCommentAttr("顧客CD")]
	[Comment("コード")]
	public partial string Code { get; set; } = string.Empty;
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(80)]
	[OldTableCommentAttr("顧客名")]
	[Comment("名前")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("略称")]
	public partial string Ryaku { get; set; } = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("カナ")]
	[Comment("カナ")]
	public partial string Kana { get; set; } = string.Empty;
	/// <summary>
	/// ランク
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("顧客ランク")]
	[Comment("ランク")]
	public partial string Rank { get; set; } = string.Empty;
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("店舗CD")]
	[ForeignKey(nameof(MasterTokui), tenType: 6)]
	[Comment("店舗Id")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("店舗データ")]
	public partial CodeNameView VTenpo { get; set; } = new();
	/// <summary>
	/// 誕生日 yyyyMMdd
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("誕生日")]
	[Comment("誕生日 yyyyMMdd")]
	public partial string Birthday { get; set; } = string.Empty;
	/// <summary>
	/// 誕生日 MMdd
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(4)]
	[Comment("誕生日 MMdd")]
	public partial string BirthNoyear { get; set; } = string.Empty;
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[OldTableCommentAttr("メモ")]
	[Comment("メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 性別 0=不明 1=男性 2=女性
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnGender))]
	[OldTableCommentAttr("性別")]
	[Comment("性別 0=不明 1=男性 2=女性")]
	public partial int Gender { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumGender EnGender {
		get => (EnumGender)Gender;
		set => Gender = (int)value;
	}
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[ForeignKey(nameof(MasterMeisho), meishoListKubunTop: MasterMeisho.KubunTopEndCustomer)]
	[Comment("名称リスト")]
	public partial List<MasterGeneralMeisho>? Jsub { get; set; }
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[Comment("詳細内容")]
	public partial BaseDetailClass? Jdetail { get; set; }
}

/// <summary>
/// 顧客マスターに紐づけられた会員情報テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_Customer))]
[Comment("顧客マスターに紐づけられた会員情報をもつ")]
[OldTableCommentAttr("HC$MASTER_KOKYAKU_LOGIN", "顧客CD=HC$MASTER_KOKYAKU.顧客CD")]
public sealed partial class MasterEndCustomerAccount : BaseDbClass {
	/// <summary>
	/// 顧客Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterEndCustomer))]
	[Comment("顧客Id")]
	public partial long Id_Customer { get; set; }
	/// <summary>
	/// アカウントLoginID
	/// </summary>
	[ObservableProperty]
	[Comment("アカウントLoginID")]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU_LOGIN.ログインID")]
	public partial string AccountId { get; set; } = string.Empty;
	/// <summary>
	/// アカウントPassword
	/// </summary>
	[ObservableProperty]
	[Comment("アカウントPassword")]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU_LOGIN.PASS")]
	public partial string AccountPassword { get; set; } = string.Empty;
	/// <summary>
	/// 退会フラグ
	/// </summary>
	[ObservableProperty]
	[Comment("退会フラグ 1=退会 0=有効")]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU.退会FLG")]
	public partial int IsWithdrawalFlag { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsWithdrawal {
		get => (EnumYesNo)IsWithdrawalFlag;
		set => IsWithdrawalFlag = (int)value;
	}
	/// <summary>
	/// 退会日
	/// </summary>
	[ObservableProperty]
	[Comment("退会日 yyyyMMdd")]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU.退会日")]
	public partial string WithdrawnDate { get; set; } = string.Empty;
	/// <summary>
	/// 顧客区分
	/// </summary>
	[ObservableProperty]
	[Comment("顧客区分")]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU.顧客区分")]
	public partial int Kubun { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumCustomerLcvKubun EnKubun {
		get => (EnumCustomerLcvKubun)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	/// ポイントランク(ゴールド、シルバーなど)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("ポイント計算のランク(ゴールド、シルバーなど)")]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU.ポイントランク", "select 名称 from HC$master_meisho where 名称区分='PT1' and 名称CD=@0")]
	public partial string PointRank { get; set; } = string.Empty;
	/// <summary>
	/// 現在のポイント数
	/// </summary>
	[ObservableProperty]
	[Comment("現在のポイント数")]
	[OldTableCommentAttr("HC$POINT_REAL.REALポイント", "select Realポイント from HC$POINT_REAL where 顧客CD=@0")]
	public partial int Point { get; set; }
	/// <summary>
	/// 累計お買上金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU.累計購入金額")]
	[Comment("累計お買上金額")]
	public partial int SalesTotalKingaku { get; set; }
	/// <summary>
	/// 最終来店日 yyyyMMdd
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("最終来店日 yyyyMMdd")]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU.最終来店日")]
	public partial string LastVisitDate { get; set; } = string.Empty;
	/// <summary>
	/// 来店回数
	/// </summary>
	[ObservableProperty]
	[Comment("来店回数")]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU.累計来店回数")]
	public partial int VisitCount { get; set; }
	/// <summary>
	/// 直近の年間お買上金額
	/// </summary>
	[ObservableProperty]
	[Comment("直近の年間お買上金額")]
	[OldTableCommentAttr("HC$MASTER_KOKYAKU.年間累計購入金額")]
	public partial int AnnualSales { get; set; }
}

/// <summary>
/// 商品テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Code))]
//[KeyDml("njan1", false, "json_extract(Jcolsiz, '$.Jan1')")]
//[KeyDml("njan2", false, "json_extract(Jcolsiz, '$.Jan2')")]
//[KeyDml("njan3", false, "json_extract(Jcolsiz, '$.Jan3')")]
[Comment("マスター：商品テーブル Jcolsiz列に'色CD,サイズCD,JAN1,JAN2,JAN3'の情報を格納")]
[OldTableCommentAttr("HC$MASTER_SHOHIN", "Jcolsiz列は HC$MASTER_SHOHIN_JAN")]
public sealed partial class MasterShohin : BaseDbClass, IBaseCodeName, IDerivedOrigin {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(16)]
	[OldTableCommentAttr("商品CD")]
	[Comment("コード")]
	public partial string Code { get; set; } = "";
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(80)]
	[OldTableCommentAttr("商品名")]
	[Comment("名前")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("略称")]
	[Comment("略称")]
	public partial string Ryaku { get; set; } = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("旧コード")]
	[Comment("カナ")]
	public partial string Kana { get; set; } = string.Empty;
	/// <summary>
	/// ブランド
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("ブランドCD")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunBrand)]
	[Comment("ブランド")]
	public partial long Id_Brand { get; set; }
	/// <summary>
	/// ブランドデータ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("ブランドデータ")]
	public partial CodeNameView VBrand { get; set; } = new();
	/// <summary>
	/// アイテム
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("アイテムCD")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunItem)]
	[Comment("アイテム")]
	public partial long Id_Item { get; set; }
	/// <summary>
	/// アイテムデータ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("アイテムデータ")]
	public partial CodeNameView VItem { get; set; } = new();
	/// <summary>
	/// 展示会
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("展示会CD")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunTenji)]
	[Comment("展示会")]
	public partial long Id_Tenji { get; set; }
	/// <summary>
	/// 展示会データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("展示会データ")]
	public partial CodeNameView VTenji { get; set; } = new();
	/// <summary>
	/// メーカー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("メーカーCD")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunMaker)]
	[Comment("メーカー")]
	public partial long Id_Maker { get; set; }
	/// <summary>
	/// メーカーデータ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("メーカーデータ")]
	public partial CodeNameView VMaker { get; set; } = new();
	/// <summary>
	/// シーズン
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("シーズンCD")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunSeason)]
	[Comment("シーズン")]
	public partial long Id_Season { get; set; }
	/// <summary>
	/// シーズンデータ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("シーズンデータ")]
	public partial CodeNameView VSeason { get; set; } = new();
	/// <summary>
	/// 素材
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("素材CD")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunMaterial)]
	[Comment("素材")]
	public partial long Id_Material { get; set; }
	/// <summary>
	/// 素材データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("素材データ")]
	public partial CodeNameView VMaterial { get; set; } = new();
	/// <summary>
	/// 原産国
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("原産国CD")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunCountry)]
	[Comment("原産国")]
	public partial long Id_Country { get; set; }
	/// <summary>
	/// 原産国データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("原産国データ")]
	public partial CodeNameView VCountry { get; set; } = new();
	/// <summary>
	/// 元上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("元上代")]
	[Comment("元上代")]
	public partial int TankaJodaiOrg { get; set; }
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代")]
	[Comment("上代")]
	public partial int TankaJodai { get; set; }
	/// <summary>
	/// 原価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("原価")]
	[Comment("原価")]
	public partial int TankaGenka { get; set; }
	/// <summary>
	/// 仕入単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入価格")]
	[Comment("仕入単価")]
	public partial int TankaShiire { get; set; }
	/// <summary>
	/// 出荷日(デリバリー)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("デリバリー日")]
	[Comment("出荷日(デリバリー)")]
	public partial string DayShukka { get; set; } = "19010101";
	/// <summary>
	/// 納品日
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("納品日")]
	[Comment("納品日")]
	public partial string DayNohin { get; set; } = "19010101";
	/// <summary>
	/// 店頭投入日
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("店頭投入日")]
	[Comment("店頭投入日")]
	public partial string DayTento { get; set; } = "19010101";
	/// <summary>
	/// 消費税No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税CD")]
	[Comment("消費税No")]
	public partial long Id_Tax { get; set; } = 1;
	/// <summary>
	/// 在庫管理フラグ
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnZaiko))]
	[OldTableCommentAttr("在庫管理FLG")]
	[Comment("在庫管理フラグ")]
	public partial int IsZaiko { get; set; } = 1;
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnZaiko {
		get => (EnumYesNo)IsZaiko;
		set => IsZaiko = (int)value;
	}
	/// <summary>
	/// メーカー品番
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("メーカー品番")]
	[Comment("メーカー品番")]
	public partial string MakerHin { get; set; } = string.Empty;
	/// <summary>
	/// 商品サイズ区分
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("商品サイズ区分")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunSize + ",US0,US1,US2,US3,US4,US5,US6,US7,US8,US9")]
	[Comment("商品サイズ区分")]
	public partial string SizeKu { get; set; } = MasterMeisho.KubunSize;
	/// <summary>
	/// 基準倉庫
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("基準倉庫CD")]
	[ForeignKey(nameof(MasterTokui), tenType: 0)]
	[Comment("基準倉庫")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 倉庫データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("倉庫データ")]
	public partial CodeNameView VSoko { get; set; } = new();
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[OldTableCommentAttr("メモ")]
	[Comment("メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 原価リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[OldTableCommentAttr("JgenkaはHC$MASTER_SHOHIN_GENKAの内容を格納")]
	[Comment("原価リスト")]
	public partial List<MasterShohinGenka>? Jgenka { get; set; }
	/// <summary>
	/// 色サイズリスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[OldTableCommentAttr("JcolsizはHC$MASTER_SHOHIN_JANの内容を格納")]
	[Comment("色サイズリスト")]
	public partial List<MasterShohinColSiz>? Jcolsiz { get; set; }
	/// <summary>
	/// 品質リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[OldTableCommentAttr("JgradeはHC$MASTER_SHOHIN_GRADEの内容を格納")]
	[Comment("品質リスト")]
	public partial List<MasterShohinGrade>? Jgrade { get; set; }
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[OldTableCommentAttr("名称CD01 - 名称CD10")]
	[ForeignKey(nameof(MasterMeisho), meishoListKubunTop: MasterMeisho.KubunTopShohin)]
	[Comment("名称リスト")]
	public partial List<MasterGeneralMeisho>? Jsub { get; set; }
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[Comment("詳細内容")]
	public partial BaseDetailClass? Jdetail { get; set; }
	/// <summary>
	/// 仕入区分 0=通常仕入、3=消化仕入（原価4項目 詳細設計 §2.5.8）
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(EnumPurchaseType))]
	[Comment("仕入区分 0=通常仕入、3=消化仕入")]
	public partial int PurchaseType { get; set; } = 0;
	/// <summary>
	/// 委託仕入先ID。PurchaseType=3は1以上必須
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShiire))]
	[Comment("委託仕入先ID。PurchaseType=3は1以上必須")]
	public partial long Id_ConsignmentShiire { get; set; } = 0;
	/// <summary>
	/// 委託仕入先の現行名称。マスタV*のため名称変更を伝播する（MasterCascadeDb.VRulesへ登録済み）
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("委託仕入先の現行名称")]
	public partial CodeNameView VConsignmentShiire { get; set; } = new();
	/// <summary>
	/// 消化仕入計算区分 0=原価代用、1=上代×掛率
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(EnumConsumptionCalcType))]
	[Comment("消化仕入計算区分 0=原価代用、1=上代×掛率")]
	public partial int ConsumptionCalcType { get; set; } = 0;
	/// <summary>
	/// 掛率を1/100%単位で保持。6500=65.00%。計算区分1は1～10000
	/// </summary>
	[ObservableProperty]
	[Comment("掛率を1/100%単位で保持。6500=65.00%。計算区分1は1～10000")]
	public partial int ConsumptionRateBasisPoints { get; set; } = 0;
	/// <summary>
	/// 端数単位。1、10、100、1000円のみ
	/// </summary>
	[ObservableProperty]
	[Comment("端数単位。1、10、100、1000円のみ")]
	public partial int ConsumptionRoundingUnit { get; set; } = 1;
	/// <summary>
	/// 端数処理 0=四捨五入、1=切上、2=切捨
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(EnumRounding))]
	[Comment("端数処理 0=四捨五入、1=切上、2=切捨")]
	public partial int ConsumptionRounding { get; set; } = 0;
	[Ignore]
	public Type DerivedClass => typeof(DerivedShohinColSiz);
}

/// <summary>
/// 商品色サイズJANテーブル
/// </summary>
[SubTableDefine]
[OldTableCommentAttr("HC$MASTER_SHOHIN_JAN")]
[Comment("マスター：商品色サイズJANサブテーブル MasterShohin.Jcolsiz にJSONで格納する")]
public sealed partial class MasterShohinColSiz : BaseDbClass {
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunColor)]
	[Comment("色")]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// カラーCD
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("色CD")]
	[Comment("カラーCD")]
	public partial string Code_Col { get; set; } = string.Empty;
	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("カラー名")]
	public partial string Mei_Col { get; set; } = string.Empty;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "MasterShohinのSizeKuに依存")]
	[Comment("サイズ")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// サイズCD
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("サイズCD")]
	[Comment("サイズCD")]
	public partial string Code_Siz { get; set; } = string.Empty;
	/// <summary>
	/// サイズ名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("サイズ名")]
	public partial string Mei_Siz { get; set; } = string.Empty;
	/// <summary>
	/// JANコード1
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("JANコード1")]
	[Comment("JANコード1")]
	public partial string Jan1 { get; set; } = string.Empty;
	/// <summary>
	/// JANコード2
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("JANコード2")]
	[Comment("JANコード2")]
	public partial string Jan2 { get; set; } = string.Empty;
	/// <summary>
	/// JANコード3
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("JANコード3")]
	[Comment("JANコード3")]
	public partial string Jan3 { get; set; } = string.Empty;
}
/// <summary>
/// 品質テーブル
/// </summary>
[SubTableDefine]
[OldTableCommentAttr("HC$MASTER_SHOHIN_GRADE")]
[Comment("マスター：商品品質サブテーブル MasterShohin.Jgrade にJSONで格納する")]
public sealed partial class MasterShohinGrade : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("行NO")]
	[Comment("行No")]
	public partial int No { get; set; }
	/// <summary>
	/// 品質
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	[OldTableCommentAttr("品質")]
	[Comment("品質")]
	public partial string Hinshitu { get; set; } = string.Empty;
	/// <summary>
	/// ％
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("パーセント")]
	[Comment("％")]
	public partial int Percent { get; set; }
}
/// <summary>
/// 原価テーブル
/// </summary>
[SubTableDefine]
[OldTableCommentAttr("HC$MASTER_SHOHIN_GENKA")]
[Comment("マスター：商品原価サブテーブル MasterShohin.Jgenka にJSONで格納する")]
public sealed partial class MasterShohinGenka : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("行NO")]
	[Comment("行No")]
	public partial int No { get; set; }
	/// <summary>
	/// 原価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("原価")]
	[Comment("原価")]
	public partial int TankaGenka { get; set; }
	/// <summary>
	/// 仕入単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入価格")]
	[Comment("仕入単価")]
	public partial int TankaShiire { get; set; }
}

/// <summary>
/// 生地・付属品テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Code))]
[Comment("マスター：生地・付属品テーブル")]
[OldTableCommentAttr("HC$MASTER_SHKIJI")]
public sealed partial class MasterMaterial : BaseDbClass, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("商品CD")]
	[Comment("コード")]
	public partial string Code { get; set; } = "";
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("商品名")]
	[Comment("名前")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(60)]
	[OldTableCommentAttr("略称")]
	[Comment("略称")]
	public partial string Ryaku { get; set; } = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("カナ")]
	public partial string Kana { get; set; } = string.Empty;
	/// <summary>
	/// 区分(生地/付属等)
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("区分CD")]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: MasterMeisho.KubunKiji)]
	[Comment("区分")]
	public partial long Id_Kubun { get; set; }
	/// <summary>
	/// 区分データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("区分データ")]
	public partial CodeNameView VKubun { get; set; } = new();
	/// <summary>
	/// 仕入先
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入先CD")]
	[ForeignKey(nameof(MasterShiire))]
	[Comment("仕入先")]
	public partial long Id_Shiire { get; set; }
	/// <summary>
	/// 仕入先データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("仕入先データ")]
	public partial CodeNameView VShiire { get; set; } = new();
	/// <summary>
	/// 仕入先商品コード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(16)]
	[OldTableCommentAttr("仕入先商品CD")]
	[Comment("仕入先商品コード")]
	public partial string CodeShiire { get; set; } = string.Empty;
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("単価")]
	[Comment("単価")]
	public partial int TankaShiire { get; set; }
	/// <summary>
	/// 消費税No
	/// </summary>
	[ObservableProperty]
	[Comment("消費税No")]
	public partial long Id_Tax { get; set; } = 1;
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[OldTableCommentAttr("メモ")]
	[Comment("メモ")]
	public partial string Memo { get; set; } = string.Empty;
}

/// <summary>
/// 設定フラグテーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Name))]
[Comment("マスター：設定フラグテーブル name と val の組で設定情報を表す")]
[OldTableCommentAttr("HC$MASTER_CONFIG")]
public sealed partial class MasterConfig : BaseDbClass {
	/// <summary>
	/// システム共通設定のカテゴリ
	/// </summary>
	[Comment("システム共通設定のカテゴリ")]
	public const string CategorySystem = "System";
	/// <summary>
	/// 適用上代(DerivedJodai)の保持日数
	/// </summary>
	[Comment("適用上代(DerivedJodai)の保持日数")]
	public const string NameJodaiKeepDays = "JodaiKeepDays";
	/// <summary>
	/// 自動実行ジョブ(スケジューラ)設定のカテゴリ
	/// </summary>
	[Comment("自動実行ジョブ(スケジューラ)設定のカテゴリ")]
	public const string CategoryAutoExec = "自動実行管理";
	/// <summary>
	/// 実行フラグ設定名の接頭辞。後ろに TaskId の先頭8桁が付く
	/// </summary>
	[Comment("実行フラグ設定名の接頭辞。後ろに TaskId の先頭8桁が付く")]
	public const string NameAutoExecEnabledPrefix = "GenericSQLRegAutoExec";
	/// <summary>
	/// cron式設定名の接頭辞。後ろに TaskId の先頭8桁が付く
	/// </summary>
	[Comment("cron式設定名の接頭辞。後ろに TaskId の先頭8桁が付く")]
	public const string NameAutoExecCronPrefix = "GenericSQLRegAutoExecCron";
	/// <summary>
	/// メール送信フラグ設定名の接頭辞。後ろに TaskId の先頭8桁が付く
	/// </summary>
	[Comment("メール送信フラグ設定名の接頭辞。後ろに TaskId の先頭8桁が付く")]
	public const string NameAutoExecIsSendMailPrefix = "GenericSQLRegAutoExecIsSendMail";
	/// <summary>自動実行結果メールのSMTPサーバー</summary>
	public const string NameAutoExecMailServerIp = "AutoExecMailServerIp";
	/// <summary>自動実行結果メールのSMTPポート番号</summary>
	public const string NameAutoExecMailServerPort = "AutoExecMailServerPort";
	/// <summary>自動実行結果メールのSMTPユーザーID</summary>
	public const string NameAutoExecMailUserId = "AutoExecMailUserId";
	/// <summary>自動実行結果メールのSMTPパスワード</summary>
	public const string NameAutoExecMailUserPass = "AutoExecMailUserPass";
	/// <summary>自動実行結果メールの暗号化方式</summary>
	public const string NameAutoExecMailSecurity = "AutoExecMailSecurity";
	/// <summary>自動実行結果メールの認証方式</summary>
	public const string NameAutoExecMailAuthMode = "AutoExecMailAuthMode";
	/// <summary>自動実行結果メールの送信元アドレス</summary>
	public const string NameAutoExecMailFromAddr = "AutoExecMailFromAddr";
	/// <summary>自動実行結果メールの送信元表示名</summary>
	public const string NameAutoExecMailFromName = "AutoExecMailFromName";
	/// <summary>自動実行結果メールの送信先アドレス</summary>
	public const string NameAutoExecMailToAddr = "AutoExecMailToAddr";
	/// <summary>
	/// 実行する
	/// </summary>
	[Comment("実行する")]
	public const string ValAutoExecEnabled = "1";
	/// <summary>
	/// 実行しない
	/// </summary>
	[Comment("実行しない")]
	public const string ValAutoExecDisabled = "0";
	/// <summary>
	/// SQLite WAL checkpoint タスクの TaskId(Guid)
	/// </summary>
	[Comment("SQLite WAL checkpoint タスクの TaskId(Guid)")]
	public const string AutoExecTaskIdWalCheckpoint = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
	/// <summary>
	/// SQLite WAL checkpoint タスクの表示名
	/// </summary>
	[Comment("SQLite WAL checkpoint タスクの表示名")]
	public const string AutoExecTaskNameWalCheckpoint = "SQLite WAL checkpoint データベースにWAL履歴を反映させるタスク";
	/// <summary>
	/// SQLite WAL checkpoint タスクの既定cron式
	/// </summary>
	[Comment("SQLite WAL checkpoint タスクの既定cron式")]
	public const string AutoExecCronWalCheckpoint = "0 2 * * *";
	/// <summary>
	/// SQLite WAL checkpoint タスクの既定の実行フラグ
	/// </summary>
	[Comment("SQLite WAL checkpoint タスクの既定の実行フラグ")]
	public const string AutoExecEnabledWalCheckpoint = ValAutoExecEnabled;
	/// <summary>
	/// ワークファイル削除タスクの TaskId(Guid)
	/// </summary>
	[Comment("ワークファイル削除タスクの TaskId(Guid)")]
	public const string AutoExecTaskIdWorkFileCleanup = "b2c3d4e5-f6a7-8901-bcde-f12345678901";
	/// <summary>
	/// ワークファイル削除タスクの表示名
	/// </summary>
	[Comment("ワークファイル削除タスクの表示名")]
	public const string AutoExecTaskNameWorkFileCleanup = "Work file cleanup ワークフォルダにある古いファイルを削除するタスク";
	/// <summary>
	/// ワークファイル削除タスクの既定cron式
	/// </summary>
	[Comment("ワークファイル削除タスクの既定cron式")]
	public const string AutoExecCronWorkFileCleanup = "30 0,12 * * *";
	/// <summary>
	/// ワークファイル削除タスクの既定の実行フラグ
	/// </summary>
	[Comment("ワークファイル削除タスクの既定の実行フラグ")]
	public const string AutoExecEnabledWorkFileCleanup = ValAutoExecEnabled;
	/// <summary>
	/// 在庫/売掛/買掛 再集計タスクの TaskId(Guid)
	/// </summary>
	[Comment("在庫/売掛/買掛 再集計タスクの TaskId(Guid)")]
	public const string AutoExecTaskIdMonthlyResummary = "c3d4e5f6-a7b8-9012-cdef-123456789012";
	/// <summary>
	/// 在庫/売掛/買掛 再集計タスクの表示名
	/// </summary>
	[Comment("在庫/売掛/買掛 再集計タスクの表示名")]
	public const string AutoExecTaskNameMonthlyResummary = "在庫 売掛 買掛 の当月と前月 を再集計するタスク";
	/// <summary>
	/// 在庫/売掛/買掛 再集計タスクの既定cron式
	/// </summary>
	[Comment("在庫/売掛/買掛 再集計タスクの既定cron式")]
	public const string AutoExecCronMonthlyResummary = "10 1 * * *";
	/// <summary>
	/// 在庫/売掛/買掛 再集計タスクの既定の実行フラグ
	/// </summary>
	[Comment("在庫/売掛/買掛 再集計タスクの既定の実行フラグ")]
	public const string AutoExecEnabledMonthlyResummary = ValAutoExecEnabled;
	/// <summary>
	/// 適用上代の期限切れ削除タスクの TaskId(Guid)
	/// </summary>
	[Comment("適用上代の期限切れ削除タスクの TaskId(Guid)")]
	public const string AutoExecTaskIdJodaiPurge = "d4e5f6a7-b8c9-0123-def0-234567890123";
	/// <summary>
	/// 適用上代の期限切れ削除タスクの表示名
	/// </summary>
	[Comment("適用上代の期限切れ削除タスクの表示名")]
	public const string AutoExecTaskNameJodaiPurge = "上代 適用期間が過ぎた適用上代(DerivedJodai)を削除するタスク";
	/// <summary>
	/// 適用上代の期限切れ削除タスクの既定cron式
	/// </summary>
	[Comment("適用上代の期限切れ削除タスクの既定cron式")]
	public const string AutoExecCronJodaiPurge = "40 1 * * *";
	/// <summary>
	/// 適用上代の期限切れ削除タスクの既定の実行フラグ
	/// </summary>
	[Comment("適用上代の期限切れ削除タスクの既定の実行フラグ")]
	public const string AutoExecEnabledJodaiPurge = ValAutoExecEnabled;
	/// <summary>
	/// 商品名称再構築タスクの TaskId(Guid)
	/// </summary>
	[Comment("商品名称再構築タスクの TaskId(Guid)")]
	public const string AutoExecTaskIdMasterShohinMeishoRebuild = "e5f6a7b8-c9d0-1234-ef01-345678901234";
	/// <summary>
	/// 商品名称再構築タスクの表示名
	/// </summary>
	[Comment("商品名称再構築タスクの表示名")]
	public const string AutoExecTaskNameMasterShohinMeishoRebuild = "商品名称再構築 MasterShohinのId_Col/Id_Sizが0のデータから名称マスタを再構築するタスク";
	/// <summary>
	/// 商品名称再構築タスクの既定cron式
	/// </summary>
	[Comment("商品名称再構築タスクの既定cron式")]
	public const string AutoExecCronMasterShohinMeishoRebuild = "20 3 * * *";
	/// <summary>
	/// 商品名称再構築タスクの既定の実行フラグ
	/// </summary>
	[Comment("商品名称再構築タスクの既定の実行フラグ")]
	public const string AutoExecEnabledMasterShohinMeishoRebuild = ValAutoExecDisabled;
	/// <summary>
	/// V*列再同期タスクの TaskId(Guid)
	/// </summary>
	[Comment("V*列再同期タスクの TaskId(Guid)")]
	public const string AutoExecTaskIdMasterVColumnResync = "f6a7b8c9-d0e1-2345-f012-456789012345";
	/// <summary>
	/// V*列再同期タスクの表示名
	/// </summary>
	[Comment("V*列再同期タスクの表示名")]
	public const string AutoExecTaskNameMasterVColumnResync = "V*列再同期 マスタ名称の複製列(V*列)を現在のマスタ内容で再同期するタスク";
	/// <summary>
	/// V*列再同期タスクの既定cron式
	/// </summary>
	[Comment("V*列再同期タスクの既定cron式")]
	public const string AutoExecCronMasterVColumnResync = "40 3 * * *";
	/// <summary>
	/// V*列再同期タスクの既定の実行フラグ
	/// </summary>
	[Comment("V*列再同期タスクの既定の実行フラグ")]
	public const string AutoExecEnabledMasterVColumnResync = ValAutoExecDisabled;
	/// <summary>
	/// 伝票税額再更新タスクの TaskId(Guid)
	/// </summary>
	[Comment("伝票税額再更新タスクの TaskId(Guid)")]
	public const string AutoExecTaskIdTranTaxRebuild = "a7b8c9d0-e1f2-3456-0123-567890123456";
	/// <summary>
	/// 伝票税額再更新タスクの表示名
	/// </summary>
	[Comment("伝票税額再更新タスクの表示名")]
	public const string AutoExecTaskNameTranTaxRebuild = "伝票税額再更新 対象6伝票の期首日以降を取引先マスタの現在の税設定で再計算するタスク";
	/// <summary>
	/// 伝票税額再更新タスクの既定cron式
	/// </summary>
	[Comment("伝票税額再更新タスクの既定cron式")]
	public const string AutoExecCronTranTaxRebuild = "0 4 * * *";
	/// <summary>
	/// 伝票税額再更新タスクの既定の実行フラグ
	/// </summary>
	[Comment("伝票税額再更新タスクの既定の実行フラグ")]
	public const string AutoExecEnabledTranTaxRebuild = ValAutoExecDisabled;
	/// <summary>
	/// マニュアル排他制御監視タスクの TaskId(Guid)
	/// </summary>
	[Comment("マニュアル排他制御監視タスクの TaskId(Guid)")]
	public const string AutoExecTaskIdManualLockMonitor = "b8c9d0e1-f2a3-4567-1234-678901234567";
	/// <summary>
	/// マニュアル排他制御監視タスクの表示名
	/// </summary>
	[Comment("マニュアル排他制御監視タスクの表示名")]
	public const string AutoExecTaskNameManualLockMonitor = "マニュアル排他制御監視 実行中の排他行の生存を確認し長時間更新の無い行を解放するタスク";
	/// <summary>
	/// マニュアル排他制御監視タスクの既定cron式
	/// </summary>
	[Comment("マニュアル排他制御監視タスクの既定cron式")]
	public const string AutoExecCronManualLockMonitor = "*/5 * * * *";
	/// <summary>
	/// マニュアル排他制御監視タスクの既定の実行フラグ
	/// </summary>
	[Comment("マニュアル排他制御監視タスクの既定の実行フラグ")]
	public const string AutoExecEnabledManualLockMonitor = ValAutoExecEnabled;
	/// <summary>自動実行ジョブ1件の既定定義（TaskId・表示名・既定cron式・既定の実行フラグ・メール送信フラグ）</summary>
	public sealed record AutoExecJobDefault(string TaskId, string TaskName, string Cron, string Enabled, string IsSendMail);
	/// <summary>自動実行ジョブの既定定義一覧。MasterConfig の初期データと SchedulerService のジョブ定義の唯一の出典。</summary>
	public static readonly IReadOnlyList<AutoExecJobDefault> AutoExecJobDefaults = [
		new(AutoExecTaskIdWalCheckpoint, AutoExecTaskNameWalCheckpoint, AutoExecCronWalCheckpoint, AutoExecEnabledWalCheckpoint, ValAutoExecDisabled),
		new(AutoExecTaskIdWorkFileCleanup, AutoExecTaskNameWorkFileCleanup, AutoExecCronWorkFileCleanup, AutoExecEnabledWorkFileCleanup, ValAutoExecDisabled),
		new(AutoExecTaskIdMonthlyResummary, AutoExecTaskNameMonthlyResummary, AutoExecCronMonthlyResummary, AutoExecEnabledMonthlyResummary, ValAutoExecDisabled),
		new(AutoExecTaskIdJodaiPurge, AutoExecTaskNameJodaiPurge, AutoExecCronJodaiPurge, AutoExecEnabledJodaiPurge, ValAutoExecDisabled),
		new(AutoExecTaskIdMasterShohinMeishoRebuild, AutoExecTaskNameMasterShohinMeishoRebuild, AutoExecCronMasterShohinMeishoRebuild, AutoExecEnabledMasterShohinMeishoRebuild, ValAutoExecDisabled),
		new(AutoExecTaskIdMasterVColumnResync, AutoExecTaskNameMasterVColumnResync, AutoExecCronMasterVColumnResync, AutoExecEnabledMasterVColumnResync, ValAutoExecDisabled),
		new(AutoExecTaskIdTranTaxRebuild, AutoExecTaskNameTranTaxRebuild, AutoExecCronTranTaxRebuild, AutoExecEnabledTranTaxRebuild, ValAutoExecDisabled),
		new(AutoExecTaskIdManualLockMonitor, AutoExecTaskNameManualLockMonitor, AutoExecCronManualLockMonitor, AutoExecEnabledManualLockMonitor, ValAutoExecDisabled),
	];
	/// <summary>TaskId(Guid文字列)から実行フラグ設定名を組み立てる。CvDomainLogic の SchedulerJobConfigDb と同じ規則（先頭8桁）。</summary>
	public static string AutoExecEnabledName(string taskId) => NameAutoExecEnabledPrefix + AutoExecTaskIdPrefix(taskId);
	/// <summary>TaskId(Guid文字列)から cron式設定名を組み立てる。CvDomainLogic の SchedulerJobConfigDb と同じ規則（先頭8桁）。</summary>
	public static string AutoExecCronName(string taskId) => NameAutoExecCronPrefix + AutoExecTaskIdPrefix(taskId);
	/// <summary>TaskId(Guid文字列)からメール送信フラグ設定名を組み立てる。CvDomainLogic の SchedulerJobConfigDb と同じ規則（先頭8桁）。</summary>
	public static string AutoExecIsSendMailName(string taskId) => NameAutoExecIsSendMailPrefix + AutoExecTaskIdPrefix(taskId);
	/// <summary>TaskId(Guid文字列)の先頭8桁を取り出す。8文字未満なら全体を返す（防御的処理）。</summary>
	static string AutoExecTaskIdPrefix(string taskId) => string.IsNullOrEmpty(taskId) ? string.Empty : taskId[..Math.Min(8, taskId.Length)];
	[ObservableProperty]
	[OldTableCommentAttr("カテゴリ")]
	[Comment("カテゴリ 設定値をグループ分けする区分")]
	public partial string Category { get; set; } = string.Empty;
	[ObservableProperty]
	[OldTableCommentAttr("フラグ名")]
	[Comment("フラグ名 カテゴリ内で一意の設定キー")]
	public partial string Name { get; set; } = string.Empty;
	[ObservableProperty]
	[OldTableCommentAttr("値")]
	[Comment("値 設定の現在値（文字列で保持）")]
	public partial string Val { get; set; } = string.Empty;
	[ObservableProperty]
	[OldTableCommentAttr("リスト")]
	[Comment("リスト 設定可能な値の例・選択肢")]
	public partial string Example { get; set; } = string.Empty;
	[ObservableProperty]
	[OldTableCommentAttr("MEMO")]
	[Comment("MEMO 設定内容の説明")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 初期データの作成。不足している設定行だけを追加する（既存行の値は上書きしない）。
	/// 対象は JodaiKeepDays、自動実行ジョブ(<see cref="AutoExecJobDefaults"/>)ごとの実行フラグ行・cron式行・メール送信フラグ行、メール共通設定。
	/// 既存の Name 一覧と突き合わせ、未登録の行のみ Insert する（テーブルが空かどうかでは判定しない）。
	/// </summary>
	/// <param name="db"></param>
	/// <returns>実際に Insert した行の一覧（追加が無ければ空リスト）</returns>
	public static List<MasterConfig> CreateDefaultData(ExDatabase db) {
		var vdate = Common.GetVdate();
		var candidates = new List<MasterConfig>() {
			new MasterConfig { Category = CategorySystem, Name = NameJodaiKeepDays, Val = "90", Example = "30,60,90", Memo = "上代保持日数", Vdc = vdate, Vdu = vdate },
		};
		foreach (var job in AutoExecJobDefaults) {
			candidates.Add(new MasterConfig {
				Category = CategoryAutoExec,
				Name = AutoExecEnabledName(job.TaskId),
				Val = job.Enabled,
				Example = $"{ValAutoExecEnabled},{ValAutoExecDisabled}",
				Memo = $"{job.TaskName} の実行フラグ",
				Vdc = vdate,
				Vdu = vdate,
			});
			candidates.Add(new MasterConfig {
				Category = CategoryAutoExec,
				Name = AutoExecCronName(job.TaskId),
				Val = job.Cron,
				Example = "分 時 日 月 曜日",
				Memo = $"{job.TaskName} の起動cron式",
				Vdc = vdate,
				Vdu = vdate,
			});
			candidates.Add(new MasterConfig {
				Category = CategoryAutoExec,
				Name = AutoExecIsSendMailName(job.TaskId),
				Val = job.IsSendMail,
				Example = $"{ValAutoExecEnabled}=送信する,{ValAutoExecDisabled}=送信しない",
				Memo = $"{job.TaskName} の実行結果メール送信フラグ",
				Vdc = vdate,
				Vdu = vdate,
			});
		}
		candidates.AddRange([
			new MasterConfig { Category = CategoryAutoExec, Name = NameAutoExecMailServerIp, Val = "", Example = "例: mail.example.jp", Memo = "自動実行結果メールのSMTPサーバーIPアドレスまたはホスト名", Vdc = vdate, Vdu = vdate },
			new MasterConfig { Category = CategoryAutoExec, Name = NameAutoExecMailServerPort, Val = "", Example = "例: 587（送信ポート）", Memo = "自動実行結果メールのSMTPポート番号", Vdc = vdate, Vdu = vdate },
			new MasterConfig { Category = CategoryAutoExec, Name = NameAutoExecMailUserId, Val = "", Example = "例: user@example.jp（認証ユーザー）認証方式Noneなら空欄", Memo = "自動実行結果メールのSMTPユーザーID", Vdc = vdate, Vdu = vdate },
			new MasterConfig { Category = CategoryAutoExec, Name = NameAutoExecMailUserPass, Val = "", Example = "例: メール認証用パスワード 認証方式Noneなら空欄", Memo = "自動実行結果メールのSMTPパスワード", Vdc = vdate, Vdu = vdate },
			new MasterConfig { Category = CategoryAutoExec, Name = NameAutoExecMailSecurity, Val = "", Example = "None（暗号化なし）,Auto（自動選択）,StartTls,StartTlsWhenAvailable,SslOnConnect", Memo = "自動実行結果メールの暗号化方式。社内リレー（localhost:25など）はNone", Vdc = vdate, Vdu = vdate },
			new MasterConfig { Category = CategoryAutoExec, Name = NameAutoExecMailAuthMode, Val = "", Example = "None（認証なし）,Password（パスワード認証）", Memo = "自動実行結果メールの認証方式。社内リレー（localhost:25など）はNone", Vdc = vdate, Vdu = vdate },
			new MasterConfig { Category = CategoryAutoExec, Name = NameAutoExecMailFromAddr, Val = "", Example = "例: sender@example.jp（送信元）", Memo = "自動実行結果メールの送信元アドレス", Vdc = vdate, Vdu = vdate },
			new MasterConfig { Category = CategoryAutoExec, Name = NameAutoExecMailFromName, Val = "", Example = "例: 自動実行通知（省略可）", Memo = "自動実行結果メールの送信元表示名。空欄ならアドレスのみで送信する", Vdc = vdate, Vdu = vdate },
			new MasterConfig { Category = CategoryAutoExec, Name = NameAutoExecMailToAddr, Val = "", Example = "例: admin@example.jp（送信先）", Memo = "自動実行結果メールの送信先アドレス", Vdc = vdate, Vdu = vdate },
		]);

		var existingNames = new HashSet<string>(db.Fetch<string>($"SELECT Name FROM {nameof(MasterConfig)}"));
		var initData = candidates.Where(c => !existingNames.Contains(c.Name)).ToList();
		if (initData.Count > 0) {
			db.InsertBulk<MasterConfig>(initData);
		}
		return initData;
	}
}
/// <summary>
/// ハンディターミナル用のテーブル、HHTマスター作成時のみ必要
/// </summary>
[NoCreate]
[Comment("マスター：ハンディターミナル用テーブル HHTマスター作成時のみ使用し実テーブルは作成しない")]
public sealed partial class MasterHht : ObservableObject {
	/// <summary>
	/// 1 - 3	識別フラグ	SIR, SOK, TAN, TOK のいずれか
	/// </summary>
	[ObservableProperty]
	[Comment("1 - 3 識別フラグ SIR、 SOK、 TAN、 TOK のいずれか")]
	public partial string Kubun { get; set; } = string.Empty;
	/// <summary>
	/// 4 - 11	コード	8桁（社員は6桁+スペース2桁）のゼロ埋めコード
	/// </summary>
	[ObservableProperty]
	[Comment("4 - 11 コード 8桁（社員は6桁+スペース2桁）のゼロ埋めコード")]
	public partial string Code { get; set; } = string.Empty;
	/// <summary>
	/// 12 - 51	名称1	SJIS 40byteの名称（略称/カナ/名称、TRANSLATE済み）
	/// </summary>
	[ObservableProperty]
	[Comment("12 - 51 名称1 SJIS 40byteの名称（略称/カナ/名称、TRANSLATE済み）")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// 52 - 91	名称2	SJIS 40byteの名称（略称、またはスペース）
	/// </summary>
	[ObservableProperty]
	[Comment("52 - 91 名称2 SJIS 40byteの名称（略称、またはスペース）")]
	public partial string NameOpt { get; set; } = string.Empty;
	/// <summary>
	/// 92	終端符号	アスタリスク * 
	/// </summary>
	[ObservableProperty]
	[Comment("92 終端符号 アスタリスク *")]
	public partial string Eol { get; set; } = string.Empty;
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("マスター：出荷配送業者テーブル")]
public sealed partial class MasterShipping : BaseDbClass, IBaseCodeName {
	[ObservableProperty]
	[Comment("配送業者コード")]
	public partial string Code { get; set; } = string.Empty;
	[ObservableProperty]
	[Comment("配送業者名")]
	public partial string Name { get; set; } = string.Empty;
	[ObservableProperty]
	[Comment("配送業者略称")]
	public partial string Ryaku { get; set; } = string.Empty;
	[ObservableProperty]
	[Comment("配送業者カナ")]
	public partial string Kana { get; set; } = string.Empty;
	[ObservableProperty]
	[Comment("追跡サービス対応 0:非対応 1:対応")]
	public partial bool TrackingSupported { get; set; }
	[ObservableProperty]
	[Comment("追跡URLテンプレート TrackingPlaceholder を送り状番号で置換して使う")]
	public partial string TrackingUrlTemplate { get; set; } = string.Empty;
	[ObservableProperty]
	[Comment("追跡URLテンプレート内の置換文字列（既定 {no}）")]
	public partial string TrackingPlaceholder { get; set; } = "{no}";
	[ObservableProperty]
	[Comment("有効フラグ 0:未使用 1:使用")]
	public partial bool IsActive { get; set; }
	[ObservableProperty]
	[Comment("備考")]
	public partial string Notes { get; set; } = string.Empty;
	private static readonly List<MasterShipping> DefaultShippingData =
	[
		new MasterShipping {
			Id = 1,
			Code = "KURONEKO",
			Name = "クロネコヤマト",
			Ryaku = "クロネコ",
			Kana = "クロネコヤマト",
			TrackingSupported = true,
			TrackingUrlTemplate = "https://jizen.kuronekoyamato.co.jp/jizen/servlet/crjz.b.NQ0010?id={no}",
			TrackingPlaceholder = "{no}",
			IsActive = true,
			Notes = "旧来の汎用フォーム",
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		},
		new MasterShipping {
			Id = 2,
			Code = "SAGAWA",
			Name = "佐川急便",
			Ryaku = "佐川",
			Kana = "サガワキュウビン",
			TrackingSupported = true,
			TrackingUrlTemplate = "https://k2k.sagawa-exp.co.jp/p/web/okurijosearch.do?okurijoNo={no}",
			TrackingPlaceholder = "{no}",
			IsActive = true,
			Notes = "飛脚宅配便など",
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		},
		new MasterShipping {
			Id = 3,
			Code = "JP",
			Name = "日本郵便",
			Ryaku = "日本郵便",
			Kana = "ニホンユウビン",
			TrackingSupported = true,
			TrackingUrlTemplate = "https://trackings.post.japanpost.jp/services/srv/search/direct?reqCodeNo1={no}",
			TrackingPlaceholder = "{no}",
			IsActive = true,
			Notes = "シンプル版",
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		},
		new MasterShipping {
			Id = 4,
			Code = "SEINO",
			Name = "西濃運輸",
			Ryaku = "西濃",
			Kana = "セイノウウンユ",
			TrackingSupported = true,
			TrackingUrlTemplate = "https://track.seino.co.jp/cgi-bin/gnpquery.pgm?GNPNO1={no}",
			TrackingPlaceholder = "{no}",
			IsActive = true,
			Notes = "カンガルー便など",
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		},
		new MasterShipping {
			Id = 5,
			Code = "FUKUYAMA",
			Name = "福山通運",
			Ryaku = "福山",
			Kana = "フクヤマツウウン",
			TrackingSupported = true,
			TrackingUrlTemplate = "https://corp.fukutsu.co.jp/situation/tracking_no_hunt/{no}",
			TrackingPlaceholder = "{no}",
			IsActive = true,
			Notes = "パス形式",
			Vdc = Common.GetVdate(),
			Vdu = Common.GetVdate()
		}
	];
	public static List<MasterShipping> CreateDefaultData(ExDatabase db) {
		var tableCnt = db.GetTableCounts(nameof(MasterShipping));
		if (tableCnt?.FirstOrDefault()?.Item3 == 0) {
			db.InsertBulk<MasterShipping>(DefaultShippingData);
		}
		return DefaultShippingData;
	}
}
