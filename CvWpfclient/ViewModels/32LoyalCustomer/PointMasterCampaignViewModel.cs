using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace CvWpfclient.ViewModels._32LoyalCustomer;

public partial class PointMasterCampaignViewModel : BaseMenteViewModel<MasterPointCampaign> {
	public string Title => "ポイントマスタ（キャンペーン）";
	public IReadOnlyList<EnumYesNo> YesNoOptions { get; } = Enum.GetValues<EnumYesNo>();
	public IReadOnlyList<EnumPointCampaignPriority> PriorityOptions { get; } = Enum.GetValues<EnumPointCampaignPriority>();
	protected override string? ListOrder => "Code, Id";

	/// <summary>全ランクを表す対象ランクコード</summary>
	const int AllRanks = 0;

	public PointMasterCampaignViewModel() {
		// 初期表示の入力フォームにも新規の期間初期値を入れる。
		OnCurrentEditChangedCore(null, CurrentEdit);
	}

	bool ValidateEdit() {
		// サーバ(PointCampaignDb.ValidateSave)と同じ入力検査。コード一意・優先区分変更・対象重複はサーバで検査する。
		CurrentEdit.Code = (CurrentEdit.Code ?? "").Trim();
		CurrentEdit.Name = (CurrentEdit.Name ?? "").Trim();
		CurrentEdit.Memo ??= "";
		if (CurrentEdit.Code.Length is < 1 or > 20 || CurrentEdit.Name.Length is < 1 or > 80) return Reject("コード(20文字以内)・名称(80文字以内)を入力してください");
		if (CurrentEdit.Memo.Length > 200) return Reject("備考は200文字以内で入力してください");
		if (!IsDate(CurrentEdit.DayFrom) || !IsDate(CurrentEdit.DayTo) || string.CompareOrdinal(CurrentEdit.DayFrom, CurrentEdit.DayTo) > 0) return Reject("適用期間を正しく指定してください");
		if (!Enum.IsDefined(CurrentEdit.EnIsEnabled) || !Enum.IsDefined(CurrentEdit.EnPriorityType)) return Reject("有効・優先区分を選択してください");
		if (CurrentEdit.PointUnitPrice <= 0 || CurrentEdit.PointAmountProper < 0 || CurrentEdit.PointAmountSale < 0) return Reject("付与単価は1以上、P/S付与数は0以上にしてください");
		if (CurrentEdit.Id_PointBase <= 0 || !BaseOptions.Any(x => x.Id == CurrentEdit.Id_PointBase)) return Reject("親ベースの版を選択してください");
		var parent = BaseOptions.First(x => x.Id == CurrentEdit.Id_PointBase);
		if (string.CompareOrdinal(CurrentEdit.DayFrom, parent.DayFrom) < 0 || string.CompareOrdinal(CurrentEdit.DayTo, parent.DayTo) > 0) return Reject("キャンペーン期間は親ベースの適用期間内にしてください");
		if (CurrentEdit.RankKubun < 0) return Reject("対象ランクを選択してください");
		if (CurrentEdit.RankKubun != AllRanks && !allRanks.Any(x => x.Id_PointBase == CurrentEdit.Id_PointBase && x.Kubun == CurrentEdit.RankKubun)) return Reject("対象ランクは親ベースの版に登録されたランクを選択してください");
		return true;
	}

	[ObservableProperty]
	public partial List<MasterPointBase> BaseOptions { get; set; } = [];

	/// <summary>対象ランクの候補。0=全ランクと、選択中の親ベース版のランク</summary>
	[ObservableProperty]
	public partial List<KeyValuePair<int, string>> RankOptions { get; set; } = [];

	List<MasterPointRank> allRanks = [];

	protected override int? ListMaxCount => selectParam?.MaxCount;

	PointMasterSearchParameter? selectParam;

