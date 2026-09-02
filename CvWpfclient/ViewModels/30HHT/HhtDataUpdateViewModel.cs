using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Grpc.Core;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels._30HHT;

/// <summary>
/// HHTデータ更新。<see cref="TranVulcanHht"/> を Tran系各テーブルへ展開する。
/// <para>
/// 仕様は `Doc/spec/archive/2026-08-24_HHTデータ更新詳細設計.md` を参照する。
/// 変換本体はサーバ側(<c>CvDomainLogic/HhtProcessUpdate.cs</c>)にあり、
/// ここは条件の入力と <c>Msg058_HhtDataUpdate</c> の進捗表示だけを持つ。
/// </para>
/// </summary>
public partial class HhtDataUpdateViewModel : Helpers.BaseViewModel {
	/// <summary>区分の絞り込み選択肢。表示名 → 対象 <see cref="TranVulcanHht.Type0"/></summary>
	public sealed record TypeFilterOption(string Name, int[] Types);

	public IReadOnlyList<TypeFilterOption> TypeFilters { get; } = [
		new("全て", []),
		new("売上・返品", [1, 2, 9, 10]),
		new("仕入・発注", [5, 6, 8]),
		new("移動・入出庫", [3, 4, 11]),
		new("棚卸", [7]),
	];

	[ObservableProperty]
	public partial string DateFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DateTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial TypeFilterOption? TypeFilter { get; set; }

	/// <summary>エラーデータも対象にするか。OFF なら ErrorMsg が空の行だけを変換する</summary>
	[ObservableProperty]
	public partial bool RetryError { get; set; } = true;

	[ObservableProperty]
	public partial int UnconvertedCount { get; set; }

	[ObservableProperty]
	public partial int ErrorCount { get; set; }

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "対象日付を yyyy/MM/dd 形式で入力し、更新を押してください。";

	[ObservableProperty]
	public partial bool IsProcessing { get; set; }

	[ObservableProperty]
	public partial int ProgressValue { get; set; }

	public string TargetSummary => $"未変換 {UnconvertedCount:N0} 件（うちエラー {ErrorCount:N0} 件）";

	partial void OnUnconvertedCountChanged(int value) => OnPropertyChanged(nameof(TargetSummary));
	partial void OnErrorCountChanged(int value) => OnPropertyChanged(nameof(TargetSummary));

	[RelayCommand]
	private async Task InitAsync(CancellationToken ct) {
		// 既定は当月1日～当日（決定 12-J）。全件を既定にすると誤操作時の影響が大きい
		var today = DateTime.Now;
		DateFrom = new DateTime(today.Year, today.Month, 1).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		DateTo = today.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
		TypeFilter = TypeFilters[0];
		await RefreshCountAsync(ct);
	}

	[RelayCommand]
	private async Task RefreshCountAsync(CancellationToken ct) {
		try {
			var (rows, error) = await QueryCountAsync(ct);
			UnconvertedCount = rows;
			ErrorCount = error;
		}
		catch (OperationCanceledException) {
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			return;
		}
		catch (Exception ex) {
			StatusMessage = $"対象件数の取得に失敗しました: {ex.Message}";
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ExecuteAsync(CancellationToken ct) {
		if (!TryBuildParameter(out var param, out var validationError)) {
			StatusMessage = validationError;
			MessageEx.ShowWarningDialog(validationError, owner: ClientLib.GetActiveView(this));
			return;
		}

		try {
			await RefreshCountAsync(ct);
			if (UnconvertedCount == 0) {
				StatusMessage = "対象データがありません。";
				MessageEx.ShowWarningDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
				return;
			}
			var confirm = $"{UnconvertedCount:N0}件を更新します。よろしいですか？";
			if (MessageEx.ShowQuestionDialog(confirm, owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
				return;
			}

			IsProcessing = true;
			ProgressValue = 0;
			StatusMessage = "HHTデータを更新しています...";
			ClientLib.Cursor2Wait();

			var message = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg058_HhtDataUpdate,
				DataType = typeof(HhtUpdateParameter),
				DataMsg = Common.SerializeObject(param),
			};
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(message, AppGlobal.GetDefaultCallContext(ct))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg)) {
					StatusMessage = streamMsg.DataMsg;
				}
				ProgressValue = Math.Clamp(streamMsg.Progress, 0, 100);
				if (streamMsg.IsError) {
					throw new InvalidOperationException(streamMsg.DataMsg);
				}
				if (streamMsg.IsCompleted) {
					break;
				}
			}
			ProgressValue = 100;

			// 内訳は変換後の TranVulcanHht を数え直して表示する（ストリームは件数を1つしか返さない）
			var beforeUnconverted = UnconvertedCount;
			await RefreshCountAsync(CancellationToken.None);
			var success = beforeUnconverted - UnconvertedCount;
			StatusMessage = $"更新 {success:N0}件 / 未変換 {UnconvertedCount:N0}件（うちエラー {ErrorCount:N0}件）";
			if (ErrorCount > 0) {
				StatusMessage += "\nエラーデータは『HHTエラーデータ修正入力』で確認してください。";
			}
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

	/// <summary>画面の入力から <see cref="HhtUpdateParameter"/> を組み立てる</summary>
	private bool TryBuildParameter(out HhtUpdateParameter param, out string error) {
		param = new HhtUpdateParameter(string.Empty, string.Empty, [], RetryError, []);
		error = string.Empty;

		if (!TryParseDate(DateFrom, out var from)) {
			error = $"開始日付の形式が不正です: {DateFrom}";
			return false;
		}
		if (!TryParseDate(DateTo, out var to)) {
			error = $"終了日付の形式が不正です: {DateTo}";
			return false;
		}
		if (from.Length > 0 && to.Length > 0 && string.Compare(from, to, StringComparison.Ordinal) > 0) {
			error = "開始日付は終了日付以前にしてください。";
			return false;
		}
		param = new HhtUpdateParameter(from, to, TypeFilter?.Types ?? [], RetryError, []);
		return true;
	}

	/// <summary>対象件数を数える。エラー件数は ErrorMsg が入っている行数</summary>
	private async Task<(int Rows, int Errors)> QueryCountAsync(CancellationToken ct) {
		if (!TryBuildParameter(out var param, out _)) {
			return (0, 0);
		}
		var conditions = new List<string> { "VdCnvDate = 0" };
		var parameters = new List<string>();
		if (param.DateFrom.Length > 0) {
			conditions.Add($"DenDay >= @{parameters.Count}");
			parameters.Add(param.DateFrom);
		}
		if (param.DateTo.Length > 0) {
			conditions.Add($"DenDay <= @{parameters.Count}");
			parameters.Add(param.DateTo);
		}
		if (param.Types.Length > 0) {
			conditions.Add($"Type0 in ({string.Join(",", param.Types)})");
		}
		if (!param.RetryError) {
			conditions.Add("ErrorMsg = ''");
		}
		var sql = $@"
select count(*) as TargetRows, ifnull(sum(case when ErrorMsg <> '' then 1 else 0 end), 0) as ErrorRows
from {nameof(TranVulcanHht)}
where {string.Join(" and ", conditions)}";
		var rows = await CoreServiceClient.QuerySqlListAsync<HhtTargetCountRow>(sql, parameters, ct);
		var first = rows.FirstOrDefault();
		return (first?.TargetRows ?? 0, first?.ErrorRows ?? 0);
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
