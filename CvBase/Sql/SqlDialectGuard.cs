/*
# description
SqlDialectGuard は、組み立てたSQLが PostgreSQL / MariaDB へ移せる形かを検査します。

CvWpfclient から開発時に呼ぶことを想定した入口です。CV10 1.0 は SQLite だけを扱うため、
この検査は**警告を出すだけで、SQLを書き換えず、送信も止めません**。
新しい View / ViewModel を書いた開発者が、未対応構文を使ったことをその場で気づけるようにする
のが目的です。

検査は変換器と同じ SqliteConstructCatalog を見るため、変換ルールが増えれば
自動的に警告が減ります。

# example
// 送信直前（Debugビルドまたは設定有効時のみ）
SqlDialectGuard.WarnIfUnsupported(sql, "MasterTokuiMente 一覧");
 */
using Microsoft.Extensions.Logging;

namespace CvBase.Sql;

/// <summary>組み立てたSQLの他DB移植性を検査する（警告のみ。SQLは変更しない）</summary>
public static class SqlDialectGuard {

	static readonly ILogger<object> _logger = new NLogExtender<object>(nameof(SqlDialectGuard));

	/// <summary>
	/// 検査を行うか。既定は Debug ビルドのみ有効。
	/// リリースビルドで一時的に確認したいときだけ true にする。
	/// </summary>
	public static bool Enabled { get; set; } =
#if DEBUG
		true;
#else
		false;
#endif

	/// <summary>検査対象の方言。PostgreSQL と MariaDB の両方で見る。</summary>
	static readonly ISqlDialect[] _targets = [SqlDialects.Postgre, SqlDialects.Maria];

	/// <summary>
	/// SQLに含まれる、対象方言へまだ変換できないSQLite固有構文を返す。
	/// 同じ構文が両方言で未対応でも1件にまとめる。
	/// <para>
	/// この検査は診断が目的なので、**どんな入力でも例外を投げない**。
	/// 変換ルールに不具合があっても、SQLiteで動いている画面を壊してはならないためである。
	/// 検査自体が失敗した場合は空を返す。
	/// </para>
	/// </summary>
	public static IReadOnlyList<SqlDialectFinding> Inspect(string sql) {
		if (string.IsNullOrWhiteSpace(sql) || SqlDialectOptions.Mode == SqlDialectMode.Off)
			return [];
		try {
			var findings = new List<SqlDialectFinding>();
			var seen = new HashSet<(string Id, int Position)>();
			foreach (var dialect in _targets) {
				foreach (var finding in dialect.Inspect(sql)) {
					if (seen.Add((finding.Construct.Id, finding.Position)))
						findings.Add(finding);
				}
			}
			findings.Sort((a, b) => a.Position.CompareTo(b.Position));
			return findings;
		}
		catch (Exception ex) {
			// 診断が失敗しても呼び出し元の処理は続ける
			_logger.LogWarning(ex, "SQL移植性の検査に失敗しました。検査を飛ばして続行します。");
			return [];
		}
	}

	/// <summary>
	/// 未対応構文があれば警告ログを出す。
	/// <para>
	/// SQLは変更せず、送信も止めず、例外も投げない。常に呼び出し元へ制御を返す。
	/// </para>
	/// </summary>
	/// <param name="sql">検査するSQL</param>
	/// <param name="context">ログに出す呼び出し元の識別（画面名など）</param>
	public static void WarnIfUnsupported(string sql, string context) {
		if (!Enabled)
			return;
		try {
			var findings = Inspect(sql);
			if (findings.Count == 0)
				return;
			// 構文ごとに件数をまとめて出す。位置まで出すとSQLが長い画面でログが読めなくなる
			var summary = string.Join(", ", findings
				.GroupBy(f => f.Construct.Id)
				.Select(g => $"{g.Key}×{g.Count()}"));
			_logger.LogWarning("SQL移植性 未対応構文 対象={Context} 構文={Summary}", context, summary);
		}
		catch (Exception) {
			// ログ出力の失敗で画面を止めない
		}
	}
}
