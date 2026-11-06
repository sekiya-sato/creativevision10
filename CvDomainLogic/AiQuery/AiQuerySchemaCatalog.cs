using CvBase;
using Newtonsoft.Json;
using NPoco;
using System.Reflection;
using System.Text;

namespace CvDomainLogic.AiQuery;

/// <summary>
/// AI照会でLLMへ渡すスキーマ説明（Doc/spec/2026-10-19_AI-query_最小変更設計案.md §6.3）。
/// <para>
/// 取得方法は cv-sqlite(McpSql) の list_tables / describe_table と同じ（sqlite_master と、表名をバインドした
/// pragma_table_info）。実DBを正とし、CvBase の <see cref="CommentAttribute"/> を業務説明として付け加える。
/// 件数・結果行は返さない。Sys* 表（ログイン情報を含む）は一覧にも説明にも出さない。
/// PostgreSQL / MariaDB では同じ内容を information_schema（現在のスキーマ・DBだけ）から取得し、
/// PostgreSQL が小文字で作る表名・列名は定義クラスの名前（Tran01Tenuri、DenDay 等）に揃えて返す。
/// SQLは接続先によらずSQLite方言で書かせるため、名前の表記をDB間で変えない。
/// </para>
/// </summary>
public sealed class AiQuerySchemaCatalog(ExDatabase db) {

	/// <summary>表名 → 定義クラス（DefineDataTable の事前作成テーブル）</summary>
	readonly Dictionary<string, Type> _types = DefineDataTable.TableTypes
		.GroupBy(db.GetTableName, StringComparer.OrdinalIgnoreCase)
		.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

	/// <summary>表名（大文字小文字を区別しない）→ 定義クラスの表名</summary>
	readonly Dictionary<string, string> _canonicalNames = DefineDataTable.TableTypes
		.Select(db.GetTableName)
		.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
		.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

	/// <summary>information_schema で現在のスキーマ（PostgreSQL）・DB（MariaDB）を表す式</summary>
	string CurrentSchemaExpression =>
		string.Equals(db.Dialect.Name, CvBase.Sql.SqlDialects.Postgre.Name, StringComparison.Ordinal) ? "current_schema()" : "database()";

