using CodeShare;
using CvBase;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>仕入先締日単位で支払残を作成する。</summary>
public partial class PaymentCalculationViewModel : BaseBillingCalculationViewModel {
	protected override CvFlag TargetFlag => CvFlag.Msg057_SummaryKaiShi;
	protected override string ActionName => "支払計算";
	protected override string TorihikiName => "仕入先";
	protected override string MasterTableName => nameof(MasterShiire);
	protected override string PaysakiParentLabel => "支払先";
}
