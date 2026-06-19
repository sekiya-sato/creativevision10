using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uk1", true, [nameof(Id_Tokui), nameof(DenDay)])]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("トランザクション：得意先イベントデータ 得意先、日別のイベントデータ")]
public sealed partial class TranTokuiPromotion : BaseDbClass {
	/// <summary>
	/// 得意先Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui))]
	long id_Tokui;
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	string denDay = "19010101";
	/// <summary>
	/// イベント名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: System.ComponentModel.DefaultValue("")]
	string mame = "";
	/// <summary>
	/// 重要度 0=低, 1=中, 2=高
	/// </summary>
	[ObservableProperty]
	int rank;
}

[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uk1", true, [nameof(Id_Store), nameof(DenDay)])]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("トランザクション：店舗イベントデータ 店舗、日別のイベントデータ")]
public sealed partial class TranStorePromotion : BaseDbClass {
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	long id_Store;
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	string denDay = "19010101";
	/// <summary>
	/// イベント名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	[property: System.ComponentModel.DefaultValue("")]
	string mame = "";
	/// <summary>
	/// 重要度 0=低, 1=中, 2=高
	/// </summary>
	[ObservableProperty]
	int rank;
}
