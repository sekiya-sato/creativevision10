using CvBase;
using CvBase.Share;

namespace CvServer;

/// <summary>
/// DB種別から <see cref="ExDatabase"/> のインスタンスを作る。
/// <para>
/// プロバイダー別のクラス(CvBaseSqlite / CvBasePostgre / CvBaseMariadb / CvBaseOracle)を参照するのは
/// 全プロバイダーを参照できる CvServer だけなので、種別による生成の分岐はここ1か所に置く。
/// </para>
/// </summary>
public static class ExDatabaseFactory {

	/// <summary>
	/// DB種別と接続文字列から接続を作る。
	/// </summary>
	public static ExDatabase Create(EnumSqlDialect dialect, string connectionString) => dialect switch {
		EnumSqlDialect.Sqlite => CvBaseSqlite.ExDatabaseSqlite.GetDbConn(connectionString),
		EnumSqlDialect.Postgre => CvBasePostgre.ExDatabasePostgre.GetDbConn(connectionString),
		EnumSqlDialect.MariaDb or EnumSqlDialect.MySql => CvBaseMariadb.ExDatabaseMaria.GetDbConn(connectionString),
		EnumSqlDialect.Oracle => CvBaseOracle.ExDatabaseOracle.GetDbConn(connectionString),
		_ => throw new InvalidOperationException($"DB種別 '{dialect}' の接続は作成できません。")
	};

	/// <summary>
	/// ConnectionStrings のキー名からDB種別を判別する
	/// （sqlite / maria・mysql / postgre を含む名前。例: "mariadb"、"back-postgres"）。
	/// </summary>
	public static bool TryResolveByConnectionName(string name, out EnumSqlDialect dialect) {
		var key = name.ToLowerInvariant();
		if (key.Contains("sqlite")) { dialect = EnumSqlDialect.Sqlite; return true; }
		if (key.Contains("maria") || key.Contains("mysql")) { dialect = EnumSqlDialect.MariaDb; return true; }
		if (key.Contains("postgre")) { dialect = EnumSqlDialect.Postgre; return true; }
		dialect = default;
		return false;
	}
}
