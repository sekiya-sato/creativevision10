using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CvAsset;
using CvBase;
using CvBase.Share;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace CvWpfclient.ViewModels._31Monthly;

/// <summary>
/// 総平均原価更新画面（原価4項目 詳細設計 §6、§8.5）。
/// <see cref="BaseCostUpdateViewModel"/>の差分だけを実装する（<c>ConsumptionPurchaseUpdateViewModel</c>と同じ作り方）。
/// <para>
/// §6.6「過去年月の再実行」対応: プレビューは対象月だけでなく再計算される後続月の行も
/// <see cref="CostPreviewRow.SumMonth"/>で区別できる形で返す。本画面はどの月の行か分かるように
/// 一覧へ「計上月」列を追加する方式を採った（設計書§8.5の列指定に計上月は無いが、後続月が混ざる以上
/// 月の区別が無いと一覧が使えないため。月ごとのグループ化ではなく列追加を選んだのは、
/// 既存4画面がいずれも単純な一段組DataGridであり、グループ化ヘッダーを導入する変更が本Stepの
/// スコープ（画面2つの実装）に対して重くなるため）。
/// </para>
/// </summary>
public partial class TotalAverageCostUpdateViewModel : BaseCostUpdateViewModel {
	private static readonly string[] CsvHeader = [
		"計上月", "商品コード", "商品名", "前原価", "後原価", "差額",
		"前月在庫数", "前月在庫金額", "当月仕入数", "当月仕入金額", "諸掛", "分母", "状態", "エラー",
	];

	/// <summary>DataGridへ表示する現在の絞り込み結果。</summary>
	[ObservableProperty]
	public partial ObservableCollection<TotalAveragePreviewRowVm> Rows { get; set; } = [];

	/// <summary>
	/// 原価方式不一致（設計書§2.3、U-01）を検知したときのメッセージ。空文字なら不一致なし。
	/// <see cref="LastPurchaseCostRefreshViewModel.CostMethodMismatchMessage"/>と同じ扱い。
	/// </summary>
	[ObservableProperty]
	public partial string CostMethodMismatchMessage { get; set; } = string.Empty;

	/// <summary>確認(プレビュー)で取得した全行。<see cref="BaseCostUpdateViewModel.ShowErrorsOnly"/>の絞り込み前。</summary>
	private List<TotalAveragePreviewRowVm> _allRows = [];

	protected override CvFlag PreviewFlag => CvFlag.Msg086_CostTotalAveragePreview;
	protected override CvFlag ApplyFlag => CvFlag.Msg087_CostTotalAverageApply;
	protected override EnumCostProcessKind ProcessKind => EnumCostProcessKind.CostUpdate;
	protected override string ScreenName => "総平均原価更新";
	protected override EnumCostMethod CostMethodForApply => EnumCostMethod.TotalAverage;

	protected override async Task<PreviewOutcome> RunPreviewAsync(CostUpdateParameter param, CancellationToken cancellationToken) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var message = new CvMsg {
			Code = 0,
			Flag = PreviewFlag,
			DataType = typeof(CostUpdateParameter),
			DataMsg = Common.SerializeObject(param),
		};
		var reply = await coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(cancellationToken));
		if (reply.Code < 0) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "総平均原価更新の確認に失敗しました。");
		}
		if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is not CostPreviewResult result) {
			throw new InvalidOperationException("確認結果の解析に失敗しました。");
		}

		if (CostPreviewDisplay.IsCostMethodMismatchOnly(result.Rows)) {
			// 原価方式不一致(§2.3)。一覧には出さず専用メッセージで明示する。CanUpdateを塞ぐため
			// エラー件数は1件として扱う(§2.4-2「エラーが1件でもあれば更新できない」と同じ扱い)。
			CostMethodMismatchMessage = result.Rows[0].ErrorMessage;
			_allRows = [];
			ApplyRowFilter();
			return new PreviewOutcome(0, 1, result.Confirmed);
		}

		CostMethodMismatchMessage = string.Empty;
		_allRows = [.. result.Rows.Select(TotalAveragePreviewRowVm.FromDto)];
		ApplyRowFilter();

		var errorCount = _allRows.Count(x => x.IsError);
		// 対象外(設計書§6.5「2026-09-06改訂」)はエラーではないため別集計する。エラー件数には算入しない。
		var excludedCount = _allRows.Count(x => !x.IsTarget && !x.IsError);
		return new PreviewOutcome(_allRows.Count, errorCount, result.Confirmed, excludedCount);
	}

	protected override void ClearRows() {
		_allRows = [];
		Rows = [];
		CostMethodMismatchMessage = string.Empty;
	}

	/// <summary>
	/// エラー行のみ・対象外行のみの2つの絞り込みを持つ(設計書§6.5「2026-09-06改訂」)。両方チェックした場合は
	/// エラー行のみを優先する(対象外はエラーではなく、両方はそもそも同時に真にならないため優先順位に実害はない)。
	/// </summary>
	private IEnumerable<TotalAveragePreviewRowVm> FilteredRows() =>
		ShowErrorsOnly ? _allRows.Where(x => x.IsError)
		: ShowExcludedOnly ? _allRows.Where(x => !x.IsTarget && !x.IsError)
		: _allRows;

	protected override void ApplyRowFilter() {
		Rows = new ObservableCollection<TotalAveragePreviewRowVm>(FilteredRows());
	}

	protected override string BuildCsvText() {
		var rows = FilteredRows();
		var sb = new StringBuilder();
		sb.AppendLine(CsvText.BuildLine(CsvHeader));
		foreach (var row in rows) {
			sb.AppendLine(CsvText.BuildLine(row.ToCsvFields()));
		}
		return sb.ToString();
	}
}

