using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CvWpfclient.ViewModels.Sub;

public partial class SelectShohinColSizViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	string title = "色サイズ選択";

	[ObservableProperty]
	string searchCondition = string.Empty;

	[ObservableProperty]
	ObservableCollection<DerivedShohinColSiz> listData = [];

	[ObservableProperty]
	DerivedShohinColSiz? current;

	[ObservableProperty]
	int count;

	long idShohin;
	long idCol;
	long idSiz;
	bool filterByColor;

	public void SetParam(long idShohin, long idCol = 0, long idSiz = 0, bool filterByColor = false) {
		this.idShohin = idShohin;
		this.idCol = idCol;
		this.idSiz = idSiz;
		this.filterByColor = filterByColor;
		Title = filterByColor ? "サイズ選択" : "色サイズ選択";
		SearchCondition = filterByColor
			? $"商品Id: {idShohin}  カラーId: {idCol}"
			: $"商品Id: {idShohin}";
	}

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
			if (filterByColor && idCol <= 0) {
				MessageEx.ShowWarningDialog("カラーを選択してください", owner: ClientLib.GetActiveView(this));
				return;
			}

			List<string> clauses = ["Id_Shohin = @0"];
			List<string> parameters = [idShohin.ToString(CultureInfo.InvariantCulture)];
			if (filterByColor) {
				clauses.Add("Id_Col = @1");
				parameters.Add(idCol.ToString(CultureInfo.InvariantCulture));
			}

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(DerivedShohinColSiz),
					where: string.Join(" AND ", clauses),
					order: "Code_Col, Code_Siz, RowIdx",
					parameters: [.. parameters]
				))
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			ct.ThrowIfCancellationRequested();

			if (reply.Code < 0 && reply.Code != -1) {
				MessageEx.ShowErrorDialog($"データ取得失敗: {reply.Option}", owner: ClientLib.GetActiveView(this));
				return;
			}

			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;
			ListData = list == null
				? []
				: new ObservableCollection<DerivedShohinColSiz>(list.Cast<DerivedShohinColSiz>());
			Count = ListData.Count;
			Current = ListData.FirstOrDefault(x =>
				(idCol == 0 || x.Id_Col == idCol) &&
				(idSiz == 0 || x.Id_Siz == idSiz))
				?? ListData.FirstOrDefault();

			if (Current != null) {
				WeakReferenceMessenger.Default.Send(new SelectItemMessage(Current.Id));
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand]
	public void DoSelect() {
		if (Current != null) {
			ClientLib.ExitDialogResult(this, true);
			return;
		}

		MessageEx.ShowWarningDialog(message: "選択されていません", owner: ClientLib.GetActiveView(this));
	}
}
