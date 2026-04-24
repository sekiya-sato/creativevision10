using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;


/// <summary>
/// 現在庫集計ファイル: 在庫
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("unq1", true, ["Id_Soko", "Id_Shohin", "Id_Col", "Id_Siz"])]
[KeyDml("nk1", false, ["Id_Soko"])]
[KeyDml("nk2", false, ["Id_Shohin"])]
[Comment("倉庫、商品、色、サイズで集計した在庫データ")]
public partial class SummaryRealStock : BaseDbClass {
	/// <summary>
	/// 倉庫ID
	/// </summary>
	[ObservableProperty]
	long id_Soko;
	/// <summary>
	/// 商品Id
	/// </summary>
	[ObservableProperty]
	long id_Shohin;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	long id_Col;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	long id_Siz;
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	int su;
}
/// <summary>
/// 年月集計ファイル: 在庫
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("unq1", true, ["SumMonth", "Id_Soko", "Id_Shohin", "Id_Col", "Id_Siz"])]
[KeyDml("nk1", false, ["Id_Soko"])]
[KeyDml("nk2", false, ["Id_Shohin"])]
[Comment("年月、倉庫、商品、色、サイズで集計した在庫データ Suは当月のみ、CumulativeSuは累計")]
public partial class SummaryStock : SummaryRealStock {
	/// <summary>
	/// 年月
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(6)]
	string sumMonth = "190101";
	/// <summary>
	///	当月までの累計数量
	/// </summary>
	[ObservableProperty]
	int cumulativeSu;
	/// <summary>
	/// 入庫数
	/// </summary>
	[ObservableProperty]
	int inQty;
	/// <summary>
	/// 出庫数
	/// </summary>
	[ObservableProperty]
	int outQty;
	/// <summary>
	/// 移動中(入庫予定)
	/// </summary>
	[ObservableProperty]
	int transitQty;
	/// <summary>
	/// 調整数
	/// </summary>
	[ObservableProperty]
	int adjustQty;
	/// <summary>
	/// 棚卸日
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	string stocktakeDdate = "19010101";
	/// <summary>
	/// 棚卸数
	/// </summary>
	[ObservableProperty]
	int actualQty;
}

