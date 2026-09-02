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
	public partial List<long> ShohinIds { get; set; } = [];

	[ObservableProperty]
	public partial string ShohinIdsText { get; set; } = "未選択";

	[ObservableProperty]
	public partial string ShohinCodeLike { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ShohinNameLike { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsHorizontalOutput { get; set; } = true;

	[ObservableProperty]
	public partial bool IsVerticalOutput { get; set; }

	partial void OnIsHorizontalOutputChanged(bool value) {
		if (!value) return;
		IsVerticalOutput = false;
	}

	partial void OnIsVerticalOutputChanged(bool value) {
		if (!value) return;
		IsHorizontalOutput = false;
	}

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
			if (IsVerticalOutput) {
				if (IsCode39Barcode) return "MasterPrintBarcodeView_v_code39.qfm";
				if (IsNw7Barcode) return "MasterPrintBarcodeView_v_nw7.qfm";
				return "MasterPrintBarcodeView_v_jan.qfm";
			}

			if (IsCode39Barcode) return "MasterPrintBarcodeView_h_code39.qfm";
			if (IsNw7Barcode) return "MasterPrintBarcodeView_h_nw7.qfm";
			return "MasterPrintBarcodeView_h_jan.qfm";
		}
	}

	protected override string? ListWhere => BuildListWhere([]);

	protected override string? ListOrder => "S.Id_Tenji, S.Code, D.Code_Col, D.Code_Siz, D.RowIdx";

	string? BuildListWhere(List<string> parameters) {
		List<string> clauses = [];
		AddSelectedIdInClause(clauses, "S.Id_Tenji", TenjiIds);
		AddSelectedIdInClause(clauses, "S.Id_Brand", BrandIds);
		AddSelectedIdInClause(clauses, "S.Id", ShohinIds);
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
	/// バーコードqfm（<c>MasterPrintBarcodeView_*.qfm</c>）は item1〜item7 を列の並び順で読むため、
	/// 列名ではなく並び順（商品CD, 商品名, サイズ, 色, JANコード1〜3）を必ず維持すること。
	/// </summary>
	string BuildPrintSql(out string[] parameters) {
		var where = BuildWhereSql(out parameters);
		return @$"
select
S.Code 商品CD,
S.Name 商品名,
ifnull(D.Mei_Siz, '') サイズ,
ifnull(D.Mei_Col, '') 色,
ifnull(D.Jan1, '') JANコード1,
ifnull(D.Jan2, '') JANコード2,
ifnull(D.Jan3, '') JANコード3
from MasterShohin S
left join DerivedShohinColSiz D on D.Id_Shohin = S.Id
{where}
order by S.Id_Tenji, S.Code, D.Code_Col, D.Code_Siz, D.RowIdx
";
	}

	string BuildCountSql(out string[] parameters) {
		var where = BuildWhereSql(out parameters);
		return $@"
select count(*)
from MasterShohin S
left join DerivedShohinColSiz D on D.Id_Shohin = S.Id
{where}";
	}

	string BuildWhereSql(out string[] parameters) {
		List<string> list = [];
		var where = BuildListWhere(list);
		parameters = [.. list];
		return string.IsNullOrWhiteSpace(where) ? string.Empty : $"where {where}";
	}

	[RelayCommand]
	void SelectTenjiIds() {
		var selected = ShowMultiSelectDialog<MasterMeisho>(
			typeof(MasterMeisho),
			$"Kubun='{MasterMeisho.KubunTenji}'",
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
			$"Kubun='{MasterMeisho.KubunBrand}'",
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
	void SelectShohinIds() {
		var selected = ShowMultiSelectDialog<MasterShohin>(
			typeof(MasterShohin),
			"",
			"Code",
			ShohinIds,
			ShohinIds.FirstOrDefault());
		if (selected == null) return;
		ShohinIds = [.. selected.Select(x => x.Id)];
		ShohinIdsText = BuildSelectedText(selected);
	}

	[RelayCommand]
	void ClearShohinIds() {
		ShohinIds = [];
		ShohinIdsText = "未選択";
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

	static string BuildSelectedText(IReadOnlyList<MasterShohin> selected) {
		if (selected.Count == 0) return "未選択";
		return $"{selected.Count}件: {string.Join(", ", selected.Select(FormatSelectedItem))}";
	}

	// 表示書式は XAML 側の V*列共通表示(CodeNameViewDisplayConverter)と揃える
	static string FormatSelectedItem(MasterMeisho item) =>
		CodeNameDisplay.Format(item.Id, item.Code, item.Name);

	static string FormatSelectedItem(MasterShohin item) =>
		CodeNameDisplay.Format(item.Id, item.Code, item.Name);

	static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
