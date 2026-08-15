using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, [nameof(Id_Tokui), nameof(DenDay)])]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("トランザクション：得意先イベントデータ 得意先、日別のイベントデータ")]
public sealed partial class TranTokuiPromotion : BaseDbClass {
	/// <summary>
	/// 得意先Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 1)]
	[Comment("得意先Id")]
	public partial long Id_Tokui { get; set; }
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("日付 yyyyMMdd 8桁の文字列で表現")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// イベント名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[Comment("イベント名")]
	public partial string Mame { get; set; } = string.Empty;
	/// <summary>
	/// 重要度 0=低, 1=中, 2=高
	/// </summary>
	[ObservableProperty]
	[Comment("重要度 0=低、 1=中、 2=高")]
	public partial int Rank { get; set; }
	/// <summary>
	/// 得意先コード（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[ResultColumn]
	public partial string TokuiCode { get; set; } = string.Empty;
	/// <summary>
	/// 得意先名（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[ResultColumn]
	public partial string TokuiName { get; set; } = string.Empty;
	/// <summary>
	/// 重要度名（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[ResultColumn]
	public partial string RankName { get; set; } = string.Empty;
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, [nameof(Id_Shop), nameof(DenDay)])]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("トランザクション：店舗イベントデータ 店舗、日別のイベントデータ")]
public sealed partial class TranShopPromotion : BaseDbClass {
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType:6)]
	[Comment("店舗Id")]
	public partial long Id_Shop { get; set; }
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("日付 yyyyMMdd 8桁の文字列で表現")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// イベント名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[Comment("イベント名")]
	public partial string Mame { get; set; } = string.Empty;
	/// <summary>
	/// 重要度 0=低, 1=中, 2=高
	/// </summary>
	[ObservableProperty]
	[Comment("重要度 0=低、 1=中、 2=高")]
	public partial int Rank { get; set; }
	/// <summary>
	/// 店舗コード（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[ResultColumn]
	public partial string ShopCode { get; set; } = string.Empty;
	/// <summary>
	/// 店舗名（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[ResultColumn]
	public partial string ShopName { get; set; } = string.Empty;
	/// <summary>
	/// 重要度名（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[ResultColumn]
	public partial string RankName { get; set; } = string.Empty;
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, [nameof(Id_Shop)])]
[Comment("トランザクション：棚卸日一括メンテデータ MasterTokuiのTenType in (0,3,6)に対し棚卸日を設定する")]
public sealed partial class Tran60TanaDate : BaseDbClass {
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 0, additionalInfo:"TenType in (0,3,6)")]
	[Comment("店舗Id")]
	public partial long Id_Shop { get; set; }
	/// <summary>
	/// 棚卸日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("棚卸日付 yyyyMMdd 8桁の文字列で表現")]
	public partial string TanaDay { get; set; } = "19010101";
	/// <summary>
	/// 確定日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("確定日付 yyyyMMdd 8桁の文字列で表現")]
	public partial string FixDay { get; set; } = "19010101";
	/// <summary>
	/// 自動補充フラグ (0:なし 1:する(全日))
	/// </summary>
	[ObservableProperty]
	[Comment("自動補充フラグ (0:なし 1:する(全日))")]
	public partial int AutoHoju { get; set; }
}
