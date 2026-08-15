using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using CvBase.Share;
using CvWpfclient.Helpers;
using CvWpfclient.ViewModels.Sub;
using Newtonsoft.Json;
using NPoco;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;

namespace CvWpfclient.ViewModels._00System;

public partial class SysTableSpecViewModel : BaseMenteViewModel<SysTableSpecTableRow> {

	[ObservableProperty]
	public partial string Title { get; set; } = "DB定義書出力";

	[ObservableProperty]
	public partial ObservableCollection<SysTableSpecTableRow> TableList { get; set; } = [];

	[ObservableProperty]
	public partial List<long> SelectedTableIds { get; set; } = [];

	[ObservableProperty]
	public partial string SelectedTablesText { get; set; } = "未選択";

	[ObservableProperty]
	public partial int ServerTableCount { get; set; }

	[ObservableProperty]
	public partial int SelectedTableCount { get; set; }

	protected override string? FormFile => "SysTableSpec.qfm";

	protected override PrintByCsvParam? PrintByCsvParam {
		get {
			var csv = BuildPrintCsvData();
			return string.IsNullOrWhiteSpace(csv) ? null : new PrintByCsvParam(csv);
		}
	}

	[RelayCommand]
	async Task Init(CancellationToken ct) {
		await LoadServerTablesAsync(ct);
	}

	[RelayCommand]
	async Task SelectTables(CancellationToken ct) {
		if (TableList.Count == 0) {
			await LoadServerTablesAsync(ct);
		}
		if (TableList.Count == 0) {
			return;
		}

		var selectView = new Views.Sub.SelectMultiWinView();
		if (selectView.DataContext is not SelectMultiWinViewModel vm) {
			return;
		}
		vm.DisplayNameCode = " テーブル名";
		vm.DisplayNameName = " 説明";
		vm.DisplayNameRyaku = " 件数";
		vm.DisplayWidthCode = 200;
		vm.DisplayWidthName = 300;
		vm.DisplayWidthRyaku = 100;

		vm.SetLocalData(TableList, "DB定義書出力 テーブル選択", selectedIds: SelectedTableIds);
		if (ClientLib.ShowDialogView(selectView, this) != true) {
			return;
		}

		var selected = vm.GetSelectedItems<SysTableSpecTableRow>();
		SelectedTableIds = [.. selected.Select(x => x.Id)];
		SelectedTableCount = SelectedTableIds.Count;
		SelectedTablesText = BuildSelectedText(selected);
		Message = $"{SelectedTableCount:N0} テーブルを選択しました。";
	}

	[RelayCommand]
	void ClearTables() {
		SelectedTableIds = [];
		SelectedTableCount = 0;
		SelectedTablesText = "未選択";
		Message = "選択を解除しました。";
	}

	async Task LoadServerTablesAsync(CancellationToken ct) {
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
				MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
				return;
			}

