using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

// 予算マスタ
[PrimaryKey("Id", AutoIncrement = true)]
[KeyDml("nk1", false, "DenDay")]
[Comment("マスタ：店舗ブランド予算：年月(日)、ブランド、売上予算、粗利予算")]
public sealed partial class MasterYosanBrand : BaseDbClass {
	/// <summary>
	/// 店舗キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("店舗CD")]
	long id_Tenpo;
	/// <summary>
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTenpo = new();
	/// <summary>
	/// ブランド
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("ブランドCD")]
	long id_Brand;
	/// <summary>
	/// ブランドデータ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vBrand = new();
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

/* ToDo: 未作成テーブル(予算)
[Comment("マスタ：販売員予算：年月(日)、販売員Id、店舗、売上予算、粗利予算")]
public sealed partial class MasterYosanHanbai : BaseDbClass {
}
[Comment("マスタ：営業担当別予算：年月(日)、営業担当Id、店舗、売上予算、粗利予算")]
public sealed partial class MasterYosanEigyoTanto : BaseDbClass {
}
*/

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

