/*
# description
SqlDialectUnsupportedException は、SQLite固有構文をこの方言へ変換できなかったときに投げます。

Strictモードでのみ発生します。素通しして接続先DBのエラーに委ねると
「本番の特定画面だけ動かない」状態を作るため、既定では例外にします。

# example
throw new SqlDialectUnsupportedException("Postgre", findings, sql);
 */
using System.Text;

namespace CvBase.Sql;

/// <summary>SQLite固有構文を対象方言へ変換できなかった</summary>
public sealed class SqlDialectUnsupportedException : InvalidOperationException {

	/// <summary>変換できなかった構文</summary>
	public IReadOnlyList<SqlDialectFinding> Findings { get; }

	/// <summary>対象方言名</summary>
	public string DialectName { get; }

	/// <summary>変換対象だったSQL</summary>
	public string Sql { get; }

	public SqlDialectUnsupportedException(string dialectName, IReadOnlyList<SqlDialectFinding> findings, string sql)
		: base(BuildMessage(dialectName, findings, sql)) {
		DialectName = dialectName;
		Findings = findings;
		Sql = sql;
	}

	static string BuildMessage(string dialectName, IReadOnlyList<SqlDialectFinding> findings, string sql) {
		var sb = new StringBuilder();
		sb.Append($"{dialectName} へ変換できないSQLite固有構文が {findings.Count} 件あります: ");
		sb.Append(string.Join(", ", findings.Select(f => f.ToString())));
		sb.Append('。');
		// SQLは長いので先頭のみ添える。全文は呼び出し側のログへ出す
		var head = sql.Length <= 400 ? sql : sql[..400] + "...";
		sb.Append($"SQL={head}");
		return sb.ToString();
	}
}
