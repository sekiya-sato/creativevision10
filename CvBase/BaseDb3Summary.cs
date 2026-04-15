using CommunityToolkit.Mvvm.ComponentModel;

namespace CvBase;

/// <summary>
/// 年月集計ファイル: 在庫
/// </summary>
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
	[property: ColumnSizeDml(8)]
	string sumMonth = "19010101";
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

