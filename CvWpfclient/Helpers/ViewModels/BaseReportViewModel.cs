/*
# description
BaseReportViewModel は「パラメータ入力 → SQL構築 → qfmフォームでPDF出力」型の帳票画面に共通する
状態・検証・SQLパラメータ採番を提供する ViewModel 基底クラスです。
PDF出力パイプライン本体（RunPrintPdfAsync）と選択ダイアログは BaseViewModel 側にあります。

派生クラスは BuildPrintSqlParamAsync を実装するだけで、印刷実行コマンド・キャンセル・終了確認が揃います。

# example
public partial class SampleReportViewModel : BaseReportViewModel {
	protected override string ReportTitle => "サンプル帳票";
	protected override string FormFileName => "SampleReport.qfm";
	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) { ... }
}
 */
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.Helpers;

public abstract partial class BaseReportViewModel : BaseViewModel {
	/// <summary>年月入力で受け付ける書式</summary>
	static readonly string[] YearMonthFormats = ["yyyy/MM", "yyyy/M", "yyyyMM", "yyyy-MM", "yyyy-M"];

	/// <summary>日付入力で受け付ける書式</summary>
	static readonly string[] DateFormats = ["yyyy/MM/dd", "yyyy/M/d", "yyyyMMdd", "yyyy-MM-dd", "yyyy-M-d"];

	[ObservableProperty]
	public partial string Title { get; set; } = string.Empty;

	/// <summary>画面下部などに表示する状態メッセージ</summary>
	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	/// <summary>画面タイトル。派生クラスで上書きする</summary>
	protected virtual string ReportTitle => string.Empty;

	/// <summary>使用する印刷フォーム（printform配下のファイル名）。派生クラスで上書きする</summary>
	protected virtual string? FormFileName => null;

	/// <summary>この ViewModel を DataContext に持つ Window（ダイアログの Owner 用）</summary>
	protected virtual Window? ActiveWindow => ClientLib.GetActiveView(this);

	protected BaseReportViewModel() {
		Title = ReportTitle;
	}

	protected virtual bool ConfirmAction(string message) =>
		MessageEx.ShowQuestionDialog(message, owner: ActiveWindow) == MessageBoxResult.Yes;

	/// <summary>SelectWinView で単一レコードを選択させる。キャンセル時は null。</summary>
	protected TResult? ShowSelectDialog<TResult>(Type tableType, string where, string order, long startPos = 0) where TResult : BaseDbClass =>
		PrintPdfHelper.ShowSelectDialog<TResult>(this, tableType, where, order, startPos);

	/// <summary>SelectMultiWinView で複数レコードを選択させる。キャンセル時は null。</summary>
	protected IReadOnlyList<TResult>? ShowMultiSelectDialog<TResult>(Type tableType, string where, string order, IEnumerable<long>? selectedIds = null, long startPos = 0) where TResult : BaseDbClass =>
		PrintPdfHelper.ShowMultiSelectDialog<TResult>(this, tableType, where, order, selectedIds, startPos);

