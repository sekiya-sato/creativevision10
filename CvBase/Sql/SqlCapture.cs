/*
# description
SqlCapture は、方言変換の検証用に「実際に実行されたSQLite方言のSQLと引数」を JSONL へ記録します。

環境変数 CV_SQL_CAPTURE に出力ファイルのパスが設定されているときだけ動きます（既定は無効で、
無効時は Enabled の判定1回だけ。SQLite の実行経路の動作は変えない）。
記録するのは変換の入力側（SQLite方言）で、次の経路です。
- クライアントSQL: HandlerClass の QueryOne / QueryList / QueryListSql（変換前）
- サーバSQL: ExDatabase.ExecuteDialect / FetchDialect
- 印刷: PrintPre の QueryListSqlParam（サーバ側マクロ展開後）

記録は検証ツール（Doc/spec/tools/sqlreplay）が SQLite / MariaDB / PostgreSQL で再実行して照合する。
引数は型名と文字列表現で保存し、ツール側で型を復元する。

# example
CV_SQL_CAPTURE=C:\temp\capture.jsonl dotnet CvServer.dll
 */
using System.Globalization;
using System.Text.Json;

namespace CvBase.Sql;

/// <summary>検証用: 実行SQLの記録（既定は無効）</summary>
public static class SqlCapture {

	static readonly string? _path = Environment.GetEnvironmentVariable("CV_SQL_CAPTURE");
	static readonly object _sync = new();

	/// <summary>記録が有効か</summary>
	public static bool Enabled => !string.IsNullOrEmpty(_path);

	/// <summary>1件記録する。失敗しても呼出し元の処理は止めない。</summary>
	/// <param name="origin">経路（例: client:QueryListSql、server:ExecuteDialect、print）</param>
	/// <param name="sql">SQLite方言のSQL</param>
	/// <param name="args">引数</param>
	/// <param name="itemType">結果を受ける型（NPocoの自動SELECT補完に使われる型。無ければnull）</param>
	public static void Record(string origin, string sql, IEnumerable<object?>? args, Type? itemType = null) {
		if (!Enabled || string.IsNullOrWhiteSpace(sql))
			return;
		try {
			var record = new {
				origin,
				itemType = itemType?.AssemblyQualifiedName,
				sql,
				args = (args ?? []).Select(a => a is null
					? new { t = "null", v = "" }
					: new { t = a.GetType().Name, v = Convert.ToString(a, CultureInfo.InvariantCulture) ?? "" }).ToArray(),
			};
			var line = JsonSerializer.Serialize(record);
			lock (_sync) {
				File.AppendAllText(_path!, line + Environment.NewLine);
			}
		}
		catch {
			// 検証用の記録で業務処理を止めない
		}
	}
}
