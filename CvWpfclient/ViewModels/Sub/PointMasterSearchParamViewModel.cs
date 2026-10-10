using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels.Sub;

public partial class PointMasterSearchParamViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	public partial PointMasterSearchParameter Parameter { get; set; } = new();

	[ObservableProperty]
	public partial IReadOnlyList<KeyValuePair<long, string>> BaseOptions { get; set; } = [];

	public IReadOnlyList<KeyValuePair<int, string>> EnabledOptions { get; } = [
		new(PointMasterSearchParameter.AllEnabled, "すべて"),
		new(1, "有効のみ"),
		new(0, "無効のみ"),
	];

	/// <summary>親ベースの候補を「すべて」付きで設定する。includePending はランクの移行設定待ち(親Id=0)を候補に含める。</summary>
	public void Initialize(PointMasterSearchParameter param, IEnumerable<MasterPointBase>? bases = null, bool includePending = false) {
		List<KeyValuePair<long, string>> options = [new(PointMasterSearchParameter.AllBase, "すべて")];
		if (includePending) options.Add(new(0, "移行設定待ち（親Id=0）"));
		options.AddRange((bases ?? []).Select(x => new KeyValuePair<long, string>(x.Id, $"{x.Code} {x.Name} (版{x.Version})")));
		BaseOptions = options;
		// 呼出元の条件を直接書き換えないよう複製して編集する（キャンセル時に元の条件を残す）
		Parameter = param with { };
		if (!options.Any(x => x.Key == Parameter.Id_PointBase)) Parameter.Id_PointBase = PointMasterSearchParameter.AllBase;
	}

	[RelayCommand]
	void Ok() {
		ClientLib.ExitDialogResult(this, true);
	}
}
