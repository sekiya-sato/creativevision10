using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;
using Cvnet10Base.Share;
using Newtonsoft.Json;
using System.ComponentModel;
using System.Globalization;

namespace Cvnet10Base;

/// <summary>
/// 社員マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uq1", true, "Code")]
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
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTenpo = new();
	/// <summary>
	/// 部門Id
	/// </summary>
	[ObservableProperty]
	long id_Bumon;
	/// <summary>
	/// 部門データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vBumon = new();
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
	BaseDetailClass? jdetail;
}

/// <summary>
/// 顧客マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uq1", true, "Code")]
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
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTenpo = new();
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
	//[property: ColumnSizeDml(ctype:ColumnType.Enum)]
	int gendar = 0;
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
	BaseDetailClass? jdetail;
}

/// <summary>
/// 商品マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uq1", true, "Code")]
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
	long id_Brand;
	/// <summary>
	/// ブランドデータ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vBrand = new();
	/// <summary>
	/// アイテム
	/// </summary>
	[ObservableProperty]
	long id_Item;
	/// <summary>
	/// アイテムデータ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vItem = new();
	/// <summary>
	/// 展示会
	/// </summary>
	[ObservableProperty]
	long id_Tenji;
	/// <summary>
	/// 展示会データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTenji = new();
	/// <summary>
	/// メーカー
	/// </summary>
	[ObservableProperty]
	long id_Maker;
	/// <summary>
	/// メーカーデータ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vMaker = new();
	/// <summary>
	/// シーズン
	/// </summary>
	[ObservableProperty]
	long id_Season;
	/// <summary>
	/// シーズンデータ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vSeason = new();
	/// <summary>
	/// 素材
	/// </summary>
	[ObservableProperty]
	long id_Material;
	/// <summary>
	/// 素材データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vMaterial = new();
	/// <summary>
	/// 原産国
	/// </summary>
	[ObservableProperty]
	long id_Country;
	/// <summary>
	/// 原産国データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vCountry = new();
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
	/// 消費税No
	/// </summary>
	[ObservableProperty]
	long id_Tax;
	/// <summary>
	/// 在庫管理フラグ
	/// </summary>
	[ObservableProperty]
	//[property: ColumnSizeDml(ctype: ColumnType.Enum)]
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
	/// 倉庫データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vSoko = new();
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
	BaseDetailClass? jdetail;
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
	long id_Col;
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
	long id_Siz;
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

