// SQL方言変換の実DB検証ツール（sqlreplay）
//
// 使い方:
//   sqlreplay <capture.jsonl> <sqlite.db> <出力フォルダ> [--only-maria|--only-pg]
//
// CvServer を環境変数 CV_SQL_CAPTURE 付きで SQLite で起動し、UAT（画面操作）で記録した JSONL を入力にする。
// 記録は SQLite 方言のSQL（変換前）。各DBの ExDatabase で変換して実行し、次を判定する。
//   OK        : 3DBとも成功し、結果（SELECTは行の内容、更新系は影響行数）が一致
//   DIFF      : 成功したが結果が SQLite と異なる
//   ERR-Maria / ERR-PG : その DB で失敗（文法・型・関数など）
//   ERR-SQLite: 基準の SQLite で失敗（記録側の問題。比較対象外）
// 更新系（server:ExecuteDialect）はトランザクション内で実行して必ずロールバックする。
// MariaDB / PostgreSQL の接続先は CvServer/appsettings.Development.json の MariaDb / Postgres。
using CvAsset;
using CvBase;
using CvBase.Sql;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length < 3) {
	Console.Error.WriteLine("使い方: sqlreplay <capture.jsonl> <sqlite.db> <出力フォルダ> [--only-maria|--only-pg]");
	return 2;
}
var capturePath = args[0];
var sqlitePath = args[1];
var outDir = args[2];
Directory.CreateDirectory(outDir);
SqlDialectOptions.Mode = SqlDialectMode.Strict;

var repoRoot = FindRepoRoot();
var settings = File.ReadAllText(Path.Combine(repoRoot, "CvServer", "appsettings.Development.json"));
string Conn(string key) => Regex.Match(settings, $"\"{key}\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value;

// 記録の読込（同一 SQL+引数+経路 は1件にまとめる）
var records = new List<Capture>();
var seen = new HashSet<string>();
foreach (var line in File.ReadLines(capturePath)) {
	if (string.IsNullOrWhiteSpace(line)) continue;
	var c = JsonSerializer.Deserialize<Capture>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
	if (c == null) continue;
	var key = c.Origin + "\u0001" + c.Sql + "\u0001" + string.Join("\u0002", c.Args.Select(a => a.T + ":" + a.V));
	if (seen.Add(key)) records.Add(c);
}
Console.WriteLine($"記録 {records.Count} 件（重複除去後）");

var dbs = new List<(string Name, ExDatabase Db)> { ("SQLite", CvBaseSqlite.ExDatabaseSqlite.GetDbConn(sqlitePath)) };
if (!args.Contains("--only-pg")) dbs.Add(("Maria", CvBaseMariadb.ExDatabaseMaria.GetDbConn(Conn("MariaDb"))));
if (!args.Contains("--only-maria")) dbs.Add(("PG", CvBasePostgre.ExDatabasePostgre.GetDbConn(Conn("Postgres"))));
foreach (var (_, db) in dbs) db.ChangeTimeout(300);

var results = new List<Result>();
var index = 0;
foreach (var c in records) {
	index++;
	var per = new Dictionary<string, Outcome>();
	foreach (var (name, db) in dbs) {
		var sw = System.Diagnostics.Stopwatch.StartNew();
		per[name] = Run(db, c) with { ElapsedMs = sw.ElapsedMilliseconds };
	}
	var baseline = per["SQLite"];
	string verdict;
	if (baseline.Error != null) verdict = "ERR-SQLite";
	else {
		var bad = per.Where(p => p.Key != "SQLite" && p.Value.Error != null).Select(p => "ERR-" + p.Key).ToList();
		var diff = per.Where(p => p.Key != "SQLite" && p.Value.Error == null && p.Value.Fingerprint != baseline.Fingerprint).Select(p => "DIFF-" + p.Key).ToList();
		verdict = bad.Count + diff.Count == 0 ? "OK" : string.Join(",", bad.Concat(diff));
	}
	results.Add(new Result(index, c, verdict, per));
	Console.WriteLine($"[{index}/{records.Count}] {verdict} {c.Origin} {Head(c.Sql, 70)}");
	Console.WriteLine("    " + string.Join(" | ", per.Select(p => $"{p.Key} {(p.Value.Error != null ? "ERR" : p.Value.Fingerprint)} {p.Value.ElapsedMs}ms")));
}

// レポート
var md = new StringBuilder();
md.AppendLine($"# SQL方言変換 実DB検証（{DateTime.Now:yyyy-MM-dd HH:mm}）");
md.AppendLine();
md.AppendLine($"- 記録: {capturePath}（重複除去後 {records.Count} 件）");
md.AppendLine($"- 基準: SQLite {sqlitePath}");
md.AppendLine();
md.AppendLine("| 判定 | 件数 |");
md.AppendLine("|---|---:|");
foreach (var g in results.GroupBy(r => r.Verdict).OrderBy(g => g.Key))
	md.AppendLine($"| {g.Key} | {g.Count()} |");
md.AppendLine();
md.AppendLine("| 経路 | 件数 | OK |");
md.AppendLine("|---|---:|---:|");
foreach (var g in results.GroupBy(r => r.Capture.Origin).OrderBy(g => g.Key))
	md.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(r => r.Verdict == "OK")} |");
