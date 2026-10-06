using CvBase.Share;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

/// <summary>
/// 店舗別キャンペーン設定: 優先区分「店別」のキャンペーンへ対象店舗を設定する。
/// 一覧・重複確認・保存の流れは <see cref="PointCampaignTargetViewModelBase"/> にある。
/// </summary>
public partial class ShopCampaignSettingViewModel : PointCampaignTargetViewModelBase {
	public override string Title => "店舗別キャンペーン設定";
	protected override int[] PriorityTypes { get; } = [(int)EnumPointCampaignPriority.Shop];
}
