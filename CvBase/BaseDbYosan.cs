using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

// 予算マスタ
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uk1", false, "Id_Tenpo", "Id_Brand", "DenDay")]
[KeyDml("nk1", false, "DenDay")]
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
}

[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uk1", false, "Id_Shain", "DenDay")]
[KeyDml("nk1", false, "DenDay")]
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
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("uk1", false, "Id_Tenpo", "Id_Shain", "DenDay")]
[KeyDml("nk1", false, "DenDay")]
[Comment("マスタ：営業担当別予算：Tran00Uriage,Tran01Tenuri を合計した売上に対する予算")]
public sealed partial class MasterYosanEigyoTanto : BaseDbClass {
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("得意先CD")]
	long id_Tenpo;
	/// <summary>
	/// 販売員Id 
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("営業担当CD")]
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

/* ToDo: 未作成テーブル(配分)
[Comment("トランザクション：配分データ：日付、配分CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHaibun : BaseDbClass {
}
[Comment("派生テーブル：配分明細：日付、倉庫Id、商品Id、色サイズ、予定数量、実数量、完了FLG、元伝票Id")]
public sealed partial class DerivedHaibun : BaseDbClass {
}
 */

/* ToDo: 未作成テーブル(補充)
[Comment("トランザクション：補充データ：日付、配分CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHojyu : BaseDbClass {
}
 */

/* ToDo: 未作成テーブル(集計)
[Comment("集計テーブル：売掛データ：年月、得意先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryUrikake : BaseDbClass {
}
[Comment("集計テーブル：請求データ：年月+締日、得意先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryUriSei : BaseDbClass {
}
[Comment("集計テーブル：買掛データ：年月、仕入先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryKaikake : BaseDbClass {
}
[Comment("集計テーブル：支払データ：年月、仕入先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryKaiShi : BaseDbClass {
}
 */

/* ToDo: 未作成テーブル(顧客)
[Comment("ベースポイントトランク別ポイント、ボーナスポイント")]
public sealed partial class MasterPointRank : BaseDbClass {
}
[Comment("ポイント履歴テーブル：日付、顧客Id、取得ポイント、使用ポイント、残")]
public sealed partial class TranPointRireki : BaseDbClass {
}
 */