md.AppendLine();
md.AppendLine("## OK以外");
foreach (var r in results.Where(r => r.Verdict != "OK")) {
	md.AppendLine();
	md.AppendLine($"### #{r.No} {r.Verdict} {r.Capture.Origin} {r.Capture.ItemType?.Split(',')[0]}");
	md.AppendLine("```sql");
	md.AppendLine(r.Capture.Sql.Trim());
	md.AppendLine("```");
	if (r.Capture.Args.Length > 0) md.AppendLine($"引数: {string.Join(", ", r.Capture.Args.Select(a => $"{a.T}:{a.V}"))}");
	foreach (var (name, o) in r.Per) {
		md.AppendLine($"- {name}: {(o.Error != null ? "エラー " + Head(o.Error, 300) : $"{o.Summary}")}");
	}
}
File.WriteAllText(Path.Combine(outDir, "report.md"), md.ToString());
Console.WriteLine($"レポート: {Path.Combine(outDir, "report.md")}");
Console.WriteLine(string.Join(" / ", results.GroupBy(r => r.Verdict).Select(g => $"{g.Key}={g.Count()}")));
foreach (var (_, db) in dbs) db.Dispose();
return results.All(r => r.Verdict is "OK" or "ERR-SQLite") ? 0 : 1;

// ---------------------------------------------------------------

static Outcome Run(ExDatabase db, Capture c) {
	var sql = db.TranslateDialect(c.Sql);
	var args = c.Args.Select(ToValue).ToArray();
	try {
		if (c.Origin == "server:ExecuteDialect") {
			db.BeginTransaction();
			try {
				var n = db.Execute(sql, args);
				return new Outcome(null, $"rows={n}", $"影響行数={n}");
			}
			finally {
				db.AbortTransaction();
			}
		}
		if (c.Origin == "print") {
			var rows = db.RawExecCmd(sql, args);
			if (!string.IsNullOrEmpty(db.RawLastError)) return new Outcome(db.RawLastError, "", "");
			var lines = rows.Select(r => string.Join("\t", r.Values.Select(Norm))).ToList();
			return Fingerprint(lines, sql);
		}
		var type = c.ItemType != null ? Type.GetType(c.ItemType) : null;
		IList list;
		if (type == null) {
			list = db.Fetch<Dictionary<string, object>>(sql, args);
		}
		else if (c.Origin.StartsWith("client:", StringComparison.Ordinal)) {
			list = db.Fetch(type, sql, args);
		}
		else {
			var fetch = typeof(NPoco.Database).GetMethods()
				.First(m => m.Name == "Fetch" && m.IsGenericMethodDefinition && m.GetParameters() is { Length: 2 } p && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(object[]))
				.MakeGenericMethod(type);
			list = (IList)fetch.Invoke(db, [sql, args])!;
		}
		var serialized = list.Cast<object?>().Select(o => o switch {
			null => "null",
			IDictionary d => string.Join("\t", d.Values.Cast<object?>().Select(Norm)),
			_ when o.GetType().IsPrimitive || o is string or decimal => Norm(o),
			_ => NormalizeJson(Common.SerializeObject(o), sql),
		}).ToList();
		return Fingerprint(serialized, sql);
	}
	catch (Exception ex) {
		var inner = ex is System.Reflection.TargetInvocationException { InnerException: { } ie } ? ie : ex;
		return new Outcome(inner.Message.Replace("\r", " ").Replace("\n", " "), "", "");
	}
}

