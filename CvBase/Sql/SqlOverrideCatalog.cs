/*
# description
SqlOverrideCatalog は、方言ごとに手書きSQLを差し替えるための登録表です。

変換器で表現できない形が出たときに、クライアント側のSQLを書き換えることを強制しない
ための逃げ道です。CV10 は SQL の組み立てを CvWpfclient 側でも行い、SQLite方言を正典
としているため、「PG互換の書き方に直す」ためにSQLite側のSQLを触るのは避けたいからです。

使い方は、クライアントが `QueryListSqlParam.QueryKey` を指定し、その QueryKey と方言名の
組で手書きSQLを登録します。登録があればサーバは変換をせずそのSQLを使います。
SQLite には登録を置きません（SQLiteは常にクライアントのSQLをそのまま実行する）。

登録はコードで行います。設定ファイルからSQLを読み込む仕組みは作りません。
どのSQLが差し替わっているかがリポジトリの差分に残らないと追跡できないためです。

# example
SqlOverrideCatalog.Register("Zaiko.StockAging", "Postgre", "select ...");
if (SqlOverrideCatalog.TryGet(queryKey, dialect.Name, out var handWritten)) { ... }
 */
using System.Collections.Concurrent;

namespace CvBase.Sql;

/// <summary>QueryKey と方言名で手書きSQLを差し替える登録表</summary>
public static class SqlOverrideCatalog {

	static readonly ConcurrentDictionary<(string QueryKey, string Dialect), string> _entries =
		new();

	/// <summary>登録件数</summary>
	public static int Count => _entries.Count;

	/// <summary>
	/// 手書きSQLを登録する。同じ組で再登録すると上書きする。
	/// SQLite への登録は受け付けない（正典を差し替える意味がないため）。
	/// </summary>
	public static void Register(string queryKey, string dialectName, string sql) {
		ArgumentException.ThrowIfNullOrWhiteSpace(queryKey);
		ArgumentException.ThrowIfNullOrWhiteSpace(dialectName);
		ArgumentException.ThrowIfNullOrWhiteSpace(sql);
		if (string.Equals(dialectName, "Sqlite", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("SQLiteへの差し替えは登録できません。SQLiteはクライアントのSQLをそのまま実行します。", nameof(dialectName));
		_entries[(queryKey, dialectName)] = sql;
	}

	/// <summary>
	/// QueryKey と方言名に対応する手書きSQLを返す。
	/// QueryKey が未指定（クライアントが指定していない）なら常に false。
	/// </summary>
	public static bool TryGet(string? queryKey, string dialectName, out string sql) {
		sql = string.Empty;
		if (string.IsNullOrWhiteSpace(queryKey))
			return false;
		if (!_entries.TryGetValue((queryKey, dialectName), out var found))
			return false;
		sql = found;
		return true;
	}

	/// <summary>登録を全て消す（テスト用）。</summary>
	public static void Clear() => _entries.Clear();
}
