using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;

namespace CvWpfclient.ViewModels.Sub;

public partial class SelectWinViewModel : Helpers.BaseViewModel {

	ObservableCollection<dynamic>? _observedListData;

	[ObservableProperty]
	public partial string Title { get; set; } = "選択画面";

	Type MyType = typeof(string);

	string BaseWhere = string.Empty;
	string ConditionWhere = string.Empty;
	string Order = string.Empty;
	string[] Parameters = [];
	long StartPos = 0;
	int? MaxCount = AppGlobal.Application.Limit;
	SelectParameter? DisplayConditionParameter;


	bool isLocalData;

	/// <summary>
	/// ローカルデータをセットし、サーバー問い合わせをスキップする
	/// </summary>
	public void SetLocalData<T>(IEnumerable<T> items, string title = "選択画面", long startPos = 0) where T : BaseDbClass {
		isLocalData = true;
		IsDisplayConditionChangeEnabled = false;
		Title = title;
		StartPos = startPos;
		ListData = new ObservableCollection<dynamic>(items.Cast<dynamic>());
		Current = StartPos != 0
			? ListData.FirstOrDefault(x => x.Id == StartPos) ?? ListData.FirstOrDefault()
			: ListData.FirstOrDefault();
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
			var list = Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) as System.Collections.IList;
			if (list != null) {
				ListData = new ObservableCollection<dynamic>(list.Cast<dynamic>());
				Current = StartPos != 0
					? ListData.FirstOrDefault(x => x.Id == StartPos) ?? ListData.FirstOrDefault() ?? new MasterMeisho()
					: ListData.FirstOrDefault() ?? new MasterMeisho();
				WeakReferenceMessenger.Default.Send(new SelectItemMessage(StartPos));
			}
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}


	[ObservableProperty]
	public partial ObservableCollection<dynamic>? ListData { get; set; }

	partial void OnListDataChanged(ObservableCollection<dynamic>? value) {
		if (_observedListData != null) {
			_observedListData.CollectionChanged -= OnListDataCollectionChanged;
		}

		_observedListData = value;

		if (_observedListData != null) {
			_observedListData.CollectionChanged += OnListDataCollectionChanged;
		}

		Count = value?.Count ?? 0;
	}

	void OnListDataCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
		Count = ListData?.Count ?? 0;
	}

	[ObservableProperty]
	public partial object? Current { get; set; }

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial bool IsDisplayConditionChangeEnabled { get; set; } = true;

	[RelayCommand(IncludeCancelCommand = true)]
	async Task ChangeDisplayCondition(CancellationToken ct) {
		if (isLocalData) return;

		long currentId = TryGetCurrentId();
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

	[RelayCommand]
	public void DoSelect() {
		if (Current != null) {
			ClientLib.ExitDialogResult(this, true);
		}
		else
			MessageEx.ShowWarningDialog(message: "選択されていません", owner: ClientLib.GetActiveView(this));
	}

	public void SetParam(Type? type0 = null, string where = "", string order = "", string[]? parameters = null, long startPos = 0, long id = 0) {
		MyType = type0 ?? typeof(string);
		BaseWhere = where;
		ConditionWhere = string.Empty;
		Order = order;
		Parameters = parameters ?? [];
		StartPos = id != 0 ? id : startPos;
		MaxCount = AppGlobal.Application.Limit;
		DisplayConditionParameter = null;
		IsDisplayConditionChangeEnabled = !isLocalData;
	}
	[RelayCommand]
	public void Exit() {
		ClientLib.ExitDialogResult(this, false);
	}

	long TryGetCurrentId() {
		if (Current == null) return StartPos;
		var property = Current.GetType().GetProperty(nameof(BaseDbClass.Id));
		var value = property?.GetValue(Current);
		return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out long id) ? id : StartPos;
	}
}
