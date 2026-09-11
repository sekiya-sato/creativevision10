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
/// 消化仕入更新画面（原価4項目 詳細設計 §4、§8.3）。
/// <see cref="BaseCostUpdateViewModel"/>の差分だけを実装する。
/// </summary>
public partial class ConsumptionPurchaseUpdateViewModel : BaseCostUpdateViewModel {
	private static readonly string[] CsvHeader = [
		"売上種別", "売上No", "売上日", "商品コード", "商品名", "数量", "仕入先",
		"計算区分", "掛率", "生成単価", "生成金額", "税額", "状態", "エラー",
	];

	/// <summary>DataGridへ表示する現在の絞り込み結果。</summary>
	[ObservableProperty]
	public partial ObservableCollection<ConsumptionPreviewRowVm> Rows { get; set; } = [];

	/// <summary>確認(プレビュー)で取得した全行。<see cref="BaseCostUpdateViewModel.ShowErrorsOnly"/>の絞り込み前。</summary>
	private List<ConsumptionPreviewRowVm> _allRows = [];

	protected override CvFlag PreviewFlag => CvFlag.Msg081_CostConsumptionPreview;
	protected override CvFlag ApplyFlag => CvFlag.Msg082_CostConsumptionApply;
	protected override EnumCostProcessKind ProcessKind => EnumCostProcessKind.ConsumptionPurchase;
	protected override string ScreenName => "消化仕入更新";

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
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "消化仕入更新の確認に失敗しました。");
		}
		if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is not ConsumptionPreviewResult result) {
			throw new InvalidOperationException("確認結果の解析に失敗しました。");
		}

		_allRows = [.. result.Rows.Select(ConsumptionPreviewRowVm.FromDto)];
		ApplyRowFilter();

		var errorCount = _allRows.Count(x => x.IsError);
		return new PreviewOutcome(_allRows.Count, errorCount, result.Confirmed);
	}

	protected override void ClearRows() {
		_allRows = [];
		Rows = [];
	}

	protected override void ApplyRowFilter() {
		Rows = new ObservableCollection<ConsumptionPreviewRowVm>(
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
/// 消化仕入更新一覧の1行の画面表示用ラッパー（<see cref="ConsumptionPreviewRow"/>のDTOを整形する）。
/// 値は確認(プレビュー)取得時に固定されるため、編集通知は不要（<c>ObservableObject</c>を継承しない）。
/// </summary>
public sealed class ConsumptionPreviewRowVm {
	public required EnumConsumptionSourceType SourceType { get; init; }
	public string SourceTypeText => CostPreviewDisplay.FormatConsumptionSourceType(SourceType);
	public required long SourceId { get; init; }
	public required int SourceLineNo { get; init; }
	public required string SourceDay { get; init; }
	public string SourceDayText => CostPreviewDisplay.FormatYmd8ToSlash(SourceDay);
	public required long Id_Shohin { get; init; }
	public required string CodeShohin { get; init; }
	public required string MeiShohin { get; init; }
	public string ShohinText => $"{CodeShohin} {MeiShohin}";
	public required long Su { get; init; }
	public required long Id_Shiire { get; init; }
	public required string MeiShiire { get; init; }
	public required EnumConsumptionCalcType CalcType { get; init; }
	public string CalcTypeText => CostPreviewDisplay.FormatConsumptionCalcType(CalcType);
	public required int RateBasisPoints { get; init; }
	public string RateText => RateBasisPoints > 0 ? CostPreviewDisplay.FormatRateBasisPoints(RateBasisPoints) : string.Empty;
	public required long UnitCost { get; init; }
	public required long Kingaku { get; init; }
	public required long Tax { get; init; }
	public required EnumCostCalcError Error { get; init; }
	public required string ErrorMessage { get; init; }
	public bool IsError => CostPreviewDisplay.IsErrorRow(Error, ErrorMessage);
	public string StatusText => CostPreviewDisplay.FormatRowStatus(Error, ErrorMessage);

	public static ConsumptionPreviewRowVm FromDto(ConsumptionPreviewRow row) => new() {
		SourceType = row.SourceType,
		SourceId = row.SourceId,
		SourceLineNo = row.SourceLineNo,
		SourceDay = row.SourceDay,
		Id_Shohin = row.Id_Shohin,
		CodeShohin = row.CodeShohin,
		MeiShohin = row.MeiShohin,
		Su = row.Su,
		Id_Shiire = row.Id_Shiire,
		MeiShiire = row.MeiShiire,
		CalcType = row.CalcType,
		RateBasisPoints = row.RateBasisPoints,
		UnitCost = row.UnitCost,
		Kingaku = row.Kingaku,
		Tax = row.Tax,
		Error = row.Error,
		ErrorMessage = row.ErrorMessage,
	};

	/// <summary>CSV出力用の列(<see cref="ConsumptionPurchaseUpdateViewModel"/>のヘッダ順と一致させる)。</summary>
	public IEnumerable<string> ToCsvFields() => [
		SourceTypeText,
		SourceId.ToString(CultureInfo.InvariantCulture),
		SourceDayText,
		CodeShohin,
		MeiShohin,
		Su.ToString(CultureInfo.InvariantCulture),
		MeiShiire,
		CalcTypeText,
		RateText,
		UnitCost.ToString(CultureInfo.InvariantCulture),
		Kingaku.ToString(CultureInfo.InvariantCulture),
		Tax.ToString(CultureInfo.InvariantCulture),
		StatusText,
		ErrorMessage,
	];
}
