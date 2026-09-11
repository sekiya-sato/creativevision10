using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Grpc.Core;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>
/// 評価替え画面（原価4項目 詳細設計 §16、§8.6）。
/// <para>
/// 【継承しない判断】<see cref="BaseCostUpdateViewModel"/>は<c>CostUpdateParameter</c>・
/// <c>EnumCostProcessKind</c>（<c>Msg080_CostMonthStatus</c>による月次状態照会）を前提にしている。
/// 評価替えは <see cref="CostRevaluationParameter"/> を使い（<c>CostUpdateParameter</c>ではない）、
/// <see cref="EnumCostProcessKind"/>にも評価替えの区分値は無い（設計書§16.11のとおり本画面は
/// 独自のDTO・フラグ群を持つ）。さらに一覧が集計行＋明細行の2段構成（§16.6）、抽出条件が
/// 項目選択式の条件行（§16.4）、ヘッダ単位の取消（§16.7、<c>Msg090</c>）を持ち、これらは他4画面に
/// 存在しない。無理に継承すると基底の<c>CostUpdateParameter</c>前提のメソッド群（<c>RunPreviewAsync</c>の
/// シグネチャ等）をすべて型不一致のまま迂回する必要が生じ、<see cref="SundryChargesUpdateViewModel"/>と
/// 同じ理由（Step 10-4の判断）で、本画面も<see cref="Helpers.BaseViewModel"/>から直接派生する独立実装とする。
/// </para>
/// <para>
/// 【掛率の表示】設計書§16.5「画面ラベルは『率』ではなく『掛率』とし、入力欄の直後に計算式
/// `新原価 = 元原価 × 掛率%`を表示して誤入力を防ぐ」に従い、ラベルは「掛率」、
/// <see cref="RateFormulaText"/>を入力欄の直後に表示する。
/// </para>
/// <para>
/// 【対象外とエラーの区別】明細行の「状態」列は<see cref="CostPreviewDisplay.FormatRevaluationRowStatus"/>で
/// エラー(<c>AfterCost&lt;=0</c>)・対象外(在庫0／原価0／引き下げにならない)・対象の3値を区別する
/// （設計書§16.9「対象外はエラーではない」）。<see cref="ShowExcludedOnly"/>で対象外行だけに絞り込める。
/// </para>
/// <para>
/// 【取消】ヘッダ単位の取消（設計書§16.7、§13 U-23）。<see cref="TranGenkaReval"/>を<c>Status=0</c>で
/// 照会し（<c>Msg101_Op_Query</c>経由）、選んだ1件を<c>Msg090_CostRevaluationCancel</c>で取り消す。
/// 取消はYes/No確認を必須にし、サーバーが拒否した場合（新しい評価替えが対象商品にある等）は
/// <c>CostUpdateResult.Message</c>をそのまま表示する。
/// </para>
/// </summary>
public partial class CostRevaluationViewModel : Helpers.BaseViewModel {
	/// <summary>抽出条件の項目選択肢（設計書§16.4。年度は含まない、§13 U-17）。</summary>
	public sealed record CondFieldOption(EnumCostRevalCondField Value, string Name);
	/// <summary>集計単位の選択肢（設計書§16.4）。</summary>
	public sealed record GroupKeyOption(EnumCostRevalGroupKey Value, string Name);
	/// <summary>適用時点の選択肢（設計書§16.4）。</summary>
	public sealed record ApplyPointOption(EnumCostRevalApplyPoint Value, string Name);
	/// <summary>指定方式の選択肢（設計書§16.4）。</summary>
	public sealed record MethodOption(EnumCostRevaluationMethod Value, string Name);
	/// <summary>端数処理の選択肢（設計書§16.4）。</summary>
	public sealed record RoundingOption(EnumRounding Value, string Name);

