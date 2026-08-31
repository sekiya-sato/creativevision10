/*
# description
PostgreSqlDialect は SQLite 方言のSQLを PostgreSQL 向けへ変換します。

Phase 1 ではルールを1つも持ちません。変換は行われず、SQLite固有構文が残っていることを
検出して報告するだけです。ルールは Phase 2 以降で
`.omo/2026-08-25_sql_dialect_translator_detail_design.md` §4 のカタログ順に追加します。

PostgreSQL は下限を 16 とします。json_valid 相当の `IS JSON` 述語が 16 以降にしか無く、
16未満だと不正JSONガード18箇所を自前関数で再実装することになるためです。

# example
var sql = SqlDialects.Postgre.Translate(clientSql);
 */
using CvBase.Share;
using CvBase.Sql.Rules;

namespace CvBase.Sql.Dialects;

/// <summary>PostgreSQL向けの方言変換</summary>
public sealed class PostgreSqlDialect : SqlDialectBase {

	/// <summary>方言名</summary>
	public const string DialectName = nameof(EnumSqlDialect.Postgre);

	public PostgreSqlDialect() : base(DialectName, BuildRules()) {
	}

	/// <summary>
	/// 変換ルール。Phase 3 以降で B01〜B08 を追加する。
	/// <para>
	/// A02(CAST型) は PostgreSQL が TEXT / REAL / INTEGER をそのまま解釈するため不要。
	/// A04(NULLS FIRST) は既定で無効（<see cref="SqlDialectOptions.EnableNullsFirst"/>）。
	/// C04(UPSERT) は PostgreSQL が SQLite と同じ <c>ON CONFLICT ... DO UPDATE</c> と
	/// <c>excluded.列</c> を使えるため不要（<see cref="NativeConstructIds"/> で扱う）。
	/// </para>
	/// </summary>
	static IEnumerable<ISqlRewriteRule> BuildRules() => [
		new IfnullRule(),
		JsonExtractRule.ForPostgre(),
		JsonEachRule.ForPostgre(),
		new JsonEachKeyRule(),
		JsonFunctionRule.JsonValidForPostgre(),
		JsonFunctionRule.JsonCastForPostgre(),
		JsonRenameRule.GroupArrayForPostgre(),
		JsonRenameRule.ObjectForPostgre(),
		new JsonSetRule(),
		StrftimeRule.ForPostgre(),
		PrintfRule.ForPostgre(),
		DateModifierRule.ForPostgre(),
		JuliandayDiffRule.ForPostgre(),
		ReservedIdentifierRule.ForPostgre(),
		new NullsOrderRule(),
	];

	/// <summary>
	/// 変換なしでそのまま通るSQLite固有構文。
	/// UPSERT は PostgreSQL が SQLite と同じ構文を持つ。
	/// </summary>
	protected override IReadOnlySet<string> NativeConstructIds { get; } =
		new HashSet<string>(["C04-Upsert"], StringComparer.OrdinalIgnoreCase);

	public override IReadOnlyList<string> Validate(string serverVersion) {
		var version = SqlDialectVersions.Parse(serverVersion);
		if (version == null)
			return [$"PostgreSQLのバージョンを判定できません。値={serverVersion}"];
		return version < SqlDialectVersions.PostgreMinimum
			? [$"PostgreSQL {SqlDialectVersions.PostgreMinimum.Major} 以降が必要です(IS JSON 述語)。接続先={version}"]
			: [];
	}
}
