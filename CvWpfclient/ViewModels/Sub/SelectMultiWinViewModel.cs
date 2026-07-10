using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace CvWpfclient.ViewModels.Sub;

public partial class SelectMultiWinViewModel : Helpers.BaseViewModel {
	ObservableCollection<SelectMultiWinItem>? _observedListData;
	HashSet<long> initialSelectedIds = [];

	[ObservableProperty]
	public partial string Title { get; set; } = "複数選択画面";

	[ObservableProperty]
	public partial string DisplayNameCode { get; set; } = " Code";
	[ObservableProperty]
	public partial string DisplayNameName { get; set; } = " 名前";
	[ObservableProperty]
	public partial string DisplayNameRyaku { get; set; } = " 略称";
	[ObservableProperty]
	public partial int DisplayWidthCode { get; set; } = 80;
	[ObservableProperty]
	public partial int DisplayWidthName { get; set; } = 280;
	[ObservableProperty]
	public partial int DisplayWidthRyaku { get; set; } = 120;



	Type MyType = typeof(string);

	string BaseWhere = string.Empty;
	string ConditionWhere = string.Empty;
	string Order = string.Empty;
	string[] Parameters = [];
	long StartPos = 0;
	int? MaxCount = AppGlobal.Application.Limit;
	SelectParameter? DisplayConditionParameter;

	bool isLocalData;

	public IReadOnlyList<object> SelectedItems =>
		ListData?.Where(row => row.IsSelected).Select(row => row.Item).ToList() ?? [];

	public IReadOnlyList<T> GetSelectedItems<T>() where T : BaseDbClass =>
		SelectedItems.OfType<T>().ToList();

