using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uk1", true, "Kubun")]
[Comment("ベースポイント、ランク別ポイント")]
public sealed partial class MasterPointRank : BaseDbClass {
	/// <summary>
	/// ポイントランク区分
	/// </summary>
	[ObservableProperty]
	int kubun;
	/// <summary>
	/// ポイントランク名称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(40)]
	[property: System.ComponentModel.DefaultValue("")]
	string name = string.Empty;
	/// <summary>
	/// ポイント付与単価
	/// </summary>
	[ObservableProperty]
	int pointUnitPrice = 100;
	/// <summary>
	/// ポイント付与数P
	/// </summary>
	[ObservableProperty]
	int pointAmountProper = 1;
	/// <summary>
	/// ポイント付与数S
	/// </summary>
	[ObservableProperty]
	int pointAmountSale = 1;
}

[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uk1", true, "Id_Customer", "DenDay")]
[Comment("ポイント履歴テーブル")]
public sealed partial class TranPointRireki : BaseDbClass {
	/// <summary>
	/// 日付
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("請求日")]
	string denDay = "19010101";
	/// <summary>
	/// 顧客Id
	/// </summary>
	[ObservableProperty]
	int id_Customer;
	/// <summary>
	/// 取得ポイント
	/// </summary>
	[ObservableProperty]
	int pointGet;
	/// <summary>
	/// 使用ポイント
	/// </summary>
	[ObservableProperty]
	int pointUse;
	/// <summary>
	/// 摘要
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(40)]
	[property: System.ComponentModel.DefaultValue("")]
	string memo = string.Empty;
}

[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uk1", true, "Id_Customer")]
[Comment("ポイント残高テーブル")]
public sealed partial class SummaryPoint : BaseDbClass {
	/// <summary>
	/// 顧客Id
	/// </summary>
	[ObservableProperty]
	int id_Customer;
	/// <summary>
	/// 合計ポイント
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
}
