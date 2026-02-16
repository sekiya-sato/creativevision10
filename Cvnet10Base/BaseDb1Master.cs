using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;
using Cvnet10Base.Share;
using Newtonsoft.Json;
using System.ComponentModel;

namespace Cvnet10Base;

/// <summary>
/// 社員マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("MasterShain_uq1", false, "Code")]
public sealed partial class MasterShain : BaseDbClass, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(12)]
	[property: DefaultValue("")]
	string code = string.Empty;
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: DefaultValue("")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string ryaku = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string kana = string.Empty;
	/// <summary>
	/// メールアドレス
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: DefaultValue("")]
	string mail = string.Empty;
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	long id_Tenpo;
	/// <summary>
	/// 店舗CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Tenpo = string.Empty;
	/// <summary>
	/// 店舗名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Tenpo = string.Empty;
	/// <summary>
	/// 部門Id
	/// </summary>
	[ObservableProperty]
	long id_Bumon;
	/// <summary>
	/// 部門CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Bumon = string.Empty;
	/// <summary>
	/// 部門名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Bumon = string.Empty;
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterGeneralMeisho>? jsub;
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	DetailDbClass? jdetail;
	/* ToDo: JSON シリアライズにプロパティを含める含めないの制御は必要かどうか検討する IBaseSerializeMeisho の必要性
	[JsonIgnore]
	public bool Ser { get; set; } = false;
	public bool ShouldSerializeMei_Tenpo() => Ser;
	public bool ShouldSerializeMei_Bumon() => Ser;
	*/
}

/// <summary>
/// 顧客マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("MasterEndCustomer_uq1", false, "Code")]
public sealed partial class MasterEndCustomer : BaseDbHasAddress, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(12)]
	[property: DefaultValue("")]
	string code = string.Empty;
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: DefaultValue("")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string ryaku = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string kana = string.Empty;
	/// <summary>
	/// ランク
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: DefaultValue("")]
	string rank = string.Empty;
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	long id_Tenpo;
	/// <summary>
	/// 店舗CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Tenpo = string.Empty;
	/// <summary>
	/// 店舗名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Tenpo = string.Empty;
	/// <summary>
	/// 誕生日 yyyyMMdd
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: DefaultValue("")]
	string birthday = string.Empty;
	/// <summary>
	/// 誕生日 MMdd
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(4)]
	[property: DefaultValue("")]
	string birthNoyear = string.Empty;
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: DefaultValue("")]
	string memo = string.Empty;
	/// <summary>
	/// 性別 0=不明 1=男性 2=女性
	/// </summary>
	[ObservableProperty]
	int gendar;
	/// <summary>
	/// ポイント
	/// </summary>
	[ObservableProperty]
	int point;
	/// <summary>
	/// 累計購買回数
	/// </summary>
	[ObservableProperty]
	int salesCount;
	/// <summary>
	/// 累計購買金額
	/// </summary>
	[ObservableProperty]
	int salesKingaku;
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterGeneralMeisho>? jsub;
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	DetailDbClass? jdetail;
}

