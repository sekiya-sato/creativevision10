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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "BMN")]
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
	[ForeignKey(nameof(MasterMeisho), meishoListKubunTop: 'E')]
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
	/// ポイント
	/// </summary>
	[ObservableProperty]
	[Comment("ポイント")]
	public partial int Point { get; set; }
	/// <summary>
	/// 累計購買回数
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("累計購入数量", "ToDo")]
	[Comment("累計購買回数")]
	public partial int SalesCount { get; set; }
	/// <summary>
	/// 累計購買金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("累計購入金額")]
	[Comment("累計購買金額")]
	public partial int SalesKingaku { get; set; }
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	[ForeignKey(nameof(MasterMeisho), meishoListKubunTop: 'K')]
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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "BRD")]
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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "ITM")]
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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "TNJ")]
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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "MKR")]
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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "SZN")]
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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "SZI")]
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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "GEN")]
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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "SIZ,US0,US1,US2,US3,US4,US5,US6,US7,US8,US9")]
	[Comment("商品サイズ区分")]
	public partial string SizeKu { get; set; } = "SIZ";
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
	[ForeignKey(nameof(MasterMeisho), meishoListKubunTop: 'B')]
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
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "COL")]
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
/// 設定フラグテーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Name))]
[Comment("マスター：設定フラグテーブル name と val の組で設定情報を表す")]
[OldTableCommentAttr("HC$MASTER_CONFIG")]
public sealed partial class MasterConfig : BaseDbClass {
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
