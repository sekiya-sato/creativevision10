using CodeShare;
using CvBase;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>得意先締日単位で請求残を作成する。</summary>
public partial class BillingCalculationViewModel : BaseBillingCalculationViewModel {
	protected override CvFlag TargetFlag => CvFlag.Msg056_SummaryUriSei;
	protected override string ActionName => "請求計算";
	protected override string TorihikiName => "得意先";
	protected override string MasterTableName => nameof(MasterTokui);
	protected override bool SupportsReissue => true;
	protected override string PaysakiParentLabel => "請求先";
}
