using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace CvWpfclient.ViewModels.Sub;

public partial class SelectShohinViewModel : Helpers.BaseViewModel {
	int MaxCount = AppGlobal.Limit;

	[ObservableProperty]
	string title = "商品検索";

	[ObservableProperty]
	long? shohinIdFrom;

	[ObservableProperty]
	long? shohinIdTo;

	[ObservableProperty]
	string shohinCodeFrom = string.Empty;

	[ObservableProperty]
	string shohinCodeTo = string.Empty;

	[ObservableProperty]
	string shohinName = string.Empty;

	[ObservableProperty]
	List<long> brandIds = [];

	[ObservableProperty]
	string brandIdsText = "未選択";

	[ObservableProperty]
	List<long> itemIds = [];

	[ObservableProperty]
	string itemIdsText = "未選択";

	[ObservableProperty]
	string jan = string.Empty;

	[ObservableProperty]
	ObservableCollection<SelectShohinRow> listData = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(DoSelectCommand))]
	SelectShohinRow? current;

	[ObservableProperty]
	int count;

	[ObservableProperty]
	bool isSearchMode = true;

	[ObservableProperty]
	string searchActionText = "一覧表示";

	[ObservableProperty]
	bool isConditionOnlyMode;

	[ObservableProperty]
	string message = "条件を入力して一覧表示してください";

	public MasterShohin? SelectedShohin => Current?.Shohin;

	public bool IsResultMode => !IsSearchMode;

	partial void OnIsSearchModeChanged(bool value) {
		OnPropertyChanged(nameof(IsResultMode));
		Title = value ? "商品検索" : "商品一覧選択";
	}

	partial void OnIsConditionOnlyModeChanged(bool value) {
		SearchActionText = value ? "決定" : "一覧表示";
		Message = value ? "条件を入力して決定してください" : "条件を入力して一覧表示してください";
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task SearchOrCommit(CancellationToken ct) {
		if (IsConditionOnlyMode) {
			ClientLib.ExitDialogResult(this, true);
			return;
		}

		await Search(ct);
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task Search(CancellationToken ct) {
		try {
			Message = "商品一覧を取得しています...";
			List<MasterShohin> shohinList = await LoadShohinListAsync(ct);
			ListData = new ObservableCollection<SelectShohinRow>(shohinList.Select(x => new SelectShohinRow(x)));
			Count = ListData.Count;
			Current = ListData.FirstOrDefault();
			IsSearchMode = false;
			Message = Count >= MaxCount
				? $"検索結果が {MaxCount:N0} 件に達しました。必要に応じて条件を絞り込んでください"
				: $"{Count:N0} 件の商品を取得しました";
		}
		catch (OperationCanceledException) {
			Message = "検索を中断しました";
		}
		catch (Exception ex) {
			Message = $"商品検索失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
	}

	[RelayCommand]
	void Back() {
		IsSearchMode = true;
		Message = "検索条件を変更できます";
	}

	[RelayCommand(CanExecute = nameof(CanSelect))]
	public void DoSelect() {
		if (Current == null) {
			MessageEx.ShowWarningDialog("商品が選択されていません", owner: ActiveWindow);
			return;
		}

		ClientLib.ExitDialogResult(this, true);
	}

	bool CanSelect() => Current != null;

	[RelayCommand]
	void SelectBrandIds() {
		var selected = ShowMultiSelectDialog("ブランド選択", "Kubun='BRD'", BrandIds, BrandIds.FirstOrDefault());
		if (selected == null) return;
		BrandIds = [.. selected.Select(x => x.Id)];
		BrandIdsText = BuildSelectedText(selected);
	}

	[RelayCommand]
	void ClearBrandIds() {
		BrandIds = [];
		BrandIdsText = "未選択";
	}

	[RelayCommand]
	void SelectItemIds() {
		var selected = ShowMultiSelectDialog("アイテム選択", "Kubun='ITM'", ItemIds, ItemIds.FirstOrDefault());
		if (selected == null) return;
		ItemIds = [.. selected.Select(x => x.Id)];
		ItemIdsText = BuildSelectedText(selected);
	}

	[RelayCommand]
	void ClearItemIds() {
		ItemIds = [];
		ItemIdsText = "未選択";
	}

	public void ApplySelectParameter(SelectParameter? parameter) {
		if (parameter?.MaxCount.HasValue == true) {
			MaxCount = parameter.MaxCount.Value;
		}

		ShohinIdFrom = parameter?.FromId;
		ShohinIdTo = parameter?.ToId;
		ShohinCodeFrom = parameter?.FromCode ?? string.Empty;
		ShohinCodeTo = parameter?.ToCode ?? string.Empty;
		ShohinName = parameter?.Name ?? string.Empty;
		BrandIds = NormalizeIds(parameter?.Ids);
		BrandIdsText = NormalizeSelectedText(BrandIds, parameter?.IdsText);
		ItemIds = NormalizeIds(parameter?.ItemIds);
		ItemIdsText = NormalizeSelectedText(ItemIds, parameter?.ItemIdsText);
		Jan = parameter?.Jan ?? string.Empty;
	}

	public SelectParameter CreateSelectParameter(string? displayName = null) =>
		new() {
			FromId = ShohinIdFrom,
			ToId = ShohinIdTo,
			Ids = NormalizeIds(BrandIds),
			IdsText = NormalizeSelectedText(BrandIds, BrandIdsText),
			IdsDisplayName = "ブランド",
			ItemIds = NormalizeIds(ItemIds),
			ItemIdsText = NormalizeSelectedText(ItemIds, ItemIdsText),
			FromCode = NormalizeNullableText(ShohinCodeFrom),
			ToCode = NormalizeNullableText(ShohinCodeTo),
			DisplayName = displayName,
			Name = NormalizeNullableText(ShohinName),
			Jan = NormalizeNullableText(Jan),
			MaxCount = MaxCount
		};

	async Task<List<MasterShohin>> LoadShohinListAsync(CancellationToken ct) {
		List<string> parameters = [];
		List<string> clauses = BuildShohinClauses(parameters);
		string where = clauses.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", clauses)}";
		string sql = $"""
			SELECT
				M.*
			FROM MasterShohin M
				LEFT JOIN MasterMeisho Brd ON Brd.Id = M.Id_Brand
				LEFT JOIN MasterMeisho Item ON Item.Id = M.Id_Item
			{where}
			ORDER BY M.Code
			LIMIT {MaxCount}
			""";

		return await QuerySqlListAsync<MasterShohin>(sql, parameters, ct);
	}

	List<string> BuildShohinClauses(List<string> parameters) {
		List<string> clauses = [];
		AddIdRange(clauses, "M.Id", ShohinIdFrom, ShohinIdTo);
		AddCodeRange(clauses, parameters, "M.Code", ShohinCodeFrom, ShohinCodeTo);
		AddLike(clauses, parameters, "M.Name", ShohinName);
		AddSelectedIdInClause(clauses, "M.Id_Brand", BrandIds);
		AddSelectedIdInClause(clauses, "M.Id_Item", ItemIds);

		string normalizedJan = Normalize(Jan);
		if (!string.IsNullOrEmpty(normalizedJan)) {
			string janParameter = AddParameter(parameters, $"%{normalizedJan}%");
			clauses.Add($"""
				EXISTS (
					SELECT 1
					FROM DerivedShohinColSiz D
					WHERE D.Id_Shohin = M.Id
						AND (D.Jan1 LIKE {janParameter} OR D.Jan2 LIKE {janParameter} OR D.Jan3 LIKE {janParameter})
				)
				""");
		}

		return clauses;
	}

	async Task<List<T>> QuerySqlListAsync<T>(string sql, IEnumerable<string> parameters, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListSqlParam),
			DataMsg = Common.SerializeObject(new QueryListSqlParam(typeof(T), sql, [.. parameters]))
		};

		CvMsg reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		ct.ThrowIfCancellationRequested();
		if (reply.Code < 0 && reply.Code != -1) {
			throw new InvalidOperationException(reply.Option ?? reply.DataMsg ?? "サーバQueryでエラーが発生しました");
		}

		if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is not IList list) return [];
		return list.Cast<T>().ToList();
	}

	protected override void OnExit() {
		ClientLib.ExitDialogResult(this, false);
	}

	Window? ActiveWindow => ClientLib.GetActiveView(this);

	IReadOnlyList<MasterMeisho>? ShowMultiSelectDialog(string title, string where, IEnumerable<long>? selectedIds, long startPos) {
		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return null;
		vm.Title = title;
		vm.SetParam(typeof(MasterMeisho), where, "Code", startPos: startPos, selectedIds: selectedIds);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.GetSelectedItems<MasterMeisho>();
	}

	static string BuildSelectedText(IReadOnlyList<MasterMeisho> selected) {
		if (selected.Count == 0) return "未選択";
		return $"{selected.Count}件: {string.Join(", ", selected.Select(FormatSelectedItem))}";
	}

	static string FormatSelectedItem(MasterMeisho item) {
		string label = JoinCodeName(item.Code, item.Name);
		if (label.Length == 0) return item.Id.ToString(CultureInfo.InvariantCulture);
		return $"{item.Id} {label}";
	}

	static string JoinCodeName(string? code, string? name) {
		string cd = code?.Trim() ?? string.Empty;
		string mei = name?.Trim() ?? string.Empty;
		if (cd.Length == 0) return mei;
		if (mei.Length == 0) return cd;
		return $"{cd} {mei}";
	}

	static void AddIdRange(List<string> clauses, string column, long? from, long? to) {
		if (from.HasValue) {
			clauses.Add($"{column} >= {from.Value.ToString(CultureInfo.InvariantCulture)}");
		}

		if (to.HasValue) {
			clauses.Add($"{column} <= {to.Value.ToString(CultureInfo.InvariantCulture)}");
		}
	}

	static void AddSelectedIdInClause(List<string> clauses, string column, IEnumerable<long>? ids) {
		string[] values = NormalizeIds(ids)
			.Select(id => id.ToString(CultureInfo.InvariantCulture))
			.ToArray();
		if (values.Length == 0) return;

		clauses.Add($"{column} IN ({string.Join(",", values)})");
	}

	static void AddCodeRange(List<string> clauses, List<string> parameters, string column, string? from, string? to) {
		string normalizedFrom = Normalize(from);
		string normalizedTo = Normalize(to);

		if (!string.IsNullOrEmpty(normalizedFrom)) {
			clauses.Add($"{column} >= {AddParameter(parameters, normalizedFrom)}");
		}

		if (!string.IsNullOrEmpty(normalizedTo)) {
			clauses.Add($"{column} <= {AddParameter(parameters, normalizedTo)}");
		}
	}

	static void AddLike(List<string> clauses, List<string> parameters, string column, string? value) {
		string normalized = Normalize(value);
		if (string.IsNullOrEmpty(normalized)) return;

		clauses.Add($"{column} LIKE {AddParameter(parameters, $"%{normalized}%")}");
	}

	static string AddParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

	static string Normalize(string? value) => value?.Trim() ?? string.Empty;

	static string? NormalizeNullableText(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	static List<long> NormalizeIds(IEnumerable<long>? ids) =>
		ids?.Where(id => id > 0).Distinct().ToList() ?? [];

	static string NormalizeSelectedText(IEnumerable<long>? ids, string? text) {
		int count = NormalizeIds(ids).Count;
		if (count == 0) return "未選択";
		return string.IsNullOrWhiteSpace(text) || text == "未選択" ? $"{count}件" : text;
	}

}

public sealed class SelectShohinRow(MasterShohin shohin) {
	public MasterShohin Shohin { get; } = shohin;
	public long Id => Shohin.Id;
	public string Code => Shohin.Code;
	public string Name => Shohin.Name;
	public int TankaJodai => Shohin.TankaJodai;
	public string BrandDisplay => FormatCodeName(Shohin.VBrand);
	public string ItemDisplay => FormatCodeName(Shohin.VItem);

	static string FormatCodeName(CodeNameView? value) {
		if (value == null) return string.Empty;
		string cd = value.Cd?.Trim() ?? string.Empty;
		string mei = value.Mei?.Trim() ?? string.Empty;
		if (cd.Length == 0) return mei;
		if (mei.Length == 0) return cd;
		return $"{cd} {mei}";
	}
}
