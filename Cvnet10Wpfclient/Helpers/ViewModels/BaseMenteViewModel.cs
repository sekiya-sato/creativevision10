using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Base.Share;
using Cvnet10Wpfclient.ViewModels.Sub;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Cvnet10Wpfclient.Helpers;

/// <summary>
/// メンテ画面共通処理
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract partial class BaseMenteViewModel<T> : BaseViewModel where T : BaseDbClass, new() {
	[ObservableProperty]
	ObservableCollection<T> listData = [];

	[ObservableProperty]
	T current = new();

	partial void OnCurrentChanged(T? oldValue, T newValue) => OnCurrentChangedCore(oldValue, newValue);

	protected virtual void OnCurrentChangedCore(T? oldValue, T newValue) {
		if (newValue == null) {
			CurrentEdit = new();
			return;
		}
		if (oldValue?.Id != newValue.Id && newValue.Id > 0) {
			CurrentEdit = Common.CloneObject(newValue);
		}
		Message = string.Empty;
	}

	[ObservableProperty]
	T currentEdit = new();

	// Source generator will declare a partial method `OnCurrentEditChanged(T? oldValue, T newValue)`
	// Implement it here to forward to a virtual core method that derived viewmodels can override.
	partial void OnCurrentEditChanged(T? oldValue, T newValue) => OnCurrentEditChangedCore(oldValue, newValue);

	protected virtual void OnCurrentEditChangedCore(T? oldValue, T newValue) { }

	[ObservableProperty]
	int count;

	[ObservableProperty]
	DateTime startTime = DateTime.Now;

	[ObservableProperty]
	TimeSpan getListTime = TimeSpan.Zero;

	[ObservableProperty]
	string message = string.Empty;

	protected virtual Type Tabletype => typeof(T);
	protected virtual string? ListOrder => "Code";
	protected virtual int? ListMaxCount => SelectCodeParam?.MaxCount;

	/// <summary>コード範囲ダイアログのパラメータ（nullならBeforeListAsyncでダイアログ非表示）</summary>
	protected SelectParameter? SelectCodeParam;

	/// <summary>コード範囲選択ダイアログの表示名称（nullならダイアログをスキップ）</summary>
	protected virtual string? SelectCodeDisplayName => null;

	protected virtual string? ListWhere => BuildSelectCodeWhere(SelectCodeParam);

	protected virtual string[]? ListParams => null;
	protected virtual Window? ActiveWindow => ClientLib.GetActiveView(this);

	protected virtual bool ConfirmAction(string message) =>
		MessageEx.ShowQuestionDialog(message, owner: ActiveWindow) == MessageBoxResult.Yes;

	protected virtual string GetInsertConfirmMessage() => $"追加しますか？ (CD={GetCode(CurrentEdit)})";
	protected virtual string GetUpdateConfirmMessage() => $"修正しますか？ (CD={GetCode(CurrentEdit)}, Id={CurrentEdit.Id})";
	protected virtual string GetDeleteConfirmMessage() => $"削除しますか？ (CD={GetCode(CurrentEdit)}, Id={CurrentEdit.Id})";

	protected virtual bool CanUpdate() => true;

	protected virtual bool CanDelete() {
		if (ListData.Count == 0) return false;
		return CurrentEdit.Id > 0;
	}

	protected virtual object CreateInsertParam() =>
		new InsertParam(Tabletype, Common.SerializeObject(CurrentEdit));

	protected virtual object CreateUpdateParam() =>
		new UpdateParam(Tabletype, Common.SerializeObject(CurrentEdit));

	protected virtual object CreateDeleteParam() =>
		new DeleteParam(Tabletype, Common.SerializeObject(CurrentEdit));

	protected virtual void AfterList(IList list) { }
	protected virtual void AfterInsert(T item) => Message = $"追加しました (CD={GetCode(item)}, Id={item.Id})";
	protected virtual void AfterUpdate(T item) => Message = $"修正しました (CD={GetCode(item)}, Id={item.Id})";
	protected virtual void AfterDelete(T removedItem) => Message = $"削除しました (CD={GetCode(removedItem)}, Id={removedItem.Id})";

	protected virtual QueryListParam CreateListQueryParam() =>
		new(
			itemType: Tabletype,
			where: ListWhere,
			order: ListOrder,
			parameters: ListParams,
			maxCount: ListMaxCount
		);

	protected virtual CvnetMsg CreateListMessage() =>
		new() {
			Code = 0,
			Flag = CvnetFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(CreateListQueryParam())
		};

	protected virtual CvnetMsg CreateExecuteMessage(object parameter, Type dataType) =>
		new() {
			Code = 0,
			Flag = CvnetFlag.Msg201_Op_Execute,
			DataType = dataType,
			DataMsg = Common.SerializeObject(parameter)
		};

	protected virtual ValueTask<CvnetMsg> SendMessageAsync(CvnetMsg message, CancellationToken ct) {
		var coreService = AppGlobal.GetgRPCService<ICvnetCoreService>();
		return new ValueTask<CvnetMsg>(coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct)));
	}

	protected virtual bool HasExecuteError(CvnetMsg reply, string actionName) {
		if (reply.Code >= 0) {
			return false;
		}

		var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
		MessageEx.ShowErrorDialog($"{actionName}エラー: {detail} ({reply.Code})", owner: ActiveWindow);
		return true;
	}

	protected virtual bool TryShowSelectCodeDialog(SelectParameter? currentParameter, string displayName, out SelectParameter parameter) {
		var selWin = new Views.Sub.RangeParamView();
		if (selWin.DataContext is not RangeParamViewModel vm) {
			parameter = currentParameter ?? new SelectParameter { DisplayName = displayName };
			return true;
		}

		vm.Initialize(currentParameter ?? new SelectParameter { DisplayName = displayName });
		if (ClientLib.ShowDialogView(selWin, this, true) != true) {
			parameter = vm.Parameter;
			return false;
		}

		parameter = NormalizeSelectParameter(vm.Parameter, displayName);
		return true;
	}

	protected virtual SelectParameter NormalizeSelectParameter(SelectParameter? parameter, string? displayName = null) =>
		new() {
			FromId = parameter?.FromId,
			ToId = parameter?.ToId,
			FromCode = NormalizeNullableText(parameter?.FromCode),
			ToCode = NormalizeNullableText(parameter?.ToCode),
			Name = NormalizeNullableText(parameter?.Name),
			MaxCount = parameter?.MaxCount,
			DisplayName = NormalizeNullableText(parameter?.DisplayName) ?? displayName
		};

	protected virtual string? BuildSelectCodeWhere(SelectParameter? parameter) {
		if (parameter == null) {
			return null;
		}

		List<string> clauses = [];
		if (parameter.FromId.HasValue) {
			clauses.Add($"Id >= {parameter.FromId.Value}");
		}
		if (parameter.ToId.HasValue) {
			clauses.Add($"Id <= {parameter.ToId.Value}");
		}
		if (!string.IsNullOrWhiteSpace(parameter.FromCode)) {
			clauses.Add($"Code >= '{EscapeSqlLiteral(parameter.FromCode)}'");
		}
		if (!string.IsNullOrWhiteSpace(parameter.ToCode)) {
			clauses.Add($"Code <= '{EscapeSqlLiteral(parameter.ToCode)}'");
		}
		if (!string.IsNullOrWhiteSpace(parameter.Name)) {
			clauses.Add($"Name LIKE '%{EscapeSqlLiteral(parameter.Name)}%'");
		}

		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	protected static string? NormalizeNullableText(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value;

	protected static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

	protected static string GetCode(BaseDbClass item) =>
		item is IBaseCodeName cn ? cn.Code : item.Id.ToString();

	/// <summary>
	/// 一覧取得
	/// </summary>
	/// <param name="ct"></param>
	/// <returns></returns>
	protected virtual ValueTask<bool> BeforeListAsync(CancellationToken ct) {
		if (string.IsNullOrEmpty(SelectCodeDisplayName)) return new ValueTask<bool>(true);

		ct.ThrowIfCancellationRequested();
		if (!TryShowSelectCodeDialog(SelectCodeParam, SelectCodeDisplayName, out var parameter)) {
			return new ValueTask<bool>(false);
		}
		SelectCodeParam = parameter;
		return new ValueTask<bool>(true);
	}

	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task DoList(CancellationToken ct) {
		if (!await BeforeListAsync(ct)) {
			Message = "一覧表示の処理を中断しました";
			return;
		}
		StartTime = DateTime.Now;
		try {
			ClientLib.Cursor2Wait();

			var reply = await SendMessageAsync(CreateListMessage(), ct);

			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list) {
				ListData = new ObservableCollection<T>(list.Cast<T>());
				Count = ListData.Count;
				Current = ListData.FirstOrDefault() ?? new T();
				AfterList(list);
			}
			GetListTime = DateTime.Now - StartTime;
			Message = $"{StartTime.ToDtStrDateTime().Substring(5)} 取得、画面展開{GetListTime.ToStrSpan()}";
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
			return;
		}
		catch (Exception ex) {
			Message = $"データ取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}
	/// <summary>
	/// 挿入
	/// </summary>
	/// <param name="ct"></param>
	/// <returns></returns>
	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task DoInsert(CancellationToken ct) {
		if (!ConfirmAction(GetInsertConfirmMessage())) return;

		try {
			ct.ThrowIfCancellationRequested();

			var reply = await SendMessageAsync(CreateExecuteMessage(CreateInsertParam(), typeof(InsertParam)), ct);
			if (HasExecuteError(reply, "追加")) {
				return;
			}
			var item = Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType) as T;

			if (item != null) {
				ListData.Add(item);
				Count = ListData.Count;
				Current = item;
				AfterInsert(item);
			}
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
			return;
		}
		catch (Exception ex) {
			Message = $"追加失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
	}
	/// <summary>
	/// 更新
	/// </summary>
	/// <param name="ct"></param>
	/// <returns></returns>
	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task DoUpdate(CancellationToken ct) {
		if (!CanUpdate()) return;
		if (!ConfirmAction(GetUpdateConfirmMessage())) return;

		try {
			ct.ThrowIfCancellationRequested();

			var reply = await SendMessageAsync(CreateExecuteMessage(CreateUpdateParam(), typeof(UpdateParam)), ct);
			if (HasExecuteError(reply, "修正")) {
				return;
			}

			if (Common.DeserializeObject(reply.DataMsg ?? "", reply.DataType) is T item) {
				Common.DeepCopyValue(Tabletype, item, Current);
				CurrentEdit = Common.CloneObject(Current);
				AfterUpdate(item);
			}
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
			return;
		}
		catch (Exception ex) {
			Message = $"修正失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
	}
	/// <summary>
	/// 削除
	/// </summary>
	/// <param name="ct"></param>
	/// <returns></returns>
	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task DoDelete(CancellationToken ct) {
		if (!CanDelete()) {
			MessageEx.ShowWarningDialog("削除対象を選択してください", owner: ActiveWindow);
			return;
		}

		if (!ConfirmAction(GetDeleteConfirmMessage())) return;

		try {
			ct.ThrowIfCancellationRequested();

			var reply = await SendMessageAsync(CreateExecuteMessage(CreateDeleteParam(), typeof(DeleteParam)), ct);
			if (HasExecuteError(reply, "削除")) {
				return;
			}

			var removedItem = Current;
			var currentIndex = ListData.IndexOf(Current);
			var nextIndex = currentIndex + 1 < ListData.Count ? currentIndex + 1 : 0;
			var nextItem = ListData.ElementAtOrDefault(nextIndex);

			ListData.Remove(Current);
			Current = nextItem ?? ListData.FirstOrDefault() ?? new T();
			Count = ListData.Count;

			AfterDelete(removedItem);
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
			return;
		}
		catch (Exception ex) {
			Message = $"削除失敗：{ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
	}

	protected TResult? ShowSelectDialog<TResult>(Type tableType, string where, string order, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectWinView();
		if (selWin.DataContext is not SelectWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.Current as TResult;
	}

	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task DoOutputJson(CancellationToken ct) {
		ct.ThrowIfCancellationRequested();

		var outstr = JsonConvert.SerializeObject(ListData, Formatting.Indented);
		var dialog = new Microsoft.Win32.SaveFileDialog {
			FileName = Tabletype.Name + DateTime.Now.ToDtStrDate2(),
			DefaultExt = ".json",
			Filter = "Text documents (.json)|*.json",
			DefaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
		};

		if (dialog.ShowDialog(ActiveWindow) != true) return;

		ct.ThrowIfCancellationRequested();
		await File.WriteAllTextAsync(dialog.FileName, outstr, ct);
	}

}
