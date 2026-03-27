using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Wpfclient.Helpers;
using Grpc.Core;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.ViewModels;

public partial class SampleViewModel : Helpers.BaseViewModel {
	/*
	[ObservableProperty]
	private string? orgTest01;

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		await DoSomeList(ct);
	}
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task DoSomeTask(CancellationToken cancellationToken) {
	}
	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ClientLib.GetActiveView(this)) == MessageBoxResult.Yes) {
			ExitWithResultFalse();
		}
	}
	*/
	/// <summary>
	/// カラーバランスの見本
	/// </summary>
	[ObservableProperty]
	private ObservableCollection<ColorBalanceItem> colorItems = new()
	{
		new("#212025", "#E74545", "#742424","#FFFFFF"),
		new("#4B3A42", "#F38884", "#A45055","#FFFFFF"),
		new("#180A14", "#91242C", "#501919","#"),
		new("#FFFBF2", "#876931", "#E7DECB","#"),
		new("#FFF4E1", "#C6A260", "#F0E1CC","#"),
		new("#72563B", "#392F28", "#866E5A","#"),
		new("#65D0C4", "#FF4F84", "#B4EC38","#"),
		new("#FFCA3E", "#FF8D89", "#C9E9E5","#"),
		new("#C8F0EB", "#F2E073", "#FF90BE","#"),
		new("#46938A", "#BA8C27", "#B7255A","#"),
		new("#AF949B", "#E95774", "#9C6372","#FFFFFF"),
		new("#EEE9E4", "#D75A00", "#FAD61B","#"),
		new("#F8B97E", "#FBFBF8", "#C5BEB6","#"),
		new("#58C6F1", "#EBD6BC", "#F2B134","#227190"),
		new("#FFE4E4", "#E22B26", "#FFCBBD","#EF593C"),
		new("#F1F2EF", "#EFEC49", "#33933A","#D2D6C1"),
		new("#DCF0F8", "#2A61B3", "#FFFFFF","#B7E0E4"),
		new("#131A34", "#2F2F2F", "#F5F5F5","#ADFF00"),
		new("#F2F2F2", "#95A4B7", "#5B5F6A","#FFFFFF"),
		new("#FCE07E", "#323232", "#D4F170","#F4E5B3"),
		new("#DEF1EF", "#BAE6DF", "#FFDED5","#EBA295"),
		new("#FDFEE1", "#C9E3B4", "#F7C8B1","#EEEE89"),
		new("#D3E4F4", "#424F7A", "#DCD4E6","#E9DCED"),
		new("#019AA7", "#F5BBB9", "#F5D235","#"),
		new("#5449EA", "#FE4A87", "#FE4137","#"),
		new("#E5C1CC", "#FC6B91", "#BC8496",""),
		new("#725661", "#93344A", "#4C1E2D",""),
		new("#", "#", "#","#"),
	};

	#region ストリーミングテスト
	[ObservableProperty]
	string streamStatusText = "TestStream";

	[ObservableProperty]
	ObservableCollection<string> streamMessages = [];

	[ObservableProperty]
	int progressValue;

	[ObservableProperty]
	bool isProgressVisible = false;

