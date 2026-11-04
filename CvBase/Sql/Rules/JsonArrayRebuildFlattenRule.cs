/*
# description
JsonArrayRebuildFlattenRule は「JSON配列の要素を差し替えて作り直す」問い合わせを、
MariaDB で実行できる形へ組み替えます（MariaDB 専用の文全体の書換え。B04 系）。

SQLite（正典）の形:
  select json_group_array(json(X.value2))
    from ( select J.key, <式> as value2
             from json_each(<配列>) as J [<結合>]
            order by cast(J.key as integer) ) as X

MariaDB では次の2点で実行できません。
- 派生表 X の中の JSON_TABLE が外側の UPDATE 対象（S.Jsub など）を参照できない（LATERAL が無い）。
- CAST(x AS JSON) が無い（MySQL にはある）。

そこで派生表を外し、順序は集約関数の ORDER BY で保証する形へ組み替えます。語彙は SQLite のまま返し、
json_each → JSON_TABLE、J.key → J.jkey、json → JSON_COMPACT、json_group_array → JSON_ARRAYAGG は
後続の字句ルールに任せます。

  select json_group_array(json(<式>) order by J.key)
    from json_each(<配列>) as J [<結合>]

形が一致しない問い合わせは変換しません（後続の字句ルールだけが働く）。
SQLite・PostgreSQL の変換経路には登録しません。

# example
MariaDB: 上記の SQLite 形 -> JSON_ARRAYAGG(JSON_COMPACT(<式>) ORDER BY J.jkey) from JSON_TABLE(...) AS J
 */
using System.Text.RegularExpressions;

namespace CvBase.Sql.Rules;

/// <summary>MariaDB: JSON配列再構築の派生表を外す（文全体の書換え）</summary>
public static partial class JsonArrayRebuildFlattenRule {

	/// <summary>
	/// 括弧の釣り合いを取りながら json_each( ... ) の引数全体を取る（CASE WHEN json_valid(..) ... END も含む）。
	/// 別名は大小文字・名前を問わず、派生表の別名(x)と json_each の別名(j)の対応を後方参照で確かめる。
	/// </summary>
	[GeneratedRegex("""
		json_group_array\(\s*json\(\s*(?<x>\w+)\.value2\s*\)\s*\)
		\s+from\s*\(\s*select\s+(?<j>\w+)\.key\s*,\s*
		(?<expr>.+?)
		\s+as\s+value2\s+from\s+json_each
		(?<src>\((?>[^()]+|\((?<d>)|\)(?<-d>))*(?(d)(?!))\))
		\s+as\s+\k<j>\b
		(?<joins>.*?)
		\s*order\s+by\s+cast\(\s*\k<j>\.key\s+as\s+integer\s*\)\s*\)\s*as\s+\k<x>\b
		""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace)]
	private static partial Regex Pattern();

	/// <summary>一致する箇所をすべて組み替える。一致しなければ引数の参照をそのまま返す。</summary>
	public static string Apply(string sql) {
		if (sql.IndexOf("json_group_array", StringComparison.OrdinalIgnoreCase) < 0)
			return sql;
		var pattern = Pattern();
		if (!pattern.IsMatch(sql))
			return sql;
		return pattern.Replace(sql, m =>
			$"json_group_array(json({m.Groups["expr"].Value}) order by {m.Groups["j"].Value}.key) " +
			$"from json_each{m.Groups["src"].Value} as {m.Groups["j"].Value}{m.Groups["joins"].Value}");
	}
}
