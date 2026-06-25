using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels.Sub;

public partial class TranTokuiPromotionSearchParamViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	TranTokuiPromotionSearchParameter parameter = new();

	public void Initialize(TranTokuiPromotionSearchParameter? param) {
		Parameter = param ?? new TranTokuiPromotionSearchParameter { DisplayName = "得意先イベント", MaxCount = AppGlobal.Limit };
	}

	[RelayCommand]
	void Ok() {
		if (!ValidateParameter()) return;

		ClientLib.ExitDialogResult(this, true);
	}

	bool ValidateParameter() {
		if (Parameter.FromTokuiId.HasValue && Parameter.ToTokuiId.HasValue && Parameter.FromTokuiId.Value > Parameter.ToTokuiId.Value) {
			MessageEx.ShowWarningDialog("得意先Idの開始は終了以下で入力してください", owner: ClientLib.GetActiveView(this));
			return false;
		}
		if (!IsValidDate(Parameter.FromDate)) {
			MessageEx.ShowWarningDialog("日付(開始)は yyyyMMdd の8桁で入力してください", owner: ClientLib.GetActiveView(this));
			return false;
		}
		if (!IsValidDate(Parameter.ToDate)) {
			MessageEx.ShowWarningDialog("日付(終了)は yyyyMMdd の8桁で入力してください", owner: ClientLib.GetActiveView(this));
			return false;
		}
		if (!string.IsNullOrWhiteSpace(Parameter.FromDate)
			&& !string.IsNullOrWhiteSpace(Parameter.ToDate)
			&& string.CompareOrdinal(Parameter.FromDate, Parameter.ToDate) > 0) {
			MessageEx.ShowWarningDialog("日付の開始は終了以下で入力してください", owner: ClientLib.GetActiveView(this));
			return false;
		}
		if (Parameter.MaxCount.HasValue && Parameter.MaxCount.Value <= 0) {
			MessageEx.ShowWarningDialog("件数は1以上、または空白で入力してください", owner: ClientLib.GetActiveView(this));
			return false;
		}

		return true;
	}

	static bool IsValidDate(string? value) =>
		string.IsNullOrWhiteSpace(value)
		|| DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
