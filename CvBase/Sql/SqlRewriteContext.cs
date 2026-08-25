/*
# description
SqlRewriteContext は変換ルールが字句列を編集するための作業領域です。

ルールは字句の位置を指定して差し替えます。差し替えた断片は SqlTokenKind.Raw に
なるため、他のルールが再照合して二重変換することがありません。

一度も差し替えが起きなければ <see cref="Mutated"/> は false のままで、
呼び出し側は元のSQL文字列をそのまま返せます（同一参照が保たれる）。

# example
var ctx = new SqlRewriteContext(sql);
// ifnull( → coalesce(
ctx.ReplaceRange(index, 1, "coalesce");
var result = ctx.Mutated ? ctx.Render() : sql;
 */
namespace CvBase.Sql;

/// <summary>変換ルールが字句列を編集する作業領域</summary>
public sealed class SqlRewriteContext {

	readonly List<SqlToken> _tokens;

	public SqlRewriteContext(string sql) {
		Source = sql;
		_tokens = SqlTokenizer.Tokenize(sql);
		OrdinalityAliases = ScanOrdinalityAliases(_tokens);
	}

	/// <summary>変換前のSQL</summary>
	public string Source { get; }

	/// <summary>
	/// <c>json_each(...) AS J</c> の別名のうち、SQL内で <c>J.key</c> を参照しているもの。
	/// <para>
	/// SQLiteの <c>json_each</c> は要素の並び順を <c>key</c> 列で返す。他DBでは行番号列
	/// (PostgreSQL は <c>WITH ORDINALITY</c>、MariaDB は <c>FOR ORDINALITY</c>) を作る必要があるが、
	/// 使っていない場合に付けるのは無駄なので、参照している別名だけを対象にする。
	/// 走査は右から左に進むため <c>J.key</c> の位置に来た時点では FROM 句をまだ見ていない。
	/// そのため別名の収集だけを事前に1回行う。
	/// </para>
	/// </summary>
	public IReadOnlySet<string> OrdinalityAliases { get; }

	/// <summary>
	/// 他DBでの行番号列の名前。SQLiteの <c>key</c> は MariaDB の予約語なので別名にする。
	/// </summary>
	public const string OrdinalityColumn = "jkey";

