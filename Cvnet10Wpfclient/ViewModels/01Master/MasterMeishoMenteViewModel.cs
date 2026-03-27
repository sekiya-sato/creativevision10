using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.Helpers;
using System.Collections;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.ViewModels._01Master;

public partial class MasterMeishoMenteViewModel : Helpers.BaseCodeNameLightMenteViewModel<MasterMeisho> {
	[ObservableProperty]
	string title = "名称マスターメンテ";

	protected override string[] AdditionalLightweightColumns => ["Kubun", "Odr", "KubunName"];

	[ObservableProperty]
	ObservableCollection<MasterMeisho> kubunList = new();

	[ObservableProperty]
	MasterMeisho? selectedKubun;

	bool suppressSelectedKubunChanged;

	protected override string? ListOrder => "Kubun,Code";

	protected override string? ListWhere {
		get {
			var code = SelectedKubun?.Code;
			if (string.IsNullOrWhiteSpace(code))
				return null;
			var safeCode = code.Replace("'", "''");
			return $"Kubun='{safeCode}'";
		}
	}

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		await LoadKubunListAsync(ct);
		if (KubunList.Count == 0) {
			ListData.Clear();
			Count = 0;
			return;
		}
		suppressSelectedKubunChanged = true;
		SelectedKubun = KubunList.First(c => c.Code == "BRD") ?? KubunList.FirstOrDefault();
		suppressSelectedKubunChanged = false;
		if (SelectedKubun != null) {
			await RefreshListForSelectedKubunAsync(ct);
		}
	}

	async Task LoadKubunListAsync(CancellationToken ct) {
		try {
			ClientLib.Cursor2Wait();
			var msg = new CodeShare.CvnetMsg {
				Code = 0,
				Flag = CodeShare.CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(MasterMeisho),
					where: "Kubun='IDX'",
					order: "Code"
				))
			};

			var reply = await SendMessageAsync(msg, ct);
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list) {
				KubunList = new ObservableCollection<MasterMeisho>(list.Cast<MasterMeisho>());
			}
		}
		catch (OperationCanceledException) {
			Message = "区分一覧取得がキャンセルされました";
		}
		catch (Exception ex) {
			Message = $"区分一覧取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	async Task RefreshListForSelectedKubunAsync(CancellationToken ct) {
		if (SelectedKubun == null) {
			ListData.Clear();
			Count = 0;
			return;
		}
		await DoListCommand.ExecuteAsync(ct);
	}

	[RelayCommand]
	void DoSelectKubun() {
		var selWin = new Views.Sub.SelectKubunView();
		var vm = selWin.DataContext as Sub.SelectKubunViewModel;
		if (vm == null) return;
		vm.SetParam("Kubun='IDX'", CurrentEdit.Kubun);
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		CurrentEdit.Kubun = meisho?.Code ?? CurrentEdit.Kubun;
		CurrentEdit.KubunName = meisho?.Name ?? CurrentEdit.KubunName;
	}

	partial void OnSelectedKubunChanged(MasterMeisho? oldValue, MasterMeisho? newValue) {
		if (suppressSelectedKubunChanged) return;
		_ = RefreshListForSelectedKubunAsync(CancellationToken.None);
	}
}
