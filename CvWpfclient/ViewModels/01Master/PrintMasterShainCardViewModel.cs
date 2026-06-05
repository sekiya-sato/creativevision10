using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.Generic;

namespace CvWpfclient.ViewModels._01Master;

public partial class PrintMasterShainCardViewModel : BaseMenteViewModel<MasterShain> {
	[ObservableProperty]
	string title = "社員証カード印刷";

	[ObservableProperty]
	string tenpoCodeFrom = string.Empty;

	[ObservableProperty]
	string tenpoCodeTo = "99999999";

	[ObservableProperty]
	bool isCode39 = true;

	public PrintMasterShainCardViewModel() {
		SelectCodeParam = new() { DisplayName = "社員" };
	}

	protected override string? SelectCodeDisplayName => "社員";

	protected override string? FormFile => IsCode39 ? "PrintMasterShainCard39.qfm" : "PrintMasterShainCard.qfm";

	protected override string? ListWhere => BuildListWhere();

	string? BuildListWhere() {
		var codeWhere = BuildSelectCodeWhere(SelectCodeParam);
		var clauses = new List<string>();

		if (!string.IsNullOrEmpty(codeWhere))
			clauses.Add(codeWhere);

		if (!string.IsNullOrWhiteSpace(TenpoCodeFrom) && long.TryParse(TenpoCodeFrom, out var tenpoFrom)) {
			clauses.Add($"id_Tenpo >= {tenpoFrom}");
		}
		if (!string.IsNullOrWhiteSpace(TenpoCodeTo) && long.TryParse(TenpoCodeTo, out var tenpoTo)) {
			clauses.Add($"id_Tenpo <= {tenpoTo}");
		}

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	protected override QueryListSqlParam? PrintBySqlParam {
		get {
			var query = CreateListQueryParam();
			var sql = $@"
select A.Code, A.Name,
coalesce(json_extract(A.Jdetail, '$.yobi1'), '') 画像,
A.id_Tenpo,
coalesce(T.Name, '') 店舗名,
coalesce((select S.Name from MasterSysKanri S limit 1), '') 自社名,
case when coalesce(json_extract(A.Jdetail, '$.yobi1'), '')='' then 0 else 1 end 画像表示判定用
from MasterShain A
left join MasterTokui T on T.Id = A.id_Tenpo
{query.AddWhereOrder()}
";
			return new QueryListSqlParam(typeof(MasterShain), sql, query.Parameters);
		}
	}

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);
}
