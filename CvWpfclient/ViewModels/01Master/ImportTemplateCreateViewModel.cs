using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using Microsoft.Win32;
using Newtonsoft.Json;
using NPoco;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace CvWpfclient.ViewModels._01Master;

public partial class ImportTemplateCreateViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	private ObservableCollection<ImportTemplateTableRow> tableList = [];

	[ObservableProperty]
	private ImportTemplateTableRow? selectedTable;

	[ObservableProperty]
	private ObservableCollection<ImportTemplateColumnRow> columnList = [];

	[ObservableProperty]
	private ImportTemplateColumnRow? currentColumn;

	[ObservableProperty]
	private DateTime selectedDate = DateTime.Today;

	[ObservableProperty]
	private string oldTableName = string.Empty;

	[ObservableProperty]
	private long selectedRowCount;

	[ObservableProperty]
	private string message = string.Empty;

	[ObservableProperty]
	private int selectedColumnCount;

	private readonly Dictionary<string, Type> tableTypeMap = CreateTableTypeMap();
	private readonly List<object> outputDataRows = [];
	private bool isDataLoaded;

	partial void OnSelectedTableChanged(ImportTemplateTableRow? value) {
		OldTableName = value?.OldTableName ?? string.Empty;
		SelectedRowCount = value?.RowCount ?? 0;
		ColumnList = [];
		outputDataRows.Clear();
		isDataLoaded = false;
		SelectedColumnCount = 0;
	}

	[RelayCommand]
	private async Task Init(CancellationToken ct) {
		await LoadTablesAsync(ct);
	}

	[RelayCommand]
	private void RefreshDisplay() {
		outputDataRows.Clear();
		isDataLoaded = false;
		var table = SelectedTable;
		if (table == null) {
			Message = "Table名を選択してください。";
			return;
		}

		ColumnList = new ObservableCollection<ImportTemplateColumnRow>(
			BuildColumnRows(table.ModelType).Select((x, index) => {
				x.No = index + 1;
				x.PropertyChanged += (_, e) => {
					if (e.PropertyName == nameof(ImportTemplateColumnRow.IsSelected)) {
						UpdateSelectedColumnCount();
					}
				};
				return x;
			}));
		CurrentColumn = ColumnList.FirstOrDefault();
		UpdateSelectedColumnCount();
		Message = $"{table.DisplayName} の列情報を表示しました。";
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task LoadDataAsync(CancellationToken ct) {
		var table = SelectedTable;
		if (table == null) {
			MessageEx.ShowWarningDialog("Table名を選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (ColumnList.Count == 0) {
			RefreshDisplay();
		}

		try {
			ClientLib.Cursor2Wait();
			ct.ThrowIfCancellationRequested();
			outputDataRows.Clear();

			var fromTicks = ToLocalDateStartUtcTicks(SelectedDate);
			var query = new QueryListSqlParam(
				table.ModelType,
				$"select * from {table.TableName} where Vdu >= @0 order by Vdu, Id",
				[fromTicks.ToString(CultureInfo.InvariantCulture)]);
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg101_Op_Query,
				DataType = typeof(QueryListSqlParam),
				DataMsg = Common.SerializeObject(query)
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			ct.ThrowIfCancellationRequested();
			if (reply.Code < 0 && reply.Code != -1) {
				var detail = string.IsNullOrWhiteSpace(reply.Option) ? reply.DataMsg : reply.Option;
				MessageEx.ShowErrorDialog($"データ取得失敗: {detail} ({reply.Code})", owner: ClientLib.GetActiveView(this));
				return;
			}

			var listType = typeof(List<>).MakeGenericType(table.ModelType);
			if (Common.DeserializeObject(reply.DataMsg ?? "[]", listType) is IEnumerable rows) {
				outputDataRows.AddRange(rows.Cast<object>());
			}

			isDataLoaded = true;
			Message = $"{SelectedDate:yyyy/MM/dd} 以降に更新されたデータを {outputDataRows.Count:N0} 件取得しました。";
		}
		catch (OperationCanceledException) {
			Message = "データ取得をキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"データ取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task CreateFileAsync(CancellationToken ct) {
		var table = SelectedTable;
		if (table == null) {
			MessageEx.ShowWarningDialog("Table名を選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (ColumnList.Count == 0) {
			RefreshDisplay();
		}

		var selectedColumns = ColumnList.Where(x => x.IsSelected).OrderBy(x => x.No).ToList();
		if (selectedColumns.Count == 0) {
			MessageEx.ShowWarningDialog("出力する列を選択してください。", owner: ClientLib.GetActiveView(this));
			return;
		}

		var dialog = new SaveFileDialog {
			Title = "取込レイアウトCSVを保存",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			DefaultExt = ".csv",
			FileName = $"{table.TableName}.csv"
		};
		if (dialog.ShowDialog(ClientLib.GetActiveView(this)) != true) {
			return;
		}

		try {
			ct.ThrowIfCancellationRequested();
			var lines = BuildOutputLines(table, selectedColumns);
			var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			await File.WriteAllLinesAsync(dialog.FileName, lines, encoding, ct);
			Message = $"{dialog.FileName} を作成しました。";
			MessageEx.ShowInformationDialog("ファイル作成が完了しました。", owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			Message = "ファイル作成をキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"ファイル作成失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
	}

	private async Task LoadTablesAsync(CancellationToken ct) {
		try {
			ClientLib.Cursor2Wait();
			ct.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg042_GetTableList
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			ct.ThrowIfCancellationRequested();
			if (reply.Code < 0) {
				Message = $"テーブル一覧取得失敗: {reply.Option} ({reply.Code})";
				MessageEx.ShowErrorDialog(Message, owner: ClientLib.GetActiveView(this));
				return;
			}

			var tableCounts = Common.DeserializeObject<List<Tuple<string, string, long>>>(reply.DataMsg ?? "[]") ?? [];
			TableList = new ObservableCollection<ImportTemplateTableRow>(
				tableCounts
					.Where(x => tableTypeMap.ContainsKey(x.Item1))
					.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
					.Select(x => CreateTableRow(x.Item1, x.Item2, x.Item3)));
			SelectedTable = TableList.FirstOrDefault();
			Message = $"{TableList.Count:N0} テーブルを取得しました。";
		}
		catch (OperationCanceledException) {
			Message = "テーブル一覧取得をキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"テーブル一覧取得失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	private ImportTemplateTableRow CreateTableRow(string tableName, string comment, long rowCount) {
		var modelType = tableTypeMap[tableName];
		return new ImportTemplateTableRow {
			TableName = tableName,
			DisplayName = BuildDisplayName(tableName, comment),
			Description = comment,
			OldTableName = NormalizeOldTableName(GetOldTableName(modelType)),
			RowCount = rowCount,
			ModelType = modelType
		};
	}

	private List<string> BuildOutputLines(ImportTemplateTableRow table, IReadOnlyList<ImportTemplateColumnRow> selectedColumns) {
		List<string> lines = [
			BuildCsvLine([table.OldTableNameOrTableName, .. selectedColumns.Select(_ => "1")]),
			BuildCsvLine([string.Empty, .. selectedColumns.Select(x => x.OutputColumnName)]),
			BuildCsvLine([string.Empty, .. selectedColumns.Select(x => x.ImportTypeText)])
		];

		if (isDataLoaded) {
			lines.AddRange(outputDataRows.Select(row =>
				BuildCsvLine([string.Empty, .. selectedColumns.Select(column => FormatOutputValue(column.Property.GetValue(row), column.Property))])));
		}

		return lines;
	}

	private static IEnumerable<ImportTemplateColumnRow> BuildColumnRows(Type tableType) {
		foreach (var property in GetDbProperties(tableType)) {
			if (!TryGetColumnSpec(property, out var dataType, out var length, out var importTypeText, out var isJson)) {
				continue;
			}

			yield return new ImportTemplateColumnRow {
				IsSelected = !isJson,
				ColumnName = property.Name,
				OutputColumnName = GetPropertyOldName(property),
				DataType = dataType,
				LengthText = length,
				ImportTypeText = importTypeText,
				Note = isJson ? "JSON項目: CSV 1項目内にJSON文字列として出力" : GetPropertyOldComment(property),
				Property = property
			};
		}
	}

	private void UpdateSelectedColumnCount() {
		SelectedColumnCount = ColumnList.Count(x => x.IsSelected);
	}

	private static IEnumerable<PropertyInfo> GetDbProperties(Type type) {
		var props = type
			.GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(x => x.CanRead)
			.Where(x => x.GetIndexParameters().Length == 0)
			.ToList();

		return [
			.. props.Where(x => x.Name == "Id"),
			.. props.Where(x => x.Name == "Vdc"),
			.. props.Where(x => x.Name == "Vdu"),
			.. props.Where(x => x.Name != "Id" && x.Name != "Vdc" && x.Name != "Vdu")
		];
	}

	private static bool TryGetColumnSpec(PropertyInfo property, out string dataType, out string length, out string importTypeText, out bool isJson) {
		dataType = string.Empty;
		length = string.Empty;
		importTypeText = string.Empty;
		isJson = false;

		if (property.GetCustomAttribute<IgnoreAttribute>() != null
			|| property.GetCustomAttribute<ComputedColumnAttribute>() != null
			|| property.GetCustomAttribute<ResultColumnAttribute>() != null
			|| property.GetCustomAttribute<JsonIgnoreAttribute>() != null) {
			return false;
		}

		var actualType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
		var size = property.GetCustomAttribute<ColumnSizeDmlAttribute>();
		if (property.GetCustomAttribute<SerializedColumnAttribute>() != null
			|| size?.ColType == ColumnType.Json
			|| IsJsonLikeType(actualType)) {
			isJson = true;
			dataType = "JSON";
			length = size?.Size.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
			importTypeText = string.IsNullOrWhiteSpace(length) ? "JSON" : $"JSON{length}";
			return true;
		}

		if (property.Name == "Id") {
			dataType = "NUMBER";
			length = "14";
			importTypeText = "数字14";
			return true;
		}

		if (size != null) {
			if (size.ColType == ColumnType.Enum) {
				dataType = "NUMBER";
				length = "10";
				importTypeText = "数字10";
			}
			else {
				dataType = "VARCHAR2";
				length = size.Size.ToString(CultureInfo.InvariantCulture);
				importTypeText = $"半角{length}";
			}
			return true;
		}

		if (actualType.IsEnum) {
			dataType = "NUMBER";
			length = "10";
			importTypeText = "数字10";
			return true;
		}

		switch (actualType.Name) {
			case "Boolean":
				dataType = "NUMBER";
				length = "1";
				importTypeText = "数字1";
				return true;
			case "Byte":
			case "Char":
			case "SByte":
				dataType = "VARCHAR2";
				length = "1";
				importTypeText = "半角1";
				return true;
			case "Int16":
			case "UInt16":
				dataType = "NUMBER";
				length = "5";
				importTypeText = "数字5";
				return true;
			case "Int32":
			case "UInt32":
				dataType = "NUMBER";
				length = "10";
				importTypeText = "数字10";
				return true;
			case "Int64":
			case "UInt64":
				dataType = "NUMBER";
				length = "14";
				importTypeText = "数字14";
				return true;
			case "Decimal":
			case "Double":
			case "Single":
				dataType = "NUMBER";
				length = "14,8";
				importTypeText = "数字14,8";
				return true;
			case "String":
				dataType = "VARCHAR2";
				length = "255";
				importTypeText = "半角255";
				return true;
			case "DateTime":
				dataType = "DATE";
				importTypeText = "日付";
				return true;
			default:
				return false;
		}
	}

	private static string FormatOutputValue(object? value, PropertyInfo property) {
		if (value == null) {
			return string.Empty;
		}

		if (property.GetCustomAttribute<SerializedColumnAttribute>() != null || IsJsonLikeType(value.GetType())) {
			return JsonConvert.SerializeObject(value, Formatting.None);
		}

		return value switch {
			DateTime date => date.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture),
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
			_ => value.ToString() ?? string.Empty
		};
	}

	private static string BuildCsvLine(IEnumerable<string> fields) =>
		string.Join(",", fields.Select(EscapeCsvField));

	private static string EscapeCsvField(string? value) {
		var text = value ?? string.Empty;
		if (text.Contains('"')) {
			text = text.Replace("\"", "\"\"");
		}

		return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
			? $"\"{text}\""
			: text;
	}

	private static long ToLocalDateStartUtcTicks(DateTime date) {
		var localStart = DateTime.SpecifyKind(date.Date, DateTimeKind.Local);
		return localStart.ToUniversalTime().Ticks;
	}

	private static bool IsJsonLikeType(Type type) =>
		(type != typeof(string) && type != typeof(byte[]) && typeof(IEnumerable).IsAssignableFrom(type))
		|| (type.IsClass && type != typeof(string));

	private static string BuildDisplayName(string tableName, string comment) {
		if (string.IsNullOrWhiteSpace(comment)) {
			return tableName;
		}
		var display = comment;
		foreach (var prefix in new[] { "マスター：", "システム：", "派生テーブル：", "集計テーブル：" }) {
			display = display.Replace(prefix, string.Empty, StringComparison.Ordinal);
		}
		var index = display.IndexOf("テーブル", StringComparison.Ordinal);
		return index > 0 ? display[..index] : display;
	}

	private static string GetOldTableName(Type type) =>
		type.GetCustomAttribute<OldTableCommentAttr>()?.Name ?? string.Empty;

	private static string NormalizeOldTableName(string tableName) =>
		tableName.StartsWith("HC$", StringComparison.OrdinalIgnoreCase)
			? tableName[3..]
			: tableName;

	private static string GetPropertyOldName(PropertyInfo property) {
		var attr = property.GetCustomAttribute<OldTableCommentAttr>();
		if (!string.IsNullOrWhiteSpace(attr?.Name)) {
			return attr.Name;
		}

		var declaringType = property.DeclaringType;
		if (declaringType == null) {
			return property.Name;
		}

		var fieldName = property.Name.Length == 0
			? string.Empty
			: char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
		var field = declaringType.GetField(
			fieldName,
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		return field?.GetCustomAttribute<OldTableCommentAttr>()?.Name ?? property.Name;
	}

	private static string GetPropertyOldComment(PropertyInfo property) {
		var attr = property.GetCustomAttribute<OldTableCommentAttr>();
		if (!string.IsNullOrWhiteSpace(attr?.Content)) {
			return attr.Content;
		}

		var declaringType = property.DeclaringType;
		if (declaringType == null) {
			return string.Empty;
		}

		var fieldName = property.Name.Length == 0
			? string.Empty
			: char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
		var field = declaringType.GetField(
			fieldName,
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		return field?.GetCustomAttribute<OldTableCommentAttr>()?.Content ?? string.Empty;
	}

	private static Dictionary<string, Type> CreateTableTypeMap() =>
		AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(SafeGetTypes)
			.Where(x => typeof(BaseDbClass).IsAssignableFrom(x) && !x.IsAbstract)
			.Where(x => x.GetCustomAttribute<NoCreateAttribute>() == null)
			.GroupBy(GetTableName, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

	private static IEnumerable<Type> SafeGetTypes(Assembly assembly) {
		try {
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex) {
			return ex.Types.Where(x => x != null).Cast<Type>();
		}
	}

	private static string GetTableName(Type type) =>
		type.GetCustomAttribute<TableNameAttribute>()?.Value ?? type.Name;
}

public sealed partial class ImportTemplateColumnRow : ObservableObject {
	[ObservableProperty]
	private bool isSelected;

	public int No { get; set; }
	public string ColumnName { get; set; } = string.Empty;
	public string OutputColumnName { get; set; } = string.Empty;
	public string DataType { get; set; } = string.Empty;
	public string LengthText { get; set; } = string.Empty;
	public string ImportTypeText { get; set; } = string.Empty;
	public string Note { get; set; } = string.Empty;
	public required PropertyInfo Property { get; init; }
}

public sealed class ImportTemplateTableRow {
	public string TableName { get; init; } = string.Empty;
	public string DisplayName { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public string OldTableName { get; init; } = string.Empty;
	public string OldTableNameOrTableName => string.IsNullOrWhiteSpace(OldTableName) ? TableName : OldTableName;
	public long RowCount { get; init; }
	public required Type ModelType { get; init; }
}
