using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;

namespace CvBase;

public sealed partial class TranTokuiPromotion {
	/// <summary>
	/// 得意先コード（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[property: ResultColumn]
	string tokuiCode = string.Empty;
	/// <summary>
	/// 得意先名（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[property: ResultColumn]
	string tokuiName = string.Empty;
	/// <summary>
	/// 重要度名（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[property: ResultColumn]
	string rankName = string.Empty;
}

public sealed partial class TranShopPromotion {
	/// <summary>
	/// 店舗コード（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[property: ResultColumn]
	string shopCode = string.Empty;
	/// <summary>
	/// 店舗名（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[property: ResultColumn]
	string shopName = string.Empty;
	/// <summary>
	/// 重要度名（一覧表示用）
	/// </summary>
	[ObservableProperty]
	[property: ResultColumn]
	string rankName = string.Empty;
}
