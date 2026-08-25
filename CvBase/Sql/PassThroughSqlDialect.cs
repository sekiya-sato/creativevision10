/*
# description
PassThroughSqlDialect は何も変換しない方言です。

現行の SQLite 運用を壊さないための実装で、Translate は引数の参照をそのまま返します。
字句解析も行いません。SQLite で接続している間、方言変換のコードパスは
「プロパティを1回読んで引数を返す」だけになります。

ExDatabase の既定値と ExDatabaseSqlite の両方がこれを使います。
SQLite では未対応構文の検出も行いません（SQLite方言が正典なので、そもそも未対応が無い）。

# example
public override ISqlDialect Dialect => SqlDialects.Sqlite;
 */
namespace CvBase.Sql;

/// <summary>変換しない方言。SQLite と ExDatabase の既定値で使う。</summary>
public sealed class PassThroughSqlDialect : ISqlDialect {

	/// <summary>プロバイダー未特定時の既定インスタンス</summary>
	public static PassThroughSqlDialect Instance { get; } = new("PassThrough");

	public PassThroughSqlDialect(string name) {
		Name = name;
	}

	public string Name { get; }

	public IReadOnlyList<string> SessionSetupCommands => [];

	/// <summary>この方言はSQLを書き換えない。呼び出し側は変換処理を丸ごと飛ばす。</summary>
	public bool TranslatesSql => false;

	/// <summary>引数の参照をそのまま返す。</summary>
	public string Translate(string sql) => sql;

	/// <summary>SQLite方言が正典のため、未対応構文は無い。</summary>
	public IReadOnlyList<SqlDialectFinding> Inspect(string sql) => [];

	/// <summary>
	/// SQLite の下限バージョン(3.38)を下回る場合の文言を返す。
	/// 現行運用を止めないため、CvServer 側ではこの結果を警告ログにとどめる。
	/// </summary>
	public IReadOnlyList<string> Validate(string serverVersion) {
		if (!string.Equals(Name, "Sqlite", StringComparison.OrdinalIgnoreCase))
			return [];
		var version = SqlDialectVersions.Parse(serverVersion);
		if (version == null)
			return [$"SQLiteのバージョンを判定できません。値={serverVersion}"];
		return version < SqlDialectVersions.SqliteMinimum
			? [$"SQLite {SqlDialectVersions.SqliteMinimum} 以降が必要です。接続先={version}"]
			: [];
	}
}

/// <summary>各DBの下限バージョン</summary>
public static class SqlDialectVersions {

	/// <summary>SQLite下限。JSON1が既定有効になる 3.38。</summary>
	public static Version SqliteMinimum { get; } = new(3, 38);

	/// <summary>PostgreSQL下限。json_valid 相当の IS JSON 述語が 16 以降。</summary>
	public static Version PostgreMinimum { get; } = new(16, 0);

	/// <summary>MariaDB下限。JSON_TABLE が 10.6 以降。10.6 はEOLのため 10.11 LTS。</summary>
	public static Version MariaMinimum { get; } = new(10, 11);

	/// <summary>
	/// バージョン文字列から先頭の major.minor(.build) を取り出す。
	/// SQLite "3.49.1" / PostgreSQL "17.2" / MariaDB "11.4.2-MariaDB" のいずれも解釈できる。
	/// </summary>
	public static Version? Parse(string serverVersion) {
		if (string.IsNullOrWhiteSpace(serverVersion))
			return null;
		var span = serverVersion.AsSpan().TrimStart();
		var end = 0;
		var dots = 0;
		while (end < span.Length) {
			var c = span[end];
			if (char.IsAsciiDigit(c)) {
				end++;
				continue;
			}
			if (c == '.' && dots < 2 && end + 1 < span.Length && char.IsAsciiDigit(span[end + 1])) {
				dots++;
				end++;
				continue;
			}
			break;
		}
		if (end == 0)
			return null;
		var text = dots == 0 ? $"{span[..end]}.0" : span[..end].ToString();
		return Version.TryParse(text, out var version) ? version : null;
	}
}