/// <summary>
/// 商品マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("MasterShohin_uq1", false, "Code")]
public sealed partial class MasterShohin : BaseDbClass, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(16)]
	string code = "";
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: DefaultValue("")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string ryaku = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string kana = string.Empty;
	/// <summary>
	/// ブランド
	/// </summary>
	[ObservableProperty]
	long id_MeiBrand;
	/// <summary>
	/// ブランドCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Brand = string.Empty;
	/// <summary>
	/// ブランド名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Brand = string.Empty;
	/// <summary>
	/// アイテム
	/// </summary>
	[ObservableProperty]
	long id_MeiItem;
	/// <summary>
	/// アイテムCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Item = string.Empty;
	/// <summary>
	/// アイテム名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Item = string.Empty;
	/// <summary>
	/// 展示会
	/// </summary>
	[ObservableProperty]
	long id_MeiTenji;
	/// <summary>
	/// 展示会CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Tenji = string.Empty;
	/// <summary>
	/// 展示会名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Tenji = string.Empty;
	/// <summary>
	/// メーカー
	/// </summary>
	[ObservableProperty]
	long id_MeiMaker;
	/// <summary>
	/// メーカーCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Maker = string.Empty;
	/// <summary>
	/// メーカー名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Maker = string.Empty;
	/// <summary>
	/// シーズン
	/// </summary>
	[ObservableProperty]
	long id_MeiSeason;
	/// <summary>
	/// シーズンCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Season = string.Empty;
	/// <summary>
	/// シーズン名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Season = string.Empty;
	/// <summary>
	/// 素材
	/// </summary>
	[ObservableProperty]
	long id_MeiMaterial;
	/// <summary>
	/// 素材CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Material = string.Empty;
	/// <summary>
	/// 素材名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Material = string.Empty;
	/// <summary>
	/// 原産国
	/// </summary>
	[ObservableProperty]
	long id_MeiGensan;
	/// <summary>
	/// 原産国CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Gensan = string.Empty;
	/// <summary>
	/// 原産国名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Gensan = string.Empty;
	/// <summary>
	/// 元上代
	/// </summary>
	[ObservableProperty]
	int tankaJodaiOrg;
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	int tankaJodai;
	/// <summary>
	/// 原価
	/// </summary>
	[ObservableProperty]
	int tankaGenka;
	/// <summary>
	/// 仕入単価
	/// </summary>
	[ObservableProperty]
	int tankaShiire;
	/// <summary>
	/// 出荷日(デリバリー)
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string dayShukka = "19010101";
	/// <summary>
	/// 納品日
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string dayNohin = "19010101";
	/// <summary>
	/// 店頭投入日
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string dayTento = "19010101";
	/// <summary>
	/// 消費税計算方法
	/// </summary>
	[ObservableProperty]
	int taxCalc;
	/// <summary>
	/// 消費税CD
	/// </summary>
	[ObservableProperty]
	long id_Tax;
	/// <summary>
	/// 在庫管理フラグ
	/// </summary>
	[ObservableProperty]
	int isZaiko = 1;
		/// <summary>
	/// メーカー品番
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string makerHin = string.Empty;
	/// <summary>
	/// 商品サイズ区分
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string sizeKu = "SIZ";
	/// <summary>
	/// 基準倉庫
	/// </summary>
	[ObservableProperty]
	long id_Soko;
	/// <summary>
	/// 倉庫CD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Soko = string.Empty;
	/// <summary>
	/// 倉庫名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Soko = string.Empty;
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: DefaultValue("")]
	string memo = string.Empty;
	/// <summary>
	/// 原価リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterShohinGenka>? jgenka;
	/// <summary>
	/// 色サイズリスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterShohinColSiz>? jcolsiz;
	/// <summary>
	/// 品質リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterShohinGrade>? jgrade;
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterGeneralMeisho>? jsub;
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	DetailDbClass? jdetail;
}


/// <summary>
/// 商品色サイズJANマスター
/// </summary>
[NoCreate]
public sealed partial class MasterShohinColSiz : BaseDbClass {
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	long id_MeiCol;
	/// <summary>
	/// カラーCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Col = string.Empty;
	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Col = string.Empty;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	long id_MeiSiz;
	/// <summary>
	/// サイズCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string code_Siz = string.Empty;
	/// <summary>
	/// サイズ名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei_Siz = string.Empty;
	/// <summary>
	/// JANコード1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string jan1 = string.Empty;
	/// <summary>
	/// JANコード2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string jan2 = string.Empty;
	/// <summary>
	/// JANコード3
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string jan3 = string.Empty;
	/*
	/// <summary>
	/// JSON シリアライズ時に Mei_Col / Mei_Siz を含めるか (デフォルト: false)
	/// </summary>
	[JsonIgnore]
	public bool Ser { get; set; } = false;
	public bool ShouldSerializeCode_Col() => Ser;
	public bool ShouldSerializeMei_Col() => Ser;
	public bool ShouldSerializeCode_Siz() => Ser;
	public bool ShouldSerializeMei_Siz() => Ser;

	readonly static public string ViewSql = """
select * from (
select T.*, m1.Name as Mei_Col, m2.Name as  Mei_Siz
from MasterShohinColSiz T
left join MasterMeisho m1 on T.id_MeiCol = m1.Id
left join MasterMeisho m2 on T.id_MeiSiz = m2.Id
) as Vw_MasterShohinColSiz
""";
	 */
}
/// <summary>
/// 品質マスター
/// </summary>
[NoCreate]
public sealed partial class MasterShohinGrade : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	int no;
	/// <summary>
	/// 品質
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(40)]
	[property: DefaultValue("")]
	string hinshitu = string.Empty;
	/// <summary>
	/// ％
	/// </summary>
	[ObservableProperty]
	int percent;
}
/// <summary>
/// 原価マスター
/// </summary>
[NoCreate]
public sealed partial class MasterShohinGenka: ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	int no;
	/// <summary>
	/// 原価
	/// </summary>
	[ObservableProperty]
	int tankaGenka;
	/// <summary>
	/// 仕入単価
	/// </summary>
	[ObservableProperty]
	int tankaShiire;
}

