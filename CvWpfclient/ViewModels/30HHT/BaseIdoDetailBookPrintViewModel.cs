using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using Newtonsoft.Json;
using System.Globalization;

namespace CvWpfclient.ViewModels._30HHT;

/// <summary>
/// HHT 移動明細書 / 即時移動明細書の共通基底（旧 SubDlg_08prn_hhtlist05 / 06）。
/// <para>
/// 帳票は旧 cvnet60prn02.qfm を移した <see cref="IdoDetailBookForm"/> を共用し、SELECT は旧と同じ34列の並びで出す。
/// 通常発行は IsPrint=0 の伝票だけを印刷して IsPrint=1 にする。再発行は IsPrint=1 の伝票を印刷し、更新しない。
/// </para>
/// </summary>
public abstract partial class BaseIdoDetailBookPrintViewModel<TDen> : BaseReportViewModel where TDen : TranAllHeader {
	/// <summary>移動明細書系で共用する帳票（旧 cvnet60prn02.qfm）</summary>
	public const string IdoDetailBookForm = "IdoDetailBook.qfm";

	protected override string? FormFileName => IdoDetailBookForm;

	/// <summary>対象テーブル名</summary>
	protected abstract string TableName { get; }

	/// <summary>帳票タイトル（item1）。qfm はこの文字列で表示するバーコードを切り替える</summary>
	protected abstract string BookTitle { get; }

	/// <summary>伝票の並び順（ヘッダ別名 h）。明細の並びはこの後ろに付く</summary>
	protected abstract string HeaderOrder { get; }

	/// <summary>true=通常発行（未発行のみ・発行済みにする） / false=再発行（発行済みのみ・更新しない）</summary>
	[ObservableProperty]
	public partial bool IsNormalIssue { get; set; } = true;

