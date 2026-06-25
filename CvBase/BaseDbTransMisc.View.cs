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
