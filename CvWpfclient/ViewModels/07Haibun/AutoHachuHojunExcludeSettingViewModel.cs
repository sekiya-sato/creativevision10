namespace CvWpfclient.ViewModels._07Haibun;

public partial class AutoHachuHojunExcludeSettingViewModel : AutoReplenishSettingViewModel {
	protected override bool IsStockSetting => false;
	public AutoHachuHojunExcludeSettingViewModel() => FlagValue = true;
}
