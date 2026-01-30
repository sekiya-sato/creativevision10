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
				TestConnectButtonText = await TestQueryMsg(channel);
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
	}
}
