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
	[Comment("ポイントランク区分")]
	public partial int Kubun { get; set; }
	/// <summary>
	/// ポイントランク名称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	[Comment("ポイントランク名称")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// ポイント付与単価
	/// </summary>
	[ObservableProperty]
	[Comment("ポイント付与単価")]
	public partial int PointUnitPrice { get; set; } = 100;
	/// <summary>
	/// ポイント付与数P
	/// </summary>
	[ObservableProperty]
	[Comment("ポイント付与数P")]
	public partial int PointAmountProper { get; set; } = 1;
	/// <summary>
	/// ポイント付与数S
	/// </summary>
	[ObservableProperty]
	[Comment("ポイント付与数S")]
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
	[Comment("日付")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 顧客Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterEndCustomer))]
	[Comment("顧客Id")]
	public partial int Id_Customer { get; set; }
	/// <summary>
	/// 取得ポイント
	/// </summary>
	[ObservableProperty]
	[Comment("取得ポイント")]
	public partial int PointGet { get; set; }
	/// <summary>
	/// 使用ポイント
	/// </summary>
	[ObservableProperty]
	[Comment("使用ポイント")]
	public partial int PointUse { get; set; }
	/// <summary>
	/// 摘要
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	[Comment("摘要")]
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
	[ForeignKey(nameof(MasterEndCustomer))]
	[Comment("顧客Id")]
	public partial int Id_Customer { get; set; }
	/// <summary>
	/// 合計ポイント
	/// </summary>
	[ObservableProperty]
	[Comment("合計ポイント")]
	public partial int Point { get; set; }
	/// <summary>
	/// 累計購買回数
	/// </summary>
	[ObservableProperty]
	[Comment("累計購買回数")]
	public partial int SalesCount { get; set; }
	/// <summary>
	/// 累計購買金額
	/// </summary>
	[ObservableProperty]
	[Comment("累計購買金額")]
	public partial int SalesKingaku { get; set; }
}
