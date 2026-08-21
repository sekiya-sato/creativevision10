using CodeShare;
using CvAsset;
using CvBase;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace CvWpfclient.Helpers;

/// <summary>取込レイアウトCSVの1列と、対応するモデルのプロパティ。</summary>
public sealed record CsvImportColumnSpec(int ColumnIndex, string ColumnName, string TypeText, PropertyInfo Property);

/// <summary>
/// 取込レイアウトCSV（1行目=テーブル名＋採用フラグ、2行目=列名、3行目=型）の共通処理。
/// <para>
/// 外部CSVマスタ取込（<c>ExternalCsvImportView</c>）・取込レイアウト作成（<c>ImportTemplateCreateView</c>）・
/// 残高登録処理（<c>BalanceRegistrationView</c>）で共有する。
/// CSVテキストそのものの分解・組み立ては <see cref="CsvText"/>（CvAsset）にある。
/// </para>
/// </summary>
public static class CsvImportEngine {
	/// <summary>テーブル名・旧テーブル名・モデル名からモデル型を引く辞書。</summary>
	public static Dictionary<string, Type> CreateTableTypeMap() {
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

	/// <summary>テーブル名で型を引く辞書（<see cref="TableNameAttribute"/> かクラス名だけを鍵にする）。</summary>
	public static Dictionary<string, Type> CreateTableNameOnlyMap() =>
		AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(SafeGetTypes)
			.Where(x => typeof(BaseDbClass).IsAssignableFrom(x) && !x.IsAbstract)
			.Where(x => x.GetCustomAttribute<NoCreateAttribute>() == null)
			.GroupBy(GetTableName, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

	public static string GetTableName(Type type) =>
		type.GetCustomAttribute<TableNameAttribute>()?.Value ?? type.Name;

	/// <summary>旧Oracle名の <c>HC$</c> 接頭辞を落として照合キーにする。</summary>
	public static string NormalizeTableKey(string tableName) {
		var normalized = tableName.Trim();
		if (normalized.StartsWith("HC$", StringComparison.OrdinalIgnoreCase)) {
			normalized = normalized[3..];
		}
		return normalized;
	}

	public static IEnumerable<Type> SafeGetTypes(Assembly assembly) {
		try {
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex) {
			return ex.Types.Where(x => x != null).Cast<Type>();
		}
	}

	/// <summary>UTF-8としてCSVを読み、行×フィールドへ分解する。BOMは有無どちらも受け付ける。</summary>
	public static async Task<List<CsvTextRow>> ReadCsvRowsAsync(string path, CancellationToken ct) {
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

		return CsvText.Parse(text);
	}

	/// <summary>UTF-8としてCSVの全文を読む。BOMは有無どちらも受け付ける。</summary>
	public static async Task<string> ReadCsvTextAsync(string path, CancellationToken ct) {
		var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
		try {
			await using var stream = File.OpenRead(path);
			using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
			return await reader.ReadToEndAsync(ct);
		}
		catch (DecoderFallbackException ex) {
			throw new InvalidDataException($"UTF-8として読み込めません。文字コードを確認してください。{ex.Message}");
		}
	}

	/// <summary>1行目の採用フラグ。空・1・true・○ を採用とみなす。</summary>
	public static bool IsSelectedFlag(string value) {
		var text = value.Trim();
		return text is "" or "1" or "true" or "TRUE" or "True" or "○" or "〇";
	}

	/// <summary>
	/// 列名行・型行から取込対象列を決める。列名は現行プロパティ名と旧列名のどちらでも解決する。
	/// </summary>
	/// <param name="addError">行番号・列名・内容を受け取るエラー収集</param>
	public static List<CsvImportColumnSpec> BuildColumnSpecs(
		CsvTextRow columnRow, CsvTextRow typeRow, CsvTextRow tableRow, Type modelType,
		Action<int, string, string> addError) {
		var specs = new List<CsvImportColumnSpec>();
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
				addError(columnRow.LineNo, $"列{columnIndex + 1}", "列名が空です。");
				continue;
			}

			var flagIndex = flagStart + (columnIndex - columnStart);
			if (flagIndex < tableRow.Fields.Count && !IsSelectedFlag(tableRow.Fields[flagIndex])) {
				continue;
			}

			if (!propertyMap.TryGetValue(columnName, out var property)) {
				addError(columnRow.LineNo, columnName, $"モデル {modelType.Name} に対応するプロパティがありません。");
				continue;
			}

			var typeIndex = typeStart + (columnIndex - columnStart);
			var typeText = typeIndex < typeRow.Fields.Count ? typeRow.Fields[typeIndex].Trim() : string.Empty;
			if (string.IsNullOrWhiteSpace(typeText)) {
				addError(typeRow.LineNo, columnName, "型行の値が空です。");
			}

			specs.Add(new CsvImportColumnSpec(columnIndex, columnName, typeText, property));
		}

		return specs;
	}

