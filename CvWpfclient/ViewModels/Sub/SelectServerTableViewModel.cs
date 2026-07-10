using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;

namespace CvWpfclient.ViewModels.Sub;

public partial class SelectServerTableViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	public partial string Title { get; set; } = "サーバーテーブル選択";

	[ObservableProperty]
	public partial ObservableCollection<ServerTableCountRow> ListData { get; set; } = [];

	[ObservableProperty]
	public partial ServerTableCountRow? Current { get; set; }

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial string SelectedTableName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int SelectedRowCount { get; set; } = AppGlobal.Limit;

	partial void OnCurrentChanged(ServerTableCountRow? value) {
		SelectedTableName = value?.TableName ?? string.Empty;
	}

	[RelayCommand]
	async Task Init(CancellationToken cancellationToken) {
		await InitList(cancellationToken);
	}

	async Task InitList(CancellationToken cancellationToken) {
		try {
			ClientLib.Cursor2Wait();
			cancellationToken.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg { Code = 0, Flag = CvFlag.Msg042_GetTableList };
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(cancellationToken));

			if (reply?.Code < 0) {
				MessageEx.ShowErrorDialog($"テーブル一覧取得失敗: {reply?.Option} ({reply?.Code})", owner: ClientLib.GetActiveView(this));
				return;
			}

			if (reply?.DataMsg != null && reply?.DataType != null) {
				var tableCounts = Common.DeserializeObject<List<Tuple<string, string, long>>>(reply.DataMsg)
					?? [];

				ListData = new ObservableCollection<ServerTableCountRow>(
					tableCounts
						.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
						.Select(x => new ServerTableCountRow {
							TableName = x.Item1,
							Comment = x.Item2,
							RowCount = x.Item3
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
		finally {
			ClientLib.Cursor2Normal();
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
	public string Comment { get; init; } = string.Empty;
	public long RowCount { get; init; }
}