	public static IReadOnlyList<CondFieldOption> CondFieldOptionsStatic { get; } =
		[.. Enum.GetValues<EnumCostRevalCondField>().Select(v => new CondFieldOption(v, CostPreviewDisplay.FormatCostRevalCondField(v)))];
	public static IReadOnlyList<GroupKeyOption> GroupKeyOptionsStatic { get; } =
		[.. Enum.GetValues<EnumCostRevalGroupKey>().Select(v => new GroupKeyOption(v, CostPreviewDisplay.FormatCostRevalGroupKey(v)))];
	public static IReadOnlyList<ApplyPointOption> ApplyPointOptionsStatic { get; } =
		[.. Enum.GetValues<EnumCostRevalApplyPoint>().Select(v => new ApplyPointOption(v, CostPreviewDisplay.FormatCostRevalApplyPoint(v)))];
	public static IReadOnlyList<MethodOption> MethodOptionsStatic { get; } =
		[.. Enum.GetValues<EnumCostRevaluationMethod>().Select(v => new MethodOption(v, CostPreviewDisplay.FormatCostRevaluationMethod(v)))];
	public static IReadOnlyList<RoundingOption> RoundingOptionsStatic { get; } =
		[.. Enum.GetValues<EnumRounding>().Select(v => new RoundingOption(v, CostPreviewDisplay.FormatRounding(v)))];
	public static IReadOnlyList<int> RoundingUnitOptionsStatic { get; } = [1, 10, 100];

	public IReadOnlyList<CondFieldOption> CondFieldOptions => CondFieldOptionsStatic;
	public IReadOnlyList<GroupKeyOption> GroupKeyOptions => GroupKeyOptionsStatic;
	public IReadOnlyList<ApplyPointOption> ApplyPointOptions => ApplyPointOptionsStatic;
	public IReadOnlyList<MethodOption> MethodOptions => MethodOptionsStatic;
	public IReadOnlyList<RoundingOption> RoundingOptions => RoundingOptionsStatic;
	public IReadOnlyList<int> RoundingUnitOptions => RoundingUnitOptionsStatic;

	private static readonly string[] SummaryCsvHeader = [
		"集計単位", "対象品番数", "数量", "元上代金額", "在庫金額", "評価減後金額", "評価減差額",
	];
	private static readonly string[] DetailCsvHeader = [
		"商品", "シーズン", "ブランド", "アイテム", "上代", "数量", "前原価", "後原価", "在庫金額", "評価減後金額", "状態", "エラー",
	];

	// ------------------------------------------------------------------
	// 入力（設計書§16.4）
	// ------------------------------------------------------------------

	[ObservableProperty]
	public partial string TargetMonth { get; set; } = DateTime.Today.AddMonths(-1).ToString("yyyy/MM", CultureInfo.InvariantCulture);
	[ObservableProperty]
	public partial EnumCostRevalApplyPoint ApplyPoint { get; set; } = EnumCostRevalApplyPoint.MonthEnd;
	[ObservableProperty]
	public partial ObservableCollection<CostRevalCondRowVm> CondRows { get; set; } = [];
	[ObservableProperty]
	public partial EnumCostRevalGroupKey GroupKey { get; set; } = EnumCostRevalGroupKey.Brand;
	[ObservableProperty]
	public partial EnumCostRevaluationMethod Method { get; set; } = EnumCostRevaluationMethod.ByRate;
	[ObservableProperty]
	public partial int RatePercent { get; set; } = 100;
	[ObservableProperty]
	public partial int FixedCost { get; set; }
	[ObservableProperty]
	public partial int RoundingUnit { get; set; } = 1;
	[ObservableProperty]
	public partial EnumRounding Rounding { get; set; } = EnumRounding.Round;

	/// <summary>掛率入力欄の直後に表示する計算式（設計書§16.5）。</summary>
	public string RateFormulaText => CostPreviewDisplay.BuildRevaluationRateFormulaText(RatePercent);
	partial void OnRatePercentChanged(int value) => OnPropertyChanged(nameof(RateFormulaText));

	// ------------------------------------------------------------------
	// 画面共通の状態
	// ------------------------------------------------------------------

	[ObservableProperty]
	public partial string PeriodText { get; set; } = "－";
	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "対象月・抽出条件・指定方式を入力し、確認を実行してください。";
	[ObservableProperty]
	public partial bool IsProcessing { get; set; }
	[ObservableProperty]
	public partial int ProgressValue { get; set; }
	[ObservableProperty]
	public partial long TargetCount { get; set; }
	[ObservableProperty]
	public partial long ErrorCount { get; set; }
	[ObservableProperty]
	public partial bool ShowExcludedOnly { get; set; }
	[ObservableProperty]
	public partial ObservableCollection<string> InfoMessages { get; set; } = [];
	[ObservableProperty]
	public partial ObservableCollection<RevaluationSummaryRowVm> SummaryRows { get; set; } = [];
	[ObservableProperty]
	public partial RevaluationSummaryRowVm? TotalRow { get; set; }
	[ObservableProperty]
	public partial ObservableCollection<RevaluationDetailRowVm> DetailRows { get; set; } = [];

