using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections;
using System.Globalization;

namespace CvWpfclient.ViewModels._01Master;

public partial class MasterPrintBarcodeViewModel : BaseMenteViewModel<MasterShohin> {
	[ObservableProperty]
	public partial string Title { get; set; } = "商品バーコードブック";

	[ObservableProperty]
	public partial List<long> TenjiIds { get; set; } = [];

	[ObservableProperty]
	public partial string TenjiIdsText { get; set; } = "未選択";

	[ObservableProperty]
	public partial List<long> BrandIds { get; set; } = [];

	[ObservableProperty]
	public partial string BrandIdsText { get; set; } = "未選択";

	[ObservableProperty]
	public partial string ShohinCodeLike { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinNameLike { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsSkuOutput { get; set; } = true;

	[ObservableProperty]
	public partial bool IsJanBarcode { get; set; } = true;

	[ObservableProperty]
	public partial bool IsCode39Barcode { get; set; }

	[ObservableProperty]
	public partial bool IsNw7Barcode { get; set; }

	partial void OnIsJanBarcodeChanged(bool value) {
		if (!value) return;
		IsCode39Barcode = false;
		IsNw7Barcode = false;
	}

	partial void OnIsCode39BarcodeChanged(bool value) {
		if (!value) return;
		IsJanBarcode = false;
		IsNw7Barcode = false;
	}

	partial void OnIsNw7BarcodeChanged(bool value) {
		if (!value) return;
		IsJanBarcode = false;
		IsCode39Barcode = false;
	}

	protected override string? FormFile {
		get {
			if (IsSkuOutput) {
				if (IsCode39Barcode) return "MasterPrintBarcode0021.qfm";
				if (IsNw7Barcode) return "MasterPrintBarcode0022.qfm";
				return "MasterPrintBarcode002.qfm";
			}

			if (IsCode39Barcode) return "MasterPrintBarcodeCode39.qfm";
			if (IsNw7Barcode) return "MasterPrintBarcodeNw7.qfm";
			return "MasterPrintBarcodeSho.qfm";
		}
	}

	protected override string? ListWhere => BuildListWhere([]);

	protected override string? ListOrder => IsSkuOutput
		? "S.Id_Tenji, S.Code, D.Code_Col, D.Code_Siz, D.RowIdx"
		: "S.Id_Tenji, S.Code";

	string? BuildListWhere(List<string> parameters) {
		List<string> clauses = [];
		AddSelectedIdInClause(clauses, "S.Id_Tenji", TenjiIds);
		AddSelectedIdInClause(clauses, "S.Id_Brand", BrandIds);
		AddLike(clauses, parameters, "S.Code", ShohinCodeLike);
		AddLike(clauses, parameters, "S.Name", ShohinNameLike);
		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var sql = BuildPrintSql(out var parameters);
			return new QueryListSqlParam(typeof(MasterShohin), sql, parameters);
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task OutputPdf(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();

		try {
			ClientLib.Cursor2Wait();
			Message = "印刷対象件数を確認しています";
			var count = await GetPrintCountAsync(ct);
			var maxCount = AppGlobal.Application.Limit;
			if (count <= 0) {
				Message = "印刷対象データがありませんでした";
				MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
				return;
			}

			if (maxCount > 0 && count > maxCount) {
				Message = $"印刷対象が最大件数 {maxCount:N0} 件を超えています。対象件数: {count:N0} 件。条件を絞り込んでください。";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}

			Message = $"{count:N0} 件を印刷します";
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
			return;
		}
		catch (Exception ex) {
			Message = $"印刷対象件数確認失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
			return;
		}
		finally {
			ClientLib.Cursor2Normal();
		}

		await DoOutputPdf(ct);
	}

	async Task<long> GetPrintCountAsync(CancellationToken ct) {
		var sql = BuildCountSql(out var parameters);
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(long), sql, parameters))
		};

		var reply = await SendMessageAsync(msg, ct);
		ct.ThrowIfCancellationRequested();
		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
		}

