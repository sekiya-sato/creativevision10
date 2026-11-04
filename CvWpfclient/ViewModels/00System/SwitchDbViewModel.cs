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

/// <summary>
/// DB切替（データ移行）。サーバの接続定義から移動元・移動先を選び、全テーブルを複写する。
/// メインDBの切替そのもの（appsettings の Database:Provider 変更とサーバ再起動）は手作業で行う。
/// </summary>
public partial class SwitchDbViewModel : BaseViewModel {
	const string DefaultFrom = "sqlite";
	const string DefaultTo = "mariadb";

	/// <summary>選択できる接続定義名（Oracle は旧DB変換専用なので除く）</summary>
	[ObservableProperty]
	public partial ObservableCollection<string> ConnectionNames { get; set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
	public partial string? FromConnection { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
	public partial string? ToConnection { get; set; }

	[ObservableProperty]
	public partial bool IsRunning { get; set; }

	/// <summary>
	/// 一度でも実行を開始したか。移動先の表は作り直されるため、誤って再実行しないよう
	/// 実行開始後は <see cref="ExecuteCommand"/> を無効のままにする（ウィンドウを開き直すまで再実行不可）。
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
			ConnectionNames = [.. connections.Where(c => !string.Equals(c, "oracle", StringComparison.OrdinalIgnoreCase))];
			if (ConnectionNames.Count < 2) {
				MessageEx.ShowErrorDialog("DB切替には、サーバ設定(ConnectionStrings)に移動元と移動先の2つ以上の接続定義が必要です。", owner: ClientLib.GetActiveView(this));
				ClientLib.Exit(this);
				return;
			}
			FromConnection = FindName(DefaultFrom) ?? ConnectionNames[0];
			ToConnection = FindName(DefaultTo) ?? ConnectionNames.FirstOrDefault(c => c != FromConnection);
		}
		catch (OperationCanceledException) {
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"接続状態の確認中にエラーが発生しました。\n{ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	string? FindName(string name) =>
		ConnectionNames.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

	private bool CanExecute() =>
		!HasExecuted
		&& !string.IsNullOrEmpty(FromConnection)
		&& !string.IsNullOrEmpty(ToConnection)
		&& !string.Equals(FromConnection, ToConnection, StringComparison.OrdinalIgnoreCase);

	[RelayCommand(CanExecute = nameof(CanExecute), IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken cancellationToken) {
		if (IsRunning || string.IsNullOrEmpty(FromConnection) || string.IsNullOrEmpty(ToConnection)) {
			return;
		}
		var from = FromConnection;
		var to = ToConnection;
		if (MessageEx.ShowQuestionDialog(
			$"移動元「{from}」の全データを、移動先「{to}」へ複写します。\n\n" +
			$"移動先「{to}」の対象テーブルはすべて削除して作り直します（既存データは失われます）。\n" +
			"実行中は他の利用者が更新しないようにしてください。\n\n実行しますか？",
			owner: ClientLib.GetActiveView(this), defaultResult: MessageBoxResult.No) != MessageBoxResult.Yes) {
			return;
		}
		var hadError = false;
		var completed = false;
		try {
			IsRunning = true;
			// 実行を開始した時点で確定的に無効化する（キャンセル・エラーで終わっても再実行させない）
			HasExecuted = true;
			ProgressValue = 0;
			StreamMessages.Clear();
			StreamMessages.Insert(0, $"===== 実行開始: DB切替データ移行 {from} → {to} ----{DateTime.Now: MM/dd HH:mm:ss.fff}");
			cancellationToken.ThrowIfCancellationRequested();

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg043_SwitchDb,
				DataType = typeof(SwitchDbParam),
				DataMsg = Common.SerializeObject(new SwitchDbParam(from, to))
			};

			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
					StreamMessages.Insert(0, streamMsg.DataMsg);
				}
				hadError |= streamMsg.IsError || streamMsg.Code != 0;
				ProgressValue = streamMsg.Progress;
				if (streamMsg.IsCompleted) {
					completed = true;
					break;
				}
			}
		}
		catch (OperationCanceledException) {
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
		}
		catch (Exception ex) {
			hadError = true;
			StreamMessages.Insert(0, $"===== エラー: {ex.Message} ----{DateTime.Now: MM/dd HH:mm:ss.fff}");
			MessageEx.ShowErrorDialog($"DB切替データ移行でエラーが発生しました。\n{ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsRunning = false;
		}

		if (!completed) {
			return;
		}
		if (hadError) {
			StreamMessages.Insert(0, "===== エラーのあるテーブルがあります。メインDBは切り替えず、原因を解消してから再実行してください。");
			MessageEx.ShowErrorDialog("エラーのあるテーブルがあります。ログを確認してください。\nメインDBは切り替えないでください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		var provider = GetProviderName(to);
		StreamMessages.Insert(0, $"===== 移行完了。切替手順: サーバ appsettings.json の ConnectionStrings:{ProviderKey(provider)} に移動先の接続文字列を設定し、Database:Provider を \"{provider}\" にしてサーバを再起動してください。");
		MessageEx.ShowInformationDialog(
			$"データ移行が完了しました。\n\nメインDBを切り替えるには、サーバの appsettings.json で\n" +
			$"・ConnectionStrings:{ProviderKey(provider)} に「{to}」の接続文字列を設定\n" +
			$"・Database:Provider を \"{provider}\" に変更\nしてサーバを再起動してください。",
			owner: ClientLib.GetActiveView(this));
	}

	/// <summary>接続定義名から Database:Provider に書く値を推定する（サーバの判別規則と同じ）</summary>
	static string GetProviderName(string connectionName) {
		var key = connectionName.ToLowerInvariant();
		if (key.Contains("sqlite")) return "Sqlite";
		if (key.Contains("maria") || key.Contains("mysql")) return "MariaDb";
		return "Postgres";
	}

	/// <summary>Database:Provider ごとにサーバが読む ConnectionStrings のキー名</summary>
	static string ProviderKey(string provider) => provider switch {
		"Sqlite" => "sqlite",
		"MariaDb" => "mariadb",
		_ => "postgres",
	};
}
