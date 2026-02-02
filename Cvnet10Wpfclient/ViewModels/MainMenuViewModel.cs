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
using System.Windows;

namespace Cvnet10Wpfclient.ViewModels {
    public partial class MainMenuViewModel: ObservableObject {

		[ObservableProperty]
		string testConnectButtonText = "TestConnect";
 
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

		[RelayCommand]
		public async Task TestConnect() {
			TestConnectButtonText = "接続中...";
			try {
				using var httpClient = new HttpClient();
				httpClient.DefaultRequestHeaders.TryAddWithoutValidation("grpc-accept-encoding", "gzip");
				httpClient.Timeout = TimeSpan.FromSeconds(30);
				using var channel = GrpcChannel.ForAddress(AppCurrent.Url, new GrpcChannelOptions { HttpClient = httpClient });
				// TestConnectButtonText = await TestQueryMsgAsync(channel);
				//TestConnectButtonText = await TestQueryMsg(channel);
				TestConnectButtonText = await Test202601Msg2(channel);
			}
			catch (RpcException rpcEx) {
				TestConnectButtonText = $"gRPC: {rpcEx.Status.Detail}";
			}
			catch (HttpRequestException httpEx) {
				TestConnectButtonText = $"HTTP ERR: {httpEx.Message}";
			}
			catch (Exception ex) {
				TestConnectButtonText = $"ERR: {ex.Message}";
			}
		}
		[RelayCommand]
		public async Task TestMsgCnv(){
			TestConnectButtonText = "接続中...";
			try {
				using var httpClient = new HttpClient();
				httpClient.DefaultRequestHeaders.TryAddWithoutValidation("grpc-accept-encoding", "gzip");
				httpClient.Timeout = TimeSpan.FromSeconds(30);
				using var channel = GrpcChannel.ForAddress(AppCurrent.Url, new GrpcChannelOptions { HttpClient = httpClient });
				// TestConnectButtonText = await TestQueryMsgAsync(channel);
				//TestConnectButtonText = await TestQueryMsg(channel);
				TestConnectButtonText = await Test202601Msg3(channel);
			}
			catch (RpcException rpcEx) {
				TestConnectButtonText = $"gRPC: {rpcEx.Status.Detail}";
			}
			catch (HttpRequestException httpEx) {
				TestConnectButtonText = $"HTTP ERR: {httpEx.Message}";
			}
			catch (Exception ex) {
				TestConnectButtonText = $"ERR: {ex.Message}";
			}
		}
		[RelayCommand]
		public async Task TestMsg000() {
			TestConnectButtonText = "接続中...";
			try {
				using var httpClient = new HttpClient();
				httpClient.DefaultRequestHeaders.TryAddWithoutValidation("grpc-accept-encoding", "gzip");
				httpClient.Timeout = TimeSpan.FromSeconds(30);
				using var channel = GrpcChannel.ForAddress(AppCurrent.Url, new GrpcChannelOptions { HttpClient = httpClient });
				TestConnectButtonText = await Test202601Msg4(channel);
			}
			catch (RpcException rpcEx) {
				TestConnectButtonText = $"gRPC: {rpcEx.Status.Detail}";
			}
			catch (HttpRequestException httpEx) {
				TestConnectButtonText = $"HTTP ERR: {httpEx.Message}";
			}
			catch (Exception ex) {
				TestConnectButtonText = $"ERR: {ex.Message}";
			}
		}

		[RelayCommand]
		public async Task AddRecord() {
			// TODO: 追加処理を実装
			if (MessageEx.ShowQuestionDialog("追加しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
				// 追加処理を実行
				using var httpClient = new HttpClient();
				httpClient.DefaultRequestHeaders.TryAddWithoutValidation("grpc-accept-encoding", "gzip");
				httpClient.Timeout = TimeSpan.FromSeconds(30);
				using var channel = GrpcChannel.ForAddress(AppCurrent.Url, new GrpcChannelOptions { HttpClient = httpClient });
				var coreService = channel.CreateGrpcService<ICvnetCoreService>();
				AppCurrent.LoginJwt = dummyToken;
				var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg102_GetDbExecuteSingle };
				msg.DataType = typeof(Test202601Master);
				msg.DataMsg = Common.SerializeObject(new ParamExecute {

					ExecuteType = ParamExecuteType.Insert,
					Id = 0,
					TableType = typeof(Test202601Master),
					JsonData = Common.SerializeObject(SelectedTestMaster!)
				});
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
		public void UpdateRecord() {
			// TODO: 修正処理を実装
			if (SelectedTestMaster == null) {
				MessageEx.ShowWarningDialog("レコードが選択されていません", owner: ClientLib.GetActiveView(this));
				return;
			}
			MessageEx.ShowInformationDialog($"修正ボタンがクリックされました（未実装）\nコード: {SelectedTestMaster.Code}", owner: ClientLib.GetActiveView(this));
		}

		[RelayCommand]
		public void DeleteRecord() {
			// TODO: 削除処理を実装
			if (SelectedTestMaster == null) {
				MessageEx.ShowWarningDialog("レコードが選択されていません", owner: ClientLib.GetActiveView(this));
				return;
			}
			if (MessageEx.ShowQuestionDialog($"コード「{SelectedTestMaster.Code}」を削除しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
				MessageEx.ShowInformationDialog("削除処理を実行します（未実装）", owner: ClientLib.GetActiveView(this));
			}
		}
	}
}
