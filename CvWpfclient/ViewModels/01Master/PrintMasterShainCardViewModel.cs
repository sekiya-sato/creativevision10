using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._01Master;

public partial class PrintMasterShainCardViewModel : BaseMenteViewModel<MasterShain> {
	[ObservableProperty]
	string title = "社員証カード印刷";

	[ObservableProperty]
	long? shainIdFrom;

	[ObservableProperty]
	long? shainIdTo;

	[ObservableProperty]
	string shainCodeFrom = string.Empty;

	[ObservableProperty]
	string shainCodeTo = string.Empty;

	[ObservableProperty]
	List<long> tenpoIds = [];

	[ObservableProperty]
	string tenpoIdsText = "未選択";

	[ObservableProperty]
	bool isCode39 = true;

	protected override string? FormFile => IsCode39 ? "PrintMasterShainCard39.qfm" : "PrintMasterShainCard.qfm";

	protected override string? ListWhere => BuildListWhere();

	protected override string? ListOrder => "A.Code";
	string? BuildListWhere() {
		var clauses = new List<string>();

		if (ShainIdFrom.HasValue) {
			clauses.Add($"A.Id >= {ShainIdFrom.Value}");
		}
		if (ShainIdTo.HasValue) {
			clauses.Add($"A.Id <= {ShainIdTo.Value}");
		}

		if (!string.IsNullOrWhiteSpace(ShainCodeFrom)) {
			clauses.Add($"A.Code >= '{EscapeSqlLiteral(ShainCodeFrom)}'");
		}
		if (!string.IsNullOrWhiteSpace(ShainCodeTo)) {
			clauses.Add($"A.Code <= '{EscapeSqlLiteral(ShainCodeTo)}'");
		}

		AddSelectedIdInClause(clauses, "A.Id_Tenpo", TenpoIds);

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	[RelayCommand]
	void SelectShainIdFrom() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Id", startPos: ShainIdFrom ?? 0);
		if (shain == null) return;
		ShainIdFrom = shain.Id;
	}

	[RelayCommand]
	void SelectShainIdTo() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Id", startPos: ShainIdTo ?? 0);
		if (shain == null) return;
		ShainIdTo = shain.Id;
	}

	[RelayCommand]
	void SelectShainCodeFrom() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code");
		ShainCodeFrom = shain?.Code ?? string.Empty;
	}

	[RelayCommand]
	void SelectShainCodeTo() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code");
		ShainCodeTo = shain?.Code ?? string.Empty;
	}

	[RelayCommand]
	void SelectTenpoIds() {
		var selected = ShowMultiSelectDialog<MasterTokui>(
			typeof(MasterTokui),
			"TenType=6",
			"Code",
			TenpoIds,
			TenpoIds.FirstOrDefault());
		if (selected == null) return;
		TenpoIds = [.. selected.Select(x => x.Id)];
		TenpoIdsText = BuildSelectedText(selected);
	}

	[RelayCommand]
	void ClearTenpoIds() {
		TenpoIds = [];
		TenpoIdsText = "未選択";
	}

	static string BuildSelectedText(IReadOnlyList<MasterTokui> selected) {
		if (selected.Count == 0) return "未選択";
		return $"{selected.Count}件: {string.Join(", ", selected.Select(FormatSelectedItem))}";
	}

	static string FormatSelectedItem(MasterTokui item) {
		var label = JoinCodeName(item.Code, item.Name);
		if (label.Length == 0) return item.Id.ToString();
		return $"{item.Id} {label}";
	}

	static string JoinCodeName(string? code, string? name) {
		var cd = code?.Trim() ?? string.Empty;
		var mei = name?.Trim() ?? string.Empty;
		if (cd.Length == 0) return mei;
		if (mei.Length == 0) return cd;
		return $"{cd} {mei}";
	}

	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var query = CreateListQueryParam();
			var sql = $@"
select A.Code, A.Name,
__serverimgshain__(A.Code) 画像,
A.id_Tenpo,
coalesce(T.Name, '') 店舗名,
coalesce((select S.Name from MasterSysMan S limit 1), '') 自社名,
case when coalesce(json_extract(A.Jdetail, '$.yobi1'), '')='' then 0 else 1 end 画像表示判定用
from MasterShain A
left join MasterTokui T on T.Id = A.id_Tenpo
{query.AddWhereOrder()}
";
			return new QueryListSqlParam(typeof(MasterShain), sql, query.Parameters);
		}
	}

	[RelayCommand]
	async Task Init() { }
}
