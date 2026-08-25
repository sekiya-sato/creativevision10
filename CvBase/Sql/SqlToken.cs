/*
# description
SqlToken は SqlTokenizer が返す字句1個分の情報です。

方言変換は SQL を構文解析せず、この字句列の上だけで行います。
構文木を作らないのは「変換器が理解できない SQL を壊さない」ためで、
理解できた形だけを差し替え、それ以外は元の字句をそのまま出力します。

# example
var tokens = SqlTokenizer.Tokenize("select ifnull(a,'') from T");
var text = string.Concat(tokens.Select(t => t.Text)); // 入力と完全一致する
 */
namespace CvBase.Sql;

/// <summary>字句の種類</summary>
public enum SqlTokenKind {
	/// <summary>空白・改行</summary>
	Whitespace,
	/// <summary><c>-- ...</c> 行コメント（改行は含まない）</summary>
	LineComment,
	/// <summary><c>/* ... */</c> ブロックコメント</summary>
	BlockComment,
	/// <summary><c>'...'</c> 文字列リテラル</summary>
	StringLiteral,
	/// <summary><c>"..."</c> / <c>`...`</c> / <c>[...]</c> の引用識別子</summary>
	QuotedIdent,
	/// <summary>数値リテラル</summary>
	Number,
	/// <summary>識別子・キーワード・関数名</summary>
	Word,
	/// <summary><c>@0</c> / <c>@name</c> / <c>:name</c> / <c>?</c> のパラメータ</summary>
	Parameter,
	/// <summary>演算子・記号</summary>
	Operator,
	/// <summary>
	/// 変換ルールが差し込んだ生成済み断片。
	/// 二重変換を防ぐため、ルールはこの字句を照合対象にしない。
	/// </summary>
	Raw,
}

/// <summary>
/// SQL の字句1個。<see cref="Start"/> は入力文字列内の開始位置。
/// </summary>
/// <param name="Kind">字句の種類</param>
/// <param name="Text">字句の文字列（入力からの切り出しそのまま）</param>
/// <param name="Start">入力文字列内の開始位置</param>
public readonly record struct SqlToken(SqlTokenKind Kind, string Text, int Start) {
	/// <summary>変換対象になり得る字句か。空白とコメントは対象外。</summary>
	public bool IsCode => Kind is not (SqlTokenKind.Whitespace or SqlTokenKind.LineComment or SqlTokenKind.BlockComment);

	/// <summary>語（キーワード・関数名）を大文字小文字を無視して比較する。</summary>
	public bool IsWord(string word) =>
		Kind == SqlTokenKind.Word && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);

	/// <summary>記号を比較する。</summary>
	public bool IsOperator(string op) =>
		Kind == SqlTokenKind.Operator && Text == op;

	public override string ToString() => $"{Kind}({Text})@{Start}";
}
