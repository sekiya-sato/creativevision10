using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base; // MasterMeisho クラスに必要
using Cvnet10Wpfclient.Util;
using System.Collections.ObjectModel;
using System.Windows;

namespace Cvnet10Wpfclient.ViewModels;

public partial class MasterMeishoMenteViewModel : ObservableObject {
	[ObservableProperty]
	string title = "名称マスター保守";

	[ObservableProperty]
	ObservableCollection<MasterMeisho> listData = [];

	[ObservableProperty]
	MasterMeisho? current;

	[ObservableProperty]
	MasterMeisho currentEdit = new();

	[ObservableProperty]
	int count;

	[ObservableProperty]
	string? desc0;

	// 初期化
	[RelayCommand]
	async Task Init(CancellationToken ct) {
		await DoList(ct);
	}

	[RelayCommand]
	void Exit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			ClientLib.Exit(this);
		}
	}

	// 検索/一覧取得 (F5)
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoList(CancellationToken ct) {
		try {
			var coreService = AppCurrent.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(MasterMeisho),
					where: null, order: "Kubun,Code"
				))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext(ct));
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;

			if (list != null) {
				ListData = new ObservableCollection<MasterMeisho>(list.Cast<MasterMeisho>());
				Count = ListData.Count;
				Current = ListData.FirstOrDefault();
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	// 追加 (F4)
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoInsert(CancellationToken ct) {
		if (MessageEx.ShowQuestionDialog("新規登録しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) return;

		try {
			var coreService = AppCurrent.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg201_Op_Execute,
				DataType = typeof(InsertParam),
				DataMsg = Common.SerializeObject(new InsertParam(typeof(MasterMeisho), Common.SerializeObject(CurrentEdit)))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext(ct));
			var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType) as MasterMeisho;

			if (item != null) {
				ListData.Add(item);
				Count = ListData.Count;
				Current = item;
				MessageEx.ShowInformationDialog("登録しました", owner: ClientLib.GetActiveView(this));
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"登録失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	// 更新 (F2)
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoUpdate(CancellationToken ct) {
		if (Current == null) {
			MessageEx.ShowWarningDialog("更新対象を選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}

		if (MessageEx.ShowQuestionDialog("更新しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) return;

		try {
			var coreService = AppCurrent.GetgRPCService<ICvnetCoreService>();
			// Currentの内容をCurrentEditで更新（ID等はCurrentのものを使用）
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg201_Op_Execute,
				DataType = typeof(UpdateParam),
				DataMsg = Common.SerializeObject(new UpdateParam(typeof(MasterMeisho), Common.SerializeObject(CurrentEdit)))
			};

			var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext(ct));
			var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType) as MasterMeisho;

			if (item != null) {
				//Current.CopyFrom(item); // サーバーから戻った値でリスト内のオブジェクトを更新
				MessageEx.ShowInformationDialog("更新しました", owner: ClientLib.GetActiveView(this));
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"更新失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	// 削除 (F3)
	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoDelete(CancellationToken ct) {
		if (Current == null) {
			MessageEx.ShowWarningDialog("削除対象を選択してください", owner: ClientLib.GetActiveView(this));
			return;
		}

		if (MessageEx.ShowQuestionDialog($"コード「{Current.Code}」を削除しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) return;

		try {
			var coreService = AppCurrent.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg201_Op_Execute,
				DataType = typeof(DeleteParam),
				DataMsg = Common.SerializeObject(new DeleteParam(typeof(MasterMeisho), Common.SerializeObject(Current)))
			};

			await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext(ct));

			ListData.Remove(Current);
			Count = ListData.Count;
			Current = ListData.FirstOrDefault();
			MessageEx.ShowInformationDialog("削除しました", owner: ClientLib.GetActiveView(this));
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"削除失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand]
	void DoSelKubun() {
		// 区分選択ダイアログ呼び出し等をここに実装（現状はダミー）
		CurrentEdit.Kubun = "100";
	}

	partial void OnCurrentChanged(MasterMeisho? value) {
		if (value != null) {
			// クローンを作成して編集用プロパティにセット
			//CurrentEdit = Common.DeserializeObject<MasterMeisho>(Common.SerializeObject(value)) ?? new();
		}
		else {
			CurrentEdit = new();
		}
	}
}