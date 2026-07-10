using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Globalization;

namespace CvWpfclient.ViewModels._01Master;

public partial class TranTokuiPromotionMenteViewModel : Helpers.BaseMenteViewModel<TranTokuiPromotion> {
	public sealed record RankOption(int Value, string Name);

	[ObservableProperty]
	public partial string Title { get; set; } = "得意先イベントメンテ";

	TranPromotionSearchParameter? selectParam;

	public IReadOnlyList<RankOption> RankOptions { get; } = [
		new(0, "低"),
		new(1, "中"),
		new(2, "高")
	];

	protected override string? ListOrder => "P.DenDay DESC, P.Id_Tokui, P.Id DESC";
	protected override int? ListMaxCount => selectParam?.MaxCount;
	protected override string? ListWhere => BuildWhereClause(selectParam);

	protected override CvMsg CreateListMessage() {
		var query = CreateListQueryParam();
		var sql = @$"
select
	P.Id,
	P.Vdc,
	P.Vdu,
	P.Id_Tokui,
	P.DenDay,
	P.Mame,
	P.Rank,
	ifnull(T.Code, '') TokuiCode,
	ifnull(T.Name, '') TokuiName,
	case P.Rank when 0 then '低' when 1 then '中' when 2 then '高' else '' end RankName
from TranTokuiPromotion P
left join MasterTokui T on T.Id = P.Id_Tokui
{query.AddWhereOrder()}
";
		return new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(TranTokuiPromotion), sql, query.Parameters))
		};
	}

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();

		var selWin = new Views.Sub.TranPromotionSearchParamView();
		if (selWin.DataContext is not TranPromotionSearchParamViewModel vm) {
			return new ValueTask<bool>(true);
		}

		vm.Initialize(selectParam ?? new TranPromotionSearchParameter { DisplayName = "得意先イベント", TargetIdLabel = "得意先Id", MaxCount = AppGlobal.Limit });
		if (ClientLib.ShowDialogView(selWin, this, true) != true) {
			selectParam = vm.Parameter;
			return new ValueTask<bool>(false);
		}

		selectParam = NormalizeSearchParameter(vm.Parameter);
		return new ValueTask<bool>(true);
	}

	protected override bool CanUpdate() => CurrentEdit.Id > 0;

	protected override bool ConfirmAction(string message) {
		if ((message.StartsWith("追加", StringComparison.Ordinal) || message.StartsWith("修正", StringComparison.Ordinal)) && !ValidateCurrentEdit()) {
			return false;
		}

		return base.ConfirmAction(message);
	}

	protected override object CreateInsertParam() {
		NormalizeCurrentEdit();
		return base.CreateInsertParam();
	}

	protected override object CreateUpdateParam() {
		NormalizeCurrentEdit();
		return base.CreateUpdateParam();
	}

	protected override void AfterInsert(TranTokuiPromotion item) {
		ApplyDisplayColumns(item);
		base.AfterInsert(item);
	}

	protected override void AfterUpdate(TranTokuiPromotion item) {
		ApplyDisplayColumns(Current);
		base.AfterUpdate(item);
	}

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (得意先Id={CurrentEdit.Id_Tokui}, 日付={CurrentEdit.DenDay})";

	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (得意先Id={CurrentEdit.Id_Tokui}, 日付={CurrentEdit.DenDay}, Id={CurrentEdit.Id})";

	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (得意先Id={CurrentEdit.Id_Tokui}, 日付={CurrentEdit.DenDay}, Id={CurrentEdit.Id})";

	[RelayCommand]
	void DoSelectTokui() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), string.Empty, "Code", startPos: CurrentEdit.Id_Tokui);
		if (tokui == null) return;

		CurrentEdit.Id_Tokui = tokui.Id;
		CurrentEdit.TokuiCode = tokui.Code ?? string.Empty;
		CurrentEdit.TokuiName = tokui.Name ?? string.Empty;
	}

	bool ValidateCurrentEdit() {
		NormalizeCurrentEdit();
		if (CurrentEdit.Id_Tokui <= 0) {
			Message = "得意先Idを選択してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (!DateTime.TryParseExact(CurrentEdit.DenDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var denDay)) {
			Message = "日付は yyyyMMdd の8桁で入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (string.IsNullOrWhiteSpace(CurrentEdit.Mame)) {
			Message = "イベント名を入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (CurrentEdit.Rank < 0 || CurrentEdit.Rank > 2) {
			Message = "重要度は 0=低, 1=中, 2=高 から選択してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}

		CurrentEdit.DenDay = denDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		ApplyDisplayColumns(CurrentEdit);
		return true;
	}

	void NormalizeCurrentEdit() {
		CurrentEdit.DenDay = (CurrentEdit.DenDay ?? string.Empty).Trim();
		CurrentEdit.Mame = (CurrentEdit.Mame ?? string.Empty).Trim();
		CurrentEdit.RankName = GetRankName(CurrentEdit.Rank);
	}

	static string? BuildWhereClause(TranPromotionSearchParameter? param) {
		if (param == null) return null;

		List<string> clauses = [];
		if (param.FromTargetId.HasValue) {
			clauses.Add($"P.Id_Tokui >= {param.FromTargetId.Value}");
		}
		if (param.ToTargetId.HasValue) {
			clauses.Add($"P.Id_Tokui <= {param.ToTargetId.Value}");
		}
		if (!string.IsNullOrWhiteSpace(param.FromDate)) {
			clauses.Add($"P.DenDay >= '{EscapeSqlLiteral(param.FromDate)}'");
		}
		if (!string.IsNullOrWhiteSpace(param.ToDate)) {
			clauses.Add($"P.DenDay <= '{EscapeSqlLiteral(param.ToDate)}'");
		}

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	static TranPromotionSearchParameter NormalizeSearchParameter(TranPromotionSearchParameter? param) =>
		new() {
			FromTargetId = param?.FromTargetId,
			ToTargetId = param?.ToTargetId,
			FromDate = string.IsNullOrWhiteSpace(param?.FromDate) ? null : param.FromDate,
			ToDate = string.IsNullOrWhiteSpace(param?.ToDate) ? null : param.ToDate,
			MaxCount = param?.MaxCount,
			DisplayName = string.IsNullOrWhiteSpace(param?.DisplayName) ? "得意先イベント" : param.DisplayName,
			TargetIdLabel = "得意先Id"
		};

	static void ApplyDisplayColumns(TranTokuiPromotion item) {
		item.RankName = GetRankName(item.Rank);
	}

	static string GetRankName(int rank) =>
		rank switch {
			0 => "低",
			1 => "中",
			2 => "高",
			_ => string.Empty
		};
}