/// <summary>
/// 総平均原価更新一覧の1行の画面表示用ラッパー（<see cref="CostPreviewRow"/>のDTOを整形する）。
/// <para>
/// 「分母」列(設計書§8.5)は<c>OpeningQty + PurchaseQty</c>(設計書§6.4)。<see cref="CostPreviewRow"/>に
/// 分母そのものの列は無いため<see cref="Denominator"/>として画面側で算出する。
/// 「差額」列は設計書§6.6「変更前後差額を表示する」に対応し、<c>AfterCost - BeforeCost</c>とする。
/// </para>
/// </summary>
public sealed class TotalAveragePreviewRowVm {
	public required string SumMonth { get; init; }
	public string SumMonthText => CostPreviewDisplay.FormatYm6ToSlash(SumMonth);
	public required long Id_Shohin { get; init; }
	public required string CodeShohin { get; init; }
	public required string MeiShohin { get; init; }
	public string ShohinText => $"{CodeShohin} {MeiShohin}";
	public required long BeforeCost { get; init; }
	public required long AfterCost { get; init; }
	/// <summary>変更前後差額(設計書§6.6)。<c>AfterCost - BeforeCost</c>。</summary>
	public long DiffCost => AfterCost - BeforeCost;
	public required long OpeningQty { get; init; }
	public required long OpeningAmount { get; init; }
	public required long PurchaseQty { get; init; }
	public required long PurchaseAmount { get; init; }
	public required long SundryAmount { get; init; }
	/// <summary>分母(設計書§6.4)。<c>OpeningQty + PurchaseQty</c>。</summary>
	public long Denominator => OpeningQty + PurchaseQty;
	public required EnumCostCalcError Error { get; init; }
	public required string ErrorMessage { get; init; }
	/// <summary>対象商品か（設計書§6.5「2026-09-06改訂」）。falseは負在庫／前月在庫があるが原価0円。</summary>
	public required bool IsTarget { get; init; }
	/// <summary>対象外の理由。<see cref="IsTarget"/>=falseのときのみ設定される。</summary>
	public required string ExcludeReason { get; init; }
	public bool IsError => CostPreviewDisplay.IsErrorRow(Error, ErrorMessage);
	/// <summary>「状態」列（エラー／対象外／正常の3値。設計書§6.5「2026-09-06改訂」）。</summary>
	public string StatusText => CostPreviewDisplay.FormatCostPreviewRowStatus(IsTarget, Error, ErrorMessage);
	/// <summary>「エラー」列。エラー行はエラーメッセージ、対象外行は対象外理由を表示する。</summary>
	public string ReasonText => CostPreviewDisplay.FormatCostPreviewRowReason(IsTarget, Error, ErrorMessage, ExcludeReason);

	public static TotalAveragePreviewRowVm FromDto(CostPreviewRow row) => new() {
		SumMonth = row.SumMonth,
		Id_Shohin = row.Id_Shohin,
		CodeShohin = row.CodeShohin,
		MeiShohin = row.MeiShohin,
		BeforeCost = row.BeforeCost,
		AfterCost = row.AfterCost,
		OpeningQty = row.OpeningQty,
		OpeningAmount = row.OpeningAmount,
		PurchaseQty = row.PurchaseQty,
		PurchaseAmount = row.PurchaseAmount,
		SundryAmount = row.SundryAmount,
		Error = row.Error,
		ErrorMessage = row.ErrorMessage,
		IsTarget = row.IsTarget,
		ExcludeReason = row.ExcludeReason,
	};

	/// <summary>CSV出力用の列(<see cref="TotalAverageCostUpdateViewModel"/>のヘッダ順と一致させる)。</summary>
	public IEnumerable<string> ToCsvFields() => [
		SumMonthText,
		CodeShohin,
		MeiShohin,
		BeforeCost.ToString(CultureInfo.InvariantCulture),
		AfterCost.ToString(CultureInfo.InvariantCulture),
		DiffCost.ToString(CultureInfo.InvariantCulture),
		OpeningQty.ToString(CultureInfo.InvariantCulture),
		OpeningAmount.ToString(CultureInfo.InvariantCulture),
		PurchaseQty.ToString(CultureInfo.InvariantCulture),
		PurchaseAmount.ToString(CultureInfo.InvariantCulture),
		SundryAmount.ToString(CultureInfo.InvariantCulture),
		Denominator.ToString(CultureInfo.InvariantCulture),
		StatusText,
		ReasonText,
	];
}