	protected override async ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		// 親ベース・ランクの候補は条件選択と入力フォームの両方で使うため、先に最新化する。
		if (!await LoadOptionsAsync(ct)) return false;
		var win = new Views.Sub.PointMasterSearchParamView();
		if (win.DataContext is not PointMasterSearchParamViewModel vm) return false;
		selectParam ??= new PointMasterSearchParameter { DisplayName = "ポイントキャンペーン", CodeLabel = "キャンペーンコード", IsBaseVisible = true, MaxCount = AppGlobal.Limit };
		vm.Initialize(selectParam, BaseOptions, includePending: false);
		if (ClientLib.ShowDialogView(win, this, true) != true) return false;
		selectParam = vm.Parameter;
		return true;
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
			if (selectParam.Id_PointBase != PointMasterSearchParameter.AllBase) clauses.Add($"Id_PointBase = {selectParam.Id_PointBase}");
			SelectCodeWhereParameters = [.. parameters];
			return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
		}
	}

	async ValueTask<bool> LoadOptionsAsync(CancellationToken ct) {
		try {
			var bases = await QueryAsync<MasterPointBase>("Code, Version DESC, Id", ct);
			var ranks = await QueryAsync<MasterPointRank>("Id_PointBase, Kubun, Id", ct);
			if (bases == null || ranks == null) return false;
			// 候補の差替えでComboBoxが選択値を書き戻しても、編集中の値を維持する。
			var parentId = CurrentEdit.Id_PointBase;
			var rankKubun = CurrentEdit.RankKubun;
			allRanks = ranks;
			BaseOptions = bases;
			CurrentEdit.Id_PointBase = parentId;
			CurrentEdit.RankKubun = rankKubun;
			RefreshRankOptions();
			return true;
		}
		catch (OperationCanceledException) { return false; }
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"ベース・ランク条件の取得失敗: {ex.Message}", owner: ActiveWindow);
			return false;
		}
	}

	async ValueTask<List<TRow>?> QueryAsync<TRow>(string order, CancellationToken ct) {
		var param = new QueryListParam(typeof(TRow), order: order);
		var reply = await SendMessageAsync(new CvMsg {
			Flag = CvFlag.Msg101_Op_Query, DataType = typeof(QueryListParam), DataMsg = Common.SerializeObject(param)
		}, ct);
		if (reply.Code == CvMsgErrorCode.Unexpected) throw new InvalidOperationException(reply.Option);
		return Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list ? list.Cast<TRow>().ToList() : null;
	}

	protected override void OnCurrentEditChangedCore(MasterPointCampaign? oldValue, MasterPointCampaign newValue) {
		if (oldValue != null) oldValue.PropertyChanged -= OnCurrentEditPropertyChanged;
		if (newValue == null) return;
		if (newValue.Id == 0 && string.IsNullOrEmpty(newValue.DayFrom) && string.IsNullOrEmpty(newValue.DayTo)) {
			// 新規の適用期間は当日から1週間を初期値とする。
			var today = DateTime.Today;
			newValue.DayFrom = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
			newValue.DayTo = today.AddDays(7).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		}
		newValue.PropertyChanged += OnCurrentEditPropertyChanged;
		RefreshRankOptions();
	}

	void OnCurrentEditPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		// 親版の切替、または候補にない値の設定（コード側の代入）で候補を作り直す。
		if (e.PropertyName == nameof(MasterPointCampaign.Id_PointBase)
			|| e.PropertyName == nameof(MasterPointCampaign.RankKubun) && !RankOptions.Any(x => x.Key == CurrentEdit.RankKubun)) RefreshRankOptions();
	}

	/// <summary>親ベース版のランクで候補を作る。親にない現在値も表示し、保存時の検証で拒否する。</summary>
	void RefreshRankOptions() {
		var parentId = CurrentEdit.Id_PointBase;
		var current = CurrentEdit.RankKubun;
		List<KeyValuePair<int, string>> options = [new(AllRanks, "0 全ランク")];
		options.AddRange(allRanks.Where(x => x.Id_PointBase == parentId && x.Kubun != AllRanks)
			.Select(x => new KeyValuePair<int, string>(x.Kubun, $"{x.Kubun} {x.Name}")));
		if (!options.Any(x => x.Key == current)) options.Add(new(current, $"{current} (親ベース版に未登録)"));
		RankOptions = options;
		CurrentEdit.RankKubun = current;
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

	static bool IsDate(string value) => value?.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
	bool Reject(string message) { Message = message; return false; }
}
