using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CvAsset;
using CvBase;
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
	string title = "複数選択画面";

	Type MyType = typeof(string);

	string Where = string.Empty;
	string Order = string.Empty;
	string[] Parameters = [];
	long StartPos = 0;

	bool isLocalData;

	public IReadOnlyList<object> SelectedItems =>
		ListData?.Where(row => row.IsSelected).Select(row => row.Item).ToList() ?? [];

	public IReadOnlyList<T> GetSelectedItems<T>() where T : BaseDbClass =>
		SelectedItems.OfType<T>().ToList();

	public void SetLocalData<T>(IEnumerable<T> items, string title = "複数選択画面", long startPos = 0, IEnumerable<long>? selectedIds = null) where T : BaseDbClass {
		isLocalData = true;
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
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListSimpleParam),
				DataMsg = Common.SerializeObject(new QueryListParam(
					itemType: MyType,
					where: Where,
					order: Order,
					parameters: Parameters
				))
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
	ObservableCollection<SelectMultiWinItem>? listData;

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
	SelectMultiWinItem? current;

	[ObservableProperty]
	int count;

	[ObservableProperty]
	int selectedCount;

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
		if (SelectedCount > 0) {
			ClientLib.ExitDialogResult(this, true);
		}
		else {
			MessageEx.ShowWarningDialog(message: "選択されていません", owner: ClientLib.GetActiveView(this));
		}
	}

	public void SetParam(Type? type0 = null, string where = "", string order = "", string[]? parameters = null, long startPos = 0, long id = 0, IEnumerable<long>? selectedIds = null) {
		MyType = type0 ?? typeof(string);
		Where = where;
		Order = order;
		Parameters = parameters ?? [];
		StartPos = id != 0 ? id : startPos;
		SetInitialSelectedIds(selectedIds);
	}

	[RelayCommand]
	public void Exit() {
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
		this.isSelected = isSelected;
	}

	public object Item { get; }

	[ObservableProperty]
	bool isSelected;

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
