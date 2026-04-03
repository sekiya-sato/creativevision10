using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.Helpers;
using System.Collections.ObjectModel;

namespace Cvnet10Wpfclient.ViewModels.Sub;

public partial class SelectServerTableViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	string title = "サーバーテーブル選択";

	[ObservableProperty]
	ObservableCollection<ServerTableCountRow> listData = [];

	[ObservableProperty]
	ServerTableCountRow? current;

	[ObservableProperty]
	int count;

	[ObservableProperty]
	string selectedTableName = string.Empty;

	partial void OnCurrentChanged(ServerTableCountRow? value) {
		SelectedTableName = value?.TableName ?? string.Empty;
	}

	[RelayCommand]
	async Task Init(CancellationToken cancellationToken) {
		await InitList(cancellationToken);
	}

	async Task InitList(CancellationToken cancellationToken) {
		try {
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
			var msg = new CvnetMsg { Code = 0, Flag = CvnetFlag.Msg042_GetTableCounts };
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));

			if (reply?.Code < 0) {
				MessageEx.ShowErrorDialog($"テーブル一覧取得失敗: {reply?.Option} ({reply?.Code})", owner: ClientLib.GetActiveView(this));
				return;
			}

			if (reply?.DataMsg != null && reply?.DataType != null) {
				var tableCounts = Common.DeserializeObject<List<Tuple<string, long>>>(reply.DataMsg)
					?? [];

				ListData = new ObservableCollection<ServerTableCountRow>(
					tableCounts
						.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
						.Select(x => new ServerTableCountRow {
							TableName = x.Item1,
							RowCount = x.Item2
						}));

				Count = ListData.Count;
				Current = ListData.FirstOrDefault();
			}
		}
		catch (OperationCanceledException) {
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"テーブル一覧取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	[RelayCommand]
	void DoSelect() {
		if (Current == null) {
			MessageEx.ShowWarningDialog(message: "選択されていません", owner: ClientLib.GetActiveView(this));
			return;
		}

		SelectedTableName = Current.TableName;
		ClientLib.ExitDialogResult(this, true);
	}
}

public sealed class ServerTableCountRow {
	public string TableName { get; init; } = string.Empty;
	public long RowCount { get; init; }
}