	public void SetLocalData<T>(IEnumerable<T> items, string title = "複数選択画面", long startPos = 0, IEnumerable<long>? selectedIds = null) where T : BaseDbClass {
		isLocalData = true;
		IsDisplayConditionChangeEnabled = false;
		Title = title;
		StartPos = startPos;
		SetInitialSelectedIds(selectedIds);
		ListData = WrapItems(items);
		Current = FindInitialCurrent();
	}

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		if (isLocalData) return;
		await InitList(ct);
	}

	async Task InitList(CancellationToken ct) {
		try {
			ct.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			string? where = SelectDisplayConditionHelper.CombineWhere(BaseWhere, ConditionWhere);
			QueryListParam queryListParam = typeof(IBaseCodeName).IsAssignableFrom(MyType)
				? new QueryListSimpleParam(itemType: MyType, where: where, order: Order, parameters: Parameters, maxCount: MaxCount)
				: new QueryListParam(itemType: MyType, where: where, order: Order, parameters: Parameters, maxCount: MaxCount);
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = queryListParam.GetType(),
				DataMsg = Common.SerializeObject(queryListParam)
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			ct.ThrowIfCancellationRequested();
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as IList;
			if (list != null) {
				ListData = WrapItems(list);
				Current = FindInitialCurrent();
				WeakReferenceMessenger.Default.Send(new SelectItemMessage(Current?.Id ?? StartPos));
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	[ObservableProperty]
	public partial ObservableCollection<SelectMultiWinItem>? ListData { get; set; }

	partial void OnListDataChanged(ObservableCollection<SelectMultiWinItem>? value) {
		DetachListData(_observedListData);
		_observedListData = value;
		AttachListData(_observedListData);
		UpdateCounts();
	}

	void AttachListData(ObservableCollection<SelectMultiWinItem>? value) {
		if (value == null) return;
		value.CollectionChanged += OnListDataCollectionChanged;
		foreach (var row in value) {
			row.PropertyChanged += OnRowPropertyChanged;
		}
	}

	void DetachListData(ObservableCollection<SelectMultiWinItem>? value) {
		if (value == null) return;
		value.CollectionChanged -= OnListDataCollectionChanged;
		foreach (var row in value) {
			row.PropertyChanged -= OnRowPropertyChanged;
		}
	}

	void OnListDataCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
		if (e.OldItems != null) {
			foreach (var row in e.OldItems.Cast<SelectMultiWinItem>()) {
				row.PropertyChanged -= OnRowPropertyChanged;
			}
		}

		if (e.NewItems != null) {
			foreach (var row in e.NewItems.Cast<SelectMultiWinItem>()) {
				row.PropertyChanged += OnRowPropertyChanged;
			}
		}

		UpdateCounts();
	}

	void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e) {
		if (e.PropertyName == nameof(SelectMultiWinItem.IsSelected)) {
			UpdateCounts();
		}
	}

	[ObservableProperty]
	public partial SelectMultiWinItem? Current { get; set; }

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial int SelectedCount { get; set; }

	[ObservableProperty]
	public partial bool IsDisplayConditionChangeEnabled { get; set; } = true;

	void UpdateCounts() {
		Count = ListData?.Count ?? 0;
		SelectedCount = ListData?.Count(row => row.IsSelected) ?? 0;
		OnPropertyChanged(nameof(SelectedItems));
	}

	[RelayCommand]
	public void ToggleCurrent() {
		if (Current != null) {
			Current.IsSelected = !Current.IsSelected;
		}
	}

	[RelayCommand]
	public void SelectAll() {
		if (ListData == null) return;
		var limit = AppGlobal.Application.Limit;
		if (limit > 0 && ListData.Count >= limit) {
			MessageEx.ShowErrorDialog($"選択数が多すぎます（最大 {limit:N0} 件）", owner: ClientLib.GetActiveView(this));
			return;
		}
		foreach (var row in ListData) {
			row.IsSelected = true;
		}
	}

	[RelayCommand]
	public void ClearSelection() {
		if (ListData == null) return;
		foreach (var row in ListData) {
			row.IsSelected = false;
		}
	}

	[RelayCommand]
	public void DoSelect() {
		ClientLib.ExitDialogResult(this, true);
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task ChangeDisplayCondition(CancellationToken ct) {
		if (isLocalData) return;

		long currentId = Current?.Id ?? StartPos;
		SetInitialSelectedIds(ListData?.Where(row => row.IsSelected).Select(row => row.Id));
		string displayName = SelectDisplayConditionHelper.GetDisplayName(MyType, Title);
		if (!SelectDisplayConditionHelper.TryShowConditionDialog(MyType, BaseWhere, Order, DisplayConditionParameter, this, displayName, out var parameter, out var conditionWhere, out var maxCount)) {
			return;
		}

		DisplayConditionParameter = parameter;
		ConditionWhere = conditionWhere ?? string.Empty;
		MaxCount = maxCount;
		StartPos = currentId;
		await InitList(ct);
	}

	public void SetParam(Type? type0 = null, string where = "", string order = "", string[]? parameters = null, long startPos = 0, long id = 0, IEnumerable<long>? selectedIds = null) {
		MyType = type0 ?? typeof(string);
		BaseWhere = where;
		ConditionWhere = string.Empty;
		Order = order;
		Parameters = parameters ?? [];
		StartPos = id != 0 ? id : startPos;
		MaxCount = AppGlobal.Application.Limit;
		DisplayConditionParameter = null;
		IsDisplayConditionChangeEnabled = !isLocalData;
		SetInitialSelectedIds(selectedIds);
	}

	protected override void OnExit() {
		ClientLib.ExitDialogResult(this, false);
	}

	void SetInitialSelectedIds(IEnumerable<long>? selectedIds) {
		initialSelectedIds = selectedIds?.Where(id => id != 0).ToHashSet() ?? [];
	}

	ObservableCollection<SelectMultiWinItem> WrapItems(IEnumerable items) =>
		new(items.Cast<object>().Select(item => new SelectMultiWinItem(item, initialSelectedIds.Contains(SelectMultiWinItem.GetItemId(item)))));

	SelectMultiWinItem? FindInitialCurrent() {
		if (ListData == null || ListData.Count == 0) return null;
		if (StartPos != 0) {
			return ListData.FirstOrDefault(row => row.Id == StartPos) ?? ListData.FirstOrDefault();
		}
		return ListData.FirstOrDefault(row => row.IsSelected) ?? ListData.FirstOrDefault();
	}
}

public partial class SelectMultiWinItem : ObservableObject {
	public SelectMultiWinItem(object item, bool isSelected = false) {
		Item = item;
		IsSelected = isSelected;
	}

	public object Item { get; }

	[ObservableProperty]
	public partial bool IsSelected { get; set; }

	public long Id => GetItemId(Item);

	public string Code => GetStringValue(nameof(Code));

	public string Name => GetStringValue(nameof(Name));

	public string Ryaku => GetStringValue(nameof(Ryaku));

	public string Desc0 => GetStringValue(nameof(Desc0));

	public static long GetItemId(object item) {
		var value = GetRawValue(item, "Id");
		return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var id) ? id : 0;
	}

	string GetStringValue(string propertyName) =>
		Convert.ToString(GetRawValue(Item, propertyName), CultureInfo.CurrentCulture) ?? string.Empty;

	static object? GetRawValue(object item, string propertyName) {
		var property = item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		return property?.GetValue(item);
	}
}
