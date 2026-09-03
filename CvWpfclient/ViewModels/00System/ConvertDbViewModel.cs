using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections.ObjectModel;
using System.Windows;

namespace CvWpfclient.ViewModels._00System;

public partial class ConvertDbViewModel : BaseViewModel {
	[ObservableProperty]
	public partial bool IsInitDb { get; set; }

	[ObservableProperty]
	public partial bool IsRunning { get; set; }

	/// <summary>
	/// 一度でも実行を開始したか。誤って再実行することを防ぐため、実行開始後は
	/// <see cref="ExecuteCommand"/> を無効のままにする（ウィンドウを開き直すまで再実行不可）。
	/// </summary>
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
	public partial bool HasExecuted { get; set; }

	[ObservableProperty]
	public partial int ProgressValue { get; set; }

	[ObservableProperty]
	public partial ObservableCollection<string> StreamMessages { get; set; } = [];

	[RelayCommand]
	private async Task InitAsync(CancellationToken cancellationToken) {
		try {
			var connections = await CoreServiceClient.GetConnectionStatusAsync(cancellationToken);
			if (!connections.Any(c => string.Equals(c, "oracle", StringComparison.OrdinalIgnoreCase))) {
				MessageEx.ShowErrorDialog("変換に必要なOracle接続が不足してます", owner: ClientLib.GetActiveView(this));
				ClientLib.Exit(this);
			}
		}
		catch (OperationCanceledException) {
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"接続状態の確認中にエラーが発生しました。\n{ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	private bool CanExecute() => !HasExecuted;

	[RelayCommand(CanExecute = nameof(CanExecute), IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken cancellationToken) {
		if (MessageEx.ShowQuestionDialog("データベースの変換を開始しますか？", owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		if (IsRunning) {
			return;
		}
		try {
			IsRunning = true;
			// 実行を開始した時点で確定的に無効化する（キャンセル・エラーで終わっても再実行させない。
			// サーバ側で一部書き込みが進んでいる可能性があるため、再実行はウィンドウの開き直しを要求する）
			HasExecuted = true;
			ProgressValue = 0;
			StreamMessages.Clear();
			// 実行の区切りを最初に置く（サーバからの各ステップ行は、この行より上に積まれる）
			StreamMessages.Insert(0, $"===== 実行開始: 全変換 初期化={(IsInitDb ? "あり" : "なし")} ----{DateTime.Now: MM/dd HH:mm:ss.fff}");
			cancellationToken.ThrowIfCancellationRequested();

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg040_ConvertDb,
				DataType = typeof(ConvertDbParam),
				DataMsg = Common.SerializeObject(new ConvertDbParam(IsInitDb))
			};

			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
					StreamMessages.Insert(0, streamMsg.DataMsg);
				}
				ProgressValue = streamMsg.Progress;
				if (streamMsg.IsCompleted) {
					break;
				}
			}
		}
		catch (OperationCanceledException) {
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
		}
		finally {
			IsRunning = false;
		}
	}
}
