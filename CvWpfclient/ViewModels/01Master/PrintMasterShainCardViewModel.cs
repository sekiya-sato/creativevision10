using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;

namespace CvWpfclient.ViewModels._01Master;

public partial class PrintMasterShainCardViewModel : BaseMenteViewModel<MasterShain> {
	[ObservableProperty]
	string title = "社員証カード印刷";

	[ObservableProperty]
	string shainCodeFrom = string.Empty;

	[ObservableProperty]
	string shainCodeTo = string.Empty;

	[ObservableProperty]
	string tenpoCodeFrom = string.Empty;

	[ObservableProperty]
	string tenpoCodeTo = string.Empty;

	[ObservableProperty]
	bool isCode39 = true;

	protected override string? FormFile => IsCode39 ? "PrintMasterShainCard39.qfm" : "PrintMasterShainCard.qfm";

	protected override string? ListWhere => BuildListWhere();

	protected override string? ListOrder => "A.Code";
	string? BuildListWhere() {
		var clauses = new List<string>();

		if (!string.IsNullOrWhiteSpace(ShainCodeFrom)) {
			clauses.Add($"A.Code >= '{EscapeSqlLiteral(ShainCodeFrom)}'");
		}
		if (!string.IsNullOrWhiteSpace(ShainCodeTo)) {
			clauses.Add($"A.Code <= '{EscapeSqlLiteral(ShainCodeTo)}'");
		}

		if (!string.IsNullOrWhiteSpace(TenpoCodeFrom)) {
			clauses.Add($"T.Code >= '{EscapeSqlLiteral(TenpoCodeFrom)}'");
		}
		if (!string.IsNullOrWhiteSpace(TenpoCodeTo)) {
			clauses.Add($"T.Code <= '{EscapeSqlLiteral(TenpoCodeTo)}'");
		}

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
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
	void SelectTenpoCodeFrom() {
		var tenpo = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code");
		TenpoCodeFrom = tenpo?.Code ?? string.Empty;
	}

	[RelayCommand]
	void SelectTenpoCodeTo() {
		var tenpo = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType=6", "Code");
		TenpoCodeTo = tenpo?.Code ?? string.Empty;
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
