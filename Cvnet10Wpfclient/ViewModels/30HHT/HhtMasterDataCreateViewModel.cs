using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.Helpers;
using Grpc.Core;
using System.IO;
using System.Text;

namespace Cvnet10Wpfclient.ViewModels._30HHT;

public partial class HhtMasterDataCreateViewModel : Helpers.BaseViewModel {
	private const string DefaultOutputPath = @"C:\hht\hksnds1";

	[ObservableProperty]
	private bool isCsvFormat = true;

	[ObservableProperty]
	private bool isFixedLengthFormat;

	[ObservableProperty]
	private string outputPath = string.Empty;

	[RelayCommand]
	private void Init() {
		OutputPath = DefaultOutputPath;
		IsCsvFormat = true;
	}

	partial void OnIsCsvFormatChanged(bool value) {
		if (value && IsFixedLengthFormat) {
			IsFixedLengthFormat = false;
		}
	}

	partial void OnIsFixedLengthFormatChanged(bool value) {
		if (value && IsCsvFormat) {
			IsCsvFormat = false;
		}

		if (!value && !IsCsvFormat) {
			IsCsvFormat = true;
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task CreateDataAsync(CancellationToken ct) {
		if (string.IsNullOrWhiteSpace(OutputPath)) {
			MessageEx.ShowErrorDialog("出力先を入力してください。", owner: ClientLib.GetActiveView(this));
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			ct.ThrowIfCancellationRequested();

			var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
			var msg = new CvnetMsg {
				Code = 0,
				Flag = CvnetFlag.Msg300_Op_OutData,
				DataMsg = Common.SerializeObject(new OutDataHhtMasterParam(IsFixedLengthFormat, 0)),
				DataType = typeof(OutDataHhtMasterParam)
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0) {
				var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
				MessageEx.ShowErrorDialog($"HHTマスタデータ作成エラー: {detail} ({reply.Code})", owner: ClientLib.GetActiveView(this));
				return;
			}

			var outputLines = Common.DeserializeObject<List<string>>(reply.DataMsg) ?? [];
			var fullPath = Path.GetFullPath(OutputPath);
			var directoryPath = Path.GetDirectoryName(fullPath);
			if (!string.IsNullOrWhiteSpace(directoryPath)) {
				Directory.CreateDirectory(directoryPath);
			}

			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			var encoding = Encoding.GetEncoding("shift_jis");
			await File.WriteAllLinesAsync(fullPath, outputLines, encoding, ct);

			MessageEx.ShowInformationDialog($"完了しました({fullPath})", owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"出力に失敗しました: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}
}
