using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvWpfclient.Helpers;
using System.Globalization;

namespace CvWpfclient.ViewModels.Sub;

public partial class TranPromotionSearchParamViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	public partial TranPromotionSearchParameter Parameter { get; set; } = new();

	public void Initialize(TranPromotionSearchParameter? param) {
		Parameter = param ?? new TranPromotionSearchParameter { DisplayName = "イベント", MaxCount = AppGlobal.Limit };
	}

	[RelayCommand]
	void Ok() {
		if (!ValidateParameter()) return;

		ClientLib.ExitDialogResult(this, true);
	}

	bool ValidateParameter() {
		if (Parameter.FromTargetId.HasValue && Parameter.ToTargetId.HasValue && Parameter.FromTargetId.Value > Parameter.ToTargetId.Value) {
			MessageEx.ShowWarningDialog($"{Parameter.TargetIdLabel}の開始は終了以下で入力してください", owner: ClientLib.GetActiveView(this));
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
