using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>
/// 諸掛確認画面（原価4項目 詳細設計 §3.4～§3.8、§8.2、§8.1）。
/// <para>
/// 諸掛は独立した更新処理を持たない参照専用画面であり（§3.8「更新ボタンを持たない参照専用画面」）、
/// サーバー側にも更新(Apply)に相当するAPIは存在しない（<c>CvDomainLogic/CostUpdateDbSundry.cs</c>）。
/// 総平均原価更新（Step 7）が実行時に<c>SumSundryChargesByShohin</c>を直接集計するため(U-02)、
/// この画面は「確認」→「更新」の二段階を前提とする<see cref="BaseCostUpdateViewModel"/>を継承しない。
/// </para>
/// <para>
/// <see cref="BaseCostUpdateViewModel"/>は<c>ApplyFlag</c>(abstract、既定なし)・<c>CanUpdate</c>・
/// <c>UpdateCommand</c>・<c>ConfirmedSnapshot</c>の往復など、更新を伴う3画面
/// （消化仕入更新・最終仕入原価更新・総平均原価更新）の二段階フローを前提にした状態を抱えている。
/// この画面にはその「更新」段階が存在しないため、無理に継承すると存在しないApplyFlagへダミー値を
/// 割り当てる必要が生じ、かつUpdateCommandが（バインドを忘れない限り）画面に出てしまう危険がある。
/// 継承後に「更新」関連だけを無効化する差分は基底の公開契約（<c>CanUpdate</c>、<c>UpdateCommand</c>）
/// まで踏み込む改修になり、既に動作確認済みの3画面に影響する規模になるため、本画面は
/// <see cref="Helpers.BaseViewModel"/>から直接派生する独立実装とする（判断はStep 10-4の作業報告を参照）。
/// </para>
/// </summary>
public partial class SundryChargesUpdateViewModel : Helpers.BaseViewModel {
	private static readonly string[] DetailCsvHeader = [
		"伝票No", "伝票日", "取引区分", "仕入先", "明細No", "費目", "商品", "数量", "金額", "状態", "エラー",
	];
	private static readonly string[] SummaryCsvHeader = [
		"商品", "諸掛件数", "諸掛金額", "当月仕入数", "当月仕入金額", "前月在庫数", "状態", "エラー",
	];

	[ObservableProperty]
	public partial string TargetMonth { get; set; } = DateTime.Today.ToString("yyyy/MM", CultureInfo.InvariantCulture);
	[ObservableProperty]
	public partial string PeriodText { get; set; } = "－";
	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "対象月を指定し、確認を実行してください。";
	[ObservableProperty]
	public partial bool IsProcessing { get; set; }
	[ObservableProperty]
	public partial long ErrorCount { get; set; }
	[ObservableProperty]
	public partial long WarningCount { get; set; }
	/// <summary>エラー・警告行だけに絞り込むか（設計書§3.8「エラーと警告を区別できるように」）。</summary>
	[ObservableProperty]
	public partial bool ShowErrorsAndWarningsOnly { get; set; }
	/// <summary>
	/// 画面上部に表示する情報メッセージ（§3.8: 現在の原価方式=最終仕入原価、対象月に諸掛明細が0件）。
	/// </summary>
	[ObservableProperty]
	public partial ObservableCollection<string> InfoMessages { get; set; } = [];
	[ObservableProperty]
	public partial ObservableCollection<SundryChargeDetailRowVm> DetailRows { get; set; } = [];
	[ObservableProperty]
	public partial ObservableCollection<SundryChargeSummaryRowVm> SummaryRows { get; set; } = [];

	/// <summary>確認(照会)で取得した全明細行。<see cref="ShowErrorsAndWarningsOnly"/>の絞り込み前。</summary>
	private List<SundryChargeDetailRowVm> _allDetailRows = [];
	/// <summary>確認(照会)で取得した全集計行。<see cref="ShowErrorsAndWarningsOnly"/>の絞り込み前。</summary>
	private List<SundryChargeSummaryRowVm> _allSummaryRows = [];
	/// <summary>直近の照会結果を保持したかどうか。CSV出力・件数0時の警告に使う。</summary>
	private bool _hasResult;

