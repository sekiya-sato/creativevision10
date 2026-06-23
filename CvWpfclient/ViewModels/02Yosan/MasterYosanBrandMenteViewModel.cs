using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels._02Yosan;

public partial class MasterYosanBrandMenteViewModel : Helpers.BaseMenteViewModel<MasterYosanBrand> {
	[ObservableProperty]
	string title = "店ブランド予算マスタメンテ";

	protected override string? ListOrder => "DenDay DESC, Id_Tenpo, Id_Brand";
	protected override int? ListMaxCount => AppGlobal.Limit;

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	protected override bool CanUpdate() => CurrentEdit.Id > 0;

	protected override bool ConfirmAction(string message) {
		if ((message.StartsWith("追加", StringComparison.Ordinal) || message.StartsWith("修正", StringComparison.Ordinal)) && !ValidateCurrentEdit()) {
			return false;
		}

		return base.ConfirmAction(message);
	}

	protected override object CreateInsertParam() {
		NormalizeCurrentEdit();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		NormalizeCurrentEdit();
		return base.CreateUpdateParam();
	}

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (日付={CurrentEdit.DenDay}, 店舗Id={CurrentEdit.Id_Tenpo}, ブランドId={CurrentEdit.Id_Brand})";

	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (日付={CurrentEdit.DenDay}, 店舗Id={CurrentEdit.Id_Tenpo}, ブランドId={CurrentEdit.Id_Brand}, Id={CurrentEdit.Id})";

	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (日付={CurrentEdit.DenDay}, 店舗Id={CurrentEdit.Id_Tenpo}, ブランドId={CurrentEdit.Id_Brand}, Id={CurrentEdit.Id})";

	[RelayCommand]
	void DoSelectShop() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType in (1,3,6)", "Code", startPos: CurrentEdit.Id_Tenpo);
		if (tokui == null) return;
		CurrentEdit.Id_Tenpo = tokui.Id;
	}

	[RelayCommand]
	void DoSelectBrand() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='BRD'", "Code", startPos: CurrentEdit.Id_Brand);
		if (meisho == null) return;
		CurrentEdit.Id_Brand = meisho.Id;
	}

	bool ValidateCurrentEdit() {
		NormalizeCurrentEdit();
		if (CurrentEdit.Id_Tenpo <= 0) {
			Message = "店舗Idを入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (CurrentEdit.Id_Brand <= 0) {
			Message = "ブランドIdを入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (!DateTime.TryParseExact(CurrentEdit.DenDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var denDay)) {
			Message = "日付は yyyyMMdd の8桁で入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (CurrentEdit.UriYosan < 0 || CurrentEdit.ArariYosan < 0) {
			Message = "予算金額には0以上の値を入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}

		CurrentEdit.DenDay = denDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return true;
	}

	void NormalizeCurrentEdit() {
		CurrentEdit.DenDay = (CurrentEdit.DenDay ?? string.Empty).Trim();
	}
}
