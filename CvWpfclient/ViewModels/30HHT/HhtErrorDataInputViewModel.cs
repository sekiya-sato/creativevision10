using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._30HHT;

/// <summary>
/// HHTエラーデータ修正入力。HHTデータ更新で変換できなかった <see cref="TranVulcanHht"/> を確認・修正する。
/// <para>
/// 仕様は `Doc/spec/archive/2026-08-24_HHTデータ更新詳細設計.md` の 9章を参照する。
/// <see cref="TranVulcanHht"/> は副作用を持たないため <c>PartialUpdateParam</c> ではなく
/// <c>UpdateParam</c>（行全体・楽観排他あり）で保存する
/// （<c>WriteEffectRunner.PartialUpdateDeniedColumns</c> に DenDay / Su 等が含まれ部分更新できない）。
/// </para>
/// </summary>
public partial class HhtErrorDataInputViewModel : Helpers.BaseViewModel {
	/// <summary>区分の表示用。VULCANの Type0 と名称の対応</summary>
	public sealed record TypeOption(int Value, string Name) {
		public override string ToString() => $"{Value}:{Name}";
	}

	public IReadOnlyList<TypeOption> TypeOptions { get; } = [
		new(1, "売上"), new(2, "返品"), new(3, "入庫"), new(4, "出庫"),
		new(5, "仕入"), new(6, "仕入返品"), new(7, "棚卸"), new(8, "発注"),
		new(9, "卸売"), new(10, "卸返品"), new(11, "移動"), new(12, "客数"),
	];

	/// <summary>販売区分の表示用</summary>
	public IReadOnlyList<TypeOption> HanKubunOptions { get; } = [
		new(0, "プロパー/買取"), new(1, "セール/委託"), new(2, "社販"), new(9, "未使用"),
	];

	[ObservableProperty]
	public partial ObservableCollection<TranVulcanHht> ListData { get; set; } = [];

	[ObservableProperty]
	public partial TranVulcanHht? SelectedItem { get; set; }

