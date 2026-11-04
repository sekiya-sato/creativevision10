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
