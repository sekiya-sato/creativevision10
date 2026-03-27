using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.Helpers;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.ViewModels.Sub;

public partial class SelectKubunViewModel : Helpers.BaseViewModel {

	[ObservableProperty]
	string title = "区分選択";
	string Where = string.Empty;
	string Order = "Kubun,Code";
	string StartPos = string.Empty;


	[RelayCommand]
	async Task Init(CancellationToken ct) {
		await InitList(ct);
	}

	async Task InitList(CancellationToken ct) {
		try {
			ct.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListSimpleParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(MasterMeisho),
					where: Where,
					order: Order
				))
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			ct.ThrowIfCancellationRequested();
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;
			if (list != null) {
				ListData = new ObservableCollection<dynamic>(list.Cast<dynamic>());
				Count = ListData.Count;
				Current = !string.IsNullOrEmpty(StartPos)
					? ListData.FirstOrDefault(x => x.Code == StartPos) ?? ListData.FirstOrDefault() ?? new MasterMeisho()
					: ListData.FirstOrDefault() ?? new MasterMeisho();
				WeakReferenceMessenger.Default.Send(new SelectStringMessage(StartPos));
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

	public void SetParam(string where = "", string startPos = "") {
		Where = where;
		StartPos = startPos;
	}
}
