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
	[OldTableCommentAttr("得意先CD")]
	long id_Tokui;
	/// <summary>
	/// 年月 yyyyMM 6桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(6)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("年月")]
	string denMonth = "190101";
	/// <summary>
	/// 当月残高
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("当月残高")]
	long balance;
	/// <summary>
	/// 当月入金合計
	/// </summary>
	[ObservableProperty]
	long totalIn;
	/// <summary>
	/// 当月売上合計
	/// </summary>
	[ObservableProperty]
	long totalSales;
	/// <summary>
	/// 売上金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("売上金額")]
	long uriage;
	/// <summary>
	/// 返品金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("返品金額")]
	long henpin;
	/// <summary>
	/// 値引金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引金額")]
	long nebiki;
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税")]
	long tax;
	/// <summary>
	/// 現金入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("現金入金")]
	long cash;
	/// <summary>
	/// 振込手数料
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("振込手数料")]
	long fee;
	/// <summary>
	/// 電子記録債権
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("手形入金")]
	long densai;
	/// <summary>
	/// 相殺入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("相殺入金")]
	long offset;
	/// <summary>
	/// その他入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("その他入金")]
	long other;
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
	[OldTableCommentAttr("得意先CD")]
	long id_Tokui;
	/// <summary>
	/// 請求日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("請求日")]
	string denDay = "19010101";
	/// <summary>
	/// 請求開始日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("請求開始日")]
	string dayFrom = "19010101";
	/// <summary>
	/// 請求終了日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("請求終了日")]
	string dayTo = "19010101";
	/// <summary>
	/// 当月残高
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("当月残高")]
	long balance;
	/// <summary>
	/// 当月入金合計
	/// </summary>
	[ObservableProperty]
	long totalIn;
	/// <summary>
	/// 当月売上合計
	/// </summary>
	[ObservableProperty]
	long totalSales;
	/// <summary>
	/// 売上金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("売上金額")]
	long uriage;
	/// <summary>
	/// 返品金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("返品金額")]
	long henpin;
	/// <summary>
	/// 値引金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引金額")]
	long nebiki;
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税")]
	long tax;
	/// <summary>
	/// 現金入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("現金入金")]
	long cash;
	/// <summary>
	/// 振込手数料
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("振込手数料")]
	long fee;
	/// <summary>
	/// 電子記録債権
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("手形入金")]
	long densai;
	/// <summary>
	/// 相殺入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("相殺入金")]
	long offset;
	/// <summary>
	/// その他入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("その他入金")]
	long other;
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
	[OldTableCommentAttr("仕入先CD")]
	long id_Shiire;
	/// <summary>
	/// 年月 yyyyMM 6桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(6)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("年月")]
	string denMonth = "190101";
	/// <summary>
	/// 当月残高
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("当月残高")]
	long balance;
	/// <summary>
	/// 当月支払合計
	/// </summary>
	[ObservableProperty]
	long totalOut;
	/// <summary>
	/// 当月仕入合計
	/// </summary>
	[ObservableProperty]
	long totalShiire;
	/// <summary>
	/// 仕入金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入金額")]
	long shiire;
	/// <summary>
	/// 返品金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("返品金額")]
	long henpin;
	/// <summary>
	/// 値引金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引金額")]
	long nebiki;
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税")]
	long tax;
	/// <summary>
	/// 現金支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("現金支払")]
	long cash;
	/// <summary>
	/// 振込手数料
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("振込手数料")]
	long fee;
	/// <summary>
	/// 電子記録債権
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("手形支払")]
	long densai;
	/// <summary>
	/// 相殺支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("相殺支払")]
	long offset;
	/// <summary>
	/// その他支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("その他支払")]
	long other;
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
	[OldTableCommentAttr("仕入先CD")]
	long id_Shiire;
	/// <summary>
	/// 支払日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("支払日")]
	string denDay = "19010101";
	/// <summary>
	/// 支払開始日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("支払開始日")]
	string dayFrom = "19010101";
	/// <summary>
	/// 支払終了日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("支払終了日")]
	string dayTo = "19010101";
	/// <summary>
	/// 当月残高
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("当月残高")]
	long balance;
	/// <summary>
	/// 当月支払合計
	/// </summary>
	[ObservableProperty]
	long totalOut;
	/// <summary>
	/// 当月仕入合計
	/// </summary>
	[ObservableProperty]
	long totalShiire;
	/// <summary>
	/// 仕入金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入金額")]
	long shiire;
	/// <summary>
	/// 返品金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("返品金額")]
	long henpin;
	/// <summary>
	/// 値引金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引金額")]
	long nebiki;
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税")]
	long tax;
	/// <summary>
	/// 現金入金
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("現金入金")]
	long cash;
	/// <summary>
	/// 振込手数料
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("振込手数料")]
	long fee;
	/// <summary>
	/// 電子記録債権
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("手形支払")]
	long densai;
	/// <summary>
	/// 相殺支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("相殺支払")]
	long offset;
	/// <summary>
	/// その他支払
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("その他支払")]
	long other;
}
