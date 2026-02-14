using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewServices;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Cvnet10Wpfclient.ViewModels;

public partial class MasterMeishoMenteViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	string title = "名称マスター保守";

	readonly Type Tabletype = typeof(MasterMeisho);

	[ObservableProperty]
	ObservableCollection<MasterMeisho> listData = [];

	[ObservableProperty]
	MasterMeisho current = new();
	partial void OnCurrentChanged(MasterMeisho? oldValue, MasterMeisho newValue) {
		if (newValue == null) {
			CurrentEdit = new();
			return;
		}
		if (oldValue?.Id != newValue.Id) {
			CurrentEdit = Common.CopyObject<MasterMeisho>(Current);
		}
	}

	[ObservableProperty]
	MasterMeisho currentEdit = new();

	[ObservableProperty]
	int count;


	[ObservableProperty]
	DateTime startTime = DateTime.Now;
	[ObservableProperty]
	TimeSpan getListTime = TimeSpan.Zero;

	// 初期化
	[RelayCommand]
	async Task Init(CancellationToken ct) {
		//await DoList(ct);
	}

	// JSON出力 (F6)
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoOutputJson(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		if (ListData == null) return;

		var outstr = JsonConvert.SerializeObject(ListData, Formatting.Indented);
		var dialog = new Microsoft.Win32.SaveFileDialog {
			FileName = Tabletype.Name + DateTime.Now.ToDtStrDate2(), // Default file name
			DefaultExt = ".json", // Default file extension
			Filter = "Text documents (.json)|*.json", // Filter files by extension
			DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
		};

		if (dialog.ShowDialog(ClientLib.GetActiveView(this)) != true)
			return;
		ct.ThrowIfCancellationRequested();
		await File.WriteAllTextAsync(dialog.FileName, outstr, ct);
	}

	// 一覧取得 (F5)
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoList(CancellationToken ct) {
		StartTime = DateTime.Now;
		try {
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: Tabletype,
					where: null, order: "Kubun,Code"
				))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));

			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is System.Collections.IList list) {
				ListData = new ObservableCollection<MasterMeisho>(list.Cast<MasterMeisho>());
				Count = ListData.Count;
				Current = ListData.First() ?? new();
			}
			GetListTime = DateTime.Now - StartTime;
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	// 追加 (F4)
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoInsert(CancellationToken ct) {
		if (MessageEx.ShowQuestionDialog($"新規登録しますか？ {Current.Code}", 
			owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) return;

		try {
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg201_Op_Execute,
				DataType = typeof(InsertParam),
				DataMsg = Common.SerializeObject(new InsertParam(Tabletype, Common.SerializeObject(CurrentEdit)))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType) as MasterMeisho;

			if (item != null) {
				ListData.Add(item);
				Count = ListData.Count;
				Current = item;
				CurrentEdit = Common.CopyObject<MasterMeisho>(Current);

				MessageEx.ShowInformationDialog($"登録しました (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})", owner: ClientLib.GetActiveView(this));
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"登録失敗 (CD={CurrentEdit.Code}, Id={CurrentEdit.Id}): {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}
	// 削除 (F3)
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoDelete(CancellationToken ct) {
		if (Current == null) {
			MessageEx.ShowWarningDialog("削除対象を選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}

		if (MessageEx.ShowQuestionDialog($"削除しますか？ (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) return;

		try {
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg201_Op_Execute,
				DataType = typeof(DeleteParam),
				DataMsg = Common.SerializeObject(new DeleteParam(Tabletype, Common.SerializeObject(Current)))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));

			if (reply.Code != 0) {
				MessageEx.ShowErrorDialog("削除できませんでした", owner: ClientLib.GetActiveView(this));
				return;
			}
			// Currentの次のオブジェクトを取得 [Retrieve the next object of the current one]
			var currentIndex = ListData.IndexOf(Current);
			var nextIndex = currentIndex + 1 < ListData.Count ? currentIndex + 1 : 0;
			var nextItem = ListData.ElementAtOrDefault(nextIndex);
			ListData.Remove(Current);
			Current = nextItem ?? ListData.First() ?? new MasterMeisho();
			Count = ListData.Count;
			MessageEx.ShowInformationDialog($"削除しました (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})", owner: ClientLib.GetActiveView(this));
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"削除失敗 (CD={CurrentEdit.Code}, Id={CurrentEdit.Id}): {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	// 更新 (F2)
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoUpdate(CancellationToken ct) {
		if (Current == null) {
			MessageEx.ShowWarningDialog("更新対象を選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}

		if (MessageEx.ShowQuestionDialog($"更新しますか？ (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) return;

		try {
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			// Currentの内容をCurrentEditで更新（ID等はCurrentのものを使用）
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg201_Op_Execute,
				DataType = typeof(UpdateParam),
				DataMsg = Common.SerializeObject(new UpdateParam(Tabletype, Common.SerializeObject(CurrentEdit)))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));

			if (Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType) is MasterMeisho item) {
				Common.CopyValue(Tabletype, item, Current);
				CurrentEdit = Common.CopyObject<MasterMeisho>(Current);
				MessageEx.ShowInformationDialog($"更新しました (CD={CurrentEdit.Code}, Id={CurrentEdit.Id})", owner: ClientLib.GetActiveView(this));
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"更新失敗 (CD={CurrentEdit.Code}, Id={CurrentEdit.Id}): {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}


	[RelayCommand]
	void DoSelectKubun() {
		// 区分選択ダイアログ呼び出し等をここに実装（現状はダミー）
		var selWin = new Views.Sub.SelectWinView();
		var vm = selWin.DataContext as Sub.SelectWinViewModel;
		if (vm == null) return;
		vm.SetParam(typeof(MasterMeisho), "Kubun='IDX'", "Code");
		if (ClientLib.ShowDialogView(selWin, this) != true) return;
		var meisho = vm.Current as MasterMeisho;
		CurrentEdit.Kubun = meisho?.Code ?? CurrentEdit.Kubun;
		CurrentEdit.KubunName = meisho?.Name ?? CurrentEdit.KubunName;
	}
}