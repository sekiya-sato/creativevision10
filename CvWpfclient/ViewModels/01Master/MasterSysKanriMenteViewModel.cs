using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvWpfclient.ViewModels._01Master;

/// <summary>
/// システム管理マスターメンテ ViewModel（単一レコード、XAMLはCurrent.*に直接バインド）
/// </summary>
public partial class MasterSysKanriMenteViewModel : Helpers.BaseMenteViewModel<MasterSysman> {

	[ObservableProperty]
	string title = "システム管理マスターメンテ画面";

	[ObservableProperty]
	string? desc0;

	// MasterSysman は単一レコードのため、ListOrderは不要だが、初期値がCodeのため上書きする必要がある
	protected override string? ListOrder => "Id";

	public IReadOnlyList<EnumShime> ShimeBiItems { get; } = Enum.GetValues<EnumShime>();

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	protected override void AfterList(System.Collections.IList list) {
		if (list.Count > 0) {
			var timespan = DateTime.Now - StartTime;
			Desc0 = $"開始{StartTime} 取得、画面展開{timespan.ToStrSpan()}";
		}
	}

	// XAMLがCurrent.*に直接バインドしているため、CurrentEditではなくCurrentを送信
	protected override object CreateUpdateParam() =>
		new UpdateParam(Tabletype, Common.SerializeObject(Current));

	protected override bool CanDelete() => false;
}
