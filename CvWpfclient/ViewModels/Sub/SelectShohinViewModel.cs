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
	string shohinCodeFrom = string.Empty;

	[ObservableProperty]
	string shohinCodeTo = string.Empty;

	[ObservableProperty]
	string shohinName = string.Empty;

	[ObservableProperty]
	string brandCodeFrom = string.Empty;

	[ObservableProperty]
	string brandCodeTo = string.Empty;

	[ObservableProperty]
	string itemCodeFrom = string.Empty;

	[ObservableProperty]
	string itemCodeTo = string.Empty;

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
	string message = "条件を入力して一覧表示してください";

	public MasterShohin? SelectedShohin => Current?.Shohin;

	public bool IsResultMode => !IsSearchMode;

	partial void OnIsSearchModeChanged(bool value) {
		OnPropertyChanged(nameof(IsResultMode));
		Title = value ? "商品検索" : "商品一覧選択";
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
	void SelectShohinCodeFrom() => SelectShohinCode(x => ShohinCodeFrom = x);

	[RelayCommand]
	void SelectShohinCodeTo() => SelectShohinCode(x => ShohinCodeTo = x);

	[RelayCommand]
	void SelectBrandCodeFrom() => SelectCode<MasterMeisho>("Kubun='BRD'", "Code", x => BrandCodeFrom = x);

	[RelayCommand]
	void SelectBrandCodeTo() => SelectCode<MasterMeisho>("Kubun='BRD'", "Code", x => BrandCodeTo = x);

	[RelayCommand]
	void SelectItemCodeFrom() => SelectCode<MasterMeisho>("Kubun='ITM'", "Code", x => ItemCodeFrom = x);

	[RelayCommand]
	void SelectItemCodeTo() => SelectCode<MasterMeisho>("Kubun='ITM'", "Code", x => ItemCodeTo = x);

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
		AddCodeRange(clauses, parameters, "M.Code", ShohinCodeFrom, ShohinCodeTo);
		AddLike(clauses, parameters, "M.Name", ShohinName);
		AddCodeRange(clauses, parameters, "IFNULL(Brd.Code, '')", BrandCodeFrom, BrandCodeTo);
		AddCodeRange(clauses, parameters, "IFNULL(Item.Code, '')", ItemCodeFrom, ItemCodeTo);

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

	void SelectCode<T>(string where, string order, Action<string> setCode)
		where T : BaseDbClass, IBaseCodeName {
		var view = new Views.Sub.SelectWinView();
		if (view.DataContext is not SelectWinViewModel vm) return;

		vm.SetParam(typeof(T), where, order);
		if (ClientLib.ShowDialogView(view, this) != true) return;
		if (vm.Current is not T selected) return;

		setCode(selected.Code ?? string.Empty);
	}

	void SelectShohinCode(Action<string> setCode) {
		var view = new Views.Sub.SelectShohinView();
		if (view.DataContext is not SelectShohinViewModel vm) return;

		vm.ShohinCodeFrom = ShohinCodeFrom;
		vm.ShohinCodeTo = ShohinCodeTo;
		vm.ShohinName = ShohinName;
		vm.BrandCodeFrom = BrandCodeFrom;
		vm.BrandCodeTo = BrandCodeTo;
		vm.ItemCodeFrom = ItemCodeFrom;
		vm.ItemCodeTo = ItemCodeTo;
		vm.Jan = Jan;

		if (ClientLib.ShowDialogView(view, this) != true) return;
		MasterShohin? selected = vm.SelectedShohin;
		if (selected == null) return;

		setCode(selected.Code ?? string.Empty);
	}

	protected override void OnExit() {
		ClientLib.ExitDialogResult(this, false);
	}

	Window? ActiveWindow => ClientLib.GetActiveView(this);

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
