using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CvWpfclient.ViewModels.Sub;

public partial class InputShohinColSizViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	string title = "色サイズ一括数量入力";

	[ObservableProperty]
	string searchCondition = string.Empty;

	[ObservableProperty]
	ObservableCollection<InputShohinColSizRow> listData = [];

	[ObservableProperty]
	InputShohinColSizRow? current;

	[ObservableProperty]
	int count;

	long idShohin;

	public void SetParam(long idShohin) {
		this.idShohin = idShohin;
		SearchCondition = $"商品Id: {idShohin}";
	}

	public List<InputShohinColSizRow> GetResults() =>
		[.. ListData.Where(x => x.Su != 0)];

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		await InitList(ct);
	}

	async Task InitList(CancellationToken ct) {
		try {
			ct.ThrowIfCancellationRequested();
			if (idShohin <= 0) {
				MessageEx.ShowWarningDialog("商品を選択してください", owner: ClientLib.GetActiveView(this));
				return;
			}

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(DerivedShohinColSiz),
					where: "Id_Shohin = @0",
					order: "Code_Col, Code_Siz, RowIdx",
					parameters: [idShohin.ToString(CultureInfo.InvariantCulture)]
				))
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			ct.ThrowIfCancellationRequested();

			if (reply.Code < 0 && reply.Code != -1) {
				MessageEx.ShowErrorDialog($"データ取得失敗: {reply.Option}", owner: ClientLib.GetActiveView(this));
				return;
			}

			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;
			var sourceList = list == null
				? []
				: new ObservableCollection<DerivedShohinColSiz>(list.Cast<DerivedShohinColSiz>());

			ListData = new ObservableCollection<InputShohinColSizRow>(
				sourceList.Select(x => new InputShohinColSizRow { Source = x, Su = 0 }));
			Count = ListData.Count;
			Current = ListData.FirstOrDefault();
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand]
	public void DoDecideQuantity() {
		var results = GetResults();
		if (results.Count == 0) {
			MessageEx.ShowWarningDialog(message: "数量が入力されていません", owner: ClientLib.GetActiveView(this));
			return;
		}

		ClientLib.ExitDialogResult(this, true);
	}
}

public partial class InputShohinColSizRow : ObservableObject {
	[ObservableProperty]
	int su;

	public DerivedShohinColSiz Source { get; set; } = null!;
}