			var tableCounts = Common.DeserializeObject<List<Tuple<string, string, long>>>(reply.DataMsg ?? "[]") ?? [];
			TableList = new ObservableCollection<SysTableSpecTableRow>(
				tableCounts
					.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase)
					.Select((x, index) => new SysTableSpecTableRow {
						Id = index + 1,
						Code = x.Item1,
						Name = x.Item2,
						Ryaku = $"{x.Item3:N0}件",
						RowCount = x.Item3
					}));
			ServerTableCount = TableList.Count;
			Message = $"{ServerTableCount:N0} テーブルを取得しました。";
		}
		catch (OperationCanceledException) {
			Message = "テーブル一覧取得をキャンセルしました。";
		}
		catch (Exception ex) {
			Message = $"テーブル一覧取得失敗: {ex.Message}";
			MessageEx.ShowErrorDialog(Message, owner: ActiveWindow);
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	string BuildPrintCsvData() {
		var selectedRows = TableList
			.Where(x => SelectedTableIds.Contains(x.Id))
			.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (selectedRows.Count == 0) {
			Message = "印刷対象テーブルが選択されていません。";
			return string.Empty;
		}

		var tableTypes = CreateTableTypeMap();
		List<string> lines = [];
		List<string> skippedTables = [];
		foreach (var row in selectedRows) {
			if (!tableTypes.TryGetValue(row.Code, out var type)) {
				skippedTables.Add(row.Code);
				continue;
			}

			var tableComment = GetClassComment(type);
			var oldTableName = GetOldTableName(type);
			lines.AddRange(BuildTableCsvLines(row.Code, tableComment, oldTableName, type));
		}

		if (lines.Count == 0) {
			Message = skippedTables.Count == 0
				? "印刷対象データがありません。"
				: $"対応するモデル定義が見つかりません: {string.Join(", ", skippedTables)}";
			return string.Empty;
		}

		Message = skippedTables.Count == 0
			? $"{selectedRows.Count:N0} テーブルの定義を印刷します。"
			: $"{selectedRows.Count - skippedTables.Count:N0} テーブルを印刷します。未対応: {string.Join(", ", skippedTables)}";
		return string.Join("\r\n", lines) + "\r\n";
	}

	static IEnumerable<string> BuildTableCsvLines(string tableName, string tableComment, string oldTableName, Type tableType) {
		var fieldNo = 1;
		foreach (var property in GetDbProperties(tableType)) {
			if (!TryGetColumnSpec(property, out var dataType, out var length)) {
				continue;
			}

			yield return BuildCsvLine([
				tableName,
				tableComment,
				property.Name,
				dataType,
				length,
				GetPropertyDescription(property),
				string.Empty,
				"フィールド定義",
				"0",
				oldTableName,
				fieldNo.ToString(CultureInfo.InvariantCulture)
			]);
			fieldNo++;
		}

		var indexNo = 1;
		var primaryKey = tableType.GetCustomAttribute<PrimaryKeyAttribute>();
		if (primaryKey != null) {
			yield return BuildIndexCsvLine(
				tableName,
				tableComment,
				oldTableName,
				$"{tableName}_PK",
				isPrimary: true,
				isUnique: true,
				GetPrimaryKeyColumns(primaryKey),
				indexNo++);
		}

		foreach (var key in tableType.GetCustomAttributes<KeyDmlAttribute>()) {
			yield return BuildIndexCsvLine(
				tableName,
				tableComment,
				oldTableName,
				$"{tableName}_{key.KeyName}",
				isPrimary: false,
				isUnique: key.IsUnique,
				string.Join(",", key.ColNames),
				indexNo++);
		}
	}

	static string BuildIndexCsvLine(string tableName, string tableComment, string oldTableName, string indexName, bool isPrimary, bool isUnique, string columns, int no) =>
		BuildCsvLine([
			tableName,
			tableComment,
			indexName,
			FormatBool(isPrimary),
			FormatBool(isUnique),
			string.Empty,
			columns,
			"インデックス定義",
			"1",
			oldTableName,
			no.ToString(CultureInfo.InvariantCulture)
		]);

	static IEnumerable<PropertyInfo> GetDbProperties(Type type) {
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

	static bool TryGetColumnSpec(PropertyInfo property, out string dataType, out string length) {
		dataType = string.Empty;
		length = string.Empty;

		if (property.GetCustomAttribute<IgnoreAttribute>() != null
			|| property.GetCustomAttribute<ComputedColumnAttribute>() != null
			|| property.GetCustomAttribute<ResultColumnAttribute>() != null
			|| property.GetCustomAttribute<JsonIgnoreAttribute>() != null) {
			return false;
		}

		if (property.Name == "Id") {
			dataType = "NUMBER";
			length = "14,0";
			return true;
		}

		var size = property.GetCustomAttribute<ColumnSizeDmlAttribute>();
		if (size != null) {
			(dataType, length) = size.ColType switch {
				ColumnType.Json => ("JSON", string.Empty),
				ColumnType.Enum => ("NUMBER", "10,0"),
				_ => ("VARCHAR2", size.Size.ToString(CultureInfo.InvariantCulture))
			};
			return true;
		}

		var actualType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
		if (actualType.IsEnum) {
			dataType = "NUMBER";
			length = "10,0";
			return true;
		}

		switch (actualType.Name) {
			case "Boolean":
				dataType = "NUMBER";
				length = "1,0";
				return true;
			case "Byte":
			case "Char":
			case "SByte":
				dataType = "VARCHAR2";
				length = "1";
				return true;
			case "Int16":
			case "UInt16":
				dataType = "NUMBER";
				length = "5,0";
				return true;
			case "Int32":
			case "UInt32":
				dataType = "NUMBER";
				length = "10,0";
				return true;
			case "Int64":
			case "UInt64":
				dataType = "NUMBER";
				length = "14,0";
				return true;
			case "Decimal":
			case "Double":
			case "Single":
				dataType = "NUMBER";
				length = "14,8";
				return true;
			case "String":
				dataType = "VARCHAR2";
				length = "255";
				return true;
			case "DateTime":
				dataType = "DATE";
				return true;
			default:
				if (actualType.Name.StartsWith("List", StringComparison.Ordinal)) {
					return false;
				}
				dataType = "VARCHAR2";
				length = "1000";
				return true;
		}
	}

	static Dictionary<string, Type> CreateTableTypeMap() =>
		AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(SafeGetTypes)
			.Where(x => typeof(BaseDbClass).IsAssignableFrom(x) && !x.IsAbstract)
			.Where(x => x.GetCustomAttribute<NoCreateAttribute>() == null)
			.GroupBy(GetTableName, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

	static IEnumerable<Type> SafeGetTypes(Assembly assembly) {
		try {
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex) {
			return ex.Types.Where(x => x != null).Cast<Type>();
		}
	}

	static string GetTableName(Type type) =>
		type.GetCustomAttribute<TableNameAttribute>()?.Value ?? type.Name;

	static string GetPrimaryKeyColumns(PrimaryKeyAttribute primaryKey) {
		var value = primaryKey.GetType().GetProperty("Value")?.GetValue(primaryKey)?.ToString();
		return string.IsNullOrWhiteSpace(value) ? "Id" : value;
	}

	static string BuildSelectedText(IReadOnlyList<SysTableSpecTableRow> selected) {
		if (selected.Count == 0) return "未選択";
		return $"{selected.Count}件: {string.Join(", ", selected.Select(x => $"{x.Code}({x.RowCount:N0})"))}";
	}

	static string BuildCsvLine(IEnumerable<string> fields) =>
		string.Join(",", fields.Select(EscapeCsvField));

	static string EscapeCsvField(string? value) {
		var text = value ?? string.Empty;
		if (text.Contains('"')) {
			text = text.Replace("\"", "\"\"");
		}

		return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
			? $"\"{text}\""
			: text;
	}

	static string GetClassComment(Type type) =>
		type.GetCustomAttribute<CommentAttribute>()?.Content?.Replace(",", string.Empty) ?? string.Empty;

	static string GetOldTableName(Type type) =>
		type.GetCustomAttribute<OldTableCommentAttr>()?.Name ?? string.Empty;

	static string GetPropertyDescription(PropertyInfo property) {
		var descriptions = new List<string>();
		// カラムの [Comment] を説明の先頭に出す (DDLのカラムコメントは未使用のため定義書だけで利用する)
		var comment = GetPropertyAttribute<CommentAttribute>(property)?.Content;
		if (!string.IsNullOrWhiteSpace(comment)) {
			descriptions.Add(comment.Replace(",", string.Empty));
		}

		var oldComment = GetPropertyAttribute<OldTableCommentAttr>(property)?.Content;
		if (!string.IsNullOrWhiteSpace(oldComment)) {
			descriptions.Add(oldComment);
		}

		var foreignKey = GetPropertyAttribute<ForeignKeyAttribute>(property);
		if (foreignKey != null) {
			var conditions = new List<string>();
			if (!string.IsNullOrWhiteSpace(foreignKey.MeishoKubun)) {
				conditions.Add($"Kubun={foreignKey.MeishoKubun}");
			}
			if (foreignKey.TableName == nameof(MasterTokui)) {
				conditions.Add($"TenType={foreignKey.TenType}");
			}
			if (foreignKey.MeishoListKubunTop != '\0') {
				conditions.Add($"Kubun先頭={foreignKey.MeishoListKubunTop}");
			}
			if (!string.IsNullOrWhiteSpace(foreignKey.AdditionalInfo)) {
				conditions.Add(foreignKey.AdditionalInfo);
			}

			var reference = $"参照: {foreignKey.TableName}.{foreignKey.KeyName}";
			descriptions.Add(conditions.Count == 0
				? reference
				: $"{reference} ({string.Join(", ", conditions)})");
		}

		return string.Join(" / ", descriptions);
	}

	static TAttribute? GetPropertyAttribute<TAttribute>(PropertyInfo property)
		where TAttribute : Attribute {
		var attr = property.GetCustomAttribute<TAttribute>();
		if (attr != null) {
			return attr;
		}

		var declaringType = property.DeclaringType;
		if (declaringType == null || property.Name.Length == 0) {
			return null;
		}

		// ObservableProperty attributes are emitted on the generated backing field.
		var fieldName = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
		var field = declaringType.GetField(
			fieldName,
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		return field?.GetCustomAttribute<TAttribute>();
	}

	static string FormatBool(bool value) => value ? "TRUE" : "FALSE";
}

public sealed class SysTableSpecTableRow : BaseDbClass, IBaseCodeName {
	public string Code { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public string Ryaku { get; set; } = string.Empty;
	public string Kana { get; set; } = string.Empty;
	public long RowCount { get; init; }
}
