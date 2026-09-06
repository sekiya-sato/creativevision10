using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Grpc.Core;
using Microsoft.Win32;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>
/// 原価4処理（消化仕入更新・最終仕入原価更新・総平均原価更新・評価替え）の共通ViewModel
/// （原価4項目 詳細設計 §8.1、§2.4、§2.5.6、§9.4）。
/// <para>
/// <see cref="BaseBillingCalculationViewModel"/>・<see cref="BaseStocktakeViewModel"/>と同じ流儀
/// （<c>[ObservableProperty]</c>、<c>[RelayCommand(IncludeCancelCommand = true)]</c>、
/// <c>protected abstract</c>/<c>protected virtual</c>で派生へ差分を出す）で書く。
/// </para>
/// <para>
/// 「確認」→一覧表示→「更新」の二段階（§2.4-1・§8.1）、確認後の変更検知用指紋
/// <see cref="CostConfirmSnapshot"/>の保持と往復（§2.4-4）、対象月・締日基準の期間・前回実行日時・
/// 状態の表示（§2.5.6）、CSV出力（UTF-8 BOM付き、§8.1）、エラー行の絞り込み（§6.5に倣い全画面へ適用）
/// を本基底へ集約する。PDFチェックリストは対象外（§8.1「10.1初回実装の必須範囲は画面一覧とCSVまで」）。
/// </para>
/// </summary>
public abstract partial class BaseCostUpdateViewModel : BaseViewModel {
	/// <summary>
	/// 確認(プレビュー)1回ぶんの結果概要。一覧行そのものはDTO型が画面ごとに異なるため派生側が保持する。
	/// <para>
	/// <paramref name="ExcludedCount"/>は総平均原価更新のみ意味を持つ(設計書§6.5「2026-09-06改訂」)。
	/// 既定値0のため、対象外を持たない画面（消化仕入更新・最終仕入原価更新）は呼び出し側を変更しなくてよい。
	/// </para>
	/// </summary>
	protected sealed record PreviewOutcome(long TargetCount, long ErrorCount, CostConfirmSnapshot Confirmed, long ExcludedCount = 0);

	// ------------------------------------------------------------------
	// 派生側が差し込む項目
	// ------------------------------------------------------------------

	/// <summary>確認(プレビュー)用の<see cref="CvFlag"/>。<c>QueryMsgAsync</c>経由。</summary>
	protected abstract CvFlag PreviewFlag { get; }
	/// <summary>更新実行用の<see cref="CvFlag"/>。<c>QueryMsgStreamAsync</c>経由。</summary>
	protected abstract CvFlag ApplyFlag { get; }
	/// <summary>月次状態照会(<c>Msg080</c>)で突合する処理区分。</summary>
	protected abstract EnumCostProcessKind ProcessKind { get; }
	/// <summary>確認ダイアログ・完了メッセージに使う画面名（例:「消化仕入更新」）。</summary>
	protected abstract string ScreenName { get; }
	/// <summary>
	/// <see cref="CostUpdateParameter.CostMethod"/>に載せる値。原価更新（最終仕入原価更新・総平均原価更新）の
	/// 画面だけが意味を持つ（§2.3）ため、既定は<see cref="EnumCostMethod.Fixed"/>（未使用）。
	/// </summary>
	protected virtual EnumCostMethod CostMethodForApply => EnumCostMethod.Fixed;

	/// <summary>
	/// 確認(プレビュー)を実行する。DTO型・一覧行の展開・派生側の一覧コレクションへの反映は派生の責務。
	/// 戻り値の対象件数・エラー件数・指紋を基底が見出し表示と「更新」ボタンの活性化に使う（§2.4-2）。
	/// </summary>
	protected abstract Task<PreviewOutcome> RunPreviewAsync(CostUpdateParameter param, CancellationToken cancellationToken);

	/// <summary>派生側の一覧コレクションを空にする（対象月変更時・再確認前・更新成功後）。</summary>
	protected abstract void ClearRows();

	/// <summary>
	/// 現在の絞り込み設定(<see cref="ShowErrorsOnly"/>)に従って確認一覧と同じ列でCSV全文を組み立てる
	/// （ヘッダ行を含む。§8.1「CSV出力は確認一覧と同じ列」）。対象行が無い場合は空文字を返してよい。
	/// </summary>
	protected abstract string BuildCsvText();

	// ------------------------------------------------------------------
	// 画面共通の状態
	// ------------------------------------------------------------------