	/// <summary>
	/// gRPCストリーミングのテスト
	/// </summary>
	/// <param name="cancellationToken"></param>
	/// <returns></returns>

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task Streaming(CancellationToken cancellationToken) {
		try {
			ProgressValue = 0;
			IsProgressVisible = true;
			StreamMessages.Clear();
			cancellationToken.ThrowIfCancellationRequested();
			// 処理を実行
			var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.MSg060_StreamingTest };
			msg.DataType = typeof(string);
			msg.DataMsg = "ストリーミングテスト";
			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				StreamMessages.Insert(0, streamMsg.DataMsg);
				ProgressValue = streamMsg.Progress;
				if (streamMsg.IsCompleted) break;
			}
			IsProgressVisible = false;
		}
		catch (OperationCanceledException) {
			IsProgressVisible = false;
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			IsProgressVisible = false;
			return;
		}
	}
	#endregion

	#region テストメッセージ 001, 002, 003
	[ObservableProperty]
	string testMsg001Text = $"テストメッセージ {DateTime.Now}";
	[ObservableProperty]
	string testMsg001Result = string.Empty;

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestMsg001(CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
		var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg001_CopyReply };
		msg.DataType = typeof(string);
		msg.DataMsg = TestMsg001Text;
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		TestMsg001Result = reply.DataMsg;
	}

	[ObservableProperty]
	string testMsg002Result = string.Empty;

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestMsg002(CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
		var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg002_GetVersion };
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		if (reply?.DataMsg != null && reply?.DataType != null) {
			var versionInfo = Common.DeserializeObject<Cvnet10Base.Share.VersionInfo>(reply.DataMsg);
			TestMsg002Result = $"{versionInfo?.Product}-{versionInfo?.BuildDate} Ver.{versionInfo?.Version} Base:{versionInfo?.BaseDir}";
		}
	}
	#endregion

	#region Convertテスト、Printテスト
	[ObservableProperty]
	ObservableCollection<EnvDisplayItem> testMsg003Items = [];

	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestMsg003(CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
		var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg003_GetEnv };
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		if (reply?.DataMsg != null && reply?.DataType != null) {
			var envItems = Common.DeserializeObject<Dictionary<string, string>>(reply.DataMsg)
				?? new Dictionary<string, string>();
			TestMsg003Items = [.. envItems
				.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
				.Select(x => new EnvDisplayItem(x.Key, x.Value))];
		}
	}
	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestConvert(CancellationToken cancellationToken) {
		try {
			ProgressValue = 0;
			IsProgressVisible = true;
			StreamMessages.Clear();
			cancellationToken.ThrowIfCancellationRequested();
			// 処理を実行
			var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.MSg040_ConvertDb };
			msg.DataType = typeof(string);
			msg.DataMsg = "コンバートストリーミング DBConvert";
			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				StreamMessages.Insert(0, streamMsg.DataMsg);
				ProgressValue = streamMsg.Progress;
				if (streamMsg.IsCompleted) break;
			}
			IsProgressVisible = false;
		}
		catch (OperationCanceledException) {
			IsProgressVisible = false;
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			IsProgressVisible = false;
			return;
		}
	}
	[RelayCommand(IncludeCancelCommand = true)]
	public async Task TestPrint(CancellationToken cancellationToken) {
		try {
			ClientLib.Cursor2Wait();
			ProgressValue = 0;
			IsProgressVisible = true;
			StreamMessages.Clear();
			cancellationToken.ThrowIfCancellationRequested();
			// 処理を実行
			var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
			var msg = new PrintOperation {
				DataType = typeof(string),
				DataMsg = "コンバートストリーミング Printのテスト",
			};
			var pdfdata = "";
			await foreach (var streamMsg in coreService.PrintPdfAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				StreamMessages.Insert(0, streamMsg.DataMsg);
				ProgressValue = streamMsg.Progress;
				if (streamMsg.IsCompleted) {
					pdfdata = streamMsg.DataMsg;
					break;
				}
			}
			IsProgressVisible = false;
			var view = new Views.Sub.WebpdfView { Title = "Print Test - PDF表示" };
			var vm = view.DataContext as Sub.WebpdfViewModel;
			if (vm != null) {
				var url = $"{AppGlobal.Url}/wrk/{pdfdata}";
				vm.Pdfdata = url;
				ClientLib.ShowDialogView(view, this, IsDialog: false);
				view.Owner = null;
			}

		}
		catch (OperationCanceledException) {
			IsProgressVisible = false;
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			IsProgressVisible = false;
			return;
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	#endregion
}


/// <summary>
/// カラーバランス確認用アイテムモデル
/// </summary>
public record ColorBalanceItem(string Color1, string Color2, string Color3, string Color4);

public record EnvDisplayItem(string Key, string Value);