	/// <summary>確認(プレビュー)で取得した全明細行。<see cref="ShowExcludedOnly"/>の絞り込み前。</summary>
	private List<RevaluationDetailRowVm> _allDetailRows = [];
	/// <summary>確認時点の指紋（設計書§2.4-4）。更新実行時にそのまま返送する。<c>null</c>は「未確認」を意味する。</summary>
	private CostConfirmSnapshot? _confirmedSnapshot;
	/// <summary>確認・更新で同一値を使う実行Id（設計書§2.4-4の往復規約）。</summary>
	private string _batchId = string.Empty;

	/// <summary>「更新」を実行できるか（エラー0件かつ確認済み・対象1件以上。設計書§2.4-2、§16.9）。</summary>
	public bool CanUpdate => _confirmedSnapshot is not null && ErrorCount == 0 && TargetCount > 0;

	partial void OnTargetMonthChanged(string value) => DiscardConfirmedState("対象月を変更しました。確認をやり直してください。");
	partial void OnApplyPointChanged(EnumCostRevalApplyPoint value) => DiscardConfirmedState("適用時点を変更しました。確認をやり直してください。");
	partial void OnGroupKeyChanged(EnumCostRevalGroupKey value) => DiscardConfirmedState("集計単位を変更しました。確認をやり直してください。");
	partial void OnMethodChanged(EnumCostRevaluationMethod value) => DiscardConfirmedState("指定方式を変更しました。確認をやり直してください。");
	partial void OnErrorCountChanged(long value) => UpdateCommand.NotifyCanExecuteChanged();
	partial void OnTargetCountChanged(long value) => UpdateCommand.NotifyCanExecuteChanged();
	partial void OnShowExcludedOnlyChanged(bool value) => ApplyRowFilter();

	private void DiscardConfirmedState(string message) {
		_confirmedSnapshot = null;
		_batchId = string.Empty;
		TargetCount = 0;
		ErrorCount = 0;
		_allDetailRows = [];
		DetailRows = [];
		SummaryRows = [];
		TotalRow = null;
		InfoMessages = [];
		StatusMessage = message;
		UpdateCommand.NotifyCanExecuteChanged();
	}

	private void ApplyRowFilter() {
		DetailRows = new ObservableCollection<RevaluationDetailRowVm>(
			ShowExcludedOnly ? _allDetailRows.Where(x => !x.IsTarget) : _allDetailRows);
	}

	// ------------------------------------------------------------------
	// 抽出条件行（設計書§16.4。MasterJouDaiBulkChangeViewModelのJodaiCondRowと同じ方式）
	// ------------------------------------------------------------------

	[RelayCommand]
	private void AddCondRow() => CondRows.Add(new CostRevalCondRowVm());

	[RelayCommand]
	private void RemoveCondRow(CostRevalCondRowVm? row) {
		if (row != null) {
			CondRows.Remove(row);
		}
	}

	// ------------------------------------------------------------------
	// 状態表示・実行履歴（設計書§8.1、§16.7「再実行と取消」）
	// ------------------------------------------------------------------

	[ObservableProperty]
	public partial ObservableCollection<RevalHistoryRowVm> HistoryRows { get; set; } = [];
	[ObservableProperty]
	public partial RevalHistoryRowVm? SelectedHistoryRow { get; set; }

	/// <summary>ウィンドウ表示時に<see cref="BaseWindow"/>が自動実行する（<c>InitCommand</c>）。</summary>
	[RelayCommand]
	private async Task InitAsync(CancellationToken cancellationToken) {
		await RefreshPeriodAsync(cancellationToken);
		await RefreshHistoryAsync(cancellationToken);
	}