	[ObservableProperty]
	public partial string TargetMonth { get; set; } = DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);
	[ObservableProperty]
	public partial string PeriodText { get; set; } = "－";
	[ObservableProperty]
	public partial string LastRunAtText { get; set; } = "－";
	[ObservableProperty]
	public partial string ProcessStatusText { get; set; } = "－";
	[ObservableProperty]
	public partial long TargetCount { get; set; }
	[ObservableProperty]
	public partial long ErrorCount { get; set; }
	/// <summary>
	/// 対象外件数（設計書§6.5「2026-09-06改訂」）。総平均原価更新のみ意味を持つ。他画面は常に0のまま
	/// （既定の<see cref="PreviewOutcome.ExcludedCount"/>=0を反映するだけになるため、既存3画面は無変更で動く）。
	/// </summary>
	[ObservableProperty]
	public partial long ExcludedCount { get; set; }
	[ObservableProperty]
	public partial bool ShowErrorsOnly { get; set; }
	/// <summary>
	/// 対象外行のみ表示（設計書§6.5「2026-09-06改訂」）。<see cref="ShowErrorsOnly"/>と同じ仕組みで
	/// 派生側の<see cref="ApplyRowFilter"/>を呼び直す。対象外を持たない画面では派生側が無視してよい。
	/// </summary>
	[ObservableProperty]
	public partial bool ShowExcludedOnly { get; set; }
	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "対象月を指定し、状態更新・確認の順に実行してください。";
	[ObservableProperty]
	public partial bool IsProcessing { get; set; }
	[ObservableProperty]
	public partial int ProgressValue { get; set; }

	/// <summary>確認時点の指紋（§2.4-4）。更新実行時にそのまま返送する。<c>null</c>は「未確認」を意味する。</summary>
	protected CostConfirmSnapshot? ConfirmedSnapshot { get; private set; }

	/// <summary>「更新」を実行できるか。エラー0件かつ確認済みのときだけ許可する（§2.4-2、§8.1）。</summary>
	public bool CanUpdate => ConfirmedSnapshot is not null && ErrorCount == 0;

	partial void OnTargetMonthChanged(string value) {
		// 対象月を変更したら保持中の指紋は別の月のものになるため破棄する(§2.4-4の趣旨。別月の指紋を送らない)。
		DiscardConfirmedState();
		PeriodText = "－";
		LastRunAtText = "－";
		ProcessStatusText = "－";
		StatusMessage = "対象月を変更しました。状態更新・確認をやり直してください。";
	}

	partial void OnErrorCountChanged(long value) => UpdateCommand.NotifyCanExecuteChanged();

	/// <summary>
	/// エラー行の絞り込み（<see cref="ShowErrorsOnly"/>）が変わったら派生の一覧を再描画する
	/// （設計書§6.5に倣い、確認一覧はエラー行だけに絞り込んで表示・CSV出力できるようにする。全画面に効かせるため基底に持たせる）。
	/// </summary>
	partial void OnShowErrorsOnlyChanged(bool value) => ApplyRowFilter();

	/// <summary>
	/// 対象外行の絞り込み（<see cref="ShowExcludedOnly"/>）が変わったら派生の一覧を再描画する
	/// （設計書§6.5「2026-09-06改訂」。対象外を持たない画面は<see cref="ApplyRowFilter"/>の既定実装が
	/// 何もしないため、この変更を無視してよい）。
	/// </summary>
	partial void OnShowExcludedOnlyChanged(bool value) => ApplyRowFilter();

	/// <summary>
	/// <see cref="ShowErrorsOnly"/>の現在値に従って、派生側が保持する一覧全体から表示用コレクションを
	/// 再構築する。既定は何もしない（一覧を持たない状況はない想定だが、安全側の既定として空実装にする）。
	/// </summary>
	protected virtual void ApplyRowFilter() { }

	private void DiscardConfirmedState() {
		ConfirmedSnapshot = null;
		TargetCount = 0;
		ErrorCount = 0;
		ExcludedCount = 0;
		ClearRows();
		UpdateCommand.NotifyCanExecuteChanged();
	}

	// ------------------------------------------------------------------
	// 状態表示（対象月・締日基準の開始日終了日・前回実行日時・状態。§8.1）
	// ------------------------------------------------------------------

	/// <summary>ウィンドウ表示時に<see cref="BaseWindow"/>が自動実行する（<c>InitCommand</c>）。</summary>
	[RelayCommand]
	private async Task InitAsync(CancellationToken cancellationToken) {
		await RefreshStatusCoreAsync(cancellationToken);
	}

	/// <summary>「状態更新」ボタン用。<see cref="InitAsync"/>と同じ内容を明示的に呼び直す。</summary>
	[RelayCommand(IncludeCancelCommand = true)]
	private async Task RefreshStatusAsync(CancellationToken cancellationToken) {
		await RefreshStatusCoreAsync(cancellationToken);
	}

	private async Task RefreshStatusCoreAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(TargetMonth, out var yyyymm)) {
			ShowWarn($"対象月の形式が不正です: {TargetMonth}");
			return;
		}

		IsProcessing = true;
		StatusMessage = "状態を取得しています...";
		ClientLib.Cursor2Wait();
		try {
			// 締日基準の対象期間はCostMonthStatusに含まれないため、クライアント側でClosingMonthCalculatorを
			// 使って算出する(設計書§2.1「period = ClosingMonthCalculator.GetPeriod(TargetMonth, MasterSysman.ShimeBi)」)。
			// MasterSysman.ShimeBiの取得はBaseBillingCalculationViewModel.InitAsyncと同じ作法(Msg101経由)。
			var shimeRows = await QuerySqlListAsync<MasterSysman>(
				$"SELECT ShimeBi FROM {nameof(MasterSysman)} ORDER BY Id LIMIT 1", [], cancellationToken);
			var shimeBi = shimeRows.Count > 0 ? shimeRows[0].ShimeBi : (int)EnumShime.DayLast;
			var period = ClosingMonthCalculator.GetPeriod(yyyymm, shimeBi);
			PeriodText = $"{CostPreviewDisplay.FormatYmd8ToSlash(period.DayFrom)} ～ {CostPreviewDisplay.FormatYmd8ToSlash(period.DayTo)}";

			var statuses = await FetchCostMonthStatusesAsync(yyyymm, cancellationToken);
			var status = statuses.FirstOrDefault(x => x.ProcessKind == ProcessKind);
			if (status is null) {
				ProcessStatusText = CostPreviewDisplay.FormatCostProcessStatus(EnumCostProcessStatus.NotRun);
				LastRunAtText = "－";
			}
			else {
				ProcessStatusText = CostPreviewDisplay.FormatCostProcessStatus(status.Status);
				LastRunAtText = status.LastRunAt > 0
					? new DateTime(status.LastRunAt, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)
					: "－";
			}
			StatusMessage = "状態を取得しました。確認を実行してください。";
		}
		catch (OperationCanceledException) {
			StatusMessage = "状態取得をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"状態取得に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	private async Task<IReadOnlyList<CostMonthStatus>> FetchCostMonthStatusesAsync(string yyyymm, CancellationToken cancellationToken) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg080_CostMonthStatus,
			DataType = typeof(string),
			DataMsg = Common.SerializeObject(yyyymm),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken));
		if (reply.Code < 0) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "月次状態照会に失敗しました。");
		}
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as IReadOnlyList<CostMonthStatus> ?? [];
	}

	// ------------------------------------------------------------------
	// 確認（プレビュー）
	// ------------------------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ConfirmAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(TargetMonth, out var yyyymm)) {
			ShowWarn($"対象月の形式が不正です: {TargetMonth}");
			return;
		}

		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = "確認しています...";
		ClientLib.Cursor2Wait();
		try {
			DiscardConfirmedState();
			var param = new CostUpdateParameter {
				TargetMonth = yyyymm,
				ProcessKind = ProcessKind,
				CostMethod = CostMethodForApply,
				IsPreview = true,
			};
			var outcome = await RunPreviewAsync(param, cancellationToken);
			TargetCount = outcome.TargetCount;
			ErrorCount = outcome.ErrorCount;
			ExcludedCount = outcome.ExcludedCount;
			ConfirmedSnapshot = outcome.Confirmed;
			UpdateCommand.NotifyCanExecuteChanged();

			// 対象外(設計書§6.5「2026-09-06改訂」)はエラーではなく更新を妨げないため、件数表示だけ分けて出す。
			StatusMessage = ErrorCount > 0
				? $"確認しました。対象 {TargetCount:N0} 件（エラー {ErrorCount:N0} 件、対象外 {ExcludedCount:N0} 件）。エラーを解消してから再度確認してください。"
				: ExcludedCount > 0
					? $"確認しました。対象 {TargetCount:N0} 件（対象外 {ExcludedCount:N0} 件）。更新を実行できます。"
					: $"確認しました。対象 {TargetCount:N0} 件。更新を実行できます。";
		}
		catch (OperationCanceledException) {
			StatusMessage = "確認をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"確認に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ProgressValue = 0;
			ClientLib.Cursor2Normal();
		}
	}

	// ------------------------------------------------------------------
	// 更新実行（ストリーミング。§9.3「開始通知→実行→結果通知」）
	// ------------------------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanUpdate))]
	private async Task UpdateAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(TargetMonth, out var yyyymm)) {
			ShowWarn($"対象月の形式が不正です: {TargetMonth}");
			return;
		}
		if (ConfirmedSnapshot is null || ErrorCount > 0) {
			ShowWarn("先に確認を実行し、エラーが無いことを確認してください。");
			return;
		}
		if (MessageEx.ShowQuestionDialog($"{CostPreviewDisplay.FormatYm6ToSlash(yyyymm)} の{ScreenName}を実行しますか？\n対象 {TargetCount:N0} 件",
			owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = $"{ScreenName}を開始します...";
		ClientLib.Cursor2Wait();
		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var message = new CvMsg {
				Code = 0,
				Flag = ApplyFlag,
				DataType = typeof(CostUpdateParameter),
				// Id_ShainとBatchIdはサーバーが上書き・採番するため送らない(空のままでよい)。
				// ConfirmedはStep 9-6の指紋往復規約により、確認結果をそのまま返送する(§2.4-4)。
				DataMsg = Common.SerializeObject(new CostUpdateParameter {
					TargetMonth = yyyymm,
					ProcessKind = ProcessKind,
					CostMethod = CostMethodForApply,
					IsPreview = false,
					Confirmed = ConfirmedSnapshot,
				}),
			};

			StreamMsg? finalMsg = null;
			await foreach (var streamMsg in coreService.QueryMsgStreamAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken))) {
				if (!string.IsNullOrEmpty(streamMsg.DataMsg) && !streamMsg.IsCompleted) {
					StatusMessage = streamMsg.DataMsg;
				}
				ProgressValue = Math.Clamp(streamMsg.Progress, 0, 100);
				if (streamMsg.IsCompleted) {
					finalMsg = streamMsg;
					break;
				}
			}
			if (finalMsg is null) {
				throw new InvalidOperationException("更新結果を受信できませんでした。");
			}
			if (Common.DeserializeObject(finalMsg.DataMsg ?? string.Empty, finalMsg.DataType) is not CostUpdateResult result) {
				throw new InvalidOperationException("更新結果の解析に失敗しました。");
			}

			ProgressValue = 100;
			if (!result.IsSuccess || finalMsg.IsError) {
				StatusMessage = $"更新に失敗しました。{result.Message}";
				MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
			}
			else {
				StatusMessage = $"{ScreenName}が完了しました。{result.UpdatedCount:N0} 件を更新しました。\n{result.Message}";
				MessageEx.ShowInformationDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
				// 更新後は確認済み状態を破棄し、再確認を要求する(次の更新は新しい指紋が必要なため)。
				DiscardConfirmedState();
				await RefreshStatusCoreAsync(CancellationToken.None);
			}
		}
		catch (OperationCanceledException) {
			StatusMessage = $"{ScreenName}をキャンセルしました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = $"{ScreenName}をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"エラーが発生しました: {ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	// ------------------------------------------------------------------
	// CSV出力（§8.1「確認一覧と同じ列をUTF-8 BOM付きで出力」）
	// ------------------------------------------------------------------

	[RelayCommand]
	private void ExportCsv() {
		if (TargetCount == 0) {
			MessageEx.ShowWarningDialog("出力する明細がありません。先に確認を実行してください。", owner: ClientLib.GetActiveView(this));
			return;
		}

		var dialog = new SaveFileDialog {
			Title = $"{ScreenName}確認一覧をCSV出力",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			DefaultExt = ".csv",
			FileName = $"{ScreenName}_{TargetMonth.Replace("/", string.Empty, StringComparison.Ordinal)}.csv",
		};
		if (dialog.ShowDialog(ClientLib.GetActiveView(this)) != true) {
			return;
		}

		try {
			var csv = BuildCsvText();
			File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			StatusMessage = $"CSVを出力しました: {dialog.FileName}";
		}
		catch (Exception ex) {
			StatusMessage = $"CSV出力に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
	}

	// ------------------------------------------------------------------
	// 共通ヘルパー（BaseBillingCalculationViewModel・BaseStocktakeViewModelと同じ実装）
	// ------------------------------------------------------------------

	protected async Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken cancellationToken) {
		cancellationToken.ThrowIfCancellationRequested();
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(T), sql, [.. parameters])),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken));
		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
		}
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list
			? list.Cast<T>().ToList()
			: [];
	}

	protected static bool TryParseYearMonth(string input, out string yyyymm) {
		yyyymm = string.Empty;
		if (string.IsNullOrWhiteSpace(input)) {
			return false;
		}
		var trimmed = input.Trim().Replace("/", string.Empty, StringComparison.Ordinal);
		if (trimmed.Length != 6
			|| !DateTime.TryParseExact(trimmed + "01", "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) {
			return false;
		}
		yyyymm = trimmed;
		return true;
	}

	private void ShowWarn(string message) {
		StatusMessage = message;
		MessageEx.ShowWarningDialog(message, owner: ClientLib.GetActiveView(this));
	}
}
