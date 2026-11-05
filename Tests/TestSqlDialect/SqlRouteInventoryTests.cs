/*
# description
SqlRouteInventoryTests は「SQLite固有構文を含むSQLが、方言変換を通らずに実行される経路」を静的に洗い出します。

対象はサーバ側(CvBase / CvDomainLogic / CvServer)のSQLリテラルです。各リテラルについて
- PostgreSQL / MariaDB の変換器が未対応構文を報告するか（＝SQLite固有構文を含むか）
- リテラルの後ろで最初に現れるDB呼出しが、変換を通る呼出し(ExecuteDialect / FetchDialect / TranslateDialect)か、
  通らない呼出し(Execute / Fetch / ExecuteScalar / RawExecCmd / FirstOrDefault など)か
を判定し、「SQLite固有構文を含むのに変換を通らない」ものを一覧にします。

判定は字面による近似です（リテラルを変数へ入れて別メソッドで実行する形は追えない）。
一覧は修正候補の洗い出しに使い、件数はテストの合否に使いません。環境変数 CV_SQL_INVENTORY に
出力先ファイルを指定したときだけ Markdown を書き出します。

# example
CV_SQL_INVENTORY=C:\temp\inventory.md TestSqlDialect.exe --filter SqlRouteInventoryTests
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TestSqlDialect;

[TestClass]
public sealed partial class SqlRouteInventoryTests {

	/// <summary>変換を通る呼出し</summary>
	[GeneratedRegex(@"\.(ExecuteDialect|FetchDialect|TranslateDialect)\s*[<(]")]
	private static partial Regex TranslatedCall();

	/// <summary>変換を通らない呼出し（NPoco / ExDatabase の直接実行）</summary>
	[GeneratedRegex(@"\.(Execute|ExecuteScalar|Fetch|FirstOrDefault|SingleOrDefault|Query|Page|RawExecCmd)\s*[<(]")]
	private static partial Regex RawCall();

	/// <summary>通常の文字列リテラル（$"..." を含む。複数行の verbatim/raw は SqlCorpus が拾う）</summary>
	[GeneratedRegex("""\$?"((?:[^"\\\r\n]|\\.)*)" """, RegexOptions.IgnorePatternWhitespace)]
	private static partial Regex RegularString();

	[TestMethod]
	public void SQLite固有構文を変換なしで実行する経路を一覧にする() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		var root = SqlCorpus.FindRepositoryRoot();
		var rows = new List<(string Where, string Call, string Constructs, string Head)>();
		var translatedCount = 0;
		var total = 0;
		foreach (var directory in new[] { "CvBase", "CvDomainLogic", "CvServer" }) {
			foreach (var file in Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories)) {
				if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
					continue;
				// 変換器そのもの(CvBase/Sql)は説明用のSQL例を多く含むため除く
				if (file.Contains($"{Path.DirectorySeparatorChar}Sql{Path.DirectorySeparatorChar}"))
					continue;
				var source = File.ReadAllText(file);
				var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
				foreach (var (sql, offset) in Literals(source)) {
					// 「変換が必要なSQL」= 変換で文字列が変わる、または変換しきれない構文が残るもの
					var constructs = SqlDialects.Postgre.Inspect(sql).Select(f => "残:" + f.Construct.Id)
						.Concat(SqlDialects.Maria.Inspect(sql).Select(f => "残:" + f.Construct.Id)).Distinct().ToList();
					var changed = new List<string>();
					if (!string.Equals(SqlDialects.Postgre.Translate(sql), sql, StringComparison.Ordinal)) changed.Add("PG変換");
					if (!string.Equals(SqlDialects.Maria.Translate(sql), sql, StringComparison.Ordinal)) changed.Add("Maria変換");
					if (constructs.Count == 0 && changed.Count == 0)
						continue;
					constructs.AddRange(changed);
					total++;
					var call = FirstCallAfter(source, offset);
					if (call.StartsWith("変換あり", StringComparison.Ordinal)) {
						translatedCount++;
						continue;
					}
					var line = source[..offset].Count(c => c == '\n') + 1;
					var head = Regex.Replace(sql, @"\s+", " ").Trim();
					rows.Add(($"{relative}:{line}", call, string.Join(",", constructs), head.Length > 90 ? head[..90] + "…" : head));
				}
			}
		}
		var output = Environment.GetEnvironmentVariable("CV_SQL_INVENTORY");
		if (!string.IsNullOrEmpty(output)) {
			var md = new StringBuilder();
			md.AppendLine($"SQLite固有構文を含むサーバ側SQLリテラル: {total} 本（変換経路 {translatedCount} 本 / 変換なし・不明 {rows.Count} 本）");
			md.AppendLine();
			md.AppendLine("| 位置 | 最初のDB呼出し | 構文 | 先頭 |");
			md.AppendLine("|---|---|---|---|");
			foreach (var r in rows.OrderBy(r => r.Where, StringComparer.Ordinal))
				md.AppendLine($"| {r.Where} | {r.Call} | {r.Constructs} | `{r.Head.Replace("|", "\\|")}` |");
			File.WriteAllText(output, md.ToString());
		}
	}

	/// <summary>
	/// 全SQLリテラル（クライアント・サーバ）を両方言へ変換し、未対応構文が残るものを位置付きで一覧にする。
	/// 環境変数 CV_SQL_STATIC に出力先を指定したときだけ書き出す。
	/// </summary>
	[TestMethod]
	public void 未対応構文が残るSQLリテラルを一覧にする() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		var corpus = SqlCorpus.LoadWithLocation("CvWpfclient", "CvBase", "CvDomainLogic", "CvServer")
			.Where(x => !x.File.Contains("/Sql/", StringComparison.Ordinal))
			.ToList();
		var rows = new List<string>();
		var byConstruct = new Dictionary<string, int>();
		foreach (var literal in corpus) {
			var pg = SqlDialects.Postgre.Inspect(literal.Sql).Select(f => f.Construct.Id).Distinct().ToList();
			var maria = SqlDialects.Maria.Inspect(literal.Sql).Select(f => f.Construct.Id).Distinct().ToList();
			if (pg.Count == 0 && maria.Count == 0)
				continue;
			foreach (var id in pg.Select(x => "PG:" + x).Concat(maria.Select(x => "Maria:" + x)))
				byConstruct[id] = byConstruct.TryGetValue(id, out var n) ? n + 1 : 1;
			var head = Regex.Replace(literal.Sql, @"\s+", " ").Trim();
			rows.Add($"| {literal} | {string.Join(",", pg)} | {string.Join(",", maria)} | `{(head.Length > 80 ? head[..80] + "…" : head).Replace("|", "\\|")}` |");
		}
		var output = Environment.GetEnvironmentVariable("CV_SQL_STATIC");
		if (!string.IsNullOrEmpty(output)) {
			var md = new StringBuilder();
			md.AppendLine($"SQLリテラル {corpus.Count} 本のうち未対応構文が残るもの {rows.Count} 本");
			md.AppendLine();
			md.AppendLine("構文別: " + string.Join(", ", byConstruct.OrderByDescending(x => x.Value).Select(x => $"{x.Key}={x.Value}")));
			md.AppendLine();
			md.AppendLine("| 位置 | PostgreSQL残 | MariaDB残 | 先頭 |");
			md.AppendLine("|---|---|---|---|");
			foreach (var r in rows.OrderBy(r => r, StringComparer.Ordinal))
				md.AppendLine(r);
			File.WriteAllText(output, md.ToString());
		}
	}

	/// <summary>変換を通らない実行呼出し（NPoco / ExDatabase の直接実行）。第1引数の位置を取る</summary>
	[GeneratedRegex(@"\b(?:_db|_toDb|_fromDb|db|_database)\s*\.\s*(?<m>Execute|ExecuteAsync|ExecuteScalar|ExecuteScalarAsync|Fetch|FetchAsync|FirstOrDefault|FirstOrDefaultAsync|SingleOrDefault|Single|First|Query|Page|RawExecCmd)\s*(?:<[^<>()]*(?:<[^<>()]*>[^<>()]*)*>)?\s*\(")]
	private static partial Regex RawExecutionCall();

	/// <summary>
	/// 変換を通らずに SQLite 固有構文を実行することが正当な箇所（理由付き）。
	/// (ファイル, SQLに含まれる語) の組で絞る。語が null ならそのファイルの全SQLが対象。
	/// ここに無い箇所で検出されたらテストを失敗させる。
	/// </summary>
	static readonly (string File, string? Contains, string Reason)[] _rawExecutionAllowList = [
		("CvBase/ExDatabase.cs", "sqlite_master", "DatabaseType==SQLite の分岐でだけ実行し、MariaDB / PostgreSQL はプロバイダーで override している"),
		("CvDomainLogic/AiQuery/AiQuerySchemaCatalog.cs", null, "AI照会は SQLite 専用（AiQueryHandler が SQLite 以外を拒否する）"),
		("CvDomainLogic/AiQuery/AiQuerySqlGuard.cs", null, "AI照会は SQLite 専用（Validate が SQLite 以外で CheckPlan へ進まない）"),
		("CvServer/Services/SchedulerService.cs", "PRAGMA", "WAL checkpoint タスクは Program.cs が isSqlite のときだけ登録する"),
	];

	static bool IsAllowed(string relative, string sql) =>
		_rawExecutionAllowList.Any(a => a.File == relative && (a.Contains == null || sql.Contains(a.Contains, StringComparison.OrdinalIgnoreCase)));

	/// <summary>
	/// 回帰テスト: サーバ側で、方言変換が必要な SQL を変換を通らない呼出しで実行していないこと。
	/// <para>
	/// 実行呼出しの第1引数（Fetch(Type, sql) は第2引数）が文字列リテラルならそれを、変数なら同じファイル内で
	/// 直前にその変数へ代入しているリテラルを SQL とみなし、PostgreSQL / MariaDB の変換で書き換わる・
	/// 変換しきれない構文が残るものを違反とする。メソッド呼出しで組み立てた SQL など追えないものは対象外。
	/// 正当な例外は <see cref="_rawExecutionAllowList"/> に理由付きで登録する。
	/// </para>
	/// </summary>
	[TestMethod]
	public void サーバ側で変換が必要なSQLを変換経路の外で実行していない() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		var root = SqlCorpus.FindRepositoryRoot();
		var violations = new List<string>();
		var checkedCalls = 0;
		foreach (var directory in new[] { "CvBase", "CvDomainLogic", "CvServer" }) {
			foreach (var file in Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories)) {
				if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
					|| file.Contains($"{Path.DirectorySeparatorChar}Sql{Path.DirectorySeparatorChar}"))
					continue;
				var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
				var source = File.ReadAllText(file);
				foreach (Match call in RawExecutionCall().Matches(source)) {
					var sql = ResolveSqlArgument(source, call.Index + call.Length, call.Groups["m"].Value);
					if (sql == null)
						continue;
					checkedCalls++;
					if (!NeedsTranslation(sql) || IsAllowed(relative, sql))
						continue;
					var line = source[..call.Index].Count(c => c == '\n') + 1;
					var head = Regex.Replace(sql, @"\s+", " ").Trim();
					violations.Add($"{relative}:{line} {call.Groups["m"].Value} `{(head.Length > 100 ? head[..100] + "…" : head)}`");
				}
			}
		}
		Assert.IsTrue(checkedCalls > 50, $"検査できた呼出しが少なすぎる（検出の正規表現が壊れていないか）: {checkedCalls}");
		Assert.IsEmpty(violations,
			"変換経路(ExecuteDialect / FetchDialect / FirstOrDefaultDialect / TranslateDialect)を通さずに、方言変換が必要なSQLを実行しています:\n"
			+ string.Join("\n", violations));
	}

	/// <summary>SQLite の衝突解決句 INSERT OR REPLACE / OR IGNORE（変換ルールが無く、目録は単語単位なので別に検出する）</summary>
	[GeneratedRegex(@"\binsert\s+or\s+(replace|ignore|abort|fail|rollback)\b", RegexOptions.IgnoreCase)]
	private static partial Regex InsertOrConflict();

	/// <summary>
	/// 回帰テスト: SQLite の INSERT OR REPLACE / OR IGNORE を使っていないこと（どの経路でも MariaDB / PostgreSQL で失敗する）。
	/// </summary>
	[TestMethod]
	public void SQLite固有のINSERT_OR句を使っていない() {
		var root = SqlCorpus.FindRepositoryRoot();
		var hits = new List<string>();
		foreach (var directory in new[] { "CvWpfclient", "CvBase", "CvDomainLogic", "CvServer" }) {
			foreach (var file in Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories)) {
				if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
					continue;
				var source = File.ReadAllText(file);
				foreach (Match m in InsertOrConflict().Matches(source)) {
					var line = source[..m.Index].Count(c => c == '\n') + 1;
					hits.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{line} {m.Value}");
				}
			}
		}
		Assert.IsEmpty(hits, "INSERT OR ... は SQLite 専用です。delete + insert か ON CONFLICT で書いてください:\n" + string.Join("\n", hits));
	}

	/// <summary>
	/// 先頭の WITH 句の後ろが更新文になっている SQL を許す箇所（理由付き）。(ファイル, SQLに含まれる語)。
	/// </summary>
	static readonly (string File, string Contains, string Reason)[] _cteBeforeDmlAllowList = [
		("CvDomainLogic/SummaryDb.cs", "MonthlySum", "CalcSummaryStockCumulative は製品から呼ばれていない（Pending。SQLite のテストだけが使う）"),
	];

	/// <summary>
	/// 回帰テスト: <c>WITH … INSERT / UPDATE / DELETE</c>（共通表式を更新文の前に置く形）を使っていないこと。
	/// <para>
	/// SQLite / PostgreSQL は受け付けるが MariaDB は構文エラーになる。INSERT は
	/// <c>INSERT INTO t (列…) WITH … SELECT …</c> と書けば3DBとも同じ意味で動く（2026-11-05 実DBで確認）。
	/// </para>
	/// </summary>
	[TestMethod]
	public void 検出部品が想定どおりに読む() {
		// 補間穴の中の " で文字列の終わりと誤認しない（ShippingDb の where Id in ({string.Join(",", ids)}) の形）
		const string code = """_db.Execute($"where Id in ({string.Join(",", ids)}) and ifnull(K,'') <> ''", 1);""";
		Assert.IsTrue(TryReadLiteral(code, code.IndexOf('$'), out var sql));
		Assert.AreEqual("where Id in (x) and ifnull(K,'') <> ''", sql);
		// + で続くリテラルを連結する
		const string concat = """var s = "select a " + $"from {t} " + "where ifnull(b,0)=1";""";
		Assert.IsTrue(TryReadConcatenation(concat, concat.IndexOf('"'), out var joined));
		Assert.AreEqual("select a from x where ifnull(b,0)=1", joined);
		// WITH の後ろの主文
		Assert.AreEqual("insert", MainKeywordAfterWith("WITH a AS (SELECT 1), b AS (SELECT 2) INSERT INTO t SELECT * FROM a"));
		Assert.AreEqual("update", MainKeywordAfterWith("with recursive a(x) as (select 1) update t set c = 1"));
		Assert.AreEqual("select", MainKeywordAfterWith("WITH a AS (SELECT ')') SELECT * FROM a"));
		Assert.IsNull(MainKeywordAfterWith("INSERT INTO t WITH a AS (SELECT 1) SELECT * FROM a"));
		// 派生表の別名
		Assert.AreEqual(1, UnaliasedDerivedTables("SELECT a FROM (SELECT 1 AS a) GROUP BY a").Count());
		Assert.AreEqual(1, UnaliasedDerivedTables("SELECT * FROM (SELECT ')' AS a\n -- )\n)").Count());
		Assert.AreEqual(0, UnaliasedDerivedTables("SELECT a FROM (SELECT 1 AS a) AS s GROUP BY a").Count());
		Assert.AreEqual(0, UnaliasedDerivedTables("SELECT a FROM (SELECT 1 AS a) s WHERE a = 1").Count());
		Assert.AreEqual(0, UnaliasedDerivedTables("SELECT * FROM t WHERE Id IN (SELECT Id FROM u)").Count());
	}

	[TestMethod]
	public void 共通表式を更新文の前に置いていない() {
		var corpus = SqlCorpus.LoadWithLocation("CvWpfclient", "CvBase", "CvDomainLogic", "CvServer")
			.Where(x => !x.File.Contains("/Sql/", StringComparison.Ordinal))
			.ToList();
		Assert.IsTrue(corpus.Count > 100, $"SQLリテラルが少なすぎる: {corpus.Count}");
		var hits = new List<string>();
		foreach (var literal in corpus) {
			foreach (var statement in literal.Sql.Split(';')) {
				var main = MainKeywordAfterWith(statement);
				if (main is null or "select" or "values")
					continue;
				if (_cteBeforeDmlAllowList.Any(a => literal.File.EndsWith(a.File, StringComparison.Ordinal) && statement.Contains(a.Contains, StringComparison.Ordinal)))
					continue;
				hits.Add($"{literal} WITH … {main.ToUpperInvariant()}");
			}
		}
		Assert.IsEmpty(hits, "WITH 句の後ろに更新文を置く形は MariaDB で構文エラーです。INSERT INTO t (列…) WITH … SELECT の順に書いてください:\n" + string.Join("\n", hits));
	}

	/// <summary>
	/// 回帰テスト: FROM / JOIN の派生表（括弧内の SELECT）に別名を付けていること。
	/// SQLite / PostgreSQL 16 は省略できるが、MariaDB は「Every derived table must have its own alias」で失敗する。
	/// </summary>
	[TestMethod]
	public void 派生表に別名を付けている() {
		var corpus = SqlCorpus.LoadWithLocation("CvWpfclient", "CvBase", "CvDomainLogic", "CvServer")
			.Where(x => !x.File.Contains("/Sql/", StringComparison.Ordinal))
			.ToList();
		var hits = new List<string>();
		foreach (var literal in corpus) {
			foreach (var after in UnaliasedDerivedTables(literal.Sql))
				hits.Add($"{literal} 派生表の後ろ: `{after}`");
		}
		Assert.IsEmpty(hits, "派生表に別名がありません（MariaDB で失敗します）。`) AS 別名` を付けてください:\n" + string.Join("\n", hits));
	}

	/// <summary>
	/// 回帰テスト: UPDATE の行値一括代入 <c>SET (a, b) = (SELECT …)</c> を使っていないこと（MariaDB に無い）。
	/// UpdateDb の移行SQLは DB 別の移行を別途扱う方針のため対象外。
	/// </summary>
	[TestMethod]
	public void UPDATEで行値の一括代入を使っていない() {
		var hits = SqlCorpus.LoadWithLocation("CvWpfclient", "CvBase", "CvDomainLogic", "CvServer")
			.Where(x => !x.File.Contains("/Sql/", StringComparison.Ordinal) && !x.File.EndsWith("CvBase/UpdateDb.cs", StringComparison.Ordinal))
			.Where(x => Regex.IsMatch(x.Sql, @"\bset\s*\(\s*\w+\s*,[^)]*\)\s*=\s*\(", RegexOptions.IgnoreCase))
			.Select(x => x.ToString())
			.ToList();
		Assert.IsEmpty(hits, "SET (列, 列) = (SELECT …) は MariaDB で失敗します。列ごとに SET 列 = (SELECT …) と書いてください:\n" + string.Join("\n", hits));
	}

	/// <summary>別名の代わりに来たら「別名なし」とみなす語</summary>
	static readonly HashSet<string> _clauseWords = new(StringComparer.OrdinalIgnoreCase) {
		"where", "group", "order", "union", "limit", "having", "join", "left", "inner", "cross", "on", "except", "intersect", "window",
	};

	/// <summary>FROM / JOIN 直後の括弧が SELECT / WITH で始まり、閉じ括弧の後ろに別名が無い箇所（後ろの語）を返す</summary>
	static IEnumerable<string> UnaliasedDerivedTables(string sql) {
		foreach (Match m in Regex.Matches(sql, @"\b(from|join)\s*\(", RegexOptions.IgnoreCase)) {
			var open = m.Index + m.Length - 1;
			var inner = Regex.Replace(sql[(open + 1)..Math.Min(sql.Length, open + 400)], @"--[^\n]*", " ").TrimStart();
			if (!Regex.IsMatch(inner, @"^(select|with)\b", RegexOptions.IgnoreCase))
				continue;
			var close = MatchingParen(sql, open);
			if (close < 0)
				continue;
			var rest = Regex.Replace(sql[(close + 1)..], @"^(\s|--[^\n]*)*", "");
			var word = Regex.Match(rest, @"^\w+").Value;
			if (rest.Length == 0 || rest[0] is ')' or ';' or ',' || (word.Length > 0 && _clauseWords.Contains(word)))
				yield return rest.Length == 0 ? "(文末)" : rest[..Math.Min(20, rest.Length)].ReplaceLineEndings(" ");
		}
	}

	/// <summary>位置 open の ( に対応する ) の位置。文字列リテラル・行コメントは読み飛ばす</summary>
	static int MatchingParen(string sql, int open) {
		var depth = 0;
		for (var k = open; k < sql.Length; k++) {
			var c = sql[k];
			if (c == '\'') {
				k = sql.IndexOf('\'', k + 1);
				if (k < 0) return -1;
			}
			else if (c == '-' && k + 1 < sql.Length && sql[k + 1] == '-') {
				k = sql.IndexOf('\n', k);
				if (k < 0) return -1;
			}
			else if (c == '(') depth++;
			else if (c == ')' && --depth == 0) return k;
		}
		return -1;
	}

	/// <summary>
	/// 文が WITH で始まるとき、共通表式の並びの後ろにある主文のキーワード（小文字）を返す。WITH で始まらなければ null。
	/// 括弧の深さ0にある語だけを見る（共通表式の本体は括弧内）。文字列・コメントは読み飛ばす。
	/// </summary>
	static string? MainKeywordAfterWith(string statement) {
		var words = new List<string>();
		var depth = 0;
		for (var k = 0; k < statement.Length; k++) {
			var c = statement[k];
			if (c == '\'') {
				k = statement.IndexOf('\'', k + 1);
				if (k < 0) return null;
				continue;
			}
			if (c == '-' && k + 1 < statement.Length && statement[k + 1] == '-') {
				k = statement.IndexOf('\n', k);
				if (k < 0) break;
				continue;
			}
			if (c == '(') { depth++; continue; }
			if (c == ')') { depth--; continue; }
			if (depth != 0 || !char.IsLetter(c))
				continue;
			var start = k;
			while (k < statement.Length && (char.IsLetterOrDigit(statement[k]) || statement[k] == '_')) k++;
			var word = statement[start..k].ToLowerInvariant();
			k--;
			if (words.Count == 0 && word != "with")
				return null;
			words.Add(word);
			if (words.Count > 1 && word is "select" or "insert" or "update" or "delete" or "replace" or "values")
				return word;
		}
		return null;
	}

	/// <summary>変換で文字列が変わる、または変換しきれない構文が残る SQL か</summary>
	static bool NeedsTranslation(string sql) =>
		InsertOrConflict().IsMatch(sql) ||
		!string.Equals(SqlDialects.Postgre.Translate(sql), sql, StringComparison.Ordinal)
		|| !string.Equals(SqlDialects.Maria.Translate(sql), sql, StringComparison.Ordinal)
		|| SqlDialects.Postgre.Inspect(sql).Count > 0
		|| SqlDialects.Maria.Inspect(sql).Count > 0;

	/// <summary>
	/// 呼出しの SQL 引数を取り出す。リテラルならその中身、識別子なら同じファイル内で直前にその識別子へ代入したリテラル。
	/// 追えなければ null。
	/// </summary>
	static string? ResolveSqlArgument(string source, int argStart, string method) {
		var i = SkipSpace(source, argStart);
		// Fetch(Type, sql) / Fetch(typeof(T), sql) の形は第2引数が SQL
		if (method is "Fetch" or "FetchAsync" or "FirstOrDefault" or "SingleOrDefault" or "Query" or "Page") {
			var comma = FirstTopLevelComma(source, i);
			var first = comma > 0 ? source[i..comma].Trim() : string.Empty;
			if (first.StartsWith("typeof(", StringComparison.Ordinal) || Regex.IsMatch(first, @"^\w+(\.\w+)*Type$") || first == "itemType" || first == "type")
				i = SkipSpace(source, comma + 1);
		}
		if (TryReadConcatenation(source, i, out var literal))
			return literal;
		var id = Regex.Match(source[i..Math.Min(source.Length, i + 80)], @"^[A-Za-z_]\w*(?=\s*[,)])");
		if (!id.Success)
			return null;
		// 直前の代入 `id = <リテラル>`（var / string 宣言を含む）を後ろから探す。
		// その後の `id += <リテラル>` も連結する（追えない式が混ざったらそこまでの分で判定する）
		var prefix = source[..i];
		var assignments = Regex.Matches(prefix, $@"\b{Regex.Escape(id.Value)}\s*=\s*(?=[\$@""])");
		if (assignments.Count == 0)
			return null;
		var at = assignments[^1].Index + assignments[^1].Length;
		if (!TryReadConcatenation(source, at, out var assigned))
			return null;
		var sb = new StringBuilder(assigned);
		foreach (Match append in Regex.Matches(prefix[at..], $@"\b{Regex.Escape(id.Value)}\s*\+=\s*(?=[\$@""])")) {
			if (TryReadConcatenation(source, at + append.Index + append.Length, out var more))
				sb.Append(more);
		}
		return sb.ToString();
	}

	/// <summary>位置 i から `"…" + $"…" + …` のように + で続くリテラルを連結して読む（リテラル以外の項で止まる）</summary>
	static bool TryReadConcatenation(string s, int i, out string literal) {
		if (!TryReadLiteral(s, i, out literal, out var end))
			return false;
		var sb = new StringBuilder(literal);
		while (true) {
			var plus = SkipSpace(s, end);
			if (plus >= s.Length || s[plus] != '+' || (plus + 1 < s.Length && s[plus + 1] == '='))
				break;
			if (!TryReadLiteral(s, SkipSpace(s, plus + 1), out var next, out end))
				break;
			sb.Append(next);
		}
		literal = sb.ToString();
		return true;
	}

	static int SkipSpace(string s, int i) {
		while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
		return i;
	}

	static int FirstTopLevelComma(string s, int i) {
		var depth = 0;
		for (; i < s.Length; i++) {
			var c = s[i];
			if (c is '(' or '[' or '{' or '<') depth++;
			else if (c is ')' or ']' or '}' or '>') { if (depth == 0) return -1; depth--; }
			else if (c == ',' && depth == 0) return i;
			else if (c == '"') return -1; // 文字列が先に来たら第1引数がSQL
		}
		return -1;
	}

	/// <summary>補間穴の置き換え文字（中身は追えないため、SQL として無害な識別子1語にする）</summary>
	const string HolePlaceholder = "x";

	/// <summary>位置 i から始まる C# の文字列リテラル（"…" / @"…" / $"…" / $@"…" / """…"""）を読む</summary>
	static bool TryReadLiteral(string s, int i, out string literal) => TryReadLiteral(s, i, out literal, out _);

	/// <summary>
	/// 位置 i から始まる C# の文字列リテラルを読む。<paramref name="end"/> は閉じ引用符の直後の位置。
	/// 補間文字列の穴 <c>{…}</c> は中の文字列・入れ子の括弧ごと読み飛ばし、<see cref="HolePlaceholder"/> に置き換える
	/// （穴の中の <c>"</c> を文字列の終わりと誤認しないため）。<c>{{</c> / <c>}}</c> は波括弧1文字とする。
	/// </summary>
	static bool TryReadLiteral(string s, int i, out string literal, out int end) {
		literal = string.Empty;
		end = i;
		var j = i;
		var verbatim = false;
		var dollars = 0;
		while (j < s.Length && (s[j] == '$' || s[j] == '@')) { if (s[j] == '@') verbatim = true; else dollars++; j++; }
		if (j >= s.Length || s[j] != '"')
			return false;
		var sb = new StringBuilder();
		if (j + 2 < s.Length && s[j + 1] == '"' && s[j + 2] == '"') {
			// raw 文字列。$ の個数だけ { が続くと補間穴
			var quotes = 3;
			while (j + quotes < s.Length && s[j + quotes] == '"') quotes++;
			var closer = new string('"', quotes);
			for (var k = j + quotes; k < s.Length; k++) {
				if (string.CompareOrdinal(s, k, closer, 0, quotes) == 0) {
					literal = sb.ToString();
					end = k + quotes;
					return true;
				}
				if (dollars > 0 && IsHoleStart(s, k, dollars)) {
					k = SkipHole(s, k + dollars);
					if (k < 0) return false;
					sb.Append(HolePlaceholder);
					continue;
				}
				sb.Append(s[k]);
			}
			return false;
		}
		for (var k = j + 1; k < s.Length; k++) {
			var c = s[k];
			if (dollars > 0 && (c == '{' || c == '}')) {
				if (k + 1 < s.Length && s[k + 1] == c) { sb.Append(c); k++; continue; }
				if (c == '{') {
					k = SkipHole(s, k + 1);
					if (k < 0) return false;
					sb.Append(HolePlaceholder);
					continue;
				}
			}
			if (verbatim) {
				if (c == '"') {
					if (k + 1 < s.Length && s[k + 1] == '"') { sb.Append('"'); k++; continue; }
					literal = sb.ToString();
					end = k + 1;
					return true;
				}
			}
			else {
				if (c == '\\' && k + 1 < s.Length) { sb.Append(s[k + 1]); k++; continue; }
				if (c == '"') { literal = sb.ToString(); end = k + 1; return true; }
				if (c == '\n') return false;
			}
			sb.Append(c);
		}
		return false;
	}

	/// <summary>raw 文字列で位置 k から <paramref name="dollars"/> 個の { が並ぶ（=補間穴の開始）か</summary>
	static bool IsHoleStart(string s, int k, int dollars) {
		if (k + dollars > s.Length) return false;
		for (var n = 0; n < dollars; n++)
			if (s[k + n] != '{') return false;
		return k + dollars >= s.Length || s[k + dollars] != '{';
	}

	/// <summary>補間穴の中身（開始 { の直後から）を読み飛ばし、閉じ } の位置を返す。穴の中の文字列・文字・括弧を考慮する</summary>
	static int SkipHole(string s, int k) {
		var depth = 0;
		for (; k < s.Length; k++) {
			var c = s[k];
			if (c is '(' or '[' or '{') depth++;
			else if (c is ')' or ']') depth--;
			else if (c == '}') {
				if (depth == 0) return k;
				depth--;
			}
			else if (c == '\'') {
				// 文字リテラル 'x' / '\''
				k++;
				if (k < s.Length && s[k] == '\\') k++;
				k++;
			}
			else if (c == '"' || c == '$' || c == '@') {
				if (!TryReadLiteral(s, k, out _, out var after)) {
					if (c == '"') return -1;
					continue;
				}
				k = after - 1;
			}
		}
		return -1;
	}

	static IEnumerable<(string Sql, int Offset)> Literals(string source) {
		foreach (var literal in SqlCorpus.ExtractSqlLiteralsWithOffsetPublic(source))
			yield return literal;
		foreach (Match m in RegularString().Matches(source)) {
			var text = m.Groups[1].Value;
			var lower = text.ToLowerInvariant();
			if (text.Length >= 20 && (lower.Contains("select ") || lower.Contains("update ") || lower.Contains("delete from") || lower.Contains("insert into") || lower.Contains("where ")))
				yield return (text, m.Index);
		}
	}

	/// <summary>リテラルの後ろ（最大60行）で最初に現れるDB呼出しを分類する</summary>
	static string FirstCallAfter(string source, int offset) {
		var end = offset;
		for (var lines = 0; end < source.Length && lines < 60; end++) {
			if (source[end] == '\n') lines++;
		}
		var window = source[offset..end];
		var translated = TranslatedCall().Match(window);
		var raw = RawCall().Match(window);
		if (translated.Success && (!raw.Success || translated.Index <= raw.Index))
			return $"変換あり({translated.Groups[1].Value})";
		if (raw.Success)
			return $"変換なし({raw.Groups[1].Value})";
		return "不明(別メソッドで実行)";
	}
}
