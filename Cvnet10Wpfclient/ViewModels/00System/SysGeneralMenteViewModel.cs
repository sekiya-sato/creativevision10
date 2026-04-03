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
	readonly Type targetType = typeof(MasterMeisho);
	readonly string targetName = nameof(MasterMeisho);

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
		await ReloadAsync(ct);
	}

	[RelayCommand]
	void DoAdd() {
		var item = new MasterMeisho();
		if (SelectedRow?.TryGetCell("Kubun", out var kubunCell) == true) {
			item.Kubun = kubunCell.EditText;
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
		if (SelectedRow == null) {
			return;
		}

		if (!TryBuildItem(SelectedRow, out MasterMeisho? item, out var errorMessage) || item == null) {
			Message = errorMessage;
			MessageEx.ShowErrorDialog(errorMessage, owner: ActiveWindow);
			return;
		}

		var actionName = SelectedRow.IsNew ? "追加" : "修正";
		var confirmMessage = SelectedRow.IsNew
			? $"追加しますか？ ({targetName} / CD={item.Code})"
			: $"修正しますか？ ({targetName} / CD={item.Code}, Id={item.Id})";

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
			if (Common.DeserializeObject(reply.DataMsg ?? string.Empty, reply.DataType) is MasterMeisho savedItem) {
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
		if (SelectedRow == null) {
			return;
		}

		if (SelectedRow.IsNew) {
			Rows.Remove(SelectedRow);
			Count = Rows.Count;
			SelectedRow = Rows.FirstOrDefault();
			Message = "未保存の新規行を取り消しました。";
			return;
		}

		if (!TryBuildItem(SelectedRow, out MasterMeisho? item, out var errorMessage) || item == null) {
			Message = errorMessage;
			MessageEx.ShowErrorDialog(errorMessage, owner: ActiveWindow);
			return;
		}

		var confirmMessage = $"削除しますか？ ({targetName} / CD={item.Code}, Id={item.Id})";
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
				list.Cast<MasterMeisho>().Select(item => SysGeneralEditMapper.CreateRow(item, targetType, isNew: false)));
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
		DataMsg = Common.SerializeObject(new QueryListParam(targetType, order: "Kubun,Code"))
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

	bool TryBuildItem(SysGeneralEditRow row, out MasterMeisho? item, out string errorMessage) {
		try {
			item = SysGeneralEditMapper.ToItem<MasterMeisho>(row);
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
		var kubun = GetCellText("Kubun");
		var code = GetCellText("Code");
		var name = GetCellText("Name");
		var prefix = IsNew ? "[新規] " : string.Empty;
		Title = $"{prefix}{kubun} {code} {name}".Trim();
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
}

static class SysGeneralEditMapper {
	public static SysGeneralEditRow CreateRow<T>(T item, Type originalType, bool isNew) where T : BaseDbClass {
		var row = new SysGeneralEditRow {
			IsNew = isNew,
			OriginalType = originalType
		};

		foreach (var property in GetEditableProperties(originalType)) {
			var value = property.GetValue(item);
			row.AddCell(new SysGeneralEditCell {
				Key = property.Name,
				Header = property.Name,
				EditText = ToText(value),
				IsReadOnly = property.Name is "Id" or "Vdc" or "Vdu",
				ValueType = property.PropertyType
			});
		}

		row.RefreshTitle();
		return row;
	}

	public static T ToItem<T>(SysGeneralEditRow row) where T : BaseDbClass, new() {
		var item = new T();
		foreach (var property in GetEditableProperties(typeof(T))) {
			if (!row.TryGetCell(property.Name, out var cell)) {
				continue;
			}

			var value = ConvertFromText(cell.EditText, property.PropertyType, property.Name);
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

		return IsSupportedType(property.PropertyType);
	}

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
}
