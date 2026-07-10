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

public partial class ExternalCsvImportViewModel : Helpers.BaseViewModel {
	[ObservableProperty]
	public partial string FilePath { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string TableName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ModelName { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int DataRowCount { get; set; }

	[ObservableProperty]
	public partial int ImportableRowCount { get; set; }

	[ObservableProperty]
	public partial int ErrorCount { get; set; }

	[ObservableProperty]
	public partial string Message { get; set; } = string.Empty;

	[ObservableProperty]
	public partial ObservableCollection<ExternalCsvPreviewRow> PreviewRows { get; set; } = [];

	[ObservableProperty]
	public partial ObservableCollection<ExternalCsvImportErrorRow> ErrorRows { get; set; } = [];

	[ObservableProperty]
	public partial ExternalCsvImportErrorRow? SelectedError { get; set; }

	private readonly Dictionary<string, Type> tableTypeMap = CreateTableTypeMap();
	private readonly Dictionary<string, object?> masterCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<object> importRecords = [];
	private Type? importType;
	private List<ExternalCsvColumnSpec> columnSpecs = [];

	[RelayCommand]
	private void Init() {
		Message = "取込レイアウトCSVを選択してください。";
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task SelectFileAsync(CancellationToken ct) {
		var dialog = new OpenFileDialog {
			Title = "取込レイアウトCSVを選択",
			Filter = "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
			CheckFileExists = true,
			Multiselect = false
		};
		if (dialog.ShowDialog(ClientLib.GetActiveView(this)) != true) {
			return;
		}

		FilePath = dialog.FileName;
		await ValidateFileAsync(ct);
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ValidateFileAsync(CancellationToken ct) {
		ClearImportState();
		if (string.IsNullOrWhiteSpace(FilePath)) {
			AddError(0, "", "ファイルを選択してください。");
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			ct.ThrowIfCancellationRequested();
			var rows = await ReadCsvRowsAsync(FilePath, ct);
			ct.ThrowIfCancellationRequested();
			await BuildImportRecordsAsync(rows, ct);
			RefreshSummary();
			Message = ErrorCount == 0
				? $"{ImportableRowCount:N0} 件を取込できます。"
				: $"{ErrorCount:N0} 件のエラーがあります。行番号と内容を確認してください。";
		}
		catch (OperationCanceledException) {
			Message = "検証をキャンセルしました。";
		}
		catch (Exception ex) {
			AddError(0, "", ex.Message);
			RefreshSummary();
			Message = "検証に失敗しました。";
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	[RelayCommand(IncludeCancelCommand = true)]
	private async Task ImportAsync(CancellationToken ct) {
		if (importType == null || importRecords.Count == 0) {
			MessageEx.ShowWarningDialog("取込可能なデータがありません。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (ErrorRows.Count > 0) {
			MessageEx.ShowWarningDialog("エラーが残っています。エラーを修正してから再検証してください。", owner: ClientLib.GetActiveView(this));
			return;
		}
		if (MessageEx.ShowQuestionDialog($"{ModelName} を {importRecords.Count:N0} 件登録しますか？", owner: ClientLib.GetActiveView(this)) != MsgBoxResult.Yes) {
			return;
		}

		try {
			ClientLib.Cursor2Wait();
			ct.ThrowIfCancellationRequested();
			var coreService = AppGlobal.GetGrpcService<ICoreService>();
			var msg = new CvMsg {
				Code = 0,
				Flag = CvFlag.Msg201_Op_Execute,
				DataType = typeof(InsertBulkParam),
				DataMsg = Common.SerializeObject(new InsertBulkParam(importType, JsonConvert.SerializeObject(importRecords)))
			};
			var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
			if (reply.Code < 0) {
				var detail = reply.Code < -9000 ? reply.Option : reply.DataMsg;
				MessageEx.ShowErrorDialog($"CSV取込エラー: {detail} ({reply.Code})", owner: ClientLib.GetActiveView(this));
				return;
			}

			Message = $"{importRecords.Count:N0} 件を登録しました。";
			MessageEx.ShowInformationDialog("CSV取込が完了しました。", owner: ClientLib.GetActiveView(this));
		}
		catch (OperationCanceledException) {
			Message = "取込をキャンセルしました。";
		}
		catch (Exception ex) {
			MessageEx.ShowErrorDialog($"CSV取込失敗: {ex.Message}", owner: ClientLib.GetActiveView(this));
		}
		finally {
			ClientLib.Cursor2Normal();
		}
	}

	private void ClearImportState() {
		TableName = string.Empty;
		ModelName = string.Empty;
		DataRowCount = 0;
		ImportableRowCount = 0;
		ErrorCount = 0;
		PreviewRows = [];
		ErrorRows = [];
		importRecords.Clear();
		masterCache.Clear();
		columnSpecs = [];
		importType = null;
	}

	private async Task BuildImportRecordsAsync(IReadOnlyList<CsvRow> rows, CancellationToken ct) {
		if (rows.Count < 3) {
			AddError(0, "", "CSVは最低3行（テーブル行、列名行、型行）が必要です。");
			return;
		}

		var tableRow = rows[0];
		if (tableRow.Fields.Count == 0 || string.IsNullOrWhiteSpace(tableRow.Fields[0])) {
			AddError(tableRow.LineNo, "Table名", "1行目1列目にテーブル名がありません。");
			return;
		}

		TableName = tableRow.Fields[0].Trim();
		if (!tableTypeMap.TryGetValue(NormalizeTableKey(TableName), out var modelType)) {
			AddError(tableRow.LineNo, "Table名", $"対応するモデル定義が見つかりません: {TableName}");
			return;
		}

		importType = modelType;
		ModelName = modelType.Name;
		columnSpecs = BuildColumnSpecs(rows[1], rows[2], tableRow, modelType);
		if (ErrorRows.Count > 0) {
			return;
		}

		DataRowCount = Math.Max(0, rows.Count - 3);
		for (var index = 3; index < rows.Count; index++) {
			ct.ThrowIfCancellationRequested();
			var row = rows[index];
			if (row.Fields.All(string.IsNullOrWhiteSpace)) {
				continue;
			}

			var item = Activator.CreateInstance(modelType);
			if (item == null) {
				AddError(row.LineNo, "", $"{modelType.Name} のインスタンス作成に失敗しました。");
				continue;
			}

			var beforeErrorCount = ErrorRows.Count;
			await ApplyRowAsync(item, row, ct);
			if (ErrorRows.Count == beforeErrorCount) {
				importRecords.Add(item);
				if (PreviewRows.Count < 100) {
					PreviewRows.Add(new ExternalCsvPreviewRow {
						LineNo = row.LineNo,
						Status = "取込可",
						Key = GetPreviewKey(item),
						Summary = BuildPreviewSummary(item)
					});
				}
			}
		}
	}

	private List<ExternalCsvColumnSpec> BuildColumnSpecs(CsvRow columnRow, CsvRow typeRow, CsvRow tableRow, Type modelType) {
		var specs = new List<ExternalCsvColumnSpec>();
		var properties = GetImportProperties(modelType).ToList();
		var propertyMap = properties
			.SelectMany(p => new[] { p.Name, GetPropertyOldName(p) }.Distinct(StringComparer.OrdinalIgnoreCase).Select(name => new { name, property = p }))
			.GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(x => x.Key, x => x.First().property, StringComparer.OrdinalIgnoreCase);

		var columnStart = columnRow.Fields.Count > 0 && string.IsNullOrWhiteSpace(columnRow.Fields[0]) ? 1 : 0;
		var typeStart = typeRow.Fields.Count > 0 && string.IsNullOrWhiteSpace(typeRow.Fields[0]) ? 1 : 0;
		var flagStart = tableRow.Fields.Count == columnRow.Fields.Count ? columnStart : 1;
		for (var columnIndex = columnStart; columnIndex < columnRow.Fields.Count; columnIndex++) {
			var columnName = columnRow.Fields[columnIndex].Trim();
			if (string.IsNullOrWhiteSpace(columnName)) {
				AddError(columnRow.LineNo, $"列{columnIndex + 1}", "列名が空です。");
				continue;
			}

			var flagIndex = flagStart + (columnIndex - columnStart);
			if (flagIndex < tableRow.Fields.Count && !IsSelectedFlag(tableRow.Fields[flagIndex])) {
				continue;
			}

			if (!propertyMap.TryGetValue(columnName, out var property)) {
				AddError(columnRow.LineNo, columnName, $"モデル {modelType.Name} に対応するプロパティがありません。");
				continue;
			}

			var typeIndex = typeStart + (columnIndex - columnStart);
			var typeText = typeIndex < typeRow.Fields.Count ? typeRow.Fields[typeIndex].Trim() : string.Empty;
			if (string.IsNullOrWhiteSpace(typeText)) {
				AddError(typeRow.LineNo, columnName, "型行の値が空です。");
			}

			specs.Add(new ExternalCsvColumnSpec(columnIndex, columnName, typeText, property));
		}

		if (specs.Count == 0) {
			AddError(columnRow.LineNo, "", "取込対象列がありません。");
		}
		return specs;
	}

	private async Task ApplyRowAsync(object item, CsvRow row, CancellationToken ct) {
		foreach (var spec in columnSpecs) {
			ct.ThrowIfCancellationRequested();
			var value = spec.ColumnIndex < row.Fields.Count ? row.Fields[spec.ColumnIndex] : string.Empty;
			if (spec.Property.Name is "Id" or "Vdc" or "Vdu") {
				continue;
			}

			try {
				if (IsForeignCodeProperty(spec.Property)) {
					await ApplyForeignCodeAsync(item, spec, value, row.LineNo, ct);
				}
				else {
					var converted = ConvertFieldValue(value, spec, row.LineNo);
					spec.Property.SetValue(item, converted);
				}
			}
			catch (InvalidDataException ex) {
				AddError(row.LineNo, spec.ColumnName, ex.Message);
			}
			catch (Exception ex) {
				AddError(row.LineNo, spec.ColumnName, $"値の設定に失敗しました: {ex.Message}");
			}
		}
	}

	private async Task ApplyForeignCodeAsync(object item, ExternalCsvColumnSpec spec, string value, int lineNo, CancellationToken ct) {
		var code = value.Trim();
		if (string.IsNullOrWhiteSpace(code)) {
			spec.Property.SetValue(item, 0L);
			SetCodeNameView(item, spec.Property, null);
			return;
		}

		var (masterType, where, parameters) = ResolveMasterQuery(spec.Property, item, code);
		var master = await QueryMasterAsync(masterType, where, parameters, ct);
		if (master == null) {
			throw new InvalidDataException($"{spec.ColumnName}: コード '{code}' が {masterType.Name} に存在しません。");
		}
		if (master is not BaseDbClass db) {
			throw new InvalidDataException($"{spec.ColumnName}: {masterType.Name} はIdを持つマスタではありません。");
		}

		spec.Property.SetValue(item, db.Id);
		SetCodeNameView(item, spec.Property, db);
	}

	private (Type masterType, string where, string[] parameters) ResolveMasterQuery(PropertyInfo property, object item, string code) {
		var masterType = ResolveMasterType(property, item);
		if (masterType == typeof(MasterMeisho)) {
			var kubun = ResolveMeishoKubun(property, item);
			if (string.IsNullOrWhiteSpace(kubun)) {
				throw new InvalidDataException($"{property.Name}: 名称区分を特定できません。");
			}
			return (masterType, "Kubun=@0 and Code=@1", [kubun, code]);
		}

		var where = "Code=@0";
		if (masterType == typeof(MasterTokui)) {
			where = property.Name switch {
				"Id_Soko" => "TenType=0 and Code=@0",
				"Id_Tenpo" => "TenType in (1,3,6) and Code=@0",
				_ => "Code=@0"
			};
		}

		return (masterType, where, [code]);
	}

	private async Task<object?> QueryMasterAsync(Type masterType, string where, string[] parameters, CancellationToken ct) {
		var cacheKey = $"{masterType.FullName}|{where}|{string.Join('\t', parameters)}";
		if (masterCache.TryGetValue(cacheKey, out var cached)) {
			return cached;
		}

		var coreService = AppGlobal.GetGrpcService<ICoreService>();
		var query = new QueryListParam(masterType, where, "Code", parameters, maxCount: 2);
		var msg = new CvMsg {
			Code = 0,
			Flag = CvFlag.Msg101_Op_Query,
			DataType = typeof(QueryListParam),
			DataMsg = Common.SerializeObject(query)
		};
		var reply = await coreService.QueryMsgAsync(msg, AppGlobal.GetDefaultCallContext(ct));
		if (reply.Code < 0 && reply.Code != -1) {
			var detail = string.IsNullOrWhiteSpace(reply.Option) ? reply.DataMsg : reply.Option;
			throw new InvalidDataException($"マスタ参照に失敗しました: {detail} ({reply.Code})");
		}

		object? result = null;
		if (Common.DeserializeObject(reply.DataMsg ?? "[]", reply.DataType) is IList list && list.Count > 0) {
			result = list[0];
		}
		masterCache[cacheKey] = result;
		return result;
	}

	private object? ConvertFieldValue(string value, ExternalCsvColumnSpec spec, int lineNo) {
		var property = spec.Property;
		var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
		var text = value.Trim();
		if (string.IsNullOrWhiteSpace(text)) {
			return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
		}

		if (property.GetCustomAttribute<SerializedColumnAttribute>() != null || IsJsonLikeType(targetType)) {
			try {
				return JsonConvert.DeserializeObject(text, property.PropertyType);
			}
			catch (JsonException ex) {
				throw new InvalidDataException($"{spec.ColumnName}: JSON形式が不正です。{ex.Message}");
			}
		}

		if (targetType == typeof(string)) {
			ValidateStringLength(text, property, spec);
			return text;
		}
		if (targetType == typeof(int)) return ParseInteger<int>(text, spec);
		if (targetType == typeof(long)) return ParseInteger<long>(text, spec);
		if (targetType == typeof(short)) return ParseInteger<short>(text, spec);
		if (targetType == typeof(byte)) return ParseInteger<byte>(text, spec);
		if (targetType == typeof(decimal)) return ParseDecimal(text, spec);
		if (targetType == typeof(double)) return ParseDouble(text, spec);
		if (targetType == typeof(float)) return (float)ParseDouble(text, spec);
		if (targetType == typeof(bool)) return ParseBool(text, spec);
		if (targetType == typeof(DateTime)) return ParseDateTime(text, spec);
		if (targetType.IsEnum) {
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var enumInt)) {
				return Enum.ToObject(targetType, enumInt);
			}
			if (Enum.TryParse(targetType, text, ignoreCase: true, out var enumValue)) {
				return enumValue;
			}
			throw new InvalidDataException($"{spec.ColumnName}: 列挙型に変換できません。値='{text}'");
		}

		throw new InvalidDataException($"{spec.ColumnName}: 未対応の型です。型={property.PropertyType.Name}");
	}

	private static T ParseInteger<T>(string text, ExternalCsvColumnSpec spec) where T : struct, IParsable<T> {
		var normalized = text.TrimStart('+', '-');
		if (normalized.Length == 0 || !normalized.All(char.IsAsciiDigit)) {
			throw new InvalidDataException($"{spec.ColumnName}: 数値項目に数値以外が含まれています。値='{text}'");
		}
		if (!T.TryParse(text, CultureInfo.InvariantCulture, out var result)) {
			throw new InvalidDataException($"{spec.ColumnName}: 数値に変換できません。値='{text}'");
		}
		return result;
	}

	private static decimal ParseDecimal(string text, ExternalCsvColumnSpec spec) {
		if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)) {
			throw new InvalidDataException($"{spec.ColumnName}: 小数に変換できません。値='{text}'");
		}
		return result;
	}

	private static double ParseDouble(string text, ExternalCsvColumnSpec spec) {
		if (!double.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)) {
			throw new InvalidDataException($"{spec.ColumnName}: 小数に変換できません。値='{text}'");
		}
		return result;
	}

	private static bool ParseBool(string text, ExternalCsvColumnSpec spec) {
		if (text is "1" or "true" or "TRUE" or "True") return true;
		if (text is "0" or "false" or "FALSE" or "False") return false;
		throw new InvalidDataException($"{spec.ColumnName}: 真偽値に変換できません。値='{text}'");
	}

	private static DateTime ParseDateTime(string text, ExternalCsvColumnSpec spec) {
		var formats = new[] { "yyyyMMdd", "yyyy/MM/dd", "yyyy-MM-dd", "yyyy/MM/dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
		if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)) {
			return result;
		}
		if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out result)) {
			return result;
		}
		throw new InvalidDataException($"{spec.ColumnName}: 日付に変換できません。値='{text}'");
	}

	private static void ValidateStringLength(string text, PropertyInfo property, ExternalCsvColumnSpec spec) {
		var size = property.GetCustomAttribute<ColumnSizeDmlAttribute>();
		if (size == null || size.ColType != ColumnType.String) {
			return;
		}
		if (text.Length > size.Size) {
			throw new InvalidDataException($"{spec.ColumnName}: 文字数超過です。最大={size.Size} 実際={text.Length}");
		}
	}

	private Type ResolveMasterType(PropertyInfo property, object item) {
		var attr = property.GetCustomAttribute<ForeignKeyAttribute>();
		if (attr != null) {
			var byAttr = tableTypeMap.Values.FirstOrDefault(x => string.Equals(x.Name, attr.TableName, StringComparison.OrdinalIgnoreCase));
			if (byAttr != null) {
				return byAttr;
			}
		}

		return property.Name switch {
			"Id_Shain" => typeof(MasterShain),
			"Id_Tenpo" => typeof(MasterTokui),
			"Id_Soko" => typeof(MasterTokui),
			"Id_Customer" => typeof(MasterEndCustomer),
			"Id_Paysaki" => item is MasterShiire ? typeof(MasterShiire) : typeof(MasterTokui),
			"Id_Shiire" => typeof(MasterShiire),
			_ => typeof(MasterMeisho)
		};
	}

	private static string ResolveMeishoKubun(PropertyInfo property, object item) =>
		property.Name switch {
			"Id_Brand" => "BRD",
			"Id_Item" => "ITM",
			"Id_Tenji" => "TNJ",
			"Id_Maker" => "MKR",
			"Id_Season" => "SZN",
			"Id_Material" => "SZI",
			"Id_Country" => "GEN",
			"Id_Bumon" => "BMN",
			"Id_PayMethod" => "PAY",
			"Id_Col" => "COL",
			"Id_Siz" => item.GetType().GetProperty("SizeKu")?.GetValue(item)?.ToString() ?? "SIZ",
			_ => string.Empty
		};

	private static bool IsForeignCodeProperty(PropertyInfo property) =>
		property.Name.StartsWith("Id_", StringComparison.Ordinal)
		&& property.PropertyType == typeof(long)
		&& property.Name is not ("Id_Tax");

	private static void SetCodeNameView(object item, PropertyInfo idProperty, BaseDbClass? master) {
		var suffix = idProperty.Name[3..];
		var viewProperty = item.GetType().GetProperty($"V{suffix}");
		if (viewProperty == null || !typeof(CodeNameView).IsAssignableFrom(viewProperty.PropertyType)) {
			return;
		}

		var codeName = master is IBaseCodeName code
			? new CodeNameView(master.Id, code.Code ?? string.Empty, code.Name ?? string.Empty)
			: new CodeNameView();
		viewProperty.SetValue(item, codeName);
	}

	private static IEnumerable<PropertyInfo> GetImportProperties(Type type) {
		var props = type
			.GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(x => x.CanWrite)
			.Where(x => x.GetIndexParameters().Length == 0)
			.Where(x => x.GetCustomAttribute<IgnoreAttribute>() == null)
			.Where(x => x.GetCustomAttribute<ComputedColumnAttribute>() == null)
			.Where(x => x.GetCustomAttribute<ResultColumnAttribute>() == null)
			.Where(x => x.GetCustomAttribute<JsonIgnoreAttribute>() == null)
			.ToList();

		return [
			.. props.Where(x => x.Name == "Id"),
			.. props.Where(x => x.Name == "Vdc"),
			.. props.Where(x => x.Name == "Vdu"),
			.. props.Where(x => x.Name != "Id" && x.Name != "Vdc" && x.Name != "Vdu")
		];
	}

	private static async Task<List<CsvRow>> ReadCsvRowsAsync(string path, CancellationToken ct) {
		var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
		string text;
		try {
			await using var stream = File.OpenRead(path);
			using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
			text = await reader.ReadToEndAsync(ct);
		}
		catch (DecoderFallbackException ex) {
			throw new InvalidDataException($"UTF-8として読み込めません。文字コードを確認してください。{ex.Message}");
		}

		return ParseCsvRows(text);
	}

	private static List<CsvRow> ParseCsvRows(string text) {
		List<CsvRow> rows = [];
		List<string> fields = [];
		StringBuilder current = new();
		var inQuotes = false;
		var lineNo = 1;
		var rowStartLine = 1;

		for (var index = 0; index < text.Length; index++) {
			var ch = text[index];
			if (ch == '"') {
				if (inQuotes && index + 1 < text.Length && text[index + 1] == '"') {
					current.Append('"');
					index++;
				}
				else {
					inQuotes = !inQuotes;
				}
				continue;
			}

			if (ch == ',' && !inQuotes) {
				fields.Add(current.ToString());
				current.Clear();
				continue;
			}

			if ((ch == '\r' || ch == '\n') && !inQuotes) {
				fields.Add(current.ToString());
				current.Clear();
				rows.Add(new CsvRow(rowStartLine, fields));
				fields = [];
				if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n') {
					index++;
				}
				lineNo++;
				rowStartLine = lineNo;
				continue;
			}

			if (ch == '\n') {
				lineNo++;
			}
			current.Append(ch);
		}

		if (inQuotes) {
			throw new InvalidDataException($"{rowStartLine}行目: CSVの引用符が閉じられていません。");
		}
		if (current.Length > 0 || fields.Count > 0) {
			fields.Add(current.ToString());
			rows.Add(new CsvRow(rowStartLine, fields));
		}

		return rows;
	}

	private static bool IsSelectedFlag(string value) {
		var text = value.Trim();
		return text is "" or "1" or "true" or "TRUE" or "True" or "○" or "〇";
	}

	private void AddError(int lineNo, string columnName, string detail) {
		ErrorRows.Add(new ExternalCsvImportErrorRow {
			LineNo = lineNo,
			ColumnName = columnName,
			Detail = detail
		});
	}

	private void RefreshSummary() {
		ErrorCount = ErrorRows.Count;
		ImportableRowCount = importRecords.Count;
	}

	private static string GetPreviewKey(object item) {
		var code = item.GetType().GetProperty("Code")?.GetValue(item)?.ToString();
		if (!string.IsNullOrWhiteSpace(code)) {
			return code;
		}
		return item.GetType().GetProperty("Id")?.GetValue(item)?.ToString() ?? string.Empty;
	}

	private static string BuildPreviewSummary(object item) {
		var name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
		if (!string.IsNullOrWhiteSpace(name)) {
			return name;
		}
		var ryaku = item.GetType().GetProperty("Ryaku")?.GetValue(item)?.ToString();
		return ryaku ?? string.Empty;
	}

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

	private static bool IsJsonLikeType(Type type) =>
		(type != typeof(string) && type != typeof(byte[]) && typeof(IEnumerable).IsAssignableFrom(type))
		|| (type.IsClass && type != typeof(string));

	private static Dictionary<string, Type> CreateTableTypeMap() {
		var pairs = AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(SafeGetTypes)
			.Where(x => typeof(BaseDbClass).IsAssignableFrom(x) && !x.IsAbstract)
			.Where(x => x.GetCustomAttribute<NoCreateAttribute>() == null)
			.SelectMany(type => new[] {
				new { Key = NormalizeTableKey(type.GetCustomAttribute<TableNameAttribute>()?.Value ?? type.Name), Type = type },
				new { Key = NormalizeTableKey(type.Name), Type = type },
				new { Key = NormalizeTableKey(type.GetCustomAttribute<OldTableCommentAttr>()?.Name ?? string.Empty), Type = type }
			})
			.Where(x => !string.IsNullOrWhiteSpace(x.Key));

		return pairs
			.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(x => x.Key, x => x.First().Type, StringComparer.OrdinalIgnoreCase);
	}

	private static string NormalizeTableKey(string tableName) {
		var normalized = tableName.Trim();
		if (normalized.StartsWith("HC$", StringComparison.OrdinalIgnoreCase)) {
			normalized = normalized[3..];
		}
		return normalized;
	}

	private static IEnumerable<Type> SafeGetTypes(Assembly assembly) {
		try {
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex) {
			return ex.Types.Where(x => x != null).Cast<Type>();
		}
	}
}

public sealed class ExternalCsvPreviewRow {
	public int LineNo { get; init; }
	public string Status { get; init; } = string.Empty;
	public string Key { get; init; } = string.Empty;
	public string Summary { get; init; } = string.Empty;
}

public sealed class ExternalCsvImportErrorRow {
	public int LineNo { get; init; }
	public string ColumnName { get; init; } = string.Empty;
	public string Detail { get; init; } = string.Empty;
}

internal sealed record ExternalCsvColumnSpec(int ColumnIndex, string ColumnName, string TypeText, PropertyInfo Property);

internal sealed record CsvRow(int LineNo, List<string> Fields);
