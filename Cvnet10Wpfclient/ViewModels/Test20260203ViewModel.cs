using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.ViewServices;
using Grpc.Core;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;

namespace Cvnet10Wpfclient.ViewModels;
public partial class Test20260203ViewModel : Helpers.BaseViewModel {

	[ObservableProperty]
	string testConnectStatusText = "TestConnect";

	[ObservableProperty]
	ObservableCollection<Test202601Master> testMasters = new();


	[ObservableProperty]
	Test202601Master? selectedTestMaster;

	partial void OnSelectedTestMasterChanged(Test202601Master? oldValue, Test202601Master newValue) {
		if (newValue == null) {
			CurrentEdit = new();
			return;
		}
		if (oldValue?.Id != newValue.Id) {
			CurrentEdit = Common.CloneObject<Test202601Master>(SelectedTestMaster);
		}
	}


	[ObservableProperty]
	Test202601Master currentEdit = new();



	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			ExitWithResultFalse();
		}
	}


	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestConnect(CancellationToken cancellationToken) {
		TestConnectStatusText = "接続中...リスト取得";
		try {
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(Test202601Master),
					where: "Id between @0 and @1", order: "Code asc",
					parameters: ["1" ,"9999" ]
		))};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType);
			var list0 = list as IList<Test202601Master>;
			if (list0 != null) {
				TestMasters = new ObservableCollection<Test202601Master>(list0);
				SelectedTestMaster = TestMasters.FirstOrDefault();
			}
			var msg2 = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg101_Op_Query,
				DataType = typeof(QueryListParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: typeof(MasterMeisho)
				))
			};



		}
		catch (RpcException rpcEx) {
			TestConnectStatusText = $"gRPC: {rpcEx.Status.Detail}";
		}
		catch (HttpRequestException httpEx) {
			TestConnectStatusText = $"HTTP ERR: {httpEx.Message}";
		}
		catch (Exception ex) {
			TestConnectStatusText = $"ERR: {ex.Message}";
		}
	}
	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestMsgCnv(CancellationToken cancellationToken){
		TestConnectStatusText = "接続中...変換実行";
		try {
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 202601, Flag = CvnetFlag.MSg041_ConvertDbInit };
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			TestConnectStatusText = $"Convert OK";
		}
		catch (RpcException rpcEx) {
			TestConnectStatusText = $"gRPC: {rpcEx.Status.Detail}";
		}
		catch (HttpRequestException httpEx) {
			TestConnectStatusText = $"HTTP ERR: {httpEx.Message}";
		}
		catch (Exception ex) {
			TestConnectStatusText = $"ERR: {ex.Message}";
		}
	}
	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestMsg000(CancellationToken cancellationToken) {
		TestConnectStatusText = "接続中...環境変数";
		try {
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg003_GetEnv };
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType);
			TestConnectStatusText = $"GetEnv OK";
		}
		catch (RpcException rpcEx) {
			TestConnectStatusText = $"gRPC: {rpcEx.Status.Detail}";
		}
		catch (HttpRequestException httpEx) {
			TestConnectStatusText = $"HTTP ERR: {httpEx.Message}";
		}
		catch (Exception ex) {
			TestConnectStatusText = $"ERR: {ex.Message}";
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task AddRecord(CancellationToken cancellationToken) {
		// TODO: 追加処理を実装
		if (SelectedTestMaster == null) {
			MessageEx.ShowWarningDialog("レコードが選択されていません", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog("追加しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			try {
				cancellationToken.ThrowIfCancellationRequested();
				// 追加処理を実行
				var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg201_Op_Execute };
				msg.DataType = typeof(InsertParam);
				msg.DataMsg = Common.SerializeObject(new InsertParam(typeof(Test202601Master), Common.SerializeObject(CurrentEdit!)));
				var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
				var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType);
				if(item != null) {
					var item0 = item as Test202601Master;
					TestMasters.Add(item0!);
					SelectedTestMaster = item0;
				}
			}
			catch (OperationCanceledException) {
				return;
			}
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task UpdateRecord(CancellationToken cancellationToken) {
		// TODO: 修正処理を実装
		if (SelectedTestMaster == null) {
			MessageEx.ShowWarningDialog("レコードが選択されていません", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog($"コード「{SelectedTestMaster.Code}」を修正しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			try {
				cancellationToken.ThrowIfCancellationRequested();
				// 処理を実行
				var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg201_Op_Execute };
				msg.DataType = typeof(UpdateParam);
				msg.DataMsg = Common.SerializeObject(new UpdateParam(typeof(Test202601Master), Common.SerializeObject(CurrentEdit!)));
				var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
				var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType);
				// 修正の場合、特に不要
				if (item != null) {
					Common.DeepCopyValue(typeof(Test202601Master), item, SelectedTestMaster);
					CurrentEdit = Common.CloneObject<Test202601Master>(SelectedTestMaster);

					MessageEx.ShowInformationDialog($"修正しました\nコード: {SelectedTestMaster?.Code}", owner: ClientLib.GetActiveView(this));
				}
			}
			catch (OperationCanceledException) {
				return;
			}
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task DeleteRecord(CancellationToken cancellationToken) {
		// TODO: 削除処理を実装
		if (SelectedTestMaster == null) {
			MessageEx.ShowWarningDialog("レコードが選択されていません", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog($"コード「{SelectedTestMaster.Code}」を削除しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			try {
				cancellationToken.ThrowIfCancellationRequested();
				// 処理を実行
				var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg201_Op_Execute };
				msg.DataType = typeof(DeleteParam);
				msg.DataMsg = Common.SerializeObject(new DeleteParam(typeof(Test202601Master), Common.SerializeObject(CurrentEdit!)));
				var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
				var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType);
				if (item != null) {
					TestMasters.Remove(SelectedTestMaster);
					var currentIndex = TestMasters.IndexOf(SelectedTestMaster);
					var nextIndex = currentIndex + 1 < TestMasters.Count ? currentIndex + 1 : 0;
					var nextItem = TestMasters.ElementAtOrDefault(nextIndex);
					TestMasters.Remove(SelectedTestMaster);
					SelectedTestMaster = nextItem ?? TestMasters.First() ?? new Test202601Master();

					MessageEx.ShowInformationDialog($"削除しました\nコード: {SelectedTestMaster.Code}", owner: ClientLib.GetActiveView(this));
				}
			}
			catch (OperationCanceledException) {
				return;
			}
		}
	}
	[RelayCommand(IncludeCancelCommand = true)]
	public async Task XxxRecord(CancellationToken cancellationToken) {
		try {
			cancellationToken.ThrowIfCancellationRequested();
			// 処理を実行
			var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg701_TestCase001 };
			msg.DataType = typeof(QuerybyIdParam);
			msg.DataMsg = Common.SerializeObject(new QuerybyIdParam(typeof(Test202601Master), 4));
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));
			var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType);
			if (item != null) {
			}
		}
		catch (OperationCanceledException) {
			return;
		}
	}
}