		if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is not IList list || list.Count == 0) {
			return 0;
		}

		return Convert.ToInt64(list[0], CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// 値札・棚札へ印字する価格のSQL断片。
	/// <para>
	/// 上代一括変更(<see cref="DerivedJodai"/>)の適用行があればその価格を、無ければ従来どおり
	/// <see cref="MasterShohin.TankaJodaiOrg"/>（元上代）を印字する。セール中は適用価格を刷る運用。
	/// </para>
	/// <para>
	/// フォールバックが <c>TankaJodai</c> ではなく <c>TankaJodaiOrg</c> なので、
	/// <see cref="DerivedJodai.FinalJodaiSql"/> ではなく <see cref="DerivedJodai.ResolveSql"/> を直接包む。
	/// これにより適用行が無い環境では印字内容が一切変わらない。
	/// </para>
	/// <para>
	/// <b>対象軸は店舗系の全件行（<see cref="EnumJodaiTaisho.Tenpo"/> / Id_Tenpo=0）＝全直営店共通の価格。</b>
	/// この画面の抽出条件は展示会・ブランド・商品CD/商品名だけで店舗を指定できないため、
	/// 店舗ごとに価格を変えた分は反映されない。店舗別の値札が必要になったら画面へ店舗指定を足すこと。
	/// </para>
	/// </summary>
	static string JodaiPrintSql {
		get {
			var taisho = ((int)EnumJodaiTaisho.Tenpo).ToString(CultureInfo.InvariantCulture);
			var resolve = DerivedJodai.ResolveSql("S.Id", taisho, "0", DerivedJodai.TodaySql);
			return $"ifnull({resolve}, ifnull(S.TankaJodaiOrg,0))";
		}
	}

	string BuildPrintSql(out string[] parameters) {
		var where = BuildWhereSql(out parameters);
		var conditionColumns = BuildConditionColumns();
		if (IsSkuOutput) {
			return @$"
select {conditionColumns},
S.Code 商品CD,
S.Name 商品名,
{JodaiPrintSql} 元上代,
ifnull(json_extract(S.VTenji, '$.Cd'), '') 展示会CD,
ifnull(json_extract(S.VTenji, '$.Mei'), '') 展示会名,
ifnull(json_extract(S.VBrand, '$.Cd'), '') ブランドCD,
ifnull(json_extract(S.VBrand, '$.Mei'), '') ブランド名,
ifnull(json_extract(S.VItem, '$.Cd'), '') アイテムCD,
ifnull(json_extract(S.VItem, '$.Mei'), '') アイテム名,
S.DayShukka デリバリー日,
__serverimg__(S.Code) 絵型名,
ifnull(D.Mei_Col, '') 色名,
ifnull(D.Mei_Siz, '') サイズ名,
ifnull(D.Jan1, '') JANコード1,
case when D.Jan2='' then D.Jan3 end JANコード2
from MasterShohin S
left join DerivedShohinColSiz D on D.Id_Shohin = S.Id
{where}
order by S.Id_Tenji, S.Code, D.Code_Col, D.Code_Siz, D.RowIdx
";
		}

		return @$"
select {conditionColumns},
S.Code 商品CD,
S.Name 商品名,
{JodaiPrintSql} 元上代,
ifnull(json_extract(S.VTenji, '$.Cd'), '') 展示会CD,
ifnull(json_extract(S.VTenji, '$.Mei'), '') 展示会名,
ifnull(json_extract(S.VBrand, '$.Cd'), '') ブランドCD,
ifnull(json_extract(S.VBrand, '$.Mei'), '') ブランド名,
ifnull(json_extract(S.VItem, '$.Cd'), '') アイテムCD,
ifnull(json_extract(S.VItem, '$.Mei'), '') アイテム名,
S.DayShukka デリバリー日,
__serverimg__(S.Code) 絵型名,
ifnull((
	select ifnull(D.Jan1, ifnull(D.Jan2, ifnull(D.Jan3, '')))
	from DerivedShohinColSiz D
	where D.Id_Shohin = S.Id and ifnull(D.Jan1, ifnull(D.Jan2, ifnull(D.Jan3, ''))) <> ''
	order by D.Code_Col, D.Code_Siz, D.RowIdx
	limit 1
), '') JANコード
from MasterShohin S
{where}
order by S.Id_Tenji, S.Code
";
	}

	string BuildCountSql(out string[] parameters) {
		var where = BuildWhereSql(out parameters);
		var fromSql = IsSkuOutput
			? $@"
from MasterShohin S
left join DerivedShohinColSiz D on D.Id_Shohin = S.Id
{where}"
			: $@"
from MasterShohin S
{where}";
		return $"select count(*) {fromSql}";
	}

	string BuildWhereSql(out string[] parameters) {
		List<string> list = [];
		var where = BuildListWhere(list);
		parameters = [.. list];
		return string.IsNullOrWhiteSpace(where) ? string.Empty : $"where {where}";
	}

	string BuildConditionColumns() =>
		$"{SqlLiteral(TenjiIdsText)} 範囲0,{SqlLiteral(string.Empty)} 範囲1,{SqlLiteral(BrandIdsText)} 範囲2,{SqlLiteral(string.Empty)} 範囲3,{SqlLiteral(ShohinCodeLike)} 範囲4,{SqlLiteral(ShohinNameLike)} 範囲5";

	[RelayCommand]
	void SelectTenjiIds() {
		var selected = ShowMultiSelectDialog<MasterMeisho>(
			typeof(MasterMeisho),
			"Kubun='TNJ'",
			"Code",
			TenjiIds,
			TenjiIds.FirstOrDefault());
		if (selected == null) return;
		TenjiIds = [.. selected.Select(x => x.Id)];
		TenjiIdsText = BuildSelectedText(selected);
	}

	[RelayCommand]
	void ClearTenjiIds() {
		TenjiIds = [];
		TenjiIdsText = "未選択";
	}

	[RelayCommand]
	void SelectBrandIds() {
		var selected = ShowMultiSelectDialog<MasterMeisho>(
			typeof(MasterMeisho),
			"Kubun='BRD'",
			"Code",
			BrandIds,
			BrandIds.FirstOrDefault());
		if (selected == null) return;
		BrandIds = [.. selected.Select(x => x.Id)];
		BrandIdsText = BuildSelectedText(selected);
	}

	[RelayCommand]
	void ClearBrandIds() {
		BrandIds = [];
		BrandIdsText = "未選択";
	}

	[RelayCommand]
	async Task Init() {
		await Task.CompletedTask;
	}

	static void AddLike(List<string> clauses, List<string> parameters, string column, string? value) {
		var normalized = Normalize(value);
		if (normalized.Length == 0) return;
		clauses.Add($"{column} LIKE {AddParameter(parameters, $"%{normalized}%")}");
	}

	static string AddParameter(List<string> parameters, string value) {
		parameters.Add(value);
		return $"@{parameters.Count - 1}";
	}

	static string BuildSelectedText(IReadOnlyList<MasterMeisho> selected) {
		if (selected.Count == 0) return "未選択";
		return $"{selected.Count}件: {string.Join(", ", selected.Select(FormatSelectedItem))}";
	}

	// 表示書式は XAML 側の V*列共通表示(CodeNameViewDisplayConverter)と揃える
	static string FormatSelectedItem(MasterMeisho item) =>
		CodeNameDisplay.Format(item.Id, item.Code, item.Name);

	static string Normalize(string? value) => value?.Trim() ?? string.Empty;

	static string SqlLiteral(string? value) => $"'{EscapeSqlLiteral(Normalize(value))}'";
}
