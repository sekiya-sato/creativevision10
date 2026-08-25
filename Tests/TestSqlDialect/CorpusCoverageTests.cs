/*
# description
CorpusCoverageTests は、実際に使われているSQL全件のうち何割が各方言へ変換できるかを固定します。

方言変換の進捗を数字で残すためのテストです。閾値を下回ると失敗するので、
ルールを削ったり壊したりすると気づけます。逆にルールを増やして閾値を上回ったら、
閾値を上げて進捗を記録します。

残る2本は次のとおりで、いずれも実行時の欠落ではありません。
- `C02-SqliteMaster`: `ExDatabase` のメタデータ参照。プロバイダー側で override 済み。
- `B01-JsonExtract` / `B04-JsonSet`: JSONパスがC#の文字列補間穴 `'$.{property}'` のままの
  テンプレート。実行時には実際のプロパティ名へ置き換わるため変換できる。

CV10 1.0 は SQLite のみを扱うため、この数値は出荷判定には影響しません。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace TestSqlDialect;

[TestClass]
public sealed class CorpusCoverageTests {

	[TestInitialize]
	public void Setup() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		SqlDialectOptions.EnableNullsFirst = false;
	}

	/// <summary>収集SQLのうち未対応構文が残らない本数の下限（実測に合わせて更新する）</summary>
	const int PostgreSupportedMinimum = 178;
	const int MariaSupportedMinimum = 178;

	[TestMethod]
	public void PostgreSQLの変換率を保つ() {
		var (supported, total, remaining) = Measure(SqlDialects.Postgre);
		Assert.IsTrue(supported >= PostgreSupportedMinimum,
			$"PostgreSQL 変換可能 {supported}/{total} 本。残る構文: {remaining}");
	}

	[TestMethod]
	public void MariaDBの変換率を保つ() {
		var (supported, total, remaining) = Measure(SqlDialects.Maria);
		Assert.IsTrue(supported >= MariaSupportedMinimum,
			$"MariaDB 変換可能 {supported}/{total} 本。残る構文: {remaining}");
	}

	static (int Supported, int Total, string Remaining) Measure(ISqlDialect dialect) {
		var corpus = SqlCorpus.Load();
		var supported = 0;
		var remaining = new Dictionary<string, int>();
		foreach (var sql in corpus) {
			var findings = dialect.Inspect(sql);
			if (findings.Count == 0) {
				supported++;
				continue;
			}
			foreach (var id in findings.Select(f => f.Construct.Id).Distinct())
				remaining[id] = remaining.TryGetValue(id, out var count) ? count + 1 : 1;
		}
		var summary = string.Join(", ", remaining.OrderByDescending(x => x.Value).Select(x => $"{x.Key}={x.Value}"));
		return (supported, corpus.Count, summary);
	}
}
