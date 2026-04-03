using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cvnet10Asset;
using Cvnet10Base;
using Cvnet10Wpfclient.Helpers;
using NPoco;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Windows;

namespace Cvnet10Wpfclient.ViewModels._00System;

public partial class SysGeneralMenteViewModel : Helpers.BaseViewModel {
	Type? targetType;
	string targetName = string.Empty;
	int? maxCount;

	[ObservableProperty]
	string title = "汎用マスタメンテ";

	[ObservableProperty]
	string message = string.Empty;

	[ObservableProperty]
	ObservableCollection<SysGeneralEditRow> rows = [];

	[ObservableProperty]
	SysGeneralEditRow? selectedRow;

	[ObservableProperty]
	int count;

	public bool HasSelection => SelectedRow != null;

	partial void OnSelectedRowChanged(SysGeneralEditRow? oldValue, SysGeneralEditRow? newValue) {
		OnPropertyChanged(nameof(HasSelection));
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task Init(CancellationToken ct) {
		if (!ResolveTargetType()) {
			return;
		}

		await ReloadAsync(ct);
	}

	[RelayCommand]
	void DoAdd() {
		if (targetType == null) {
			return;
		}

		if (!TryCreateNewItem(out var item)) {
			return;
		}

		var row = SysGeneralEditMapper.CreateRow(item, targetType, isNew: true);
		Rows.Add(row);
		Count = Rows.Count;
		SelectedRow = row;
		Message = "新規行を追加しました。内容を入力して保存してください。";
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoReload(CancellationToken ct) {
		await ReloadAsync(ct);
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoSave(CancellationToken ct) {
		if (SelectedRow == null || targetType == null) {
			return;
		}

		if (!TryBuildItem(SelectedRow, out var item, out var errorMessage) || item == null) {
			Message = errorMessage;
			MessageEx.ShowErrorDialog(errorMessage, owner: ActiveWindow);
			return;
		}

		var codeValue = GetPropertyText(item, "Code");
		var actionName = SelectedRow.IsNew ? "追加" : "修正";
		var confirmMessage = SelectedRow.IsNew
			? $"追加しますか？ ({targetName} / CD={codeValue})"
			: $"修正しますか？ ({targetName} / CD={codeValue}, Id={item.Id})";

		if (MessageEx.ShowQuestionDialog(confirmMessage, owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}

		try {
			ct.ThrowIfCancellationRequested();
			object parameter = SelectedRow.IsNew
				? new InsertParam(targetType, Common.SerializeObject(item))
				: new UpdateParam(targetType, Common.SerializeObject(item));

			var reply = await SendMessageAsync(CreateExecuteMessage(parameter, parameter.GetType()), ct);
			if (HasExecuteError(reply, actionName)) {
				return;
			}

			var selectedId = item.Id;
			if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is BaseDbClass savedItem) {
				selectedId = savedItem.Id;
			}

			await ReloadAsync(ct, selectedId);
			Message = $"{actionName}しました ({targetName})";
		}
		catch (OperationCanceledException ex) {
			Message = $"Cancelエラー：{ex.Message}";
		}
		catch (Exception ex) {
			Message = $"{actionName}失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	async Task DoDelete(CancellationToken ct) {
		if (SelectedRow == null || targetType == null) {
			return;
		}

		if (SelectedRow.IsNew) {
			Rows.Remove(SelectedRow);
			Count = Rows.Count;
			SelectedRow = Rows.FirstOrDefault();
			Message = "未保存の新規行を取り消しました。";
			return;
		}

		if (!TryBuildItem(SelectedRow, out var item, out var errorMessage) || item == null) {
			Message = errorMessage;
			MessageEx.ShowErrorDialog(errorMessage, owner: ActiveWindow);
			return;
		}

		var confirmMessage = $"削除しますか？ ({targetName} / CD={GetPropertyText(item, "Code")}, Id={item.Id})";
		if (MessageEx.ShowQuestionDialog(confirmMessage, owner: ActiveWindow) != MessageBoxResult.Yes) {
			return;
		}

		try {
			ct.ThrowIfCancellationRequested();
			var reply = await SendMessageAsync(
				CreateExecuteMessage(new DeleteParam(targetType, Common.SerializeObject(item)), typeof(DeleteParam)),
				ct);

			if (HasExecuteError(reply, "削除")) {
				return;
			}

			await ReloadAsync(ct);
			Message = $"削除しました ({targetName})";
		}
		catch (OperationCanceledException ex) {
			Message = $"Cancelエラー：{ex.Message}";
		}
		catch (Exception ex) {
			Message = $"削除失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
	}

	async Task ReloadAsync(CancellationToken ct, long? selectedId = null) {
		if (targetType == null) {
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			var reply = await SendMessageAsync(CreateListMessage(), ct);

			if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is not IList list) {
				Rows = [];
				SelectedRow = null;
				Count = 0;
				return;
			}

			Rows = new ObservableCollection<SysGeneralEditRow>(
				list.Cast<object>()
					.OfType<BaseDbClass>()
					.Select(item => SysGeneralEditMapper.CreateRow(item, targetType, isNew: false)));
			Count = Rows.Count;
			SelectedRow = SelectRow(selectedId);
			Message = $"{targetName} を {Count:N0} 件取得しました。";
		}
		catch (OperationCanceledException ex) {
			Message = $"Cancelエラー：{ex.Message}";
		}
		catch (Exception ex) {
			Message = $"データ取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	SysGeneralEditRow? SelectRow(long? selectedId) {
		if (selectedId.HasValue) {
			var match = Rows.FirstOrDefault(x => x.Id == selectedId.Value);
			if (match != null) {
				return match;
			}
		}

		return Rows.FirstOrDefault();
	}

	CvnetMsg CreateListMessage() => new() {
		Code = 0,
		Flag = CvnetFlag.Msg101_Op_Query,
		DataType = typeof(QueryListParam),
		DataMsg = Common.SerializeObject(new QueryListParam(targetType!, maxCount: maxCount))
	};

	CvnetMsg CreateExecuteMessage(object parameter, Type dataType) => new() {
		Code = 0,
		Flag = CvnetFlag.Msg201_Op_Execute,
		DataType = dataType,
		DataMsg = Common.SerializeObject(parameter)
	};

	ValueTask<CvnetMsg> SendMessageAsync(CvnetMsg message, CancellationToken ct) {
		var coreService = AppGlobal.GetGrpcService<ICvnetCoreService>();
		return new ValueTask<CvnetMsg>(coreService.QueryMsgAsync(message, AppGlobal.GetDefaultCallContext(ct)));
	}

	bool HasExecuteError(CvnetMsg reply, string actionName) {
		if (reply.Code >= 0) {
			return false;
		}

		var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
		MessageEx.ShowErrorDialog($"{actionName}エラー: {detail} ({reply.Code})", owner: ActiveWindow);
		return true;
	}

	bool TryBuildItem(SysGeneralEditRow row, out BaseDbClass? item, out string errorMessage) {
		try {
			if (SysGeneralEditMapper.ToItem(row, targetType!) is not BaseDbClass dbItem) {
				item = null;
				errorMessage = $"{targetName} の型変換に失敗しました。";
				return false;
			}

			item = dbItem;
			errorMessage = string.Empty;
			return true;
		}
		catch (Exception ex) {
			item = null;
			errorMessage = ex.Message;
			return false;
		}
	}

	Window? ActiveWindow => ClientLib.GetActiveView(this);

	bool ResolveTargetType() {
		var raw = AddInfo?.Trim() ?? string.Empty;
		string tableName;

		if (raw.Contains('|')) {
			var parts = raw.Split('|', 2);
			tableName = parts[0].Trim();
			if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var mc) && mc > 0) {
				maxCount = mc;
			}
		} else {
			tableName = raw;
		}

		if (string.IsNullOrWhiteSpace(tableName)) {
			MessageEx.ShowErrorDialog("テーブル名が指定されていません。", owner: ActiveWindow);
			ClientLib.Exit(this);
			return false;
		}

		var resolved = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(x => {
				try {
					return x.GetTypes();
				}
				catch (ReflectionTypeLoadException ex) {
					return ex.Types.Where(t => t != null).Cast<Type>();
				}
			})
			.Where(x => typeof(BaseDbClass).IsAssignableFrom(x) && !x.IsAbstract)
			.FirstOrDefault(x => string.Equals(GetTableName(x), tableName, StringComparison.OrdinalIgnoreCase));

		if (resolved == null) {
			MessageEx.ShowErrorDialog($"指定テーブルに対応する型が見つかりません: {tableName}", owner: ActiveWindow);
			ClientLib.Exit(this);
			return false;
		}

		targetType = resolved;
		targetName = GetTableName(resolved);
		Title = $"汎用マスタメンテ ({targetName})";
		return true;
	}

	static string GetTableName(Type type) {
		return type.GetCustomAttributes(typeof(TableNameAttribute), true).FirstOrDefault() is TableNameAttribute attr
			? attr.Value
			: type.Name;
	}

	bool TryCreateNewItem(out BaseDbClass item) {
		if (Activator.CreateInstance(targetType!) is BaseDbClass newItem) {
			item = newItem;
			return true;
		}

		item = null!;
		MessageEx.ShowErrorDialog($"{targetName} の新規作成に失敗しました。", owner: ActiveWindow);
		return false;
	}

	static void TrySetProperty(object target, string propertyName, string value) {
		var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		if (property == null || !property.CanWrite || property.PropertyType != typeof(string)) {
			return;
		}

		property.SetValue(target, value);
	}

	static string GetPropertyText(object target, string propertyName) {
		var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
		return property?.GetValue(target)?.ToString() ?? "-";
	}
}

public partial class SysGeneralEditRow : ObservableObject {
	[ObservableProperty]
	string title = string.Empty;

	[ObservableProperty]
	bool isNew;

	[ObservableProperty]
	Type originalType = typeof(object);

	public ObservableCollection<SysGeneralEditCell> Cells { get; } = [];

	public long Id => long.TryParse(GetCellText("Id"), out var id) ? id : 0;

	public bool TryGetCell(string key, out SysGeneralEditCell cell) {
		cell = Cells.FirstOrDefault(x => x.Key == key) ?? new SysGeneralEditCell();
		return cell.Key.Length > 0;
	}

	public void AddCell(SysGeneralEditCell cell) {
		cell.PropertyChanged += (_, e) => {
			if (e.PropertyName == nameof(SysGeneralEditCell.EditText)) {
				RefreshTitle();
			}
		};
		Cells.Add(cell);
	}

	public void RefreshTitle() {
		var editableCells = Cells
			.Where(c => c.Key is not ("Id" or "Vdc" or "Vdu"))
			.Take(3)
			.Select(c => c.EditText?.Trim() ?? string.Empty)
			.Where(t => t.Length > 0);
		var prefix = IsNew ? "[新規] " : string.Empty;
		var body = string.Join(" ", editableCells);
		Title = $"{prefix}{body}".Trim();
		if (string.IsNullOrWhiteSpace(Title)) {
			Title = IsNew ? "[新規] 未入力" : "未入力";
		}
	}

	string GetCellText(string key) => Cells.FirstOrDefault(x => x.Key == key)?.EditText ?? string.Empty;
}

public partial class SysGeneralEditCell : ObservableObject {
	[ObservableProperty]
	string key = string.Empty;

	[ObservableProperty]
	string header = string.Empty;

	[ObservableProperty]
	string editText = string.Empty;

	[ObservableProperty]
	bool isReadOnly;

	public Type ValueType { get; init; } = typeof(string);

	public bool IsJsonColumn { get; init; }
}

static class SysGeneralEditMapper {
	public static SysGeneralEditRow CreateRow(BaseDbClass item, Type originalType, bool isNew) {
		var row = new SysGeneralEditRow {
			IsNew = isNew,
			OriginalType = originalType
		};

		foreach (var property in GetEditableProperties(originalType)) {
			var isJson = IsJsonSerializedProperty(property);
			var value = property.GetValue(item);
			row.AddCell(new SysGeneralEditCell {
				Key = property.Name,
				Header = property.Name,
				EditText = isJson ? ToJsonText(value) : ToText(value),
				IsReadOnly = property.Name is "Id" or "Vdc" or "Vdu",
				ValueType = property.PropertyType,
				IsJsonColumn = isJson
			});
		}

		row.RefreshTitle();
		return row;
	}

	public static object ToItem(SysGeneralEditRow row, Type type) {
		if (Activator.CreateInstance(type) is not object item) {
			throw new InvalidOperationException($"{type.Name} の生成に失敗しました。");
		}

		foreach (var property in GetEditableProperties(type)) {
			if (!row.TryGetCell(property.Name, out var cell)) {
				continue;
			}

			var value = IsJsonSerializedProperty(property)
				? ConvertFromJsonText(cell.EditText, property.PropertyType, property.Name)
				: ConvertFromText(cell.EditText, property.PropertyType, property.Name);
			property.SetValue(item, value);
		}

		return item;
	}

	static IEnumerable<PropertyInfo> GetEditableProperties(Type type) {
		var props = type
			.GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(x => x.CanRead && x.CanWrite)
			.Where(x => x.GetIndexParameters().Length == 0)
			.Where(IsSupportedProperty)
			.ToList();

		return [
			.. props.Where(x => x.Name == "Id"),
			.. props.Where(x => x.Name == "Vdc"),
			.. props.Where(x => x.Name == "Vdu"),
			.. props.Where(x => x.Name != "Id" && x.Name != "Vdc" && x.Name != "Vdu")
		];
	}

	static bool IsSupportedProperty(PropertyInfo property) {
		if (HasAttribute<IgnoreAttribute>(property)
			|| HasAttribute<ResultColumnAttribute>(property)
			|| HasAttribute<ComputedColumnAttribute>(property)
			|| HasAttribute<JsonIgnoreAttribute>(property)) {
			return false;
		}

		if (HasAttribute<SerializedColumnAttribute>(property)) {
			return true;
		}

		return IsSupportedType(property.PropertyType);
	}

	static bool IsJsonSerializedProperty(PropertyInfo property) =>
		HasAttribute<SerializedColumnAttribute>(property) && !IsSupportedType(property.PropertyType);

	static bool HasAttribute<T>(MemberInfo member) where T : Attribute =>
		member.GetCustomAttribute<T>() != null;

	static bool IsSupportedType(Type type) {
		var actualType = Nullable.GetUnderlyingType(type) ?? type;
		return actualType.IsEnum
			|| actualType == typeof(string)
			|| actualType == typeof(int)
			|| actualType == typeof(long)
			|| actualType == typeof(short)
			|| actualType == typeof(decimal)
			|| actualType == typeof(double)
			|| actualType == typeof(float)
			|| actualType == typeof(bool)
			|| actualType == typeof(DateTime);
	}

	static string ToText(object? value) {
		if (value == null) {
			return string.Empty;
		}

		return value switch {
			DateTime dateTime => dateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
			_ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
		};
	}

	static object? ConvertFromText(string text, Type targetType, string propertyName) {
		var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
		var trimmed = text.Trim();

		if (actualType == typeof(string)) {
			return text;
		}

		if (string.IsNullOrWhiteSpace(trimmed)) {
			if (Nullable.GetUnderlyingType(targetType) != null) {
				return null;
			}

			throw new InvalidOperationException($"{propertyName} は必須です。");
		}

		try {
			if (actualType.IsEnum) {
				return Enum.Parse(actualType, trimmed, ignoreCase: true);
			}

			if (actualType == typeof(int)) return int.Parse(trimmed, CultureInfo.InvariantCulture);
			if (actualType == typeof(long)) return long.Parse(trimmed, CultureInfo.InvariantCulture);
			if (actualType == typeof(short)) return short.Parse(trimmed, CultureInfo.InvariantCulture);
			if (actualType == typeof(decimal)) return decimal.Parse(trimmed, CultureInfo.InvariantCulture);
			if (actualType == typeof(double)) return double.Parse(trimmed, CultureInfo.InvariantCulture);
			if (actualType == typeof(float)) return float.Parse(trimmed, CultureInfo.InvariantCulture);
			if (actualType == typeof(bool)) return bool.Parse(trimmed);
			if (actualType == typeof(DateTime)) return DateTime.Parse(trimmed, CultureInfo.InvariantCulture);
		}
		catch (Exception ex) {
			throw new InvalidOperationException($"{propertyName} の入力値変換に失敗しました: {ex.Message}", ex);
		}

		throw new InvalidOperationException($"{propertyName} の型 {actualType.Name} には未対応です。");
	}

	static string ToJsonText(object? value) {
		if (value == null) {
			return string.Empty;
		}

		return JsonConvert.SerializeObject(value, Formatting.Indented);
	}

	static object? ConvertFromJsonText(string text, Type targetType, string propertyName) {
		if (string.IsNullOrWhiteSpace(text)) {
			if (Nullable.GetUnderlyingType(targetType) != null) {
				return null;
			}

			return Activator.CreateInstance(targetType);
		}

		try {
			return JsonConvert.DeserializeObject(text.Trim(), targetType);
		}
		catch (Exception ex) {
			throw new InvalidOperationException($"{propertyName} の JSON 変換に失敗しました: {ex.Message}", ex);
		}
	}
}
