/*
# description
SqliteConstructCatalog は「クライアントSQLに現れる SQLite 固有構文」の目録です。

CV10 では SQL の組み立てを CvWpfclient 側でも行うため、SQLite 方言が正典です。
他DBへ向ける変換器は、この目録に載っている構文だけを対象にします。

目録には2つの役目があります。
1. 変換器が「SQLite固有なのに変換されなかった語」を検出する（Strictモードで例外にする）。
2. CvWpfclient が開発時に自己検査し、新規画面が未対応構文を使ったことをその場で気づく。

目録に無い語は「3DBで共通に使える」とみなします。追加する構文がSQLite固有なら
必ずここへ登録し、変換ルールの有無を明示します。

# example
var hit = SqliteConstructCatalog.Find("json_each");   // SqliteConstruct を返す
var none = SqliteConstructCatalog.Find("substr");     // null（3DB共通）
 */
namespace CvBase.Sql;

/// <summary>SQLite固有構文の分類</summary>
public enum SqliteConstructCategory {
	/// <summary>関数名の単純写像で足りるもの</summary>
	FunctionMapping,
	/// <summary>JSON関連。式・FROM句の書換が必要</summary>
	Json,
	/// <summary>日付・整形。式の書換が必要</summary>
	DateTimeFormat,
	/// <summary>DDL・保守命令。クライアントSQLには現れない</summary>
	Administrative,
}

/// <summary>
/// SQLite固有構文1件。
/// </summary>
/// <param name="Id">ルールIDと対応する識別子</param>
/// <param name="Keyword">SQL上の語（関数名など）。大文字小文字は無視して照合する</param>
/// <param name="Category">分類</param>
/// <param name="Note">用途と移行時の注意</param>
/// <param name="RequiresCall">
/// 関数呼び出しの形（直後が <c>(</c>）でなければ該当としない。
/// <c>json</c> のように他の文脈（<c>IS JSON</c>、<c>CAST(x AS JSON)</c>）でも現れる語で誤検出を防ぐ。
/// </param>
/// <param name="MinArguments">
/// この引数個数以上の呼び出しだけを該当とする。0なら個数を見ない。
/// <c>date</c> のように他DBにも同名関数があり、修飾子付きのときだけSQLite固有になる語で使う。
/// </param>
public sealed record SqliteConstruct(string Id, string Keyword, SqliteConstructCategory Category, string Note, bool RequiresCall = true, int MinArguments = 0);

/// <summary>クライアントSQLに現れるSQLite固有構文の目録</summary>
public static class SqliteConstructCatalog {

	/// <summary>目録本体。実測インベントリ (.omo/2026-08-25_sql_dialect_server_absorption_and_migration_cost.md §2) に基づく。</summary>
	public static IReadOnlyList<SqliteConstruct> All { get; } = [
		new("A01-Ifnull", "ifnull", SqliteConstructCategory.FunctionMapping,
			"PGは COALESCE。MariaDBは同名で可"),
		new("B01-JsonExtract", "json_extract", SqliteConstructCategory.Json,
			"パスは全て単一階層 $.Prop。PGは ->>、MariaDBは JSON_VALUE"),
		new("B02-JsonEach", "json_each", SqliteConstructCategory.Json,
			"配列の行展開。PGは jsonb_array_elements、MariaDBは JSON_TABLE。どちらも alias.value を提供する"),
		new("B03-JsonValid", "json_valid", SqliteConstructCategory.Json,
			"PGは16以降の IS JSON 述語、MariaDBは JSON_VALID"),
		new("B04-JsonCast", "json", SqliteConstructCategory.Json,
			"JSONとしての正規化。PGは ::jsonb、MariaDBは CAST(x AS JSON)"),
		new("B04-JsonObject", "json_object", SqliteConstructCategory.Json,
			"PGは jsonb_build_object。現状はサーバ層のみで使用"),
		new("B04-JsonGroupArray", "json_group_array", SqliteConstructCategory.Json,
			"PGは jsonb_agg、MariaDBは JSON_ARRAYAGG。順序保証が異なる。現状はサーバ層のみで使用"),
		new("B04-JsonSet", "json_set", SqliteConstructCategory.Json,
			"PGは jsonb_set、MariaDBは JSON_SET。現状はサーバ層のみで使用"),
		new("B05-Strftime", "strftime", SqliteConstructCategory.DateTimeFormat,
			"書式は %Y%m / %Y%m%d / %w / %d / %s の5種のみ"),
		new("B06-Printf", "printf", SqliteConstructCategory.DateTimeFormat,
			"書式は %02d / %04d のみ。LPAD へ写像する"),
		new("B07-DateModifier", "date", SqliteConstructCategory.DateTimeFormat,
			"修飾子付き date(x,'+1 month') のみSQLite固有。修飾子なしの date(x) は3DBで解釈できる",
			MinArguments: 2),
		new("B08-Julianday", "julianday", SqliteConstructCategory.DateTimeFormat,
			"julianday(a)-julianday(b) の減算パターンのみ。PGは日付差、MariaDBは DATEDIFF"),
		new("C04-Upsert", "conflict", SqliteConstructCategory.FunctionMapping,
			"ON CONFLICT ... DO UPDATE。PostgreSQLは同一構文、MariaDBは ON DUPLICATE KEY UPDATE",
			RequiresCall: false),
		new("C05-Changes", "changes", SqliteConstructCategory.Administrative,
			"直前の更新行数。PostgreSQLとMariaDBに同名関数は無く、実行APIの戻り値で取る必要がある"),
		new("C01-Pragma", "pragma", SqliteConstructCategory.Administrative,
			"保守命令。クライアントSQLには現れない。プロバイダー側で分離済", RequiresCall: false),
		new("C02-SqliteMaster", "sqlite_master", SqliteConstructCategory.Administrative,
			"メタデータ参照。プロバイダー側で override 済", RequiresCall: false),
		new("C03-Autoincrement", "autoincrement", SqliteConstructCategory.Administrative,
			"DDL。プロバイダー側で分離する", RequiresCall: false),
	];

