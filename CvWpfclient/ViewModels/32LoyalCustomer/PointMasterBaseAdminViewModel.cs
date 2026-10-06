using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections;
using System.Globalization;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

public partial class PointMasterBaseAdminViewModel : BaseMenteViewModel<MasterPointBase> {
	public string Title => "ポイントマスタ（ベース）";
	public IReadOnlyList<EnumYesNo> YesNoOptions { get; } = Enum.GetValues<EnumYesNo>();
	public IReadOnlyList<EnumPointTaxBasis> TaxBasisOptions { get; } = Enum.GetValues<EnumPointTaxBasis>();
	public IReadOnlyList<EnumPointCalcUnit> CalcUnitOptions { get; } = Enum.GetValues<EnumPointCalcUnit>();
	public IReadOnlyList<EnumRounding> RoundingOptions { get; } = Enum.GetValues<EnumRounding>();
	protected override string? ListOrder => "Code, Version DESC, Id";
	protected override int? ListMaxCount => selectParam?.MaxCount;

	PointMasterSearchParameter? selectParam;

	protected override ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var win = new Views.Sub.PointMasterSearchParamView();
		if (win.DataContext is not PointMasterSearchParamViewModel vm) return new ValueTask<bool>(false);
		selectParam ??= new PointMasterSearchParameter { DisplayName = "ポイントベース", CodeLabel = "制度コード", MaxCount = AppGlobal.Limit };
		vm.Initialize(selectParam);
		if (ClientLib.ShowDialogView(win, this, true) != true) return new ValueTask<bool>(false);
		selectParam = vm.Parameter;
		return new ValueTask<bool>(true);
	}

	protected override string? ListWhere {
		get {
			if (selectParam == null) return null;
			List<string> clauses = [];
			List<string> parameters = [];
			if (!string.IsNullOrWhiteSpace(selectParam.Code)) {
				clauses.Add($"Code LIKE {AddSqlParameter(parameters, $"{EscapeSqlLikePattern(selectParam.Code)}%")} ESCAPE '\\'");
			}
			if (!string.IsNullOrWhiteSpace(selectParam.TargetDay)) {
				clauses.Add($"DayFrom <= {AddSqlParameter(parameters, selectParam.TargetDay)}");
				clauses.Add($"DayTo >= {AddSqlParameter(parameters, selectParam.TargetDay)}");
			}
			if (selectParam.EnabledState != PointMasterSearchParameter.AllEnabled) clauses.Add($"IsEnabled = {selectParam.EnabledState}");
			SelectCodeWhereParameters = [.. parameters];
			return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
		}
	}

	bool ValidateEdit() {
		CurrentEdit.Code = (CurrentEdit.Code ?? "").Trim();
		CurrentEdit.Name = (CurrentEdit.Name ?? "").Trim();
		if (CurrentEdit.Code.Length is < 1 or > 20 || CurrentEdit.Name.Length is < 1 or > 80) return Reject("制度コード(20文字以内)・名称(80文字以内)を入力してください");
		if (CurrentEdit.Version < 1) return Reject("版番号は1以上にしてください");
		if (!IsDate(CurrentEdit.DayFrom) || !IsDate(CurrentEdit.DayTo) || string.CompareOrdinal(CurrentEdit.DayFrom, CurrentEdit.DayTo) > 0) return Reject("適用期間を正しく指定してください");
		if (CurrentEdit.PointUnitPrice <= 0 || CurrentEdit.PointAmountProper < 0 || CurrentEdit.PointAmountSale < 0) return Reject("付与単価は正数、付与数は0以上にしてください");
		if (!Enum.IsDefined(CurrentEdit.EnIsEnabled) || !Enum.IsDefined(CurrentEdit.EnTaxBasis) || !Enum.IsDefined(CurrentEdit.EnCalcUnit) || !Enum.IsDefined(CurrentEdit.EnRounding) || !Enum.IsDefined(CurrentEdit.EnDeductPointUse)) return Reject("各区分を選択してください");
		return true;
	}

	[RelayCommand]
	Task Init(CancellationToken ct) => DoList(ct);

	protected override bool CanUpdate() => CurrentEdit.Id > 0;
	protected override bool ConfirmAction(string message) {
		if ((message.StartsWith("追加", StringComparison.Ordinal) || message.StartsWith("修正", StringComparison.Ordinal)) && !CanSaveEdit()) {
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return false;
		}
		return base.ConfirmAction(message);
	}

	protected override object CreateInsertParam() {
		// 一覧行の条件を複写する場合も、採番と監査値は新しい行として扱う。
		var item = Common.CloneObject(CurrentEdit);
		item.Id = 0;
		item.Vdc = 0;
		item.Vdu = 0;
		return new InsertParam(Tabletype, Common.SerializeObject(item));
	}

	bool CanSaveEdit() {
		// 変換失敗時の古いモデル値を保存しない。
		if (ActiveWindow is { } window && HasInputError(window)) return Reject("数値などの入力形式を確認してください");
		return ValidateEdit();
	}

	static bool HasInputError(System.Windows.DependencyObject element) {
		if (System.Windows.Controls.Validation.GetHasError(element)) return true;
		for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++) {
			if (HasInputError(System.Windows.Media.VisualTreeHelper.GetChild(element, i))) return true;
		}
		return false;
	}

	static bool IsDate(string value) => DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
	bool Reject(string message) { Message = message; return false; }
}