	/// <summary>取込対象のプロパティ。<c>Id</c> / <c>Vdc</c> / <c>Vdu</c> を先頭に並べる。</summary>
	public static IEnumerable<PropertyInfo> GetImportProperties(Type type) {
		var props = type
			.GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(x => x.CanWrite)
			.Where(x => x.GetIndexParameters().Length == 0)
			.Where(x => x.GetCustomAttribute<IgnoreAttribute>() == null)
			.Where(x => x.GetCustomAttribute<ComputedColumnAttribute>() == null)
			.Where(x => x.GetCustomAttribute<ResultColumnAttribute>() == null)
			.Where(x => x.GetCustomAttribute<JsonIgnoreAttribute>() == null)
			.ToList();

		return OrderAuditFirst(props);
	}

	/// <summary>読み取り可能な全プロパティ。<c>Id</c> / <c>Vdc</c> / <c>Vdu</c> を先頭に並べる。</summary>
	public static IEnumerable<PropertyInfo> GetDbProperties(Type type) {
		var props = type
			.GetProperties(BindingFlags.Instance | BindingFlags.Public)
			.Where(x => x.CanRead)
			.Where(x => x.GetIndexParameters().Length == 0)
			.ToList();

		return OrderAuditFirst(props);
	}

	private static IEnumerable<PropertyInfo> OrderAuditFirst(List<PropertyInfo> props) => [
		.. props.Where(x => x.Name == "Id"),
		.. props.Where(x => x.Name == "Vdc"),
		.. props.Where(x => x.Name == "Vdu"),
		.. props.Where(x => x.Name != "Id" && x.Name != "Vdc" && x.Name != "Vdu")
	];

	/// <summary>旧列名。プロパティかバッキングフィールドの <see cref="OldTableCommentAttr"/> を使う。</summary>
	public static string GetPropertyOldName(PropertyInfo property) {
		var attr = property.GetCustomAttribute<OldTableCommentAttr>();
		if (!string.IsNullOrWhiteSpace(attr?.Name)) {
			return attr.Name;
		}
		return FindBackingFieldAttribute(property)?.Name ?? property.Name;
	}

	/// <summary>旧列コメント。</summary>
	public static string GetPropertyOldComment(PropertyInfo property) {
		var attr = property.GetCustomAttribute<OldTableCommentAttr>();
		if (!string.IsNullOrWhiteSpace(attr?.Content)) {
			return attr.Content;
		}
		return FindBackingFieldAttribute(property)?.Content ?? string.Empty;
	}

