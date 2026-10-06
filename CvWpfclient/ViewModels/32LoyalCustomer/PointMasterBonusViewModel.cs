using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections;
using System.Globalization;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

public partial class PointMasterBonusViewModel : BaseMenteViewModel<MasterPointBonus> {
	public string Title => "ポイントマスタ（ボーナス）";
	public IReadOnlyList<EnumYesNo> YesNoOptions { get; } = Enum.GetValues<EnumYesNo>();
	public IReadOnlyList<EnumPointBonusTrigger> TriggerOptions { get; } = Enum.GetValues<EnumPointBonusTrigger>();
	public IReadOnlyList<EnumPointLimitPeriod> LimitPeriodOptions { get; } = Enum.GetValues<EnumPointLimitPeriod>();
	protected override string? ListOrder => "Code, Version DESC, Id";

	bool ValidateEdit() {
		CurrentEdit.Code = (CurrentEdit.Code ?? "").Trim();
		CurrentEdit.Name = (CurrentEdit.Name ?? "").Trim();
		if (CurrentEdit.Code.Length is < 1 or > 20 || CurrentEdit.Name.Length is < 1 or > 80) return Reject("コード(20文字以内)・名称(80文字以内)を入力してください");
		if (CurrentEdit.Version < 1) return Reject("版番号は1以上にしてください");
		if (CurrentEdit.Id_PointBase <= 0 || !BaseOptions.Any(x => x.Id == CurrentEdit.Id_PointBase)) return Reject("親ベースの版を選択してください");
		if (!IsDate(CurrentEdit.DayFrom) || !IsDate(CurrentEdit.DayTo) || string.CompareOrdinal(CurrentEdit.DayFrom, CurrentEdit.DayTo) > 0) return Reject("適用期間を正しく指定してください");
		var parent = BaseOptions.First(x => x.Id == CurrentEdit.Id_PointBase);
		if (string.CompareOrdinal(CurrentEdit.DayFrom, parent.DayFrom) < 0 || string.CompareOrdinal(CurrentEdit.DayTo, parent.DayTo) > 0) return Reject("ボーナス期間は親ベースの適用期間内にしてください");
		if (CurrentEdit.PointAmount <= 0 || CurrentEdit.MinimumKingaku < 0 || CurrentEdit.LimitCount < 1) return Reject("追加付与数・回数上限は1以上、最低金額は0以上にしてください");
		if (!Enum.IsDefined(CurrentEdit.EnIsEnabled) || !Enum.IsDefined(CurrentEdit.EnIsAllRanks) || !Enum.IsDefined(CurrentEdit.EnTriggerType) || !Enum.IsDefined(CurrentEdit.EnLimitPeriodType)) return Reject("各区分を選択してください");
		if (CurrentEdit.EnIsAllRanks == EnumYesNo.Yes) CurrentEdit.RankKubun = 0;
		// 対象ランクの存在・親との整合、使用済み版と期間重複はサーバで検査する。
		return true;
	}

	[ObservableProperty]
	public partial List<MasterPointBase> BaseOptions { get; set; } = [];

	protected override async ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		try {
			var param = new QueryListParam(typeof(MasterPointBase), order: "Code, Version DESC, Id");
			var reply = await SendMessageAsync(new CvMsg {
				Flag = CvFlag.Msg101_Op_Query, DataType = typeof(QueryListParam), DataMsg = Common.SerializeObject(param)
			}, ct);
			if (reply.Code == CvMsgErrorCode.Unexpected) throw new InvalidOperationException(reply.Option);
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is not IList list) return false;
			var parentId = CurrentEdit.Id_PointBase;
			BaseOptions = list.Cast<MasterPointBase>().ToList();
			CurrentEdit.Id_PointBase = parentId;
			return true;
		}
		catch (OperationCanceledException) { return false; }
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"ベース条件の取得失敗: {ex.Message}", owner: ActiveWindow);
			return false;
		}
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