	[ObservableProperty]
	public partial string DateFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DateTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string FileNameFilter { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "エラーデータを修正し、保存してから更新実行を押してください。";

	[ObservableProperty]
	public partial bool IsProcessing { get; set; }

	public string ListSummary => $"エラーデータ {ListData.Count:N0} 件";

	[RelayCommand]
	private async Task InitAsync(CancellationToken ct) {
		await ReloadAsync(ct);
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ReloadAsync(CancellationToken ct) {
		try {
			ClientLib.Cursor2Wait();
			var conditions = new List<string> { "VdCnvDate = 0", "ErrorMsg <> ''" };
			var parameters = new List<string>();
			if (TryParseDate(DateFrom, out var from) && from.Length > 0) {
				conditions.Add($"DenDay >= @{parameters.Count}");
				parameters.Add(from);
			}
			if (TryParseDate(DateTo, out var to) && to.Length > 0) {
				conditions.Add($"DenDay <= @{parameters.Count}");
				parameters.Add(to);
			}
			if (!string.IsNullOrWhiteSpace(FileNameFilter)) {
				conditions.Add($"BackupFileName like @{parameters.Count}");
				parameters.Add($"%{FileNameFilter.Trim()}%");
			}
			var sql = $@"
select * from {nameof(TranVulcanHht)}
where {string.Join(" and ", conditions)}
order by BackupFileName, HhtNo, Serial, LineNo";
			var rows = await CoreServiceClient.QuerySqlListAsync<TranVulcanHht>(sql, parameters, ct);
			ListData = new ObservableCollection<TranVulcanHht>(rows);
			OnPropertyChanged(nameof(ListSummary));
			StatusMessage = rows.Count == 0
				? "エラーデータはありません。"
				: $"エラーデータを {rows.Count:N0} 件読み込みました。";
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			StatusMessage = $"読み込みに失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// 変更行を保存する。<see cref="TranVulcanHht.ErrorMsg"/> はクリアしない（決定 12-I）。
	/// 次回の更新実行時にサーバ側が必ずクリアするため、ここで消すと修正済みか未修正か分からなくなる。
	/// </summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task SaveAsync(CancellationToken ct) {
		if (ListData.Count == 0) {
			return;
		}
		try {
			IsProcessing = true;
			ClientLib.Cursor2Wait();
			var saved = 0;
			foreach (var row in ListData) {
				ct.ThrowIfCancellationRequested();
				var reply = await CoreServiceClient.SendExecuteAsync(
					new UpdateParam(typeof(TranVulcanHht), Common.SerializeObject(row)), ct);
				if (reply.Code < 0) {
					var detail = string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option;
					throw new InvalidOperationException($"行{row.LineNo}の保存に失敗しました: {detail}");
				}
				saved++;
			}
			StatusMessage = $"{saved:N0}件を保存しました。";
			await ReloadAsync(CancellationToken.None);
		}
		catch (OperationCanceledException) {
			StatusMessage = "保存をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"保存に失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>選択行を削除する。誤受信データや重複受信データを破棄するために使う</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task DeleteAsync(CancellationToken ct) {
		if (SelectedItem == null) {
			MessageEx.ShowWarningDialog("削除する行を選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		var target = SelectedItem;
		var confirm = $"HTNo={target.HhtNo} Serial={target.Serial}（{target.BackupFileName} 行{target.LineNo}）を削除します。よろしいですか？";
		if (MessageEx.ShowQuestionDialog(confirm, owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			ClientLib.Cursor2Wait();
			var deleted = await CoreServiceClient.DeleteBulkAsync(typeof(TranVulcanHht), [target], "HHTデータ", ct);
			StatusMessage = $"{deleted:N0}件を削除しました。";
			await ReloadAsync(CancellationToken.None);
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (Exception ex) {
			StatusMessage = $"削除に失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>表示中のエラー行だけを対象に HHTデータ更新を実行する</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task RunUpdateAsync(CancellationToken ct) {
		if (ListData.Count == 0) {
			MessageEx.ShowWarningDialog("対象データがありません。", owner: ClientLib.GetActiveView(this));
			return;
		}
		var targetIds = ListData.Where(x => x.Id > 0).Select(x => x.Id).ToArray();
		var confirm = $"表示中の {targetIds.Length:N0}件を更新します。よろしいですか？";
		if (MessageEx.ShowQuestionDialog(confirm, owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}
		try {
			IsProcessing = true;
			ClientLib.Cursor2Wait();
			StatusMessage = "HHTデータを更新しています...";
			var message = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg058_HhtDataUpdate,
				DataType = typeof(HhtUpdateParameter),
				DataMsg = Common.SerializeObject(
					new HhtUpdateParameter(string.Empty, string.Empty, [], RetryError: true, targetIds)),
			};
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(message, AppGlobal.GetDefaultCallContext(ct))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
					StatusMessage = streamMsg.DataMsg;
				}
				if (streamMsg.IsError) {
					throw new InvalidOperationException(streamMsg.DataMsg);
				}
				if (streamMsg.IsCompleted) {
					break;
				}
			}
			var before = targetIds.Length;
			await ReloadAsync(CancellationToken.None);
			StatusMessage = $"更新 {before - ListData.Count:N0}件 / 残エラー {ListData.Count:N0}件";
		}
		catch (OperationCanceledException) {
			StatusMessage = "HHTデータ更新をキャンセルしました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = "HHTデータ更新をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"HHTデータ更新でエラーが発生しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>yyyy/MM/dd または yyyyMMdd を yyyyMMdd へ正規化する。空欄は「指定なし」として許容する</summary>
	private static bool TryParseDate(string input, out string yyyymmdd) {
		yyyymmdd = string.Empty;
		if (string.IsNullOrWhiteSpace(input)) {
			return true;
		}
		var trimmed = input.Trim().Replace("/", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
		if (trimmed.Length != 8
			|| !DateTime.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
			return false;
		}
		yyyymmdd = trimmed;
		return true;
	}
}
