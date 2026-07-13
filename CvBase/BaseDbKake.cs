using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_Tokui), nameof(DenMonth))]
[KeyDml("nk1", false, nameof(DenMonth))]
[Comment("集計データ：年月別売掛 自社締日に従った売掛")]
[OldTableCommentAttr("HC$MANAGE_KAKEURI")]
public sealed partial class SummaryUriKake : BaseDbClass {
	/// <summary>
	/// 得意先Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 1)]
	[OldTableCommentAttr("得意先CD")]
	public partial long Id_Tokui { get; set; }
	/// <summary>
	/// 年月 yyyyMM 6桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(6)]
	[OldTableCommentAttr("年月")]
	public partial string DenMonth { get; set; } = "190101";
	/// <summary>
	/// 当月残高
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("当月残高")]
	public partial long Balance { get; set; }
	/// <summary>
	/// 当月入金合計
	/// </summary>
	[ObservableProperty]
	public partial long TotalIn { get; set; }
	/// <summary>
	/// 当月売上合計
	/// </summary>
	[ObservableProperty]
	public partial long TotalSales { get; set; }
	/// <summary>
	/// 売上金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("売上金額")]
	public partial long Uriage { get; set; }
	/// <summary>
	/// 返品金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("返品金額")]
	public partial long Henpin { get; set; }
	/// <summary>
	/// 値引金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引金額")]
	public partial long Nebiki { get; set; }
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税")]
	public partial long Tax { get; set; }
	/// <summary>
	/// 現金入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("現金入金")]
	public partial long Cash { get; set; }
	/// <summary>
	/// 振込手数料
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("振込手数料")]
	public partial long Fee { get; set; }
	/// <summary>
	/// 電子記録債権
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("手形入金")]
	public partial long Densai { get; set; }
	/// <summary>
	/// 相殺入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("相殺入金")]
	public partial long Offset { get; set; }
	/// <summary>
	/// その他入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("その他入金")]
	public partial long Other { get; set; }
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_Tokui), nameof(DenDay))]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("集計データ：年月別請求 相手先締日に合わせた請求データ")]
[OldTableCommentAttr("HC$MANAGE_KAKESKY")]
public sealed partial class SummaryUriSei : BaseDbClass {
	/// <summary>
	/// 得意先Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 1)]
	[OldTableCommentAttr("得意先CD")]
	public partial long Id_Tokui { get; set; }
	/// <summary>
	/// 請求日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("請求日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 請求開始日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("請求開始日")]
	public partial string DayFrom { get; set; } = "19010101";
	/// <summary>
	/// 請求終了日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("請求終了日")]
	public partial string DayTo { get; set; } = "19010101";
	/// <summary>
	/// 当月残高
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("当月残高")]
	public partial long Balance { get; set; }
	/// <summary>
	/// 当月入金合計
	/// </summary>
	[ObservableProperty]
	public partial long TotalIn { get; set; }
	/// <summary>
	/// 当月売上合計
	/// </summary>
	[ObservableProperty]
	public partial long TotalSales { get; set; }
	/// <summary>
	/// 売上金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("売上金額")]
	public partial long Uriage { get; set; }
	/// <summary>
	/// 返品金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("返品金額")]
	public partial long Henpin { get; set; }
	/// <summary>
	/// 値引金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引金額")]
	public partial long Nebiki { get; set; }
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税")]
	public partial long Tax { get; set; }
	/// <summary>
	/// 現金入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("現金入金")]
	public partial long Cash { get; set; }
	/// <summary>
	/// 振込手数料
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("振込手数料")]
	public partial long Fee { get; set; }
	/// <summary>
	/// 電子記録債権
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("手形入金")]
	public partial long Densai { get; set; }
	/// <summary>
	/// 相殺入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("相殺入金")]
	public partial long Offset { get; set; }
	/// <summary>
	/// その他入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("その他入金")]
	public partial long Other { get; set; }
}


