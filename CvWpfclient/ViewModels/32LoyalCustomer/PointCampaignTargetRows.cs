using CommunityToolkit.Mvvm.ComponentModel;
using CvBase;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

/// <summary>キャンペーン対象設定画面の左一覧1行。対象件数は子表から数えた表示用の値。</summary>
public sealed class PointCampaignListRow(MasterPointCampaign campaign, int shopCount, int shohinCount) {
	public MasterPointCampaign Campaign { get; } = campaign;
	public int ShopCount { get; } = shopCount;
	public int ShohinCount { get; } = shohinCount;
	public string RankText => Campaign.RankKubun == 0 ? "全" : Campaign.RankKubun.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>対象店舗のチェック行。店舗マスタ(店種3・6)を1行ずつ持つ。</summary>
public sealed partial class PointCampaignShopRow(MasterTokui shop) : ObservableObject {
	public MasterTokui Shop { get; } = shop;

	[ObservableProperty]
	public partial bool IsChecked { get; set; }
}

/// <summary>対象商品の明細行。</summary>
public sealed class PointCampaignShohinRow(long id, string code, string name) {
	public long Id { get; } = id;
	public string Code { get; } = code;
	public string Name { get; } = name;
}
