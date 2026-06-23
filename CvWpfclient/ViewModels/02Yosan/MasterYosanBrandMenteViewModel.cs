using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Globalization;

namespace CvWpfclient.ViewModels._02Yosan;

public partial class MasterYosanBrandMenteViewModel : Helpers.BaseMenteViewModel<MasterYosanBrand> {
	[ObservableProperty]
	string title = "店ブランド予算マスタメンテ";

	protected override string? ListOrder => "DenDay DESC, Id_Tenpo, Id_Brand";
	protected override int? ListMaxCount => AppGlobal.Limit;
	protected override string? SelectCodeDisplayName => "店ブランド予算";

	protected override SelectParameter NormalizeSelectParameter(SelectParameter? parameter, string? displayName = null) =>
		base.NormalizeSelectParameter(parameter, displayName) with {
			IdsDisplayName = "ブランド",
			IsToriVisible = true,
			ToriLabel = "店舗Id",
			ToriSearchWhere = "TenType in (1,3,6)"
		};

	protected override bool TryShowSelectCodeDialog(SelectParameter? currentParameter, string displayName, out SelectParameter parameter) {
		var selWin = new Views.Sub.RangeParamView();
		if (selWin.DataContext is not Sub.RangeParamViewModel vm) {
			parameter = currentParameter ?? new SelectParameter { DisplayName = displayName, IdsDisplayName = "ブランド", IsToriVisible = true, ToriLabel = "店舗Id" };
			return true;
		}

		var initialParameter = NormalizeSelectParameter(currentParameter ?? new SelectParameter { MaxCount = AppGlobal.Limit }, displayName);
		vm.Initialize(initialParameter, typeof(MasterMeisho), "Kubun='BRD'", "Code", typeof(MasterTokui), "TenType in (1,3,6)", "Code");
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
		AddSelectedIdInClause(clauses, "Y.Id_Tenpo", parameter.ToriIds);
		AddSelectedIdInClause(clauses, "Y.Id_Brand", parameter.Ids);
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
	Y.Id_Tenpo,
	Y.Id_Brand,
	Y.DenDay,
	Y.UriYosan,
	Y.ArariYosan,
	json_object('Sid', ifnull(T.Id, 0), 'Cd', ifnull(T.Code, ''), 'Mei', ifnull(T.Name, '')) VTenpo,
	json_object('Sid', ifnull(B.Id, 0), 'Cd', ifnull(B.Code, ''), 'Mei', ifnull(B.Name, '')) VBrand
from MasterYosanBrand Y
left join MasterTokui T on T.Id = Y.Id_Tenpo
left join MasterMeisho B on B.Id = Y.Id_Brand
{query.AddWhereOrder()}
";
		return new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(MasterYosanBrand), sql, query.Parameters))
		};
	}

	[RelayCommand]
	async Task Init() => await DoList(CancellationToken.None);

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

	protected override string GetInsertConfirmMessage() => $"追加しますか？ (日付={CurrentEdit.DenDay}, 店舗Id={CurrentEdit.Id_Tenpo}, ブランドId={CurrentEdit.Id_Brand})";

	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (日付={CurrentEdit.DenDay}, 店舗Id={CurrentEdit.Id_Tenpo}, ブランドId={CurrentEdit.Id_Brand}, Id={CurrentEdit.Id})";

	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (日付={CurrentEdit.DenDay}, 店舗Id={CurrentEdit.Id_Tenpo}, ブランドId={CurrentEdit.Id_Brand}, Id={CurrentEdit.Id})";

	[RelayCommand]
	void DoSelectShop() {
		var tokui = ShowSelectDialog<MasterTokui>(typeof(MasterTokui), "TenType in (1,3,6)", "Code", startPos: CurrentEdit.Id_Tenpo);
		if (tokui == null) return;
		CurrentEdit.Id_Tenpo = tokui.Id;
		CurrentEdit.VTenpo = new CodeNameView { Sid = tokui.Id, Cd = tokui.Code ?? string.Empty, Mei = tokui.Name ?? string.Empty };
	}

	[RelayCommand]
	void DoSelectBrand() {
		var meisho = ShowSelectDialog<MasterMeisho>(typeof(MasterMeisho), "Kubun='BRD'", "Code", startPos: CurrentEdit.Id_Brand);
		if (meisho == null) return;
		CurrentEdit.Id_Brand = meisho.Id;
		CurrentEdit.VBrand = new CodeNameView { Sid = meisho.Id, Cd = meisho.Code ?? string.Empty, Mei = meisho.Name ?? string.Empty };
	}

	bool ValidateCurrentEdit() {
		NormalizeCurrentEdit();
		if (CurrentEdit.Id_Tenpo <= 0) {
			Message = "店舗Idを入力してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		if (CurrentEdit.Id_Brand <= 0) {
			Message = "ブランドIdを入力してください";
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