	static readonly Dictionary<string, SqliteConstruct> _byKeyword =
		All.ToDictionary(x => x.Keyword, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// 語がSQLite固有構文なら該当項目を返す。3DB共通の語なら null。
	/// <para>
	/// <c>date</c> は SQLite 以外にも同名関数があり、単独では固有と判定できない。
	/// 修飾子付き <c>date(x,'+1 month')</c> の判定は B07 のルール側で行うため、ここには載せない。
	/// </para>
	/// </summary>
	public static SqliteConstruct? Find(string word) =>
		_byKeyword.TryGetValue(word, out var construct) ? construct : null;

	/// <summary>字句列からSQLite固有構文の出現を列挙する。文字列リテラルとコメントは対象外。</summary>
	public static List<SqlDialectFinding> Scan(IReadOnlyList<SqlToken> tokens) {
		var findings = new List<SqlDialectFinding>();
		for (var i = 0; i < tokens.Count; i++) {
			var token = tokens[i];
			if (token.Kind != SqlTokenKind.Word)
				continue;
			var construct = Find(token.Text);
			if (construct == null)
				continue;
			// 関数呼び出しの形が必要な構文は、直後が `(` でなければ該当としない
			var open = NextCodeIndex(tokens, i);
			var isCall = open >= 0 && tokens[open].IsOperator("(");
			if (construct.RequiresCall && !isCall)
				continue;
			// 引数個数の下限がある構文は、満たさない呼び出しを該当としない
			if (construct.MinArguments > 0 && (!isCall || CountArguments(tokens, open) < construct.MinArguments))
				continue;
			findings.Add(new SqlDialectFinding(construct, token.Text, token.Start));
		}
		return findings;
	}

	static int NextCodeIndex(IReadOnlyList<SqlToken> tokens, int index) {
		for (var i = index + 1; i < tokens.Count; i++) {
			if (tokens[i].IsCode)
				return i;
		}
		return -1;
	}

	/// <summary>開き括弧位置から、対応する閉じ括弧までの深さ0のカンマ数+1を返す。空引数なら0。</summary>
	static int CountArguments(IReadOnlyList<SqlToken> tokens, int openIndex) {
		var depth = 0;
		var commas = 0;
		var hasContent = false;
		for (var i = openIndex; i < tokens.Count; i++) {
			var token = tokens[i];
			if (token.IsOperator("(")) {
				depth++;
				continue;
			}
			if (token.IsOperator(")")) {
				depth--;
				if (depth == 0)
					return hasContent ? commas + 1 : 0;
				continue;
			}
			if (depth == 1 && token.IsOperator(",")) {
				commas++;
				continue;
			}
			if (depth >= 1 && token.IsCode)
				hasContent = true;
		}
		return hasContent ? commas + 1 : 0;
	}

	/// <summary>SQL文字列からSQLite固有構文の出現を列挙する。</summary>
	public static List<SqlDialectFinding> Scan(string sql) => Scan(SqlTokenizer.Tokenize(sql));
}

/// <summary>
/// SQLite固有構文の検出結果。
/// </summary>
/// <param name="Construct">該当した目録項目</param>
/// <param name="Text">実際に現れた語</param>
/// <param name="Position">SQL内の位置</param>
public sealed record SqlDialectFinding(SqliteConstruct Construct, string Text, int Position) {
	public override string ToString() => $"{Construct.Id} '{Text}' (位置 {Position})";
}