	private async Task RefreshPeriodAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(TargetMonth, out var yyyymm)) {
			return;
		}
		try {
			// 締日基準の対象期間の算出はBaseCostUpdateViewModel.RefreshStatusCoreAsyncと同じ作法
			// (設計書§2.1「period = ClosingMonthCalculator.GetPeriod(TargetMonth, MasterSysman.ShimeBi)」)。
			var sysmanRows = await CoreServiceClient.QuerySqlListAsync<MasterSysman>(
				$"SELECT ShimeBi, FiscalStartDate FROM {nameof(MasterSysman)} ORDER BY Id LIMIT 1", [], cancellationToken);
			var shimeBi = sysmanRows.Count > 0 ? sysmanRows[0].ShimeBi : (int)EnumShime.DayLast;

			// 適用時点=期末は、入力計上月が属する会計年度の決算期末月へ読み替える(設計書§16.4)。
			// 読み替えの年月演算はClosingMonthCalculator.ResolveFiscalYearEndMonthにあり、
			// サーバー(CostUpdateDbReval.ResolveRevaluationPeriod)と同じ実装を使うので結果は一致する。
			// 「確認」を押す前に、どの月が対象になるかを利用者へ見せるためここでも解決する。
			// 未来月かどうかの最終判定はサーバーが行う(§16.9)。
			var effectiveMonth = yyyymm;
			if (ApplyPoint == EnumCostRevalApplyPoint.FiscalEnd) {
				var fiscalStartDate = sysmanRows.Count > 0 ? sysmanRows[0].FiscalStartDate : string.Empty;
				if (fiscalStartDate.Length >= 6 && int.TryParse(fiscalStartDate.AsSpan(4, 2), out var fiscalStartMonth)) {
					effectiveMonth = ClosingMonthCalculator.ResolveFiscalYearEndMonth(yyyymm, fiscalStartMonth);
				}
			}
			var period = ClosingMonthCalculator.GetPeriod(effectiveMonth, shimeBi);
			var periodRange = $"{CostPreviewDisplay.FormatYmd8ToSlash(period.DayFrom)} ～ {CostPreviewDisplay.FormatYmd8ToSlash(period.DayTo)}";
			PeriodText = effectiveMonth == yyyymm
				? periodRange
				: $"決算期末月 {CostPreviewDisplay.FormatYm6ToSlash(effectiveMonth)}（{periodRange}）";
		}
		catch {
			// 期間表示は補助情報のため、失敗しても確認自体は続行できるようにする(確認側で改めてエラーを出す)。
			PeriodText = "－";
		}
	}

	/// <summary>
	/// 取消可能な実行履歴一覧（設計書§16.7）。<c>Status=0</c>(有効)の<see cref="TranGenkaReval"/>を
	/// 既存のSQL照会経路(<c>Msg101_Op_Query</c>)で取得する。前回実行日時・状態の一覧はこのテーブルが兼ねる
	/// （評価替えは<c>EnumCostProcessKind</c>に区分値を持たず<c>Msg080_CostMonthStatus</c>の対象外のため。
	/// 設計書§16.11・§2.5.6の「新設せずTranGenkaReval等の成果テーブルから都度算出」の趣旨に合わせた）。
	/// </summary>
	private async Task RefreshHistoryAsync(CancellationToken cancellationToken) {
		try {
			var rows = await CoreServiceClient.QuerySqlListAsync<TranGenkaReval>(
				$"SELECT * FROM {nameof(TranGenkaReval)} WHERE Status = @0 ORDER BY Id DESC LIMIT 200",
				[((int)EnumCostRevalStatus.Active).ToString(CultureInfo.InvariantCulture)], cancellationToken);
			HistoryRows = new ObservableCollection<RevalHistoryRowVm>(rows.Select(RevalHistoryRowVm.FromDto));
		}
		catch (Exception ex) {
			StatusMessage = $"実行履歴の取得に失敗しました。{ex.Message}";
		}
	}

	// ------------------------------------------------------------------
	// 確認（プレビュー。Msg088_CostRevaluationPreview、QueryMsgAsync経由）
	// ------------------------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ConfirmAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(TargetMonth, out var yyyymm)) {
			ShowWarn($"対象計上月の形式が不正です: {TargetMonth}");
			return;
		}
		var methodError = CostPreviewDisplay.ValidateRevaluationMethodValue(Method, RatePercent, FixedCost);
		if (methodError != null) {
			ShowWarn(methodError);
			return;
		}
		var roundingError = CostPreviewDisplay.ValidateRevaluationRoundingUnit(RoundingUnit);
		if (roundingError != null) {
			ShowWarn(roundingError);
			return;
		}

		IsProcessing = true;
		StatusMessage = "確認しています...";
		ClientLib.Cursor2Wait();
		try {
			DiscardConfirmedState("確認しています...");
			await RefreshPeriodAsync(cancellationToken);
			_batchId = Guid.NewGuid().ToString("D");

			var param = BuildParameter(yyyymm, confirmed: null);
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var message = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg088_CostRevaluationPreview,
				DataType = typeof(CostRevaluationParameter),
				DataMsg = Common.SerializeObject(param),
			};
			var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "評価替えの確認に失敗しました。");
			}
			if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is not RevaluationPreviewResult result) {
				throw new InvalidOperationException("確認結果の解析に失敗しました。");
			}

			_allDetailRows = [.. result.DetailRows.Select(RevaluationDetailRowVm.FromDto)];
			ApplyRowFilter();
			SummaryRows = new ObservableCollection<RevaluationSummaryRowVm>(result.SummaryRows.Select(RevaluationSummaryRowVm.FromDto));
			TotalRow = RevaluationSummaryRowVm.FromDto(result.Total);
			InfoMessages = new ObservableCollection<string>(result.InfoMessages);
			ErrorCount = result.ErrorCount;
			TargetCount = result.Total.TargetCount;
			_confirmedSnapshot = result.Confirmed;
			UpdateCommand.NotifyCanExecuteChanged();

			StatusMessage = ErrorCount > 0
				? $"確認しました。対象 {TargetCount:N0} 品番（エラー {ErrorCount:N0} 件）。エラーを解消してから再度確認してください。"
				: TargetCount > 0
					? $"確認しました。対象 {TargetCount:N0} 品番。更新を実行できます。"
					: "確認しました。更新対象はありません。";
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
			ClientLib.Cursor2Normal();
		}
	}

	// ------------------------------------------------------------------
	// 更新実行（ストリーミング。Msg089_CostRevaluationApply、QueryMsgStreamAsync経由）
	// ------------------------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanUpdate))]
	private async Task UpdateAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(TargetMonth, out var yyyymm)) {
			ShowWarn($"対象計上月の形式が不正です: {TargetMonth}");
			return;
		}
		if (_confirmedSnapshot is null || ErrorCount > 0 || TargetCount == 0) {
			ShowWarn("先に確認を実行し、エラーが無くかつ対象があることを確認してください。");
			return;
		}
		if (MessageEx.ShowQuestionDialog($"{CostPreviewDisplay.FormatYm6ToSlash(yyyymm)} の評価替えを実行しますか？\n対象 {TargetCount:N0} 品番",
			owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		IsProcessing = true;
		ProgressValue = 0;
		StatusMessage = "評価替えを開始します...";
		ClientLib.Cursor2Wait();
		try {
			var param = BuildParameter(yyyymm, confirmed: _confirmedSnapshot);
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var message = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg089_CostRevaluationApply,
				DataType = typeof(CostRevaluationParameter),
				// Id_ShainとBatchIdはサーバーが上書き・採番するため送らない(空のままでよい)。
				DataMsg = Common.SerializeObject(param),
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
				StatusMessage = $"評価替えが完了しました。{result.UpdatedCount:N0} 件を更新しました。\n{result.Message}";
				MessageEx.ShowInformationDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
				DiscardConfirmedState("更新が完了しました。次回は再度確認を実行してください。");
				await RefreshHistoryAsync(CancellationToken.None);
			}
		}
		catch (OperationCanceledException) {
			StatusMessage = "評価替えをキャンセルしました。";
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			StatusMessage = "評価替えをキャンセルしました。";
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

	private CostRevaluationParameter BuildParameter(string yyyymm, CostConfirmSnapshot? confirmed) {
		var cond = new CostRevaluationCondition { Rows = [.. CondRows.Select(r => r.ToDto())] };
		return new CostRevaluationParameter(
			yyyymm,
			ApplyPoint,
			cond,
			GroupKey,
			Method,
			Method == EnumCostRevaluationMethod.ByRate ? RatePercent : 0,
			Method == EnumCostRevaluationMethod.ByFixed ? FixedCost : 0,
			RoundingUnit,
			Rounding,
			Id_Shain: 0,
			BatchId: _batchId,
			Confirmed: confirmed);
	}

	// ------------------------------------------------------------------
	// 取消（ヘッダ単位。Msg090_CostRevaluationCancel、QueryMsgAsync経由。設計書§16.7、§13 U-23）
	// ------------------------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task CancelHistoryAsync(CancellationToken cancellationToken) {
		var target = SelectedHistoryRow;
		if (target is null) {
			ShowWarn("取消する実行履歴を選択してください。");
			return;
		}
		if (MessageEx.ShowQuestionDialog(
			$"{target.SumMonthText}（{target.RunAtText}実行）の評価替えを取り消しますか？\nこの操作は元に戻せません。",
			owner: ClientLib.GetActiveView(this)) != MessageBoxResult.Yes) {
			return;
		}

		IsProcessing = true;
		StatusMessage = "評価替えを取り消しています...";
		ClientLib.Cursor2Wait();
		try {
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var message = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg090_CostRevaluationCancel,
				DataType = typeof(long),
				// 実行社員IdはJWTからサーバー側で解決するため、リクエストにはrevalIdだけを送る。
				DataMsg = Common.SerializeObject(target.Id),
			};
			var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "評価替えの取消に失敗しました。");
			}
			if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is not CostUpdateResult result) {
				throw new InvalidOperationException("取消結果の解析に失敗しました。");
			}

			if (!result.IsSuccess) {
				StatusMessage = $"取消できませんでした。{result.Message}";
				MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
			}
			else {
				StatusMessage = $"取消しました。{result.Message}";
				MessageEx.ShowInformationDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
				await RefreshHistoryAsync(CancellationToken.None);
			}
		}
		catch (OperationCanceledException) {
			StatusMessage = "取消をキャンセルしました。";
		}
		catch (Exception ex) {
			StatusMessage = $"取消に失敗しました。{ex.Message}";
			MessageEx.ShowErrorDialog(StatusMessage, owner: ClientLib.GetActiveView(this));
		}
		finally {
			IsProcessing = false;
			ClientLib.Cursor2Normal();
		}
	}

	// ------------------------------------------------------------------
	// CSV出力（設計書§8.1「確認一覧と同じ列をUTF-8 BOM付きで出力」、§8.6「集計行・明細行の2段構成」）
	// 諸掛確認(SundryChargesUpdateViewModel)と同じく、集計行・明細行を1ファイル2ブロックで出力する
	// （列構成が異なるため。保存ダイアログを2回出すより操作が単純になる）。
	// ------------------------------------------------------------------

	[RelayCommand]
	private void ExportCsv() {
		if (TargetCount == 0 && _allDetailRows.Count == 0) {
			MessageEx.ShowWarningDialog("出力する明細がありません。先に確認を実行してください。", owner: ClientLib.GetActiveView(this));
			return;
		}

		var dialog = new SaveFileDialog {
			Title = "評価替え確認一覧をCSV出力",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			DefaultExt = ".csv",
			FileName = $"評価替え_{TargetMonth.Replace("/", string.Empty, StringComparison.Ordinal)}.csv",
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

	private string BuildCsvText() {
		var details = ShowExcludedOnly ? _allDetailRows.Where(x => !x.IsTarget) : _allDetailRows;

		var sb = new StringBuilder();
		sb.AppendLine(CsvText.BuildLine(["集計行"]));
		sb.AppendLine(CsvText.BuildLine(SummaryCsvHeader));
		foreach (var row in SummaryRows) {
			sb.AppendLine(CsvText.BuildLine(row.ToCsvFields()));
		}
		if (TotalRow != null) {
			sb.AppendLine(CsvText.BuildLine(TotalRow.ToCsvFields()));
		}
		sb.AppendLine();
		sb.AppendLine(CsvText.BuildLine(["明細行"]));
		sb.AppendLine(CsvText.BuildLine(DetailCsvHeader));
		foreach (var row in details) {
			sb.AppendLine(CsvText.BuildLine(row.ToCsvFields()));
		}
		return sb.ToString();
	}

	// ------------------------------------------------------------------
	// 共通ヘルパー（BaseCostUpdateViewModel.TryParseYearMonthと同じ実装。基底を継承しないため複製する）
	// ------------------------------------------------------------------

	private static bool TryParseYearMonth(string input, out string yyyymm) {
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

/// <summary>抽出条件の1行（設計書§16.4）。<c>MasterJouDaiBulkChangeViewModel</c>の<c>JodaiCondRow</c>と同じ方式。</summary>
public partial class CostRevalCondRowVm : ObservableObject {
	[ObservableProperty]
	public partial CostRevaluationViewModel.CondFieldOption Field { get; set; } = CostRevaluationViewModel.CondFieldOptionsStatic[0];
	[ObservableProperty]
	public partial string CodeFrom { get; set; } = string.Empty;
	[ObservableProperty]
	public partial string CodeTo { get; set; } = string.Empty;

	public CostRevaluationCondRow ToDto() => new() {
		FieldKind = (int)Field.Value,
		CodeFrom = CodeFrom,
		CodeTo = CodeTo,
	};
}

/// <summary>評価替え一覧の集計行の画面表示用ラッパー（<see cref="RevaluationSummaryRow"/>のDTOを整形する。設計書§16.6.1）。</summary>
public sealed class RevaluationSummaryRowVm {
	public required string GroupCode { get; init; }
	public required string GroupName { get; init; }
	public required long TargetCount { get; init; }
	public required long Qty { get; init; }
	public required long JodaiAmount { get; init; }
	public required long BeforeAmount { get; init; }
	public required long AfterAmount { get; init; }
	public long DiffAmount => BeforeAmount - AfterAmount;

	public static RevaluationSummaryRowVm FromDto(RevaluationSummaryRow row) => new() {
		GroupCode = row.GroupCode,
		GroupName = row.GroupName,
		TargetCount = row.TargetCount,
		Qty = row.Qty,
		JodaiAmount = row.JodaiAmount,
		BeforeAmount = row.BeforeAmount,
		AfterAmount = row.AfterAmount,
	};

	/// <summary>CSV出力用の列(<see cref="CostRevaluationViewModel"/>の集計行ヘッダ順と一致させる)。</summary>
	public IEnumerable<string> ToCsvFields() => [
		string.IsNullOrEmpty(GroupCode) ? GroupName : $"{GroupCode} {GroupName}",
		TargetCount.ToString(CultureInfo.InvariantCulture),
		Qty.ToString(CultureInfo.InvariantCulture),
		JodaiAmount.ToString(CultureInfo.InvariantCulture),
		BeforeAmount.ToString(CultureInfo.InvariantCulture),
		AfterAmount.ToString(CultureInfo.InvariantCulture),
		DiffAmount.ToString(CultureInfo.InvariantCulture),
	];
}

/// <summary>評価替え一覧の明細行の画面表示用ラッパー（<see cref="RevaluationDetailRow"/>のDTOを整形する。設計書§16.6.2）。</summary>
public sealed class RevaluationDetailRowVm {
	public required long Id_Shohin { get; init; }
	public required string CodeShohin { get; init; }
	public required string MeiShohin { get; init; }
	public string ShohinText => $"{CodeShohin} {MeiShohin}";
	public required string MeiSeason { get; init; }
	public required string MeiBrand { get; init; }
	public required string MeiItem { get; init; }
	public required long Jodai { get; init; }
	public required long Qty { get; init; }
	public required long BeforeCost { get; init; }
	public required long AfterCost { get; init; }
	public required long BeforeAmount { get; init; }
	public required long AfterAmount { get; init; }
	public required bool IsTarget { get; init; }
	public required string ExcludeReason { get; init; }
	public required EnumCostCalcError Error { get; init; }
	public required string ErrorMessage { get; init; }
	public bool IsError => CostPreviewDisplay.IsErrorRow(Error, ErrorMessage);
	public string StatusText => CostPreviewDisplay.FormatRevaluationRowStatus(IsTarget, Error, ErrorMessage);
	public string ReasonText => CostPreviewDisplay.FormatRevaluationRowReason(IsTarget, Error, ErrorMessage, ExcludeReason);

	public static RevaluationDetailRowVm FromDto(RevaluationDetailRow row) => new() {
		Id_Shohin = row.Id_Shohin,
		CodeShohin = row.CodeShohin,
		MeiShohin = row.MeiShohin,
		MeiSeason = row.MeiSeason,
		MeiBrand = row.MeiBrand,
		MeiItem = row.MeiItem,
		Jodai = row.Jodai,
		Qty = row.Qty,
		BeforeCost = row.BeforeCost,
		AfterCost = row.AfterCost,
		BeforeAmount = row.BeforeAmount,
		AfterAmount = row.AfterAmount,
		IsTarget = row.IsTarget,
		ExcludeReason = row.ExcludeReason,
		Error = row.Error,
		ErrorMessage = row.ErrorMessage,
	};

	/// <summary>CSV出力用の列(<see cref="CostRevaluationViewModel"/>の明細行ヘッダ順と一致させる)。</summary>
	public IEnumerable<string> ToCsvFields() => [
		ShohinText,
		MeiSeason,
		MeiBrand,
		MeiItem,
		Jodai.ToString(CultureInfo.InvariantCulture),
		Qty.ToString(CultureInfo.InvariantCulture),
		BeforeCost.ToString(CultureInfo.InvariantCulture),
		AfterCost.ToString(CultureInfo.InvariantCulture),
		BeforeAmount.ToString(CultureInfo.InvariantCulture),
		AfterAmount.ToString(CultureInfo.InvariantCulture),
		StatusText,
		ReasonText,
	];
}

/// <summary>
/// 評価替え実行履歴（取消対象選択用）の画面表示用ラッパー（<see cref="TranGenkaReval"/>を整形する。設計書§16.7）。
/// </summary>
public sealed class RevalHistoryRowVm {
	public required long Id { get; init; }
	public required string BatchId { get; init; }
	public required string SumMonth { get; init; }
	public required string EffectiveDay { get; init; }
	public required EnumCostRevaluationMethod Method { get; init; }
	public required int RatePercent { get; init; }
	public required int FixedCost { get; init; }
	public required int RoundingUnit { get; init; }
	public required EnumRounding Rounding { get; init; }
	public required EnumCostRevalGroupKey GroupKey { get; init; }
	public required long TargetCount { get; init; }
	public required long TargetQty { get; init; }
	public required long JodaiAmount { get; init; }
	public required long BeforeAmount { get; init; }
	public required long AfterAmount { get; init; }
	public required long Vdc { get; init; }
	public required string VShainMei { get; init; }

	public string SumMonthText => CostPreviewDisplay.FormatYm6ToSlash(SumMonth);
	public string EffectiveDayText => CostPreviewDisplay.FormatYmd8ToSlash(EffectiveDay);
	public string GroupKeyText => CostPreviewDisplay.FormatCostRevalGroupKey(GroupKey);
	public string MethodText => CostPreviewDisplay.FormatCostRevaluationMethod(Method);
	public string SpecText => Method == EnumCostRevaluationMethod.ByRate
		? $"掛率{RatePercent}%"
		: $"単価{FixedCost:N0}円";
	public string RoundingText => $"{RoundingUnit}円 {CostPreviewDisplay.FormatRounding(Rounding)}";
	public long DiffAmount => BeforeAmount - AfterAmount;
	public string RunAtText => Vdc > 0
		? new DateTime(Vdc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)
		: "－";

	public static RevalHistoryRowVm FromDto(TranGenkaReval reval) => new() {
		Id = reval.Id,
		BatchId = reval.BatchId,
		SumMonth = reval.SumMonth,
		EffectiveDay = reval.EffectiveDay,
		Method = (EnumCostRevaluationMethod)reval.Method,
		RatePercent = reval.RatePercent,
		FixedCost = reval.FixedCost,
		RoundingUnit = reval.RoundingUnit,
		Rounding = (EnumRounding)reval.Rounding,
		GroupKey = (EnumCostRevalGroupKey)reval.GroupKey,
		TargetCount = reval.TargetCount,
		TargetQty = reval.TargetQty,
		JodaiAmount = reval.JodaiAmount,
		BeforeAmount = reval.BeforeAmount,
		AfterAmount = reval.AfterAmount,
		Vdc = reval.Vdc,
		VShainMei = string.IsNullOrEmpty(reval.VShain.Cd) ? string.Empty : $"{reval.VShain.Cd} {reval.VShain.Mei}",
	};
}