	[ObservableProperty]
	public partial string SokoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string SokoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string IdoCodeFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string IdoCodeTo { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenDayFrom { get; set; } = Today();

	[ObservableProperty]
	public partial string DenDayTo { get; set; } = Today();

	[ObservableProperty]
	public partial string DenNoFrom { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string DenNoTo { get; set; } = string.Empty;

	static string Today() => DateTime.Today.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

	[RelayCommand]
	void SelectSokoFrom() => SokoCodeFrom = SelectTokuiCode() ?? SokoCodeFrom;

	[RelayCommand]
	void SelectSokoTo() => SokoCodeTo = SelectTokuiCode() ?? SokoCodeTo;

	[RelayCommand]
	void SelectIdoFrom() => IdoCodeFrom = SelectTokuiCode() ?? IdoCodeFrom;

	[RelayCommand]
	void SelectIdoTo() => IdoCodeTo = SelectTokuiCode() ?? IdoCodeTo;

	[RelayCommand]
	void ClearConditions() {
		IsNormalIssue = true;
		SokoCodeFrom = SokoCodeTo = IdoCodeFrom = IdoCodeTo = DenNoFrom = DenNoTo = string.Empty;
		DenDayFrom = DenDayTo = Today();
		Message = "検索条件をクリアしました";
	}

	/// <summary>印刷は対象伝票を先に確定してから行うため、基底の DoOutputPdf は使わない</summary>
	protected override Task<QueryListSqlParam?> BuildPrintSqlParamAsync(CancellationToken ct) =>
		Task.FromResult<QueryListSqlParam?>(null);

	[RelayCommand(IncludeCancelCommand = true)]
	async Task Print(CancellationToken ct) {
		if (!TryGetTerm(out var from, out var to)) return;
		try {
			ClientLib.Cursor2Wait();
			var targets = await FetchTargetsAsync(from, to, ct);
			if (targets.Count == 0) {
				Message = IsNormalIssue
					? "未発行の伝票がありません。条件を確認するか、再発行を選んでください。"
					: "発行済みの伝票がありません。条件を確認してください。";
				MessageEx.ShowInformationDialog(Message, owner: ActiveWindow);
				return;
			}
			await RunPrintPdfAsync(FormFileName, null, BuildPrintSqlParam(targets.Select(x => x.Id)), ct);
			if (!IsNormalIssue) return;

			// 通常発行は印刷した伝票を発行済みにする（旧: 印刷FLG のビットを立てる）
			var update = new PartialUpdateParam(typeof(TDen), ["IsPrint"],
				[.. targets.Select(x => new PartialUpdateRow(x.Id, x.Vdu, ["1"]))]);
			var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg201_Op_Execute,
				DataType = typeof(PartialUpdateParam),
				DataMsg = Common.SerializeObject(update),
			}, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0) {
				Message = $"発行済み更新に失敗しました: {reply.DataMsg}";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}
			Message = $"{targets.Count}件を発行済みにしました";
		}
		catch (OperationCanceledException) {
			Message = "印刷をキャンセルしました";
		}
		catch (Exception ex) {
			Message = $"印刷に失敗しました: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	bool TryGetTerm(out DateTime from, out DateTime to) {
		to = default;
		if (!TryParseDate(DenDayFrom, out from) || !TryParseDate(DenDayTo, out to)) return false;
		if (from <= to) return true;
		MessageEx.ShowWarningDialog("移動日の範囲が逆転しています。", owner: ActiveWindow);
		return false;
	}

	/// <summary>条件に一致する伝票（Id / Vdu）を取得する。QueryListParam は別名なしの列名で書く</summary>
	async Task<List<TDen>> FetchTargetsAsync(DateTime from, DateTime to, CancellationToken ct) {
		List<string> parameters = [];
		var where = $"DenDay >= {AddSqlParameter(parameters, ToDenDay(from))} AND DenDay <= {AddSqlParameter(parameters, ToDenDay(to))}";
		where += BuildCodeRangeWhere(parameters, "ifnull(json_extract(VSoko,'$.Cd'),'')", SokoCodeFrom, SokoCodeTo);
		where += BuildCodeRangeWhere(parameters, "ifnull(json_extract(VIdo,'$.Cd'),'')", IdoCodeFrom, IdoCodeTo);
		if (long.TryParse(DenNoFrom.Trim(), out var noFrom)) where += $" AND Id >= {noFrom}";
		if (long.TryParse(DenNoTo.Trim(), out var noTo)) where += $" AND Id <= {noTo}";
		where += IsNormalIssue ? " AND ifnull(IsPrint,0) = 0" : " AND ifnull(IsPrint,0) = 1";

		var param = new QueryListParam(typeof(TDen), where, "Id", [.. parameters]);
		var reply = await AppGlobal.GetGrpcService<ICoreService>().QueryMsgAsync(new CvMsg {
			Code = 0, Flag = CvFlag.Msg101_Op_Query, DataType = typeof(QueryListParam), DataMsg = Common.SerializeObject(param),
		}, AppGlobal.GetDefaultCallContext(ct));
		// 0件は NotFound(-1) で返るため空リストにする
		if (reply.Code == CvMsgErrorCode.NotFound) return [];
		if (reply.Code < 0) throw new InvalidOperationException(string.IsNullOrEmpty(reply.Option) ? reply.DataMsg : reply.Option);
		return JsonConvert.DeserializeObject<List<TDen>>(reply.DataMsg) ?? [];
	}

	/// <summary>
	/// 帳票SQL。列順は旧 cvnet60prn02.qfm の item1..34 に合わせる。
	/// 旧で未使用の項目（tel・住所・原価FLG 等）は空で出す。移動日(item7)は qfm が yyyyMMdd を分割して整形するので加工しない。
	/// </summary>
	QueryListSqlParam BuildPrintSqlParam(IEnumerable<long> ids) {
		var idList = string.Join(",", ids);
		var jodai = TranMeisaiSql.Num("Jodai");
		var su = TranMeisaiSql.Num("Su");
		var sql = $@"
SELECT
    '{BookTitle}'                                AS title,
    ''                                           AS tel,
    {TranMeisaiSql.HeaderCode("VSoko")}          AS sokoCode,
    {TranMeisaiSql.HeaderName("VSoko")}          AS sokoName,
    {TranMeisaiSql.HeaderCode("VIdo")}           AS idoCode,
    {TranMeisaiSql.HeaderName("VIdo")}           AS idoName,
    h.DenDay                                     AS denDay,
    h.Id                                         AS denNo,
    {TranMeisaiSql.Str("Code_Shohin")}           AS shohinCode,
    ''                                           AS genkaFlg,
    {TranMeisaiSql.Str("Mei_Col")}               AS colName,
    {TranMeisaiSql.Str("Mei_Siz")}               AS sizName,
    {su}                                         AS su,
    ifnull(nullif(d.Jan1,''),{TranMeisaiSql.Str("JanCode")}) AS jan1,
    {TranMeisaiSql.Str("Mei_Shohin")}            AS shohinName,
    {su} * {jodai}                               AS jodaiKingaku,
    {TranMeisaiSql.HeaderCode("VShain")}         AS shainCode,
    {TranMeisaiSql.HeaderName("VShain")}         AS shainName,
    ifnull(d.Jan2,'')                            AS jan2,
    ''                                           AS genka,
    '' AS address3, '' AS fax, '' AS sokoFullName, '' AS postalCode, '' AS address1, '' AS address2,
    printf('%08d', h.Id)                         AS denNoBarcode,
    {jodai}                                      AS jodai,
    ''                                           AS orderNo,
    ''                                           AS nouhinDay,
    ifnull(h.Memo,'')                            AS memo,
    'JAN1段目'                                   AS janTitle1,
    'JAN2段目'                                   AS janTitle2,
    0                                            AS genkaKbn
FROM {TableName} h
JOIN {TranMeisaiSql.From}
LEFT JOIN DerivedShohinColSiz d
  ON d.Id_Shohin = {TranMeisaiSql.Num("Id_Shohin")}
 AND d.Id_Col = {TranMeisaiSql.Num("Id_Col")}
 AND d.Id_Siz = {TranMeisaiSql.Num("Id_Siz")}
WHERE {TranMeisaiSql.Guard}
  AND h.Id IN ({idList})
ORDER BY {HeaderOrder}, {TranMeisaiSql.Str("Code_Shohin")}, {TranMeisaiSql.Str("Code_Col")}, {TranMeisaiSql.Str("Code_Siz")}, {TranMeisaiSql.Num("No")}";
		return new QueryListSqlParam(typeof(object), sql, []);
	}
}
