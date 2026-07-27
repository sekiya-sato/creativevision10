/*
# description
BaseMenteViewModel は検索・編集・登録・削除・印刷など、マスタ保守画面に共通する状態とコマンドを提供する ViewModel 基底クラスです。

# example
public partial class SampleMenteViewModel : BaseMenteViewModel<SampleEntity> { }
 */
using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.ViewModels.Sub;
using Grpc.Core;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;

namespace CvWpfclient.Helpers;

/// <summary>
/// メンテ画面共通処理
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract partial class BaseMenteViewModel<T> : BaseViewModel where T : BaseDbClass, new() {
	[ObservableProperty]
	public partial ObservableCollection<T> ListData { get; set; } = [];

	[ObservableProperty]
	public partial T Current { get; set; } = new();

	protected override void OnExit() {
		if (MessageEx.ShowQuestionDialog("終了しますか？", owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}
		ClientLib.Exit(this);
	}


	partial void OnCurrentChanged(T oldValue, T newValue) => OnCurrentChangedCore(oldValue, newValue);

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
	public partial T CurrentEdit { get; set; } = new();

	// Source generator will declare a partial method `OnCurrentEditChanged(T? oldValue, T newValue)`
	// Implement it here to forward to a virtual core method that derived viewmodels can override.
	partial void OnCurrentEditChanged(T oldValue, T newValue) => OnCurrentEditChangedCore(oldValue, newValue);

	protected virtual void OnCurrentEditChangedCore(T? oldValue, T newValue) { }

	[ObservableProperty]
	public partial int Count { get; set; }

	[ObservableProperty]
	public partial DateTime StartTime { get; set; } = DateTime.Now;

	[ObservableProperty]
	public partial TimeSpan GetListTime { get; set; } = TimeSpan.Zero;

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	protected virtual Type Tabletype => typeof(T);
	protected virtual string? ListOrder => "Code";
	protected virtual int? ListMaxCount => SelectCodeParam?.MaxCount;

	/// <summary>一覧条件ダイアログのパラメータ（nullならBeforeListAsyncでダイアログ非表示）</summary>
	protected SelectParameter? SelectCodeParam;

	/// <summary>一覧条件ダイアログの表示名称（nullならダイアログをスキップ）</summary>
	protected virtual string? SelectCodeDisplayName => null;

	protected virtual string? ListWhere => BuildSelectCodeWhere(SelectCodeParam);

	protected virtual string[]? ListParams => null;
	protected string[]? SelectCodeWhereParameters { get; set; }
	protected virtual Window? ActiveWindow => ClientLib.GetActiveView(this);
	protected virtual string? FormFile => null;
	protected virtual PrintByCsvParam? PrintByCsvParam => null;
	protected virtual QueryListSqlParam? PrintBySqlParam => null;

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

	protected virtual QueryListParam CreateListQueryParam() {
		SelectCodeWhereParameters = null;
		var where = ListWhere;
		return new(
			itemType: Tabletype,
			where: where,
			order: ListOrder,
			parameters: ListParams ?? SelectCodeWhereParameters,
			maxCount: ListMaxCount
		);
	}

	protected virtual CvMsg CreateListMessage() =>
		new() {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(CreateListQueryParam())
		};

	protected virtual CvMsg CreateExecuteMessage(object parameter, Type dataType) =>
		new() {
			Code = 0,
			Flag = CvFlag.Msg201_Op_Execute,
			DataType = dataType,
			DataMsg = Common.SerializeObject(parameter)
		};

	protected virtual ValueTask<CvMsg> SendMessageAsync(CvMsg message, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		return new ValueTask<CvMsg>(coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct)));
	}

	protected virtual bool HasExecuteError(CvMsg reply, string actionName) {
		if (reply.Code >= 0) {
			return false;
		}
		if (reply.Code == CvMsgErrorCode.ConcurrentUpdate) {
			HandleConcurrentUpdate();
			return true;
		}

		var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
		MessageEx.ShowErrorDialog($"{actionName}エラー: {detail} ({reply.Code})", owner: ActiveWindow);
		return true;
	}

	/// <summary>
	/// 他端末で更新された一覧を破棄し、最新一覧の再取得を促す。
	/// </summary>
	protected virtual void HandleConcurrentUpdate() {
		ListData = [];
		Count = 0;
		Current = new();
		CurrentEdit = new();
		Message = "他端末で更新されたため、表示中の一覧は古くなっています。［一覧取得（F5）］で最新の一覧を再取得してください。";
		MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
	}

	protected virtual bool TryShowSelectCodeDialog(SelectParameter? currentParameter, string displayName, out SelectParameter parameter) {
		var selWin = new Views.Sub.RangeParamView();
		if (selWin.DataContext is not RangeParamViewModel vm) {
			parameter = currentParameter ?? new SelectParameter { DisplayName = displayName };
			return true;
		}

		vm.Initialize(currentParameter ?? new SelectParameter { DisplayName = displayName, MaxCount = AppGlobal.Limit }, Tabletype, order: ListOrder ?? "Code");
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
			Ids = NormalizeSelectedIds(parameter?.Ids),
			IdsText = NormalizeSelectedIdsText(parameter?.Ids, parameter?.IdsText),
			IdsDisplayName = NormalizeNullableText(parameter?.IdsDisplayName) ?? displayName,
			IsToriVisible = parameter?.IsToriVisible ?? false,
			ToriLabel = string.IsNullOrWhiteSpace(parameter?.ToriLabel) ? "取引先Id" : parameter.ToriLabel,
			ToriSearchWhere = NormalizeNullableText(parameter?.ToriSearchWhere),
			ToriIds = NormalizeSelectedIds(parameter?.ToriIds),
			ToriIdsText = NormalizeSelectedIdsText(parameter?.ToriIds, parameter?.ToriIdsText),
			AdditionalIds1Label = string.IsNullOrWhiteSpace(parameter?.AdditionalIds1Label) ? "複数Id 1" : parameter.AdditionalIds1Label,
			AdditionalIds1Column = NormalizeNullableText(parameter?.AdditionalIds1Column),
			AdditionalIds1 = NormalizeAdditionalSelectedIds(parameter?.AdditionalIds1),
			AdditionalIds1Text = NormalizeAdditionalSelectedIdsText(parameter?.AdditionalIds1, parameter?.AdditionalIds1Text),
			AdditionalIds2Label = string.IsNullOrWhiteSpace(parameter?.AdditionalIds2Label) ? "複数Id 2" : parameter.AdditionalIds2Label,
			AdditionalIds2Column = NormalizeNullableText(parameter?.AdditionalIds2Column),
			AdditionalIds2 = NormalizeAdditionalSelectedIds(parameter?.AdditionalIds2),
			AdditionalIds2Text = NormalizeAdditionalSelectedIdsText(parameter?.AdditionalIds2, parameter?.AdditionalIds2Text),
			ItemIds = NormalizeSelectedIds(parameter?.ItemIds),
			ItemIdsText = NormalizeSelectedIdsText(parameter?.ItemIds, parameter?.ItemIdsText),
			FromCode = NormalizeNullableText(parameter?.FromCode),
			ToCode = NormalizeNullableText(parameter?.ToCode),
			Name = NormalizeNullableText(parameter?.Name),
			Jan = NormalizeNullableText(parameter?.Jan),
			MaxCount = parameter?.MaxCount,
			DisplayName = NormalizeNullableText(parameter?.DisplayName) ?? displayName
		};

	protected virtual string? BuildSelectCodeWhere(SelectParameter? parameter) {
		if (parameter == null) {
			return null;
		}

		List<string> clauses = [];
		List<string> parameters = [];
		AddSelectedIdInClause(clauses, "Id", parameter.Ids);
		if (parameter.FromId.HasValue) {
			clauses.Add($"Id >= {parameter.FromId.Value}");
		}
		if (parameter.ToId.HasValue) {
			clauses.Add($"Id <= {parameter.ToId.Value}");
		}
		if (!string.IsNullOrWhiteSpace(parameter.FromCode)) {
			clauses.Add($"Code >= {AddSqlParameter(parameters, parameter.FromCode.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(parameter.ToCode)) {
			clauses.Add($"Code <= {AddSqlParameter(parameters, parameter.ToCode.Trim())}");
		}
		if (!string.IsNullOrWhiteSpace(parameter.Name)) {
			clauses.Add($"Name LIKE {AddSqlParameter(parameters, $"%{EscapeSqlLikePattern(parameter.Name)}%")} ESCAPE '\\'");
		}
		AddOptionalAdditionalIdInClause(clauses, parameter.AdditionalIds1Column, parameter.AdditionalIds1);
		AddOptionalAdditionalIdInClause(clauses, parameter.AdditionalIds2Column, parameter.AdditionalIds2);

		SelectCodeWhereParameters = [.. parameters];
		return clauses.Count == 0 ? null : string.Join(" AND ", clauses);
	}

	protected static List<long> NormalizeSelectedIds(IEnumerable<long>? ids) =>
		ids?.Where(id => id > 0).Distinct().ToList() ?? [];

	protected static List<long> NormalizeAdditionalSelectedIds(IEnumerable<long>? ids) =>
		ids?.Where(id => id >= 0).Distinct().ToList() ?? [];

	protected static string NormalizeSelectedIdsText(IEnumerable<long>? ids, string? text) {
		var count = ids?.Where(id => id > 0).Distinct().Count() ?? 0;
		if (count == 0) return "未選択";
		return string.IsNullOrWhiteSpace(text) || text == "未選択" ? $"{count}件" : text;
	}

	protected static string NormalizeAdditionalSelectedIdsText(IEnumerable<long>? ids, string? text) {
		var count = ids?.Where(id => id >= 0).Distinct().Count() ?? 0;
		if (count == 0) return "未選択";
		return string.IsNullOrWhiteSpace(text) || text == "未選択" ? $"{count}件" : text;
	}

	protected static void AddSelectedIdInClause(List<string> clauses, string column, IEnumerable<long>? ids) {
		string[] values = ids?
			.Where(id => id > 0)
			.Distinct()
			.Select(id => id.ToString(CultureInfo.InvariantCulture))
			.ToArray() ?? [];
		if (values.Length == 0) return;
		clauses.Add($"{column} IN ({string.Join(",", values)})");
	}

	protected static void AddOptionalSelectedIdInClause(List<string> clauses, string? column, IEnumerable<long>? ids) {
		if (string.IsNullOrWhiteSpace(column)) return;
		AddSelectedIdInClause(clauses, column, ids);
	}

	protected static void AddOptionalAdditionalIdInClause(List<string> clauses, string? column, IEnumerable<long>? ids) {
		if (string.IsNullOrWhiteSpace(column)) return;
		string[] values = ids?
			.Where(id => id >= 0)
			.Distinct()
			.Select(id => id.ToString(CultureInfo.InvariantCulture))
			.ToArray() ?? [];
		if (values.Length == 0) return;
		clauses.Add($"{column} IN ({string.Join(",", values)})");
	}

	protected static string? NormalizeNullableText(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value;

	protected static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

	protected static string EscapeSqlLikePattern(string value) =>
		value.Trim()
			.Replace(@"\", @"\\")
			.Replace("%", @"\%")
			.Replace("_", @"\_");

	protected static string AddSqlParameter(List<string> parameters, object value) {
		parameters.Add(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
		return $"@{parameters.Count - 1}";
	}

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

	protected IReadOnlyList<TResult>? ShowMultiSelectDialog<TResult>(Type tableType, string where, string order, IEnumerable<long>? selectedIds = null, long startPos = 0) where TResult : BaseDbClass {
		var selWin = new Views.Sub.SelectMultiWinView();
		if (selWin.DataContext is not SelectMultiWinViewModel vm) return null;
		vm.SetParam(tableType, where, order, startPos: startPos, selectedIds: selectedIds);
		if (ClientLib.ShowDialogView(selWin, this) != true) return null;
		return vm.GetSelectedItems<TResult>();
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

	[RelayCommand(IncludeCancelCommand = true)]
	protected async Task DoOutputPdf(CancellationToken ct) =>
		await RunPrintPdfAsync(FormFile, PrintByCsvParam, PrintBySqlParam, ct);

	/// <summary>
	/// 指定したフォームファイルと印刷データ(CSV または SQL)で PDF を生成し、PDF表示画面を開く。
	/// 1画面で複数の帳票を出し分けたい場合は、この保護メソッドを個別コマンドから直接呼び出す。
	/// </summary>
	protected async Task RunPrintPdfAsync(string? formFile, PrintByCsvParam? csvParam, QueryListSqlParam? sqlParam, CancellationToken ct) {
		ct.ThrowIfCancellationRequested();

		if (string.IsNullOrWhiteSpace(formFile)) {
			Message = "印刷フォームファイルが設定されていません";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return;
		}

		if (csvParam is null && sqlParam is null) {
			Message = "印刷データが設定されていません";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return;
		}

		if (csvParam is not null && sqlParam is not null) {
			Message = "印刷データは CSV と SQL のどちらか一方だけ設定してください";
			MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			var param = (object?)csvParam ?? sqlParam!;
			var dataType = csvParam is not null ? typeof(PrintByCsvParam) : typeof(QueryListSqlParam);
			var msg = new PrintOperation {
				DataType = dataType,
				DataMsg = Common.SerializeObject(param),
				FormFile = formFile,
			};

			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			string? pdfdata = null;
			await foreach (var streamMsg in coreService.PrintPdfAsync(msg, AppGlobal.GetDefaultCallContext(ct))) {
				ct.ThrowIfCancellationRequested();
				Message = string.Join(" ", new[] { streamMsg.StatusString, streamMsg.DataMsg }.Where(s => !string.IsNullOrWhiteSpace(s)));
				if (streamMsg.Status == -2) {
					Message = streamMsg.DataMsg;
					MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
					return;
				}
				if (streamMsg.Status < 0) {
					var errorDetail = string.IsNullOrWhiteSpace(streamMsg.DataMsg) ? streamMsg.StatusString : streamMsg.DataMsg;
					Message = $"PDF出力失敗: {errorDetail}";
					MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
					return;
				}

				if (streamMsg.IsCompleted) {
					pdfdata = streamMsg.DataMsg;
					break;
				}
			}

			if (string.IsNullOrWhiteSpace(pdfdata)) {
				Message = "PDF出力結果が取得できませんでした";
				MessageEx.ShowWarningDialog(Message, owner: ActiveWindow);
				return;
			}

			var viewTitle = string.IsNullOrWhiteSpace(ActiveWindow?.Title)
				? "PDF表示"
				: $"{ActiveWindow.Title} - PDF表示";
			var view = new Views.Sub.WebPdfView { Title = viewTitle };
			if (view.DataContext is not WebPdfViewModel vm) {
				Message = "PDF表示画面の初期化に失敗しました";
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}

			vm.Pdfdata = $"{AppGlobal.Url}/wrk/{pdfdata}";
			view.Title += " " + vm.Pdfdata;
			ClientLib.ShowDialogView(view, this, IsDialog: false);
			view.Owner = null;
			Message = $"PDFを表示しました: {pdfdata}";
		}
		catch (OperationCanceledException cancel) {
			Message = $"Cancelエラー：{cancel.Message}";
			return;
		}
		catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled) {
			Message = "PDF出力をキャンセルしました";
			return;
		}
		catch (Exception ex) {
			Message = $"PDF出力失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

}
