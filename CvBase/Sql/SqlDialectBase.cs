/*
# description
SqlDialectBase はルールを1回走査で適用する変換器の共通実装です。

処理は次の順です。
1. モードが Off なら何もせず引数を返す。
2. 字句列を左から右へ1回走査し、各位置でルールを順に試す。
3. 1つも差し替えが起きなければ引数の参照をそのまま返す（無駄な文字列生成をしない）。
4. 変換後にSQLite固有構文が残っていれば、Strictなら例外、Autoなら Findings として返す。

走査は1回だけです。差し替えた断片は SqlTokenKind.Raw になるため再照合されません。
「変換できない形は変換しない」を守るため、ルールが一致しなかった字句は素通しします。

# example
sealed class MyDialect() : SqlDialectBase("Postgre", [new IfnullRule()]) { }
 */
using Microsoft.Extensions.Logging;

namespace CvBase.Sql;

/// <summary>ルール適用型の方言変換器</summary>
public abstract class SqlDialectBase : ISqlDialect {

	static readonly ILogger<SqlDialectBase> _logger = new NLogExtender<SqlDialectBase>();

	readonly ISqlRewriteRule[] _rules;

	protected SqlDialectBase(string name, IEnumerable<ISqlRewriteRule> rules) {
		Name = name;
		_rules = [.. rules];
	}

	public string Name { get; }

	/// <summary>ルール適用型はSQLを書き換える。</summary>
	public bool TranslatesSql => true;

	public virtual IReadOnlyList<string> SessionSetupCommands => [];

	/// <summary>
	/// この方言が変換なしでそのまま実行できるSQLite固有構文のID。
	/// 例: MariaDB の <c>ifnull</c> は同名関数があるためルール不要で、未対応構文として報告しない。
	/// </summary>
	protected virtual IReadOnlySet<string> NativeConstructIds => _emptyIds;

	static readonly HashSet<string> _emptyIds = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>登録されている変換ルールのID。</summary>
	public IReadOnlyList<string> RuleIds => [.. _rules.Select(r => r.Id)];

	public string Translate(string sql) {
		if (string.IsNullOrEmpty(sql) || SqlDialectOptions.Mode == SqlDialectMode.Off)
			return sql;

		var (result, findings) = TranslateCore(sql);
		if (findings.Count == 0)
			return result;

		if (SqlDialectOptions.Mode == SqlDialectMode.Strict)
			throw new SqlDialectUnsupportedException(Name, findings, sql);

		_logger.LogWarning("SQL方言変換 未対応構文 方言={Dialect} 件数={Count} 構文={Findings}",
			Name, findings.Count, string.Join(", ", findings.Select(f => f.ToString())));
		return result;
	}

	/// <summary>
	/// 変換を試したうえで、この方言へ移せなかった構文を挙げる。例外は投げずログも出さない。
	/// <para>
	/// 「ルールが無い構文」だけでなく「ルールはあるが対応できない形」も検出する。
	/// たとえば <c>json_extract(x,'$.a.b')</c> は B01 のルールがあっても入れ子パスなので変換されず、
	/// ここで報告される。CvWpfclient の開発時自己検査がこれを使う。
	/// </para>
	/// </summary>
	public IReadOnlyList<SqlDialectFinding> Inspect(string sql) {
		if (string.IsNullOrEmpty(sql))
			return [];
		var (_, findings) = TranslateCore(sql);
		return findings;
	}

	/// <summary>ルールを適用し、変換結果と未対応構文を返す。</summary>
	(string Result, IReadOnlyList<SqlDialectFinding> Findings) TranslateCore(string sql) {
		var context = new SqlRewriteContext(sql);
		// 右から左へ1回走査する。
		// 入れ子の内側は必ず外側より後ろの位置にあるため、右から処理すれば
		// `json_each(CASE WHEN json_valid(x) ... END) m` のように範囲ごと差し替えるルールでも
		// 内側が変換済みの状態で取り込める。差し替えは走査位置より後ろで起きるので位置ずれも起きない。
		for (var i = context.Tokens.Count - 1; i >= 0; i--) {
			if (i >= context.Tokens.Count)
				continue;
			foreach (var rule in _rules) {
				if (rule.Apply(context, i))
					break; // 同じ位置に複数ルールを重ねない
			}
		}
		var result = context.Mutated ? context.Render() : sql;
		return (result, InspectTranslated(result));
	}

	public abstract IReadOnlyList<string> Validate(string serverVersion);

	/// <summary>
	/// 変換後のSQLに残ったSQLite固有構文を挙げる。
	/// 変換なしで通る構文は除く。ルールがあるのに一致しなかった（＝対応できない形の）構文は残す。
	/// </summary>
	IReadOnlyList<SqlDialectFinding> InspectTranslated(string translated) =>
		[.. SqliteConstructCatalog.Scan(translated).Where(f => !NativeConstructIds.Contains(f.Construct.Id))];
}

/// <summary>変換器の動作設定。CvServer が起動時に一度設定する。</summary>
public static class SqlDialectOptions {

	/// <summary>
	/// 動作モード。既定は <see cref="SqlDialectMode.Auto"/>。
	/// <para>
	/// 設定 <c>Database:SqlTranslation</c> で切り替える。<c>Off</c> は障害時の退避用で、
	/// 全プロバイダーが恒等変換に落ちる。
	/// </para>
	/// </summary>
	public static SqlDialectMode Mode { get; set; } = SqlDialectMode.Auto;

	/// <summary>
	/// PostgreSQL の <c>ORDER BY</c> へ <c>NULLS FIRST</c> を付けるか（ルール A04）。
	/// <para>
	/// PostgreSQL の既定は <c>NULLS LAST</c> で、SQLite / MariaDB とNULLの並び位置が逆になる。
	/// ただし <c>ORDER BY</c> 句へ手を入れる唯一のルールなので既定は無効とし、
	/// 3DB差分テストで必要性を確認してから有効化する。
	/// 設定 <c>Database:SqlRules:A04-NullsOrder</c> で切り替える。
	/// </para>
	/// </summary>
	public static bool EnableNullsFirst { get; set; }

	/// <summary>設定文字列からモードを解釈する。空・未知の値は Auto。</summary>
	public static SqlDialectMode ParseMode(string? value) =>
		(value ?? string.Empty).Trim().ToUpperInvariant() switch {
			"OFF" => SqlDialectMode.Off,
			"STRICT" => SqlDialectMode.Strict,
			_ => SqlDialectMode.Auto,
		};
}
