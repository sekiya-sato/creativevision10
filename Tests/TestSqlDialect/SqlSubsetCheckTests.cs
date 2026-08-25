/*
# description
SqlSubsetCheckTests は、CvWpfclient のSQLが方言変換の対象範囲に収まっているかを検査します（T4）。

CV10 は SQL の組み立てをクライアント側でも行い、View / ViewModel は今後も増えます。
新しい画面が変換器の対象外の構文を使うと、SQLiteでは動くのに PostgreSQL / MariaDB では
動かないSQLが静かに増えていきます。それを増える前に気づくための歯止めがこのテストです。

**SQLiteの動作は一切阻害しません。** 失敗しても止まるのはCIだけで、
指摘された構文をそのまま使い続ける選択もできます（許容一覧へ追記する）。

新しい未対応構文を使った場合は、次のどれかで対応します。
1. 変換ルールを追加する（`CvBase/Sql/Rules/`）。
2. 変換できる書き方に変える（SQLiteで結果が変わらない範囲で）。
3. `QueryKey` で方言別の手書きSQLへ差し替える（`SqlOverrideCatalog`）。
4. 意図的に他DB非対応とするなら、このテストの許容一覧へファイル名を追記する。
 */
using CvBase.Sql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;

namespace TestSqlDialect;

[TestClass]
public sealed class SqlSubsetCheckTests {

	/// <summary>
	/// 未対応構文を含んでいても失敗させないファイル。
	/// <para>
	/// 追記するときは理由をコメントで残す。安易に増やすと歯止めの意味が無くなる。
	/// </para>
	/// </summary>
	static readonly HashSet<string> _allowedFiles = new(System.StringComparer.OrdinalIgnoreCase) {
		// テンプレート側でJSONパスをC#の文字列補間穴 '$.{property}' のまま持つため、
		// ソース走査では単一階層パスと判定できない。実行時には実名へ置き換わり変換できる。
		"CvDomainLogic/MasterCascadeDb.cs",
		// ExDatabase のメタデータ参照(sqlite_master)。プロバイダー側で override 済み。
		"CvBase/ExDatabase.cs",
	};

	[TestInitialize]
	public void Setup() {
		SqlDialectOptions.Mode = SqlDialectMode.Auto;
		SqlDialectOptions.EnableNullsFirst = false;
	}

	[TestMethod]
	public void CvWpfclientのSQLは変換対象範囲に収まっている() {
		AssertWithinSubset("CvWpfclient");
	}

	[TestMethod]
	public void サーバ層のSQLも変換対象範囲に収まっている() {
		AssertWithinSubset("CvBase", "CvDomainLogic");
	}

	[TestMethod]
	public void 許容一覧は実在するファイルだけを指している() {
		// 対象ファイルが改名・削除されたら許容一覧も畳むため
		var files = SqlCorpus.LoadWithLocation("CvWpfclient", "CvBase", "CvDomainLogic")
			.Select(x => x.File)
			.ToHashSet(System.StringComparer.OrdinalIgnoreCase);
		var stale = _allowedFiles.Where(f => !files.Contains(f)).ToList();
		Assert.AreEqual(0, stale.Count, $"許容一覧に不要な項目があります: {string.Join(", ", stale)}");
	}

	static void AssertWithinSubset(params string[] directories) {
		var findings = new List<string>();
		foreach (var literal in SqlCorpus.LoadWithLocation(directories)) {
			if (_allowedFiles.Contains(literal.File))
				continue;
			foreach (var dialect in new[] { SqlDialects.Postgre, SqlDialects.Maria }) {
				var unsupported = dialect.Inspect(literal.Sql);
				if (unsupported.Count == 0)
					continue;
				var summary = string.Join(", ", unsupported
					.GroupBy(f => f.Construct.Id)
					.Select(g => $"{g.Key}×{g.Count()}"));
				findings.Add($"{literal.File}:{literal.Line} [{dialect.Name}] {summary}");
			}
		}
		Assert.AreEqual(0, findings.Count,
			"方言変換の対象外の構文が使われています。ルール追加・書き換え・QueryKey差し替え・許容一覧追記のいずれかで対応してください。"
			+ System.Environment.NewLine + string.Join(System.Environment.NewLine, findings.Distinct()));
	}
}
