/*
# description
ISqlRewriteRule は SQLite 方言の1構文を対象DBの表現へ差し替えるルールです。

ルールは字句列を左から右へ1回走査する間に呼ばれ、自分が担当する形に一致したときだけ
差し替えます。一致しなければ何もしません。「変換できない形は変換しない」が原則で、
未対応の形は SqlDialectBase 側で検出して報告します。

# example
sealed class IfnullRule : ISqlRewriteRule {
    public string Id => "A01-Ifnull";
    public bool Apply(SqlRewriteContext ctx, int index) {
        if (!ctx.Tokens[index].IsWord("ifnull")) return false;
        ctx.Replace(index, "coalesce");
        return true;
    }
}
 */
namespace CvBase.Sql;

/// <summary>SQLite方言の1構文を対象DBの表現へ差し替えるルール</summary>
public interface ISqlRewriteRule {

	/// <summary>ルールID。設計書のルールIDと一致させる（例 A01-Ifnull）。</summary>
	string Id { get; }

	/// <summary>
	/// <paramref name="index"/> の字句から自分の担当する形が始まっていれば差し替えて true を返す。
	/// 一致しなければ何もせず false を返す。
	/// </summary>
	bool Apply(SqlRewriteContext context, int index);
}