[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_Shiire), nameof(DenMonth))]
[KeyDml("nk1", false, nameof(DenMonth))]
[Comment("集計データ：年月別買掛 自社締日に従った買掛")]
[OldTableCommentAttr("HC$MANAGE_KAKEKAI")]
public sealed partial class SummaryKaiKake : BaseDbClass {
	/// <summary>
	/// 仕入先Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShiire))]
	[OldTableCommentAttr("仕入先CD")]
	public partial long Id_Shiire { get; set; }
	/// <summary>
	/// 年月 yyyyMM 6桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(6)]
	[OldTableCommentAttr("年月")]
	public partial string DenMonth { get; set; } = "190101";
	/// <summary>
	/// 当月残高
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("当月残高")]
	public partial long Balance { get; set; }
	/// <summary>
	/// 当月支払合計
	/// </summary>
	[ObservableProperty]
	public partial long TotalOut { get; set; }
	/// <summary>
	/// 当月仕入合計
	/// </summary>
	[ObservableProperty]
	public partial long TotalShiire { get; set; }
	/// <summary>
	/// 仕入金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入金額")]
	public partial long Shiire { get; set; }
	/// <summary>
	/// 返品金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("返品金額")]
	public partial long Henpin { get; set; }
	/// <summary>
	/// 値引金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引金額")]
	public partial long Nebiki { get; set; }
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税")]
	public partial long Tax { get; set; }
	/// <summary>
	/// 現金支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("現金支払")]
	public partial long Cash { get; set; }
	/// <summary>
	/// 振込手数料
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("振込手数料")]
	public partial long Fee { get; set; }
	/// <summary>
	/// 電子記録債権
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("手形支払")]
	public partial long Densai { get; set; }
	/// <summary>
	/// 相殺支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("相殺支払")]
	public partial long Offset { get; set; }
	/// <summary>
	/// その他支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("その他支払")]
	public partial long Other { get; set; }
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_Shiire), nameof(DenDay))]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("集計データ：年月別支払 相手先締日に合わせた支払データ")]
[OldTableCommentAttr("HC$MANAGE_KAIKESHY")]
public sealed partial class SummaryKaiShi : BaseDbClass {
	/// <summary>
	/// 仕入先Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShiire))]
	[OldTableCommentAttr("仕入先CD")]
	public partial long Id_Shiire { get; set; }
	/// <summary>
	/// 支払日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("支払日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 支払開始日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("支払開始日")]
	public partial string DayFrom { get; set; } = "19010101";
	/// <summary>
	/// 支払終了日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("支払終了日")]
	public partial string DayTo { get; set; } = "19010101";
	/// <summary>
	/// 当月残高
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("当月残高")]
	public partial long Balance { get; set; }
	/// <summary>
	/// 当月支払合計
	/// </summary>
	[ObservableProperty]
	public partial long TotalOut { get; set; }
	/// <summary>
	/// 当月仕入合計
	/// </summary>
	[ObservableProperty]
	public partial long TotalShiire { get; set; }
	/// <summary>
	/// 仕入金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入金額")]
	public partial long Shiire { get; set; }
	/// <summary>
	/// 返品金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("返品金額")]
	public partial long Henpin { get; set; }
	/// <summary>
	/// 値引金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引金額")]
	public partial long Nebiki { get; set; }
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税")]
	public partial long Tax { get; set; }
	/// <summary>
	/// 現金入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("現金入金")]
	public partial long Cash { get; set; }
	/// <summary>
	/// 振込手数料
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("振込手数料")]
	public partial long Fee { get; set; }
	/// <summary>
	/// 電子記録債権
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("手形支払")]
	public partial long Densai { get; set; }
	/// <summary>
	/// 相殺支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("相殺支払")]
	public partial long Offset { get; set; }
	/// <summary>
	/// その他支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("その他支払")]
	public partial long Other { get; set; }
}
