using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10AppShared;
using Cvnet10Base;
using Cvnet10Wpfclient.Util;
using Cvnet8client.Views.Sub;
using Grpc.Core;
using Grpc.Net.Client;
using ProtoBuf.Grpc.Client;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;

namespace Cvnet10Wpfclient.ViewModels {
    public partial class Test20260203ViewModel : ObservableObject {

        readonly HttpClient grpcHttpClient = CreateGrpcHttpClient();
        GrpcChannel? grpcChannel;

		string testConnectStatusText = "TestConnect";
		public string TestConnectStatusText {
			get => testConnectStatusText;
			set => SetProperty(ref testConnectStatusText, value);
		}
 
		ObservableCollection<Test202601Master> testMasters = new();

		public ObservableCollection<Test202601Master> TestMasters {
			get => testMasters;
			set => SetProperty(ref testMasters, value);
		}

		Test202601Master? selectedTestMaster;
		public Test202601Master? SelectedTestMaster {
			get => selectedTestMaster;
			set => SetProperty(ref selectedTestMaster, value);
		}

		[RelayCommand]
		public void Exit() {
			if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
				ClientLib.Exit(this);
			}
		}

		T getService<T>() where T : class {
            AppCurrent.LoginJwt = dummyToken;
            grpcChannel ??= GrpcChannel.ForAddress(
                AppCurrent.Url,
                new GrpcChannelOptions { HttpClient = grpcHttpClient });

            var coreService = grpcChannel.CreateGrpcService<T>();
            if (coreService == null) {
				TestConnectStatusText = "サービス取得失敗";
                Debug.WriteLine("サービス取得失敗");
                // 未実装
                throw new NotImplementedException();
            }
            return coreService;
        }

        static HttpClient CreateGrpcHttpClient() {
            var client = new HttpClient {
                Timeout = TimeSpan.FromSeconds(300)
            };
            client.DefaultRequestHeaders.TryAddWithoutValidation("grpc-accept-encoding", "gzip");
            return client;
        }


		[RelayCommand]
		public async Task TestConnect() {
			TestConnectStatusText = "接続中...リスト取得";
			try {

				var coreService = getService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg101_Op_Query,
					DataType = typeof(QueryListParam),
					DataMsg = Common.SerializeObject(new QueryListParam(
						itemType: typeof(Test202601Master),
						where: "Id between @0 and @1", order: "Code asc",
						parameters: ["1" ,"9999" ]
				))};
				var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext());
				var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType);
				var list0 = list as IList<Test202601Master>;
				if (list0 != null) {
					TestMasters = new ObservableCollection<Test202601Master>(list0);
					SelectedTestMaster = TestMasters.FirstOrDefault();
				}
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
		[RelayCommand]
		public async Task TestMsgCnv(){
			TestConnectStatusText = "接続中...変換実行";
			try {
				var coreService = getService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 202601, Flag = CvnetFlag.MSg041_ConvertDbInit };
				var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext());
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
		[RelayCommand]
		public async Task TestMsg000() {
			TestConnectStatusText = "接続中...環境変数";
			try {
				var coreService = getService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg003_GetEnv };
				var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext());
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

		[RelayCommand]
		public async Task AddRecord() {
			// TODO: 追加処理を実装
			if (SelectedTestMaster == null) {
				MessageEx.ShowWarningDialog("レコードが選択されていません", owner: ClientLib.GetActiveView(this));
				return;
			}
			if (MessageEx.ShowQuestionDialog("追加しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
				// 追加処理を実行
				var coreService = getService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg201_Op_Execute };
				msg.DataType = typeof(InsertParam);
				msg.DataMsg = Common.SerializeObject(new InsertParam(typeof(Test202601Master), Common.SerializeObject(SelectedTestMaster!)));
				var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext());
				var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType);
				if(item != null) {
					var item0 = item as Test202601Master;
					TestMasters.Add(item0!);
					SelectedTestMaster = item0;
				}
			}
		}

		[RelayCommand]
		public async Task UpdateRecord() {
			// TODO: 修正処理を実装
			if (SelectedTestMaster == null) {
				MessageEx.ShowWarningDialog("レコードが選択されていません", owner: ClientLib.GetActiveView(this));
				return;
			}
			if (MessageEx.ShowQuestionDialog($"コード「{SelectedTestMaster.Code}」を修正しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
				// 処理を実行
				var coreService = getService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg201_Op_Execute };
				msg.DataType = typeof(UpdateParam);
				msg.DataMsg = Common.SerializeObject(new UpdateParam(typeof(Test202601Master), Common.SerializeObject(SelectedTestMaster!)));
				var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext());
				var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType);
				// 修正の場合、特に不要
				if (item != null) {
					MessageEx.ShowInformationDialog($"修正しました\nコード: {SelectedTestMaster.Code}", owner: ClientLib.GetActiveView(this));
				}
			}
		}

		[RelayCommand]
		public async Task DeleteRecord() {
			// TODO: 削除処理を実装
			if (SelectedTestMaster == null) {
				MessageEx.ShowWarningDialog("レコードが選択されていません", owner: ClientLib.GetActiveView(this));
				return;
			}
			if (MessageEx.ShowQuestionDialog($"コード「{SelectedTestMaster.Code}」を削除しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
				// 処理を実行
				var coreService = getService<ICvnetCoreService>();
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg201_Op_Execute };
				msg.DataType = typeof(DeleteParam);
				msg.DataMsg = Common.SerializeObject(new DeleteParam(typeof(Test202601Master), Common.SerializeObject(SelectedTestMaster!)));
				var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext());
				var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType);
				if (item != null) {
					TestMasters.Remove(SelectedTestMaster);
					MessageEx.ShowInformationDialog($"削除しました\nコード: {SelectedTestMaster.Code}", owner: ClientLib.GetActiveView(this));
				}
			}
		}
		[RelayCommand]
		public async Task XxxRecord() {
			if (SelectedTestMaster == null) {
				MessageEx.ShowWarningDialog("レコードが選択されていません", owner: ClientLib.GetActiveView(this));
				return;
			}
			// 処理を実行
			var coreService = getService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg101_Op_Query };
			msg.DataType = typeof(QuerybyIdParam);
			msg.DataMsg = Common.SerializeObject(new QuerybyIdParam(typeof(Test202601Master), 4));
			var reply = await coreService.QueryMsgAsync(msg, AppCurrent.GetDefaultCallContext());
			var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType);
			if (item != null) {
			}
		}
	}
}