	/// <summary>実在する表・ビューの名前と説明。Sys* と sqlite_* は除く。</summary>
	public List<(string Name, string Comment)> ListTables(string? namePrefix = null) {
		return QueryTableNames()
			.Where(name => name.Length > 0 && !AiQuerySqlGuard.IsSystemTable(name))
			.Where(name => string.IsNullOrEmpty(namePrefix) || name.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
			.Select(name => (name, CommentOf(_types.GetValueOrDefault(name))))
			.ToList();
	}

	/// <summary>表一覧をLLM向けの1表1行の文字列にする。</summary>
	public string ListTablesText(string? namePrefix = null) {
		var tables = ListTables(namePrefix);
		if (tables.Count == 0) {
			return "該当するテーブルはありません。";
		}
		return string.Join("\n", tables.Select(t => t.Comment.Length > 0 ? $"{t.Name} : {t.Comment}" : t.Name));
	}

	/// <summary>
	/// 表の列定義をLLM向けの文字列にする。存在しない表・Sys* 表は説明の代わりにエラー文を返す。
	/// JSON列（<see cref="SerializedColumnAttribute"/>）は要素型の項目も「JSON内項目」として返す。
	/// </summary>
	public string DescribeTable(string? table) {
		TryDescribeTable(table, out var text);
		return text;
	}

	/// <summary>表の列定義を返す。存在しない表・Sys* 表は false とエラー文を返す。</summary>
	public bool TryDescribeTable(string? table, out string text) {
		table = (table ?? string.Empty).Trim();
		if (table.Length == 0 || AiQuerySqlGuard.IsSystemTable(table)) {
			text = $"テーブル '{table}' は参照できません。list_tables で一覧を確認してください。";
			return false;
		}
		List<Dictionary<string, object>> columns;
		if (AiQuerySqlGuard.IsSqlite(db)) {
			// ★表名は連結せず必ずバインドする（McpSql と同じ）
			var master = db.RawExecCmd("SELECT name FROM sqlite_master WHERE type IN ('table','view') AND name = @0 COLLATE NOCASE", [table]);
			ThrowIfError();
			if (master.Count == 0) {
				text = $"テーブル '{table}' は存在しません。list_tables で一覧を確認してください。";
				return false;
			}
			table = master[0]["name"]?.ToString() ?? table;
			columns = db.RawExecCmd("SELECT name, type, \"notnull\", pk FROM pragma_table_info(@0) ORDER BY cid", [table]);
			ThrowIfError();
		}
		else {
			var requested = table;
			var actual = QueryActualTableNames().FirstOrDefault(x => string.Equals(x, requested, StringComparison.OrdinalIgnoreCase));
			if (actual == null) {
				text = $"テーブル '{table}' は存在しません。list_tables で一覧を確認してください。";
				return false;
			}
			table = _canonicalNames.GetValueOrDefault(actual) ?? actual;
			columns = QueryOtherColumns(actual);
		}

		var type = _types.GetValueOrDefault(table);
		var sb = new StringBuilder();
		var tableComment = CommentOf(type);
		sb.AppendLine(tableComment.Length > 0 ? $"テーブル {table} : {tableComment}" : $"テーブル {table}");
		sb.AppendLine("列（列名 型 [PK] [NOT NULL] : 説明）:");
		foreach (var col in columns) {
			var name = col["name"]?.ToString() ?? string.Empty;
			// GetProperty(name) は new による隠蔽があると AmbiguousMatchException になるため、最派生側を探す
			var properties = type?.GetProperties(BindingFlags.Public | BindingFlags.Instance) ?? [];
			var property = properties.FirstOrDefault(p => p.Name == name);
			if (property == null && !AiQuerySqlGuard.IsSqlite(db)) {
				// PostgreSQL は列名を小文字で返すため、定義クラスのプロパティ名に揃える
				property = properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
				name = property?.Name ?? name;
			}
			var flags = (Convert.ToInt64(col["pk"]) > 0 ? " PK" : string.Empty) + (Convert.ToInt64(col["notnull"]) != 0 ? " NOT NULL" : string.Empty);
			var comment = CommentOf(property);
			// 実DBに外部キー宣言は無いため、C#の [ForeignKey] を検証済みのJOIN関係として示す（推測でJOINさせない）
			var foreignKey = property?.GetCustomAttribute<ForeignKeyAttribute>() is { } fk
				? $" [参照: {fk.TableName}.{fk.KeyName}{(fk.TenType > 0 ? $"（{fk.TableName}.TenType={fk.TenType}）" : string.Empty)}]"
				: string.Empty;
			sb.AppendLine($"- {name} {col["type"]}{flags}{(comment.Length > 0 ? " : " + comment : string.Empty)}{foreignKey}");
			if (property?.GetCustomAttribute<SerializedColumnAttribute>() != null) {
				AppendJsonItems(sb, property.PropertyType);
			}
		}
		text = sb.ToString().TrimEnd();
		return true;
	}

	/// <summary>表・ビューの名前（定義クラスの表名に揃える。sqlite_* は除く）</summary>
	List<string> QueryTableNames() {
		if (!AiQuerySqlGuard.IsSqlite(db)) {
			return [.. QueryActualTableNames().Select(x => _canonicalNames.GetValueOrDefault(x) ?? x).Order(StringComparer.OrdinalIgnoreCase)];
		}
		var rows = db.RawExecCmd("""
			SELECT name FROM sqlite_master
			 WHERE type IN ('table','view') AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
			 ORDER BY name
			""");
		ThrowIfError();
		return [.. rows.Select(r => r["name"]?.ToString() ?? string.Empty)];
	}

	/// <summary>PostgreSQL / MariaDB の現在のスキーマ・DBにある表・ビューの実名</summary>
	List<string> QueryActualTableNames() {
		var rows = db.RawExecCmd($"""
			SELECT table_name AS name FROM information_schema.tables
			 WHERE table_schema = {CurrentSchemaExpression} AND table_type IN ('BASE TABLE','VIEW')
			""");
		ThrowIfError();
		return [.. rows.Select(r => r["name"]?.ToString() ?? string.Empty).Where(x => x.Length > 0)];
	}

	/// <summary>
	/// PostgreSQL / MariaDB の列定義を SQLite の pragma_table_info と同じ列名（name, type, notnull, pk）で返す。
	/// 表名はバインドする。MariaDB の主キー制約名はどの表も PRIMARY なので、表名でも結合する。
	/// </summary>
	List<Dictionary<string, object>> QueryOtherColumns(string table) {
		var schema = CurrentSchemaExpression;
		var rows = db.RawExecCmd($"""
			SELECT c.column_name AS name, c.data_type AS type,
			       CASE WHEN c.is_nullable = 'NO' THEN 1 ELSE 0 END AS required,
			       CASE WHEN k.column_name IS NULL THEN 0 ELSE 1 END AS pk
			  FROM information_schema.columns c
			  LEFT JOIN (SELECT ku.table_name, ku.column_name
			               FROM information_schema.table_constraints tc
			               JOIN information_schema.key_column_usage ku
			                 ON ku.constraint_schema = tc.constraint_schema AND ku.constraint_name = tc.constraint_name AND ku.table_name = tc.table_name
			              WHERE tc.constraint_type = 'PRIMARY KEY' AND tc.table_schema = {schema}) k
			    ON k.table_name = c.table_name AND k.column_name = c.column_name
			 WHERE c.table_schema = {schema} AND c.table_name = @0
			 ORDER BY c.ordinal_position
			""", [table]);
		ThrowIfError();
		// 列名の大文字小文字はプロバイダーにより異なるため、キーを揃える（notnull は PostgreSQL の予約語なので別名から移す）
		return [.. rows.Select(r => {
			var row = new Dictionary<string, object>(r, StringComparer.OrdinalIgnoreCase);
			row["notnull"] = row["required"];
			return row;
		})];
	}

	/// <summary>JSON列の要素型の項目を「$.項目名」として書き出す。0・false・空文字は保存時に省略される。</summary>
	static void AppendJsonItems(StringBuilder sb, Type propertyType) {
		var isArray = propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>);
		var elementType = isArray ? propertyType.GetGenericArguments()[0] : propertyType;
		sb.AppendLine($"    JSON{(isArray ? "配列" : "オブジェクト")}（要素型 {elementType.Name}。0・false・既定値の項目は省略されるため数値は COALESCE(json_extract(...),0) で読む）:");
		foreach (var p in elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
			if (!p.CanWrite || p.GetCustomAttribute<JsonIgnoreAttribute>() != null || p.GetIndexParameters().Length > 0) {
				continue;
			}
			var comment = CommentOf(p);
			sb.AppendLine($"    - $.{p.Name} ({TypeName(p.PropertyType)}){(comment.Length > 0 ? " : " + comment : string.Empty)}");
		}
	}

	static string TypeName(Type type) {
		var t = Nullable.GetUnderlyingType(type) ?? type;
		return t == typeof(string) ? "text"
			: t == typeof(int) || t == typeof(long) || t == typeof(short) || t.IsEnum ? "integer"
			: t == typeof(decimal) || t == typeof(double) || t == typeof(float) ? "real"
			: t == typeof(bool) ? "bool"
			: t.Name;
	}

	static string CommentOf(MemberInfo? member) =>
		member?.GetCustomAttribute<CommentAttribute>()?.Content ?? string.Empty;

	void ThrowIfError() {
		if (!string.IsNullOrEmpty(db.RawLastError)) {
			throw new InvalidOperationException($"スキーマ取得に失敗しました: {db.RawLastError}");
		}
	}
}
