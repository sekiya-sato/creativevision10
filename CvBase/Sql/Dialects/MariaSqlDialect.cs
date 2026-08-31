/*
# description
MariaSqlDialect は SQLite 方言のSQLを MariaDB 向けへ変換します。

Phase 1 ではルールを1つも持ちません。変換は行われず、SQLite固有構文が残っていることを
検出して報告するだけです。ルールは Phase 2 以降で
`.omo/2026-08-25_sql_dialect_translator_detail_design.md` §4 のカタログ順に追加します。

セッション設定は Phase 1 から入れます。`PIPES_AS_CONCAT` と `NO_BACKSLASH_ESCAPES` の
2語だけで、文字列連結 `||`（約110箇所）と `ESCAPE '\'`（6箇所）がSQL書換なしで解決します。
`ONLY_FULL_GROUP_BY` と `STRICT_TRANS_TABLES` は入れません（SQLiteの緩さに合わせる）。

MariaDB は下限を 10.11 LTS とします。json_each の変換に使う JSON_TABLE が 10.6 以降で、
10.6 は既にEOLのためです。

# example
var sql = SqlDialects.Maria.Translate(clientSql);
 */
using CvBase.Share;
using CvBase.Sql.Rules;

namespace CvBase.Sql.Dialects;

/// <summary>MariaDB向けの方言変換</summary>
public sealed class MariaSqlDialect : SqlDialectBase {

	/// <summary>方言名</summary>
	public const string DialectName = nameof(EnumSqlDialect.MariaDb);

	public MariaSqlDialect() : base(DialectName, BuildRules()) {
	}

	/// <summary>
	/// 変換ルール。Phase 3 以降で B01〜B08 を追加する。
	/// </summary>
	static IEnumerable<ISqlRewriteRule> BuildRules() => [
		JsonExtractRule.ForMaria(),
		JsonEachRule.ForMaria(),
		new JsonEachKeyRule(),
		JsonFunctionRule.JsonCastForMaria(),
		JsonRenameRule.GroupArrayForMaria(),
		new UpsertHeaderRule(),
		new ExcludedColumnRule(),
		StrftimeRule.ForMaria(),
		PrintfRule.ForMaria(),
		DateModifierRule.ForMaria(),
		JuliandayDiffRule.ForMaria(),
		CastTypeRule.ForMaria(),
		ReservedIdentifierRule.ForMaria(),
	];

	/// <summary>
	/// 変換なしでそのまま通るSQLite固有構文。MariaDBに同名・同意味の関数があるもの。
	/// <para>
	/// <c>json_group_array</c> は MariaDB では <c>JSON_ARRAYAGG</c> で名前が違い、
	/// さらに順序保証が異なるためここには含めない（サーバ層で個別に対応する）。
	/// </para>
	/// </summary>
	protected override IReadOnlySet<string> NativeConstructIds { get; } =
		new HashSet<string>([
			"A01-Ifnull",
			"B03-JsonValid",
			"B04-JsonObject",
			"B04-JsonSet",
		], StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// 接続確立直後に流すセッション設定。
	/// SQLと同じ意味を持たせるための最小限にとどめる。
	/// </summary>
	public override IReadOnlyList<string> SessionSetupCommands => [
		"SET SESSION sql_mode = CONCAT(@@sql_mode, ',PIPES_AS_CONCAT,NO_BACKSLASH_ESCAPES')",
	];

	public override IReadOnlyList<string> Validate(string serverVersion) {
		var version = SqlDialectVersions.Parse(serverVersion);
		if (version == null)
			return [$"MariaDBのバージョンを判定できません。値={serverVersion}"];
		return version < SqlDialectVersions.MariaMinimum
			? [$"MariaDB {SqlDialectVersions.MariaMinimum} 以降が必要です(JSON_TABLE)。接続先={version}"]
			: [];
	}
}
