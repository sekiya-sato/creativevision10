/*
# description
ISqlDialect はクライアント由来SQLを接続先DBの方言へ変換する入口です。

CV10 は SQL の組み立てを CvWpfclient 側でも行うため、ソース上のSQLは SQLite 方言が正典です。
SQLite 以外へ接続するときだけ、この実装がSQLを書き換えます。
SQLite では PassThroughSqlDialect が引数の参照をそのまま返し、変換処理は1行も走りません。

変換を差す入口は次の2経路だけです。
1. クライアント由来SQLの受け口: CvServer の HandlerClass 3点
   （HandleQueryOne / HandleQueryList / HandleQueryListSql）。
2. サーバ層のSQL: ExDatabase.ExecuteDialect / FetchDialect / TranslateDialect。
   CvDomainLogic のJSON配列再構築やUPSERTなど、DB間で構文が異なる箇所がこれ経由で通す。
それ以外（Execute / Fetch を直接呼ぶコードなど）は変換器を通さない。

# example
var sql = _db.Dialect.Translate(querySql.Sql ?? string.Empty);
 */
namespace CvBase.Sql;

/// <summary>変換器の動作モード</summary>
public enum SqlDialectMode {
	/// <summary>変換する。未対応構文は警告のみでそのまま実行する</summary>
	Auto = 0,
	/// <summary>変換する。未対応構文が残ったら例外にする</summary>
	Strict = 1,
	/// <summary>変換しない。全プロバイダーで恒等変換になる（障害時の退避用）</summary>
	Off = 2,
}

/// <summary>SQL方言変換</summary>
public interface ISqlDialect {

	/// <summary>方言名。Sqlite / Postgre / MariaDb。</summary>
	string Name { get; }

	/// <summary>
	/// この方言がSQLを書き換えるか。SQLite（恒等変換）では false。
	/// <para>
	/// 呼び出し側はこれが false のとき、変換にまつわる処理を丸ごと飛ばす。
	/// 「SQLiteの実行経路には方言変換のコードを一切通さない」ことを、
	/// 実装の性質に頼らず分岐として明示するためのフラグである。
	/// </para>
	/// </summary>
	bool TranslatesSql { get; }

	/// <summary>
	/// クライアント由来SQLを接続先の方言へ変換する。
	/// 変換が不要なら引数の参照をそのまま返す。
	/// </summary>
	string Translate(string sql);

	/// <summary>
	/// SQLite固有構文のうち、この方言で変換できなかったものを列挙する。
	/// 変換前のSQLを渡す。CvWpfclient の開発時自己検査からも使う。
	/// </summary>
	IReadOnlyList<SqlDialectFinding> Inspect(string sql);

	/// <summary>
	/// 接続先のバージョン・機能を検証する。不足内容の文言を返す。空なら合格。
	/// </summary>
	/// <param name="serverVersion">ExDatabase.Version の値</param>
	IReadOnlyList<string> Validate(string serverVersion);

	/// <summary>接続確立直後に流すセッション設定。無ければ空。</summary>
	IReadOnlyList<string> SessionSetupCommands { get; }
}
