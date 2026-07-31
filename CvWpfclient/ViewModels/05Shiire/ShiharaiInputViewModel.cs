using CommunityToolkit.Mvvm.Input;
using CvBase;

namespace CvWpfclient.ViewModels._05Shiire;

/// <summary>
/// 支払入力 — 仕入先への支払(Tran07Shiharai)を入力する。買掛の減算。
/// </summary>
public partial class ShiharaiInputViewModel : Helpers.BaseKinInputViewModel<Tran07Shiharai> {
	protected override string KinDisplayName => "支払";
	protected override string ToriLabel => "支払先Id";

	[RelayCommand]
	void DoSelectShiire() {
		var shiire = ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code", startPos: CurrentEdit.Id_Torisaki);
		if (shiire == null) return;
		ApplyTorisaki(shiire.Id, shiire.Code, shiire.Name);
	}
}
