using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewServices;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.ViewModels.Sub;

public partial class SelectWinViewModel : Helpers.BaseViewModel {

	[ObservableProperty]
	string title = "選択画面";

	Type MyType = typeof(string);

	string Kubun = string.Empty;

	string Where = string.Empty;
	string Order = string.Empty;
	string[] Parameters = [];


	[RelayCommand]
	async Task Init(CancellationToken ct) {
		await InitList(ct, MyType, Kubun);
	}

	async Task InitList(CancellationToken ct, Type type, string kubun = "", long limit = 999999) {
		try {
			ct.ThrowIfCancellationRequested();
			MyType = type;
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListSimpleParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: MyType,
					where: Where,
					order: Order,
					parameters: Parameters
				))
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			ct.ThrowIfCancellationRequested();
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;
			if (list != null) {
				ListData = new ObservableCollection<dynamic>(list.Cast<dynamic>());
				Count = ListData.Count;
				Current = ListData.FirstOrDefault() ?? new MasterMeisho();
				WeakReferenceMessenger.Default.Send(new Helpers.SelectItemMessage(InitParam));
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}


	[ObservableProperty]
	public ObservableCollection<dynamic>? listData;

	[ObservableProperty]
	public object? current;

	[ObservableProperty]
	public int count;

	[RelayCommand]
	public void DoSelect() {
		if (Current != null) {
			ClientLib.ExitDialogResult(this, true);
		}
		else
			MessageEx.ShowWarningDialog(message: "選択されていません", owner: ClientLib.GetActiveView(this));
	}

	public void SetParam(Type? type0 = null, string where = "", string order = "", string[]? parameters = null) {
		MyType = type0 ?? typeof(string);
		Where = where;
		Order = order;
		Parameters = parameters ?? [];
	}
	[RelayCommand]
	public void Exit() {
		ClientLib.ExitDialogResult(this, false);
	}
}
