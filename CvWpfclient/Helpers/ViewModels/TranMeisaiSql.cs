/*
# description
TranMeisaiSql は、伝票テーブルの明細JSON列 `Jmeisai` を SQLite の json_each で展開して
`Tran99Meisai` の各項目を取り出す SQL 断片を組み立てるヘルパーです。

帳票画面の多くが「伝票ヘッダ h を絞り込み、明細を1行ずつ展開して品番別に集計する」形になるため、
json_extract の入れ子と cast の括弧を手書きして崩すのを避ける目的で用意しています。

`json_valid()` ガードは AGENTS.md の規約（不正JSONに json_extract を当てると SQLite が
malformed JSON 例外を投げる）に従い、明細を展開する側で必ず付けます。

# example
var sql = $@"
select
    {TranMeisaiSql.Str("Code_Shohin")} shohinCode,
    sum({TranMeisaiSql.Num("Su")})     su
from Tran03Shiire h, {TranMeisaiSql.From}
where {TranMeisaiSql.Guard}
group by shohinCode";
 */
namespace CvWpfclient.Helpers;

/// <summary>
/// 伝票明細JSON(Jmeisai)展開用のSQL断片。エイリアスは h(ヘッダ) / m(明細) 固定。
/// </summary>
internal static class TranMeisaiSql {
	/// <summary>FROM句に置く明細展開。`from Tran03Shiire h, {From}` の形で使う。</summary>
	internal const string From = "json_each(h.Jmeisai) m";

	/// <summary>WHERE句に必須の不正JSONガード。</summary>
	internal const string Guard = "json_valid(h.Jmeisai)";

	/// <summary>明細の文字列項目。NULLは空文字にする。</summary>
	internal static string Str(string property) =>
		$"ifnull(json_extract(m.value,'$.{property}'),'')";

	/// <summary>明細の数値項目。NULLは0にする。</summary>
	internal static string Num(string property) =>
		$"cast(ifnull(json_extract(m.value,'$.{property}'),0) as integer)";

	/// <summary>ヘッダのV*列(CodeNameView)からコードを取り出す。</summary>
	internal static string HeaderCode(string vColumn) =>
		$"ifnull(json_extract(h.{vColumn},'$.Cd'),'')";

	/// <summary>ヘッダのV*列(CodeNameView)から名称を取り出す。</summary>
	internal static string HeaderName(string vColumn) =>
		$"ifnull(json_extract(h.{vColumn},'$.Mei'),'')";

	/// <summary>yyyyMMdd 8桁を yyyy/MM/dd 表示にする。</summary>
	internal static string DateLabel(string column) =>
		$"case when length({column})=8 then substr({column},1,4)||'/'||substr({column},5,2)||'/'||substr({column},7,2) else ifnull({column},'') end";

	/// <summary>
	/// 元帳のメモ欄。消込済(<c>EndFlag=1</c>)の伝票はメモの先頭へ <c>*</c> を出す。
	/// <para>
	/// 消込マークは専用列を作らずメモ欄へ同居させる。帳票定義(qfm)の列を増やさずに済ませるためで、
	/// メモが空でなければ <c>*</c> との間に半角空白を1つ入れる。メモが空なら <c>*</c> だけになる。
	/// 未消込の伝票と、EndFlag を持たない入金・支払・繰越の行はメモをそのまま出す。
	/// </para>
	/// </summary>
	internal static string MemoWithKesikomiMark(string endFlagColumn, string memoColumn) =>
		$"case when {endFlagColumn} = 1 then '*' || case when ifnull({memoColumn},'') = '' then '' else ' ' || {memoColumn} end"
		+ $" else ifnull({memoColumn},'') end";

	/// <summary>取引区分の数値をラベルにする case 式を作る。</summary>
	internal static string KubunLabel(string column, params (int Value, string Label)[] map) {
		var cases = string.Join(" ", map.Select(m => $"when {m.Value} then '{m.Label}'"));
		return $"case {column} {cases} else cast({column} as text) end";
	}
}
