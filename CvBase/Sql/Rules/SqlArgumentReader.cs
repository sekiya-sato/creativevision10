/*
# description
SqlArgumentReader は変換ルールが関数の引数を読むための小さな補助です。

方言変換は「対応できる形だけを変換し、それ以外は触らない」方針なので、
引数が期待どおりの形（単独の文字列リテラルなど）かを厳密に確かめる必要があります。
その判定をここへ集めています。

# example
var format = SqlArgumentReader.SingleStringLiteral(context, args[0]);  // '%Y%m' -> "%Y%m"
 */
namespace CvBase.Sql.Rules;

/// <summary>関数引数の読み取り</summary>
internal static class SqlArgumentReader {

	/// <summary>
	/// 引数が単独の文字列リテラルなら、引用符を外した中身を返す。それ以外は null。
	/// </summary>
	internal static string? SingleStringLiteral(SqlRewriteContext context, (int Start, int End) argument) {
		var index = SingleCodeToken(context, argument);
		if (index < 0)
			return null;
		var token = context.Tokens[index];
		if (token.Kind != SqlTokenKind.StringLiteral || token.Text.Length < 2)
			return null;
		return token.Text[1..^1].Replace("''", "'", StringComparison.Ordinal);
	}

	/// <summary>
	/// 引数が実コード字句1個だけならその位置を返す。複数字句なら -1。
	/// </summary>
	internal static int SingleCodeToken(SqlRewriteContext context, (int Start, int End) argument) {
		var found = -1;
		for (var i = argument.Start; i <= argument.End; i++) {
			if (!context.Tokens[i].IsCode)
				continue;
			if (found >= 0)
				return -1;
			found = i;
		}
		return found;
	}

	/// <summary>引数の字句を連結した文字列を返す（前後の空白は落とす）。</summary>
	internal static string Text(SqlRewriteContext context, (int Start, int End) argument) =>
		context.TextOf(argument.Start, argument.End).Trim();

	/// <summary>SQL文字列リテラルとして引用する。</summary>
	internal static string Quote(string value) =>
		$"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
