using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

/// <summary>
/// 年月集計ファイル: 在庫
/// </summary>
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("unq1", true, ["SumMonth", "Id_Soko", "Id_Shohin", "Id_Col", "Id_Siz"])]
[KeyDml("nk1", false, "DenDay")]
[KeyDml("nk3", false, ["Id_Soko"])]
[KeyDml("nk4", false, ["Id_Shiire"])]
[Comment("年月、倉庫、商品、色、サイズで集計した在庫データ Suは当月のみ、CumulativeSuは累計")]
public partial class SummaryStock : BaseDbClass {
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
	/// 年月
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(6)]
	string sumMonth = "190101";
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
	/// <summary>
	/// 累計数量
	/// </summary>
	[ObservableProperty]
	int cumulativeSu;
}

