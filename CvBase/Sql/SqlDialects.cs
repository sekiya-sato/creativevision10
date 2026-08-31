/*
# description
SqlDialects は方言インスタンスの取得口です。

方言実装を CvBase(層1) に置いているため、CvServer からも CvWpfclient からも同じ
インスタンスを参照できます。CvWpfclient は変換には使わず、開発時の自己検査と
SQLプレビューに使います（送信するSQLは常にSQLite正典形のまま）。

方言は状態を持たないため単一インスタンスを共有します。

# example
var findings = SqlDialects.Postgre.Inspect(sql);   // 未対応構文の確認
var dialect  = SqlDialects.ByProviderName("MariaDb");
 */
using CvBase.Share;
using CvBase.Sql.Dialects;

namespace CvBase.Sql;

/// <summary>方言インスタンスの取得口</summary>
public static class SqlDialects {

	/// <summary>SQLite。恒等変換。</summary>
	public static ISqlDialect Sqlite { get; } = new PassThroughSqlDialect(nameof(EnumSqlDialect.Sqlite));

	/// <summary>PostgreSQL</summary>
	public static ISqlDialect Postgre { get; } = new PostgreSqlDialect();

	/// <summary>MariaDB</summary>
	public static ISqlDialect Maria { get; } = new MariaSqlDialect();

	/// <summary>
	/// 設定値 <c>Database:Provider</c> の文字列から方言を得る。
	/// 未知の値は恒等変換（現行動作を壊さない側へ倒す）。
	/// </summary>
	public static ISqlDialect ByProviderName(string? provider) {
		var providerName = provider?.Trim();
		if (string.Equals(providerName, nameof(EnumSqlDialect.Postgre), StringComparison.OrdinalIgnoreCase))
			return Postgre;
		if (string.Equals(providerName, nameof(EnumSqlDialect.MariaDb), StringComparison.OrdinalIgnoreCase))
			return Maria;
		if (string.Equals(providerName, nameof(EnumSqlDialect.Sqlite), StringComparison.OrdinalIgnoreCase))
			return Sqlite;
		return PassThroughSqlDialect.Instance;
	}
}