/// <summary>ORDER BY が無ければ行の順序は比較しない（DBごとに既定の並びが異なるため）</summary>
static Outcome Fingerprint(List<string> rows, string sql) {
	var ordered = Regex.IsMatch(sql, @"\border\s+by\b", RegexOptions.IgnoreCase);
	var list = ordered ? rows : rows.OrderBy(r => r, StringComparer.Ordinal).ToList();
	var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", list))))[..16];
	var first = list.Count > 0 ? Head(list[0], 160) : "";
	return new Outcome(null, $"{list.Count}:{hash}", $"{list.Count}行 hash={hash} 先頭={first}");
}

static string Norm(object? v) => v switch {
	null or DBNull => "",
	bool b => b ? "1" : "0",
	decimal or double or float => Math.Round(Convert.ToDecimal(v, CultureInfo.InvariantCulture), 6).ToString("0.######", CultureInfo.InvariantCulture),
	byte[] bytes => Encoding.UTF8.GetString(bytes),
	IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
	_ => v.ToString() ?? "",
};

/// <summary>
/// JSON化したオブジェクトの数値表記の揺れ（1.0 と 1）を吸収する。
/// SQLが Vdc / Vdu を選択していない（* も無い）ときは、オブジェクト生成時の既定値（現在時刻）が入るので比較から外す。
/// </summary>
static string NormalizeJson(string json, string sql) {
	var normalized = Regex.Replace(json, @"(?<=[:\[,])(-?\d+)\.0+(?=[,\]\}])", "$1");
	if (!sql.Contains('*') && !Regex.IsMatch(sql, @"\bVd[cu]\b", RegexOptions.IgnoreCase))
		normalized = Regex.Replace(normalized, @"""Vd[cu]"":-?\d+,?", "");
	return normalized;
}

static object? ToValue(CaptureArg a) => a.T switch {
	"null" => null,
	"Int64" => long.Parse(a.V, CultureInfo.InvariantCulture),
	"Int32" => int.Parse(a.V, CultureInfo.InvariantCulture),
	"Int16" => short.Parse(a.V, CultureInfo.InvariantCulture),
	"Decimal" => decimal.Parse(a.V, CultureInfo.InvariantCulture),
	"Double" => double.Parse(a.V, CultureInfo.InvariantCulture),
	"Boolean" => bool.Parse(a.V),
	_ => a.V,
};

static string Head(string s, int n) {
	var t = Regex.Replace(s, @"\s+", " ").Trim();
	return t.Length <= n ? t : t[..n] + "…";
}

static string FindRepoRoot() {
	var dir = new DirectoryInfo(AppContext.BaseDirectory);
	while (dir != null && !File.Exists(Path.Combine(dir.FullName, "creativevision10.slnx"))) dir = dir.Parent;
	return dir?.FullName ?? throw new InvalidOperationException("creativevision10.slnx が見つかりません");
}

sealed record CaptureArg(string T, string V);
sealed record Capture(string Origin, string? ItemType, string Sql, CaptureArg[] Args);
sealed record Outcome(string? Error, string Fingerprint, string Summary) {
	public long ElapsedMs { get; init; }
}
sealed record Result(int No, Capture Capture, string Verdict, Dictionary<string, Outcome> Per);
