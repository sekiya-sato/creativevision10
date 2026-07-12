using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;


/// <summary>
/// 現在庫集計ファイル: 在庫
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("unq1", true, [nameof(Id_Soko), nameof(Id_Shohin), nameof(Id_Col), nameof(Id_Siz)])]
[KeyDml("nk1", false, [nameof(Id_Soko)])]
[KeyDml("nk2", false, [nameof(Id_Shohin)])]
[Comment("集計データ：倉庫、商品、色、サイズで集計した在庫データ")]
public partial class SummaryRealStock : BaseDbClass {
	/// <summary>
	/// 倉庫ID
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 0, additionalInfo: "TenType in (0,3,6)")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 商品Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在する色")]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在するサイズ")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	public partial int Su { get; set; }
}
/// <summary>
/// 年月集計ファイル: 在庫
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("unq1", true, [nameof(SumMonth), nameof(Id_Soko), nameof(Id_Shohin), nameof(Id_Col), nameof(Id_Siz)])]
[KeyDml("nk1", false, [nameof(Id_Soko)])]
[KeyDml("nk2", false, [nameof(Id_Shohin)])]
[Comment("集計データ：yyyyMM年月、倉庫、商品、色、サイズで集計した在庫データ Suは当月のみ、CumulativeSuは累計")]
public partial class SummaryStock : SummaryRealStock {
	/// <summary>
	/// 年月
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(6)]
	public partial string SumMonth { get; set; } = "190101";
	/// <summary>
	///	当月までの累計数量
	/// </summary>
	[ObservableProperty]
	public partial int CumulativeSu { get; set; }
	/// <summary>
	/// 入庫数
	/// </summary>
	[ObservableProperty]
	public partial int InQty { get; set; }
	/// <summary>
	/// 出庫数
	/// </summary>
	[ObservableProperty]
	public partial int OutQty { get; set; }
	/// <summary>
	/// 移動中(入庫予定)
	/// </summary>
	[ObservableProperty]
	public partial int TransitQty { get; set; }
	/// <summary>
	/// 調整数
	/// </summary>
	[ObservableProperty]
	public partial int AdjustQty { get; set; }
	/// <summary>
	/// 棚卸日
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string StocktakeDdate { get; set; } = "19010101";
	/// <summary>
	/// 棚卸数
	/// </summary>
	[ObservableProperty]
	public partial int ActualQty { get; set; }
}