	partial void OnTargetMonthChanged(string value) {
		ClearRows();
		PeriodText = "－";
		StatusMessage = "対象月を変更しました。確認をやり直してください。";
	}

	partial void OnShowErrorsAndWarningsOnlyChanged(bool value) => ApplyRowFilter();

	/// <summary>ウィンドウ表示時に<see cref="BaseWindow"/>が自動実行する（<c>InitCommand</c>）。</summary>
	[RelayCommand]
	private async Task InitAsync(CancellationToken cancellationToken) {
		await RefreshPeriodAsync(cancellationToken);
	}

	private async Task RefreshPeriodAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(TargetMonth, out var yyyymm)) {
			return;
		}
		try {
			// 締日基準の対象期間の算出はBaseCostUpdateViewModel.RefreshStatusCoreAsyncと同じ作法
			// (設計書§2.1「period = ClosingMonthCalculator.GetPeriod(TargetMonth, MasterSysman.ShimeBi)」)。
			var shimeRows = await CoreServiceClient.QuerySqlListAsync<MasterSysman>(
				$"SELECT ShimeBi FROM {nameof(MasterSysman)} ORDER BY Id LIMIT 1", [], cancellationToken);
			var shimeBi = shimeRows.Count > 0 ? shimeRows[0].ShimeBi : (int)EnumShime.DayLast;
			var period = ClosingMonthCalculator.GetPeriod(yyyymm, shimeBi);
			PeriodText = $"{CostPreviewDisplay.FormatYmd8ToSlash(period.DayFrom)} ～ {CostPreviewDisplay.FormatYmd8ToSlash(period.DayTo)}";
		}
		catch {
			// 期間表示だけの補助情報のため、失敗しても確認自体は続行できるようにする(確認側で改めてエラーを出す)。
			PeriodText = "－";
		}
	}

	// ------------------------------------------------------------------
	// 確認（照会。参照専用のため更新は伴わない。§3.8）
	// ------------------------------------------------------------------

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ConfirmAsync(CancellationToken cancellationToken) {
		if (!TryParseYearMonth(TargetMonth, out var yyyymm)) {
			ShowWarn($"対象月の形式が不正です: {TargetMonth}");
			return;
		}

		IsProcessing = true;
		StatusMessage = "確認しています...";
		ClientLib.Cursor2Wait();
		try {
			await RefreshPeriodAsync(cancellationToken);

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var message = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg083_CostSundryPreview,
				DataType = typeof(CostUpdateParameter),
				// 諸掛確認(Msg083)はCostMonthStatus(Msg080)の対象外(EnumCostProcessKindに諸掛の値は無い、
				// CvBase/Share/BaseEnumClass.cs「値2（諸掛）は欠番」)。ProcessKindはPreviewSundryCharges側
				// で参照しないため既定値のままでよい。
				DataMsg = Common.SerializeObject(new CostUpdateParameter {
					TargetMonth = yyyymm,
					IsPreview = true,
				}),
			};
			var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken));
			if (reply.Code < 0) {
				throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "諸掛確認に失敗しました。");
			}
			if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is not SundryChargeCheckResult result) {
				throw new InvalidOperationException("確認結果の解析に失敗しました。");
			}

			_allDetailRows = [.. result.DetailRows.Select(SundryChargeDetailRowVm.FromDto)];
			_allSummaryRows = [.. result.SummaryRows.Select(SundryChargeSummaryRowVm.FromDto)];
			_hasResult = true;
			ApplyRowFilter();

			ErrorCount = result.ErrorCount;
			WarningCount = result.WarningCount;
			InfoMessages = new ObservableCollection<string>(result.InfoMessages);

			StatusMessage = ErrorCount > 0
				? $"確認しました。明細 {_allDetailRows.Count:N0} 件・商品 {_allSummaryRows.Count:N0} 件（エラー {ErrorCount:N0} 件・警告 {WarningCount:N0} 件）。"
				: WarningCount > 0
					? $"確認しました。明細 {_allDetailRows.Count:N0} 件・商品 {_allSummaryRows.Count:N0} 件（警告 {WarningCount:N0} 件）。"
					: $"確認しました。明細 {_allDetailRows.Count:N0} 件・商品 {_allSummaryRows.Count:N0} 件。";
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

	private void ClearRows() {
		_allDetailRows = [];
		_allSummaryRows = [];
		_hasResult = false;
		DetailRows = [];
		SummaryRows = [];
		ErrorCount = 0;
		WarningCount = 0;
		InfoMessages = [];
	}

	private void ApplyRowFilter() {
		DetailRows = new ObservableCollection<SundryChargeDetailRowVm>(
			ShowErrorsAndWarningsOnly ? _allDetailRows.Where(x => x.IsErrorOrWarning) : _allDetailRows);
		SummaryRows = new ObservableCollection<SundryChargeSummaryRowVm>(
			ShowErrorsAndWarningsOnly ? _allSummaryRows.Where(x => x.IsErrorOrWarning) : _allSummaryRows);
	}

	// ------------------------------------------------------------------
	// CSV出力（§8.1「確認一覧と同じ列をUTF-8 BOM付きで出力」）
	// 明細行・集計行は列構成が異なる別表のため、1ファイル内に2ブロック（見出し行＋各表のヘッダ・データ）
	// として出力する。ファイルを分けると1回のCSV出力操作で2つの保存ダイアログが必要になり§8.1の
	// 操作感（1回のCSV出力ボタンで完結）を崩すため、単一ファイルへブロックを分けて書く方式を採る。
	// ------------------------------------------------------------------

	[RelayCommand]
	private void ExportCsv() {
		if (!_hasResult || (_allDetailRows.Count == 0 && _allSummaryRows.Count == 0)) {
			MessageEx.ShowWarningDialog("出力する明細がありません。先に確認を実行してください。", owner: ClientLib.GetActiveView(this));
			return;
		}

		var dialog = new SaveFileDialog {
			Title = "諸掛確認一覧をCSV出力",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			DefaultExt = ".csv",
			FileName = $"諸掛確認_{TargetMonth.Replace("/", string.Empty, StringComparison.Ordinal)}.csv",
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
		var details = ShowErrorsAndWarningsOnly ? _allDetailRows.Where(x => x.IsErrorOrWarning) : _allDetailRows;
		var summaries = ShowErrorsAndWarningsOnly ? _allSummaryRows.Where(x => x.IsErrorOrWarning) : _allSummaryRows;

		var sb = new StringBuilder();
		sb.AppendLine(CsvText.BuildLine(["明細行"]));
		sb.AppendLine(CsvText.BuildLine(DetailCsvHeader));
		foreach (var row in details) {
			sb.AppendLine(CsvText.BuildLine(row.ToCsvFields()));
		}
		sb.AppendLine();
		sb.AppendLine(CsvText.BuildLine(["商品別集計行"]));
		sb.AppendLine(CsvText.BuildLine(SummaryCsvHeader));
		foreach (var row in summaries) {
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

/// <summary>
/// 諸掛確認一覧の明細行の画面表示用ラッパー（<see cref="SundryChargeDetailRow"/>のDTOを整形する）。
/// </summary>
public sealed class SundryChargeDetailRowVm {
	public required long DenNo { get; init; }
	public required string DenDay { get; init; }
	public string DenDayText => CostPreviewDisplay.FormatYmd8ToSlash(DenDay);
	public required int Kubun { get; init; }
	public string KubunText => CostPreviewDisplay.FormatShiireKubun(Kubun);
	public required long Id_Shiire { get; init; }
	public required string MeiShiire { get; init; }
	public required int MeisaiNo { get; init; }
	public required long Id_Material { get; init; }
	public required string MeiMaterial { get; init; }
	public required long Id_Shohin { get; init; }
	public required string CodeShohin { get; init; }
	public required string MeiShohin { get; init; }
	public string ShohinText => Id_Shohin > 0 ? $"{CodeShohin} {MeiShohin}" : string.Empty;
	public required int Su { get; init; }
	public required long Kingaku { get; init; }
	public required EnumSundryCheckSeverity Severity { get; init; }
	public string SeverityText => CostPreviewDisplay.FormatSundryCheckSeverity(Severity);
	public required string ErrorMessage { get; init; }
	public bool IsError => Severity == EnumSundryCheckSeverity.Error;
	public bool IsWarning => Severity == EnumSundryCheckSeverity.Warning;
	public bool IsErrorOrWarning => Severity != EnumSundryCheckSeverity.Info;

	public static SundryChargeDetailRowVm FromDto(SundryChargeDetailRow row) => new() {
		DenNo = row.DenNo,
		DenDay = row.DenDay,
		Kubun = row.Kubun,
		Id_Shiire = row.Id_Shiire,
		MeiShiire = row.MeiShiire,
		MeisaiNo = row.MeisaiNo,
		Id_Material = row.Id_Material,
		MeiMaterial = row.MeiMaterial,
		Id_Shohin = row.Id_Shohin,
		CodeShohin = row.CodeShohin,
		MeiShohin = row.MeiShohin,
		Su = row.Su,
		Kingaku = row.Kingaku,
		Severity = row.Severity,
		ErrorMessage = row.ErrorMessage,
	};

	/// <summary>CSV出力用の列(<see cref="SundryChargesUpdateViewModel.DetailCsvHeader"/>のヘッダ順と一致させる)。</summary>
	public IEnumerable<string> ToCsvFields() => [
		DenNo.ToString(CultureInfo.InvariantCulture),
		DenDayText,
		KubunText,
		MeiShiire,
		MeisaiNo.ToString(CultureInfo.InvariantCulture),
		MeiMaterial,
		ShohinText,
		Su.ToString(CultureInfo.InvariantCulture),
		Kingaku.ToString(CultureInfo.InvariantCulture),
		SeverityText,
		ErrorMessage,
	];
}

/// <summary>
/// 諸掛確認一覧の商品別集計行の画面表示用ラッパー（<see cref="SundryChargeSummaryRow"/>のDTOを整形する）。
/// </summary>
public sealed class SundryChargeSummaryRowVm {
	public required long Id_Shohin { get; init; }
	public required string CodeShohin { get; init; }
	public required string MeiShohin { get; init; }
	public string ShohinText => $"{CodeShohin} {MeiShohin}";
	public required long SundryCount { get; init; }
	public required long SundryAmount { get; init; }
	public required long PurchaseQty { get; init; }
	public required long PurchaseAmount { get; init; }
	public required long OpeningQty { get; init; }
	public required EnumSundryCheckSeverity Severity { get; init; }
	public string SeverityText => CostPreviewDisplay.FormatSundryCheckSeverity(Severity);
	public required string ErrorMessage { get; init; }
	public bool IsError => Severity == EnumSundryCheckSeverity.Error;
	public bool IsWarning => Severity == EnumSundryCheckSeverity.Warning;
	public bool IsErrorOrWarning => Severity != EnumSundryCheckSeverity.Info;

	public static SundryChargeSummaryRowVm FromDto(SundryChargeSummaryRow row) => new() {
		Id_Shohin = row.Id_Shohin,
		CodeShohin = row.CodeShohin,
		MeiShohin = row.MeiShohin,
		SundryCount = row.SundryCount,
		SundryAmount = row.SundryAmount,
		PurchaseQty = row.PurchaseQty,
		PurchaseAmount = row.PurchaseAmount,
		OpeningQty = row.OpeningQty,
		Severity = row.Severity,
		ErrorMessage = row.ErrorMessage,
	};

	/// <summary>CSV出力用の列(<see cref="SundryChargesUpdateViewModel.SummaryCsvHeader"/>のヘッダ順と一致させる)。</summary>
	public IEnumerable<string> ToCsvFields() => [
		ShohinText,
		SundryCount.ToString(CultureInfo.InvariantCulture),
		SundryAmount.ToString(CultureInfo.InvariantCulture),
		PurchaseQty.ToString(CultureInfo.InvariantCulture),
		PurchaseAmount.ToString(CultureInfo.InvariantCulture),
		OpeningQty.ToString(CultureInfo.InvariantCulture),
		SeverityText,
		ErrorMessage,
	];
}
