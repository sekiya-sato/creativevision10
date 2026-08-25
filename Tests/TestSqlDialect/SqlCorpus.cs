/*
# description
SqlCorpus は CvWpfclient のソースから SQL の文字列リテラルを収集します。

方言変換を入れても現行SQLiteが壊れないことを機械的に示すために、実際に使われている
SQLをそのままテスト入力にします。収集対象は複数行SQLで使われている
verbatim文字列(`@"..."` / `$@"..."`) と raw文字列(`"""..."""` / `$"""..."""`) です。

リテラルの厳密なC#パースはしません。テスト入力を集めるのが目的なので、
取り違えた断片が混ざっても「字句列の連結が入力に一致する」という検証には影響しません。

# example
var corpus = SqlCorpus.Load();   // SQLキーワードを含むリテラルの一覧
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestSqlDialect;

/// <summary>CvWpfclient のソースから収集したSQLリテラル</summary>
public static class SqlCorpus {

	/// <summary>SQLらしさの判定に使うキーワード</summary>
	static readonly string[] _sqlMarkers = ["select ", "from ", "where ", "insert into", "update ", "delete from", "with recursive"];

	/// <summary>リポジトリのルート（creativevision10.slnx がある階層）を探す。</summary>
	public static string FindRepositoryRoot() {
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null) {
			if (File.Exists(Path.Combine(dir.FullName, "creativevision10.slnx")))
				return dir.FullName;
			dir = dir.Parent;
		}
		throw new InvalidOperationException("creativevision10.slnx が見つかりません。テストの実行位置を確認してください。");
	}

	/// <summary>CvWpfclient と CvBase から SQL リテラルを収集する。</summary>
	public static List<string> Load() {
		var root = FindRepositoryRoot();
		var literals = new List<string>();
		foreach (var directory in new[] { "CvWpfclient", "CvBase", "CvDomainLogic" }) {
			var path = Path.Combine(root, directory);
			if (!Directory.Exists(path))
				continue;
			foreach (var file in EnumerateSourceFiles(path))
				literals.AddRange(ExtractSqlLiterals(File.ReadAllText(file)));
		}
		return literals;
	}

	/// <summary>ソースファイル全文を収集する。字句解析の耐久試験に使う。</summary>
	public static List<string> LoadSourceTexts() {
		var root = FindRepositoryRoot();
		var texts = new List<string>();
		foreach (var directory in new[] { "CvWpfclient", "CvBase", "CvDomainLogic", "CvServer" }) {
			var path = Path.Combine(root, directory);
			if (!Directory.Exists(path))
				continue;
			foreach (var file in EnumerateSourceFiles(path))
				texts.Add(File.ReadAllText(file));
		}
		return texts;
	}

	static IEnumerable<string> EnumerateSourceFiles(string root) =>
		Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
				&& !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

	/// <summary>ソース1本からSQLらしいリテラルを取り出す。</summary>
	public static List<string> ExtractSqlLiterals(string source) {
		var result = new List<string>();
		foreach (var literal in ExtractRawStrings(source).Concat(ExtractVerbatimStrings(source))) {
			if (IsSqlLike(literal))
				result.Add(literal);
		}
		return result;
	}

	static bool IsSqlLike(string text) {
		if (text.Length < 20)
			return false;
		var lower = text.ToLowerInvariant();
		return _sqlMarkers.Count(m => lower.Contains(m, StringComparison.Ordinal)) >= 2;
	}

	/// <summary>raw文字列 """...""" / $"""...""" の中身を取り出す。</summary>
	static IEnumerable<string> ExtractRawStrings(string source) {
		var i = 0;
		while (true) {
			var open = source.IndexOf("\"\"\"", i, StringComparison.Ordinal);
			if (open < 0)
				yield break;
			var contentStart = open + 3;
			var close = source.IndexOf("\"\"\"", contentStart, StringComparison.Ordinal);
			if (close < 0)
				yield break;
			yield return source[contentStart..close];
			i = close + 3;
		}
	}

	/// <summary>verbatim文字列 @"..." / $@"..." の中身を取り出す（"" は " へ戻す）。</summary>
	static IEnumerable<string> ExtractVerbatimStrings(string source) {
		var i = 0;
		while (i < source.Length) {
			var at = source.IndexOf("@\"", i, StringComparison.Ordinal);
			if (at < 0)
				yield break;
			// raw文字列の """ を誤って拾わないようにする
			var contentStart = at + 2;
			var j = contentStart;
			var found = -1;
			while (j < source.Length) {
				if (source[j] != '"') {
					j++;
					continue;
				}
				if (j + 1 < source.Length && source[j + 1] == '"') {
					j += 2;
					continue;
				}
				found = j;
				break;
			}
			if (found < 0)
				yield break;
			yield return source[contentStart..found].Replace("\"\"", "\"", StringComparison.Ordinal);
			i = found + 1;
		}
	}
}
