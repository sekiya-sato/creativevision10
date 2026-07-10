using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Kubun))]
[Comment("ベースポイント、ランク別ポイント")]
public sealed partial class MasterPointRank : BaseDbClass {
	/// <summary>
	/// ポイントランク区分
	/// </summary>
	[ObservableProperty]
	public partial int Kubun { get; set; }
	/// <summary>
	/// ポイントランク名称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// ポイント付与単価
	/// </summary>
	[ObservableProperty]
	public partial int PointUnitPrice { get; set; } = 100;
	/// <summary>
	/// ポイント付与数P
	/// </summary>
	[ObservableProperty]
	public partial int PointAmountProper { get; set; } = 1;
	/// <summary>
	/// ポイント付与数S
	/// </summary>
	[ObservableProperty]
	public partial int PointAmountSale { get; set; } = 1;
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_Customer), nameof(DenDay))]
[Comment("ポイント履歴テーブル")]
public sealed partial class TranPointRireki : BaseDbClass {
	/// <summary>
	/// 日付
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("請求日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 顧客Id
	/// </summary>
	[ObservableProperty]
	public partial int Id_Customer { get; set; }
	/// <summary>
	/// 取得ポイント
	/// </summary>
	[ObservableProperty]
	public partial int PointGet { get; set; }
	/// <summary>
	/// 使用ポイント
	/// </summary>
	[ObservableProperty]
	public partial int PointUse { get; set; }
	/// <summary>
	/// 摘要
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	public partial string Memo { get; set; } = string.Empty;
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_Customer))]
[Comment("ポイント残高テーブル")]
public sealed partial class SummaryPoint : BaseDbClass {
	/// <summary>
	/// 顧客Id
	/// </summary>
	[ObservableProperty]
	public partial int Id_Customer { get; set; }
	/// <summary>
	/// 合計ポイント
	/// </summary>
	[ObservableProperty]
	public partial int Point { get; set; }
	/// <summary>
	/// 累計購買回数
	/// </summary>
	[ObservableProperty]
	public partial int SalesCount { get; set; }
	/// <summary>
	/// 累計購買金額
	/// </summary>
	[ObservableProperty]
	public partial int SalesKingaku { get; set; }
}
