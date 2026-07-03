using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Globalization;

namespace CvWpfclient.ViewModels._02Yosan;

public partial class MasterYosanHanbaiMenteViewModel : Helpers.BaseMenteViewModel<MasterYosanHanbai> {
	[ObservableProperty]
	string title = "販売員予算マスタメンテ";

	[ObservableProperty]
	string shainDisplay = string.Empty;

	protected override string? ListOrder => "DenDay DESC, Id_Shain";
	protected override int? ListMaxCount => AppGlobal.Limit;
	protected override string? SelectCodeDisplayName => "販売員予算";

	protected override SelectParameter NormalizeSelectParameter(SelectParameter? parameter, string? displayName = null) =>
		base.NormalizeSelectParameter(parameter, displayName) with {
			IdsDisplayName = "販売員"
		};

	protected override bool TryShowSelectCodeDialog(SelectParameter? currentParameter, string displayName, out SelectParameter parameter) {
		var selWin = new Views.Sub.RangeParamView();
		if (selWin.DataContext is not Sub.RangeParamViewModel vm) {
			parameter = currentParameter ?? new SelectParameter { DisplayName = displayName, IdsDisplayName = "販売員" };
			return true;
		}

		var initialParameter = NormalizeSelectParameter(currentParameter ?? new SelectParameter { MaxCount = AppGlobal.Limit }, displayName);
		vm.Initialize(initialParameter, typeof(MasterShain), "", "Code");
		if (ClientLib.ShowDialogView(selWin, this, true) != true) {
			parameter = initialParameter;
			return false;
		}

		parameter = NormalizeSelectParameter(vm.Parameter, displayName);
		return true;
	}

	protected override string? BuildSelectCodeWhere(SelectParameter? parameter) {
		if (parameter == null) return null;

		List<string> clauses = [];
		AddSelectedIdInClause(clauses, "Y.Id_Shain", parameter.Ids);
		if (parameter.FromId.HasValue) {
			clauses.Add($"Y.Id >= {parameter.FromId.Value}");
		}
		if (parameter.ToId.HasValue) {
			clauses.Add($"Y.Id <= {parameter.ToId.Value}");
		}

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	protected override CvMsg CreateListMessage() {
		var query = CreateListQueryParam();
		var sql = @$"
select
	Y.Id,
	Y.Vdc,
	Y.Vdu,
	Y.Id_Shain,
	Y.DenDay,
	Y.UriYosan,
	Y.ArariYosan
from MasterYosanHanbai Y
left join MasterShain S on S.Id = Y.Id_Shain
{query.AddWhereOrder()}
";
		return new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(MasterYosanHanbai), sql, query.Parameters))
		};
	}

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

	protected override void OnCurrentEditChangedCore(MasterYosanHanbai? oldValue, MasterYosanHanbai newValue) {
		base.OnCurrentEditChangedCore(oldValue, newValue);
		ShainDisplay = newValue.Id_Shain > 0 ? $"Id={newValue.Id_Shain}" : string.Empty;
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

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (日付={CurrentEdit.DenDay}, 販売員Id={CurrentEdit.Id_Shain})";

	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (日付={CurrentEdit.DenDay}, 販売員Id={CurrentEdit.Id_Shain}, Id={CurrentEdit.Id})";

	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (日付={CurrentEdit.DenDay}, 販売員Id={CurrentEdit.Id_Shain}, Id={CurrentEdit.Id})";

	[RelayCommand]
	void DoSelectShain() {
		var shain = ShowSelectDialog<MasterShain>(typeof(MasterShain), "", "Code", startPos: CurrentEdit.Id_Shain);
		if (shain == null) return;
		CurrentEdit.Id_Shain = shain.Id;
		ShainDisplay = $"{shain.Code ?? string.Empty} {shain.Name ?? string.Empty}".Trim();
	}

	bool ValidateCurrentEdit() {
		NormalizeCurrentEdit();
		if (CurrentEdit.Id_Shain <= 0) {
			Message = "販売員Idを入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (!DateTime.TryParseExact(CurrentEdit.DenDay, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var denDay)) {
			Message = "日付は yyyyMMdd の8桁で入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (CurrentEdit.UriYosan < 0 || CurrentEdit.ArariYosan < 0) {
			Message = "予算金額には0以上の値を入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}

		CurrentEdit.DenDay = denDay.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		return true;
	}

	void NormalizeCurrentEdit() {
		CurrentEdit.DenDay = (CurrentEdit.DenDay ?? string.Empty).Trim();
	}
}
