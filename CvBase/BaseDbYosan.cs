using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

// 予算マスタ
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", false, nameof(Id_Tenpo), nameof(Id_Brand), nameof(DenDay))]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("マスタ：店舗ブランド予算：Tran00Uriage,Tran01Tenuri を合計した売上に対する予算")]
public sealed partial class MasterYosanBrand : BaseDbClass {
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("店舗CD")]
	long id_Tenpo;
	/// <summary>
	/// ブランドId
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("ブランドCD")]
	long id_Brand;
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("日付")]
	[property: ColumnSizeDml(8)]
	string denDay = "19010101";
	/// <summary>
	/// 売上予算
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("売上予算")]
	long uriYosan;
	/// <summary>
	/// 粗利予算
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("粗利予算")]
	long arariYosan;

	[ObservableProperty]
	[property: ResultColumn]
	string tenpoCode = string.Empty;

	[ObservableProperty]
	[property: ResultColumn]
	string tenpoName = string.Empty;

	[ObservableProperty]
	[property: ResultColumn]
	string brandCode = string.Empty;

	[ObservableProperty]
	[property: ResultColumn]
	string brandName = string.Empty;
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", false, nameof(Id_Shain), nameof(DenDay))]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("マスタ：販売員予算：Tran01Tenuri を合計した売上に対する予算")]
public sealed partial class MasterYosanHanbai : BaseDbClass {
	/// <summary>
	/// 販売員Id 
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("販売員CD")]
	long id_Shain;
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("日付")]
	[property: ColumnSizeDml(8)]
	string denDay = "19010101";
	/// <summary>
	/// 売上予算
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("予算金額")]
	long uriYosan;
	/// <summary>
	/// 粗利予算
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("粗利予算")]
	long arariYosan;
}