	/// <summary>
	/// <c>json_each(...) [AS] 別名</c> を左から探し、<c>別名.key</c> を参照しているものを集める。
	/// </summary>
	static HashSet<string> ScanOrdinalityAliases(List<SqlToken> tokens) {
		var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < tokens.Count; i++) {
			if (tokens[i].Kind != SqlTokenKind.Word)
				continue;
			// json_each( ... ) [as] alias
			if (tokens[i].IsWord("json_each")) {
				var alias = ReadAliasAfterCall(tokens, i);
				if (alias != null)
					candidates.Add(alias);
				continue;
			}
			// alias . key
			if (!tokens[i].IsWord("key"))
				continue;
			var dot = PrevCodeIndex(tokens, i);
			if (dot < 0 || !tokens[dot].IsOperator("."))
				continue;
			var owner = PrevCodeIndex(tokens, dot);
			if (owner >= 0 && tokens[owner].Kind == SqlTokenKind.Word)
				aliases.Add(tokens[owner].Text);
		}
		aliases.IntersectWith(candidates);
		return aliases;
	}

	/// <summary>関数呼び出しの閉じ括弧の後ろにある別名を読む。見つからなければ null。</summary>
	static string? ReadAliasAfterCall(List<SqlToken> tokens, int wordIndex) {
		var open = NextCodeIndex(tokens, wordIndex);
		if (open < 0 || !tokens[open].IsOperator("("))
			return null;
		var depth = 0;
		var close = -1;
		for (var i = open; i < tokens.Count; i++) {
			if (tokens[i].IsOperator("("))
				depth++;
			else if (tokens[i].IsOperator(")")) {
				depth--;
				if (depth == 0) {
					close = i;
					break;
				}
			}
		}
		if (close < 0)
			return null;
		var next = NextCodeIndex(tokens, close);
		if (next < 0)
			return null;
		if (tokens[next].IsWord("as"))
			next = NextCodeIndex(tokens, next);
		return next >= 0 && tokens[next].Kind == SqlTokenKind.Word ? tokens[next].Text : null;
	}

	static int NextCodeIndex(List<SqlToken> tokens, int index) {
		for (var i = index + 1; i < tokens.Count; i++) {
			if (tokens[i].IsCode)
				return i;
		}
		return -1;
	}

	static int PrevCodeIndex(List<SqlToken> tokens, int index) {
		for (var i = index - 1; i >= 0; i--) {
			if (tokens[i].IsCode)
				return i;
		}
		return -1;
	}

	/// <summary>字句列。ルールは読み取りにこれを使う。</summary>
	public IReadOnlyList<SqlToken> Tokens => _tokens;

	/// <summary>一度でも差し替えが起きたか</summary>
	public bool Mutated { get; private set; }

	/// <summary>字句列を文字列へ戻す。</summary>
	public string Render() => SqlTokenizer.Render(_tokens);

	/// <summary>
	/// <paramref name="start"/> から <paramref name="count"/> 個の字句を生成済み断片へ差し替える。
	/// 差し替え後の字句数は1個になるため、ルールは差し替え位置より後ろへ進めて走査を続ける。
	/// </summary>
	public void ReplaceRange(int start, int count, string text) {
		ArgumentOutOfRangeException.ThrowIfNegative(start);
		ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
		if (start + count > _tokens.Count)
			throw new ArgumentOutOfRangeException(nameof(count), "字句列の範囲を超えています。");

		var position = _tokens[start].Start;
		_tokens.RemoveRange(start, count);
		_tokens.Insert(start, new SqlToken(SqlTokenKind.Raw, text, position));
		Mutated = true;
	}

	/// <summary>1個の字句だけを差し替える。</summary>
	public void Replace(int index, string text) => ReplaceRange(index, 1, text);

	/// <summary>
	/// <paramref name="openIndex"/> の <c>(</c> に対応する <c>)</c> の位置を返す。見つからなければ -1。
	/// </summary>
	public int FindMatchingParen(int openIndex) {
		if (openIndex < 0 || openIndex >= _tokens.Count || !_tokens[openIndex].IsOperator("("))
			return -1;
		var depth = 0;
		for (var i = openIndex; i < _tokens.Count; i++) {
			if (_tokens[i].IsOperator("("))
				depth++;
			else if (_tokens[i].IsOperator(")")) {
				depth--;
				if (depth == 0)
					return i;
			}
		}
		return -1;
	}

	/// <summary>
	/// <paramref name="from"/> より後ろで最初の実コード字句の位置を返す。無ければ -1。
	/// 空白とコメントを読み飛ばす。
	/// </summary>
	public int NextCode(int from) {
		for (var i = from + 1; i < _tokens.Count; i++) {
			if (_tokens[i].IsCode)
				return i;
		}
		return -1;
	}

	/// <summary>
	/// <paramref name="from"/> より前で最後の実コード字句の位置を返す。無ければ -1。
	/// </summary>
	public int PrevCode(int from) {
		for (var i = from - 1; i >= 0; i--) {
			if (_tokens[i].IsCode)
				return i;
		}
		return -1;
	}

	/// <summary>
	/// <paramref name="start"/> から <paramref name="end"/> までの字句を連結した文字列を返す（両端を含む）。
	/// </summary>
	public string TextOf(int start, int end) {
		if (start < 0 || end < start || end >= _tokens.Count)
			return string.Empty;
		return string.Concat(_tokens.Skip(start).Take(end - start + 1).Select(t => t.Text));
	}

	/// <summary>
	/// 関数呼び出し <c>name(</c> の形かを判定し、<c>(</c> と対応する <c>)</c> の位置を返す。
	/// </summary>
	public bool TryMatchCall(int wordIndex, string functionName, out int openIndex, out int closeIndex) {
		openIndex = -1;
		closeIndex = -1;
		if (wordIndex < 0 || wordIndex >= _tokens.Count)
			return false;
		if (!_tokens[wordIndex].IsWord(functionName))
			return false;
		var open = NextCode(wordIndex);
		if (open < 0 || !_tokens[open].IsOperator("("))
			return false;
		var close = FindMatchingParen(open);
		if (close < 0)
			return false;
		openIndex = open;
		closeIndex = close;
		return true;
	}

	/// <summary>
	/// 括弧内をカンマで区切った引数ごとの字句範囲を返す。入れ子の括弧は分割しない。
	/// </summary>
	public List<(int Start, int End)> SplitArguments(int openIndex, int closeIndex) {
		var result = new List<(int, int)>();
		if (openIndex < 0 || closeIndex <= openIndex)
			return result;
		var depth = 0;
		var argStart = openIndex + 1;
		for (var i = openIndex + 1; i < closeIndex; i++) {
			if (_tokens[i].IsOperator("("))
				depth++;
			else if (_tokens[i].IsOperator(")"))
				depth--;
			else if (depth == 0 && _tokens[i].IsOperator(",")) {
				result.Add((argStart, i - 1));
				argStart = i + 1;
			}
		}
		if (argStart <= closeIndex - 1)
			result.Add((argStart, closeIndex - 1));
		return result;
	}
}
