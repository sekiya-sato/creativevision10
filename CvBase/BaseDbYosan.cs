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
	/// 
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 6)]
	[OldTableCommentAttr("店舗CD")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// ブランドId
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterMeisho), meishoKubun:"BRD")]
	[OldTableCommentAttr("ブランドCD")]
	public partial long Id_Brand { get; set; }
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("日付")]
	[ColumnSizeDml(8)]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 売上予算
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("売上予算")]
	public partial long UriYosan { get; set; }
	/// <summary>
	/// 粗利予算
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("粗利予算")]
	public partial long ArariYosan { get; set; }
	/// <summary>
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[ComputedColumn]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VTenpo { get; set; } = new();
	/// <summary>
	/// ブランドデータ
	/// </summary>
	[ObservableProperty]
	[ComputedColumn]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VBrand { get; set; } = new();
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
	[ForeignKey(nameof(MasterShain))]
	[OldTableCommentAttr("販売員CD")]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("日付")]
	[ColumnSizeDml(8)]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 売上予算
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("予算金額")]
	public partial long UriYosan { get; set; }
	/// <summary>
	/// 粗利予算
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("粗利予算")]
	public partial long ArariYosan { get; set; }
}
