using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

/// <summary>店舗配分入力の一覧取得条件。</summary>
public sealed record class ShopHaibunSearchParameter {
	public long Id_Soko { get; set; }
	public string SokoCode { get; set; } = string.Empty;
	public string SokoName { get; set; } = string.Empty;
	public int Kubun { get; set; }
	public string ShohinCodeFrom { get; set; } = string.Empty;
	public string ShohinCodeTo { get; set; } = string.Empty;
	public string ShohinName { get; set; } = string.Empty;
	public string BrandFrom { get; set; } = string.Empty;
	public string BrandTo { get; set; } = string.Empty;
	public string ItemFrom { get; set; } = string.Empty;
	public string ItemTo { get; set; } = string.Empty;
	public string SeasonFrom { get; set; } = string.Empty;
	public string SeasonTo { get; set; } = string.Empty;
	public int? MaxCount { get; set; }
}

/// <summary>店舗配分入力の一覧取得条件を指定するダイアログの ViewModel。</summary>
public partial class ShopHaibunSearchParamViewModel : BaseViewModel {
	public sealed record HaibunKubunOption(int Value, string Name);

	public IReadOnlyList<HaibunKubunOption> KubunOptions { get; } = [
		new(0, "初回配分"),
		new(1, "在庫配分"),
	];

	[ObservableProperty]
	public partial ShopHaibunSearchParameter Parameter { get; set; } = new();

	public void Initialize(ShopHaibunSearchParameter? param) {
		Parameter = param ?? new ShopHaibunSearchParameter();
	}

	[RelayCommand]
	void Ok() {
		if (!Validate()) return;
		ClientLib.ExitDialogResult(this, true);
	}

	bool Validate() {
		if (Parameter.Id_Soko <= 0) {
			ShowWarning("配分元倉庫を選択してください");
			return false;
		}
		if (Parameter.MaxCount is <= 0) {
			ShowWarning("件数は1以上で入力してください");
			return false;
		}
		return true;
	}

	[RelayCommand]
	void SelectSoko() {
		var soko = ShowSelect<MasterTokui>(typeof(MasterTokui), "TenType=0", "Code", Parameter.Id_Soko);
		if (soko == null) return;
		Parameter = Parameter with { Id_Soko = soko.Id, SokoCode = soko.Code, SokoName = soko.Name };
	}

	[RelayCommand]
	void SelectShohinFrom() => SelectShohinCode(code => Parameter = Parameter with { ShohinCodeFrom = code });

	[RelayCommand]
	void SelectShohinTo() => SelectShohinCode(code => Parameter = Parameter with { ShohinCodeTo = code });

	[RelayCommand]
	void SelectBrandFrom() => SelectMeisho("BRD", code => Parameter = Parameter with { BrandFrom = code });

	[RelayCommand]
	void SelectBrandTo() => SelectMeisho("BRD", code => Parameter = Parameter with { BrandTo = code });

	[RelayCommand]
	void SelectItemFrom() => SelectMeisho("ITM", code => Parameter = Parameter with { ItemFrom = code });

	[RelayCommand]
	void SelectItemTo() => SelectMeisho("ITM", code => Parameter = Parameter with { ItemTo = code });

	[RelayCommand]
	void SelectSeasonFrom() => SelectMeisho("SZN", code => Parameter = Parameter with { SeasonFrom = code });

	[RelayCommand]
	void SelectSeasonTo() => SelectMeisho("SZN", code => Parameter = Parameter with { SeasonTo = code });

	void SelectMeisho(string kubun, Action<string> setCode) {
		var meisho = ShowSelect<MasterMeisho>(typeof(MasterMeisho), $"Kubun='{kubun}'", "Code");
		if (meisho == null) return;
		setCode(meisho.Code);
	}

	void SelectShohinCode(Action<string> setCode) {
		var view = new Views.Sub.SelectShohinView();
		if (view.DataContext is not SelectShohinViewModel vm) return;
		if (ClientLib.ShowDialogView(view, this) != true) return;
		if (vm.SelectedShohin is not MasterShohin selected) return;
		setCode(selected.Code ?? string.Empty);
	}

	TResult? ShowSelect<TResult>(Type tableType, string where, string order, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current as TResult;
	}

	void ShowWarning(string message) =>
		MessageEx.ShowWarningDialog(message, owner: ClientLib.GetActiveView(this));
}
