

using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;
using Cvnet10Base.Share;

namespace Cvnet10Base;

/// <summary>
/// 社員マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
public partial class MasterShain : BaseDbClass, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(12)]
	string code = "";
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	string? name;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	string? ryaku;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	string? kana;
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterMeisho>? jsub;
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	DetailDbClass? jdetail;
}

/// <summary>
/// 顧客マスター
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
public partial class MasterEndCustomer : BaseDbHasAddress, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(12)]
	string code = "";
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	string? name;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	string? ryaku;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	string? kana;
	/// <summary>
	/// ランク
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string? rank;
	/// <summary>
	/// 店舗キー
	/// </summary>
	[ObservableProperty]
	long id_Tenpo;
	/// <summary>
	/// 誕生日 yyyyMMdd
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string? birthday;
	/// <summary>
	/// 誕生日 MMdd
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(4)]
	string? birthNoyear;
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	string? memo;
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
	List<MasterMeisho>? jsub;
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
public partial class MasterShohin : BaseDbHasAddress, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(12)]
	string code = "";
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	string? name;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	string? ryaku;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	string? kana;
	/// <summary>
	/// ブランド
	/// </summary>
	[ObservableProperty]
	long id_MeiBrand;
	/// <summary>
	/// アイテム
	/// </summary>
	[ObservableProperty]
	long id_MeiItem;
	/// <summary>
	/// 展示会
	/// </summary>
	[ObservableProperty]
	long id_MeiTenji;
	/// <summary>
	/// メーカー
	/// </summary>
	[ObservableProperty]
	long id_MeiMaker;
	/// <summary>
	/// シーズン
	/// </summary>
	[ObservableProperty]
	long id_MeiSeason;
	/// <summary>
	/// 素材
	/// </summary>
	[ObservableProperty]
	long id_MeiMaterial;
	/// <summary>
	/// 原産国
	/// </summary>
	[ObservableProperty]
	long id_MeiGensan;
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
	string? makerHin;
	/// <summary>
	/// 商品サイズ区分
	/// </summary>
	[ObservableProperty]
	string sizeKu = "SIZ";
	/// <summary>
	/// 基準倉庫
	/// </summary>
	[ObservableProperty]
	long id_Soko;
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	string? memo;
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
	List<MasterMeisho>? jsub;
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
[PrimaryKey("Id", AutoIncrement = true)]
public partial class MasterShohinColSiz : BaseDbClass, IBaseGetListSql {
	/// <summary>
	/// 商品
	/// </summary>
	[ObservableProperty]
	long id_Shohin;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	long id_MeiCol;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	long id_MeiSiz;
	/// <summary>
	/// JANコード1
	/// </summary>
	[ObservableProperty]
	string? jan1;
	/// <summary>
	/// JANコード2
	/// </summary>
	[ObservableProperty]
	string? jan2;
	/// <summary>
	/// JANコード3
	/// </summary>
	[ObservableProperty]
	string? jan3;

	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	[property: ResultColumn]
	string? mei_Col;
	[ObservableProperty]
	[property: ResultColumn]
	/// <summary>
	/// サイズ名
	/// </summary>
	string? mei_Siz;
	readonly static string listSql = """
select T.*, m1.Name as Mei_Col, m2.Name as  Mei_Siz
from MasterShohinColSiz T
left join MasterMeisho m1 on T.id_MeiCol = m1.Id
left join MasterMeisho m2 on T.id_MeiSiz = m2.Id
""";
	public  string GetListSql() {
		return listSql;
	}



}
/// <summary>
/// 品質マスター
/// </summary>
public partial class MasterShohinGrade : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	int no;
	/// <summary>
	/// 品質
	/// </summary>
	[ObservableProperty]
	string? hinshitu;
	/// <summary>
	/// ％
	/// </summary>
	[ObservableProperty]
	int percent;
}
/// <summary>
/// 原価マスター
/// </summary>
public partial class MasterShohinGenka: ObservableObject {
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