	/// <summary>
	/// 指定したフォームファイルと印刷データ(CSV または SQL)で PDF を生成し、PDF表示画面を開く。
	/// 1画面で複数の帳票を出し分けたい場合は、この保護メソッドを個別コマンドから直接呼び出す。
	/// </summary>
	protected async Task RunPrintPdfAsync(string? formFile, PrintByCsvParam? csvParam, QueryListSqlParam? sqlParam, CancellationToken ct) =>
		await PrintPdfHelper.RunPrintPdfAsync(this, ActiveWindow, m => Message = m, formFile, csvParam, sqlParam, ct);

	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}
		ClientLib.Exit(this);
	}

	/// <summary>ContentRendered から呼ばれる初期化。必要な画面のみ上書きする</summary>
	[RelayCommand]
	protected virtual void Init() { }

	/// <summary>
	/// 印刷用SQLを構築する。パラメータ不正などで印刷しない場合は null を返す
	/// （その場合の利用者向けメッセージ表示は派生クラス側の責務）。
	/// </summary>
	protected abstract Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct);

	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task DoOutputPdf(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		try {
			ClientLib.Cursor2Wait();
			var sqlParam = await BuildPrintSqlParamAsync(ct);
			if (sqlParam == null) return;
			await RunPrintPdfAsync(FormFileName, null, sqlParam, ct);
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	/// <summary>
	/// SQLへ埋め込むユーザ入力値を `@n` プレースホルダとして採番する。
	/// 戻り値をそのままSQL文へ連結する。
	/// </summary>
	protected static string AddSqlParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	/// <summary>
	/// コード範囲（〜）のWHERE句を組み立てる。空欄の側は条件を付けない。
	/// </summary>
	protected static string BuildCodeRangeWhere(List<string> parameters, string columnName, string? codeFrom, string? codeTo) {
		var where = "";
		if (!string.IsNullOrWhiteSpace(codeFrom)) {
			where += $" AND {columnName} >= {AddSqlParameter(parameters, codeFrom.Trim())}";
		}
		if (!string.IsNullOrWhiteSpace(codeTo)) {
			where += $" AND {columnName} <= {AddSqlParameter(parameters, codeTo.Trim())}";
		}
		return where;
	}

	/// <summary>
	/// 年月文字列を検証する。不正なら警告を出して false。
	/// </summary>
	protected bool TryParseYearMonth(string? text, out DateTime yearMonth) {
		yearMonth = default;
		var value = (text ?? string.Empty).Trim();
		if (!DateTime.TryParseExact(value, YearMonthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
			MessageEx.ShowWarningDialog("年月は yyyy/MM 形式で入力してください。", owner: ActiveWindow);
			return false;
		}
		yearMonth = new DateTime(parsed.Year, parsed.Month, 1);
		return true;
	}

	/// <summary>
	/// 日付文字列を検証する。不正なら警告を出して false。空欄許可時は空欄で true を返す。
	/// </summary>
	protected bool TryParseDate(string? text, out DateTime date, bool allowEmpty = false) {
		date = default;
		var value = (text ?? string.Empty).Trim();
		if (value.Length == 0) {
			return allowEmpty;
		}
		if (!DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) {
			MessageEx.ShowWarningDialog("日付は yyyy/MM/dd 形式で入力してください。", owner: ActiveWindow);
			return false;
		}
		return true;
	}

	/// <summary>指定年月の月初〜月末を yyyyMMdd で返す</summary>
	protected static (string dateFrom, string dateTo) GetMonthRange(DateTime yearMonth) {
		var days = DateTime.DaysInMonth(yearMonth.Year, yearMonth.Month);
		return (
			new DateTime(yearMonth.Year, yearMonth.Month, 1).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
			new DateTime(yearMonth.Year, yearMonth.Month, days).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
		);
	}

	/// <summary>DateTime を伝票日付列と同じ yyyyMMdd 文字列にする</summary>
	protected static string ToDenDay(DateTime date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	/// <summary>店舗（直営店）選択ダイアログ。選択されなければ null</summary>
	protected string? SelectShopCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code")?.Code;

	/// <summary>得意先選択ダイアログ。選択されなければ null</summary>
	protected string? SelectTokuiCode() =>
		ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "", "Code")?.Code;

	/// <summary>仕入先選択ダイアログ。選択されなければ null</summary>
	protected string? SelectShiireCode() =>
		ShowSelectDialog<MasterShiire>(typeof(MasterShiire), "", "Code")?.Code;

	/// <summary>商品選択ダイアログ。選択されなければ null</summary>
	protected string? SelectShohinCode() =>
		ShowSelectDialog<MasterShohin>(typeof(MasterShohin), "", "Code")?.Code;

	/// <summary>社員選択ダイアログ。選択されなければ null</summary>
	protected string? SelectShainCode() =>
		ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code")?.Code;
}
