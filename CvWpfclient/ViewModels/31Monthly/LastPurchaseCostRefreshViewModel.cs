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
/// 最終仕入原価更新画面（原価4項目 詳細設計 §5、§8.4）。
/// <see cref="BaseCostUpdateViewModel"/>の差分だけを実装する（<c>ConsumptionPurchaseUpdateViewModel</c>と同じ作り方）。
/// </summary>
public partial class LastPurchaseCostRefreshViewModel : BaseCostUpdateViewModel {
	private static readonly string[] CsvHeader = [
		"商品コード", "商品名", "前原価", "後原価", "最終仕入日", "仕入No", "明細No", "数量", "仕入金額", "状態", "エラー",
	];

	/// <summary>DataGridへ表示する現在の絞り込み結果。</summary>
	[ObservableProperty]
	public partial ObservableCollection<LastPurchasePreviewRowVm> Rows { get; set; } = [];

	/// <summary>
	/// 原価方式不一致（設計書§2.3、U-01）を検知したときのメッセージ。空文字なら不一致なし。
	/// サーバーは方式不一致のとき商品を特定しない1行だけを返す（<c>CostUpdateDbCost.NewCostMethodMismatchRow</c>）ため、
	/// 一覧の1行としてではなく専用のメッセージとして画面上部に明示する。
	/// </summary>
	[ObservableProperty]
	public partial string CostMethodMismatchMessage { get; set; } = string.Empty;

	/// <summary>確認(プレビュー)で取得した全行。<see cref="BaseCostUpdateViewModel.ShowErrorsOnly"/>の絞り込み前。</summary>
	private List<LastPurchasePreviewRowVm> _allRows = [];

	protected override CvFlag PreviewFlag => CvFlag.Msg084_CostLastPurchasePreview;
	protected override CvFlag ApplyFlag => CvFlag.Msg085_CostLastPurchaseApply;
	protected override EnumCostProcessKind ProcessKind => EnumCostProcessKind.CostUpdate;
	protected override string ScreenName => "最終仕入原価更新";
	protected override EnumCostMethod CostMethodForApply => EnumCostMethod.LastPurchase;

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
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "最終仕入原価更新の確認に失敗しました。");
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
		_allRows = [.. result.Rows.Select(LastPurchasePreviewRowVm.FromDto)];
		ApplyRowFilter();

		var errorCount = _allRows.Count(x => x.IsError);
		return new PreviewOutcome(_allRows.Count, errorCount, result.Confirmed);
	}

	protected override void ClearRows() {
		_allRows = [];
		Rows = [];
		CostMethodMismatchMessage = string.Empty;
	}

	protected override void ApplyRowFilter() {
		Rows = new ObservableCollection<LastPurchasePreviewRowVm>(
			ShowErrorsOnly ? _allRows.Where(x => x.IsError) : _allRows);
	}

	protected override string BuildCsvText() {
		var rows = ShowErrorsOnly ? _allRows.Where(x => x.IsError) : _allRows;
		var sb = new StringBuilder();
		sb.AppendLine(CsvText.BuildLine(CsvHeader));
		foreach (var row in rows) {
			sb.AppendLine(CsvText.BuildLine(row.ToCsvFields()));
		}
		return sb.ToString();
	}
}

/// <summary>
/// 最終仕入原価更新一覧の1行の画面表示用ラッパー（<see cref="CostPreviewRow"/>のDTOを整形する）。
/// <para>
/// 設計書§8.4の列「数量／仕入金額」は、採用した最終仕入明細の数量・金額であり、
/// <see cref="CostPreviewRow.PurchaseQty"/>・<see cref="CostPreviewRow.PurchaseAmount"/>で運ばれる
/// （<c>CostUpdateDbCost.ComputeLastPurchaseForMonth</c>が設定する）。
/// <c>AfterCost = round_away_from_zero(Kingaku / Su)</c>（§5.3）の計算根拠であり、
/// これが無いと利用者が確認一覧で結果を検算できない。
/// </para>
/// <para>
/// なお <c>TranGenka</c> へは§5.3のとおり0で保存する（最終仕入原価方式では使わない列のため）。
/// プレビュー行は保存行と別物である。総平均原価（§8.5「当月仕入数／当月仕入金額」）の
/// 同名プロパティとは意味が異なる点に注意する。
/// </para>
/// </summary>
public sealed class LastPurchasePreviewRowVm {
	public required long Id_Shohin { get; init; }
	public required string CodeShohin { get; init; }
	public required string MeiShohin { get; init; }
	public string ShohinText => $"{CodeShohin} {MeiShohin}";
	public required long BeforeCost { get; init; }
	public required long AfterCost { get; init; }
	public required string SourceDay { get; init; }
	public string SourceDayText => CostPreviewDisplay.FormatYmd8ToSlash(SourceDay);
	public required long SourceTranId { get; init; }
	public required int SourceLineNo { get; init; }
	/// <summary>設計書§8.4の「数量」列。採用した最終仕入明細の数量。</summary>
	public required long Qty { get; init; }
	public string QtyText => Qty.ToString("N0");
	/// <summary>設計書§8.4の「仕入金額」列。採用した最終仕入明細の金額。</summary>
	public required long Amount { get; init; }
	public string AmountText => Amount.ToString("N0");
	public required EnumCostCalcError Error { get; init; }
	public required string ErrorMessage { get; init; }
	public bool IsError => CostPreviewDisplay.IsErrorRow(Error, ErrorMessage);
	public string StatusText => CostPreviewDisplay.FormatRowStatus(Error, ErrorMessage);

	public static LastPurchasePreviewRowVm FromDto(CostPreviewRow row) => new() {
		Id_Shohin = row.Id_Shohin,
		CodeShohin = row.CodeShohin,
		MeiShohin = row.MeiShohin,
		BeforeCost = row.BeforeCost,
		AfterCost = row.AfterCost,
		SourceDay = row.SourceDay,
		SourceTranId = row.SourceTranId,
		SourceLineNo = row.SourceLineNo,
		Qty = row.PurchaseQty,
		Amount = row.PurchaseAmount,
		Error = row.Error,
		ErrorMessage = row.ErrorMessage,
	};

	/// <summary>CSV出力用の列(<see cref="LastPurchaseCostRefreshViewModel"/>のヘッダ順と一致させる)。</summary>
	public IEnumerable<string> ToCsvFields() => [
		CodeShohin,
		MeiShohin,
		BeforeCost.ToString(CultureInfo.InvariantCulture),
		AfterCost.ToString(CultureInfo.InvariantCulture),
		SourceDayText,
		SourceTranId.ToString(CultureInfo.InvariantCulture),
		SourceLineNo.ToString(CultureInfo.InvariantCulture),
		QtyText,
		AmountText,
		StatusText,
		ErrorMessage,
	];
}
