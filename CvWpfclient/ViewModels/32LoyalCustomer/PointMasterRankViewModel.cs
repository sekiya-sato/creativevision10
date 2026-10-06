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

public partial class PointMasterRankViewModel : BaseMenteViewModel<MasterPointRank> {
	public string Title => "ポイントマスタ（ランク）";
	protected override string? ListOrder => "Id_PointBase, Kubun, Id";
	protected override string GetInsertConfirmMessage() => $"追加しますか？ (ベースId={CurrentEdit.Id_PointBase}, ランク={CurrentEdit.Kubun})";
	protected override string GetUpdateConfirmMessage() => $"修正しますか？ (Id={CurrentEdit.Id}, ランク={CurrentEdit.Kubun})";
	protected override string GetDeleteConfirmMessage() => $"削除しますか？ (Id={CurrentEdit.Id}, ランク={CurrentEdit.Kubun})";

	bool ValidateEdit() {
		CurrentEdit.Name = (CurrentEdit.Name ?? "").Trim();
		if (CurrentEdit.Id_PointBase <= 0 || !BaseOptions.Any(x => x.Id == CurrentEdit.Id_PointBase)) return Reject("親ベースの版を選択してください。親Id=0は移行設定待ちです");
		if (CurrentEdit.Name.Length is < 1 or > 40) return Reject("ランク名称を40文字以内で入力してください");
		if (CurrentEdit.PointUnitPrice <= 0 || CurrentEdit.PointAmountProper < 0 || CurrentEdit.PointAmountSale < 0) return Reject("付与単価は正数、付与数は0以上にしてください");
		return true;
	}

	[ObservableProperty]
	public partial List<MasterPointBase> BaseOptions { get; set; } = [];

	protected override int? ListMaxCount => selectParam?.MaxCount;

	PointMasterSearchParameter? selectParam;

	protected override async ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		// 親ベースの候補は条件選択と入力フォームの両方で使うため、先に最新化する。
		if (!await LoadBaseOptionsAsync(ct)) return false;
		var win = new Views.Sub.PointMasterSearchParamView();
		if (win.DataContext is not PointMasterSearchParamViewModel vm) return false;
		selectParam ??= new PointMasterSearchParameter { DisplayName = "ポイントランク", IsVersionedVisible = false, IsBaseVisible = true, MaxCount = AppGlobal.Limit };
		vm.Initialize(selectParam, BaseOptions, includePending: true);
		if (ClientLib.ShowDialogView(win, this, true) != true) return false;
		selectParam = vm.Parameter;
		return true;
	}

	protected override string? ListWhere =>
		selectParam == null || selectParam.Id_PointBase == PointMasterSearchParameter.AllBase ? null : $"Id_PointBase = {selectParam.Id_PointBase}";

	async ValueTask<bool> LoadBaseOptionsAsync(CancellationToken ct) {
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