	private static OldTableCommentAttr? FindBackingFieldAttribute(PropertyInfo property) {
		var declaringType = property.DeclaringType;
		if (declaringType == null) {
			return null;
		}

		var fieldName = property.Name.Length == 0
			? string.Empty
			: char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
		var field = declaringType.GetField(
			fieldName,
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
		return field?.GetCustomAttribute<OldTableCommentAttr>();
	}

	public static bool IsJsonLikeType(Type type) =>
		(type != typeof(string) && type != typeof(byte[]) && typeof(IEnumerable).IsAssignableFrom(type))
		|| (type.IsClass && type != typeof(string));

	/// <summary>Id_* のうちマスタコードで解決する列か。</summary>
	public static bool IsForeignCodeProperty(PropertyInfo property) =>
		property.Name.StartsWith("Id_", StringComparison.Ordinal)
		&& property.PropertyType == typeof(long)
		&& property.Name is not ("Id_Tax");

	/// <summary>CSVの1フィールドをプロパティの型へ変換する。</summary>
	/// <exception cref="InvalidDataException">型・桁・書式が合わない場合</exception>
	public static object? ConvertFieldValue(string value, CsvImportColumnSpec spec) {
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

	private static T ParseInteger<T>(string text, CsvImportColumnSpec spec) where T : struct, IParsable<T> {
		var normalized = text.TrimStart('+', '-');
		if (normalized.Length == 0 || !normalized.All(char.IsAsciiDigit)) {
			throw new InvalidDataException($"{spec.ColumnName}: 数値項目に数値以外が含まれています。値='{text}'");
		}
		if (!T.TryParse(text, CultureInfo.InvariantCulture, out var result)) {
			throw new InvalidDataException($"{spec.ColumnName}: 数値に変換できません。値='{text}'");
		}
		return result;
	}

	private static decimal ParseDecimal(string text, CsvImportColumnSpec spec) {
		if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)) {
			throw new InvalidDataException($"{spec.ColumnName}: 小数に変換できません。値='{text}'");
		}
		return result;
	}

	private static double ParseDouble(string text, CsvImportColumnSpec spec) {
		if (!double.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)) {
			throw new InvalidDataException($"{spec.ColumnName}: 小数に変換できません。値='{text}'");
		}
		return result;
	}

	private static bool ParseBool(string text, CsvImportColumnSpec spec) {
		if (text is "1" or "true" or "TRUE" or "True") return true;
		if (text is "0" or "false" or "FALSE" or "False") return false;
		throw new InvalidDataException($"{spec.ColumnName}: 真偽値に変換できません。値='{text}'");
	}

	private static DateTime ParseDateTime(string text, CsvImportColumnSpec spec) {
		var formats = new[] { "yyyyMMdd", "yyyy/MM/dd", "yyyy-MM-dd", "yyyy/MM/dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
		if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)) {
			return result;
		}
		if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out result)) {
			return result;
		}
		throw new InvalidDataException($"{spec.ColumnName}: 日付に変換できません。値='{text}'");
	}

	private static void ValidateStringLength(string text, PropertyInfo property, CsvImportColumnSpec spec) {
		var size = property.GetCustomAttribute<ColumnSizeDmlAttribute>();
		if (size == null || size.ColType != ColumnType.String) {
			return;
		}
		if (text.Length > size.Size) {
			throw new InvalidDataException($"{spec.ColumnName}: 文字数超過です。最大={size.Size} 実際={text.Length}");
		}
	}

	/// <summary>CSVへ出力する値の書式。</summary>
	public static string FormatOutputValue(object? value, PropertyInfo property) {
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
}

/// <summary>
/// <c>Id_*</c> をマスタコードから解決する。同じ問い合わせを繰り返さないよう画面単位でキャッシュする。
/// </summary>
public sealed class CsvImportMasterResolver {
	private readonly Dictionary<string, object?> masterCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Type> tableTypeMap;

	public CsvImportMasterResolver(Dictionary<string, Type> tableTypeMap) {
		this.tableTypeMap = tableTypeMap;
	}

	public void ClearCache() => masterCache.Clear();

	/// <summary>コードでマスタを引き、<c>Id_*</c> と対になる <c>V*</c>（あれば）を設定する。</summary>
	/// <exception cref="InvalidDataException">コードが存在しない、またはマスタ参照に失敗した場合</exception>
	public async Task ApplyForeignCodeAsync(object item, CsvImportColumnSpec spec, string value, CancellationToken ct) {
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

	public (Type masterType, string where, string[] parameters) ResolveMasterQuery(PropertyInfo property, object item, string code) {
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

	public async Task<object?> QueryMasterAsync(Type masterType, string where, string[] parameters, CancellationToken ct) {
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
			// [ForeignKey(meishoKubun:"KIN")] と各メンテ画面の選択条件に合わせる(旧値 "PAY" では名称マスタを引けずV*列が空になる)
			"Id_PayMethod" => "KIN",
			"Id_Col" => "COL",
			"Id_Siz" => item.GetType().GetProperty("SizeKu")?.GetValue(item)?.ToString() ?? "SIZ",
			_ => string.Empty
		};

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
}
