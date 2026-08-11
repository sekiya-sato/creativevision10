/*
# description
StockSql は在庫系帳票で繰り返し必要になる「SKU(商品×色×サイズ)と倉庫の名称解決」の
JOIN 句と列式を組み立てるヘルパーです。

在庫の集計テーブル(SummaryRealStock / SummaryStock)は Id_Soko / Id_Shohin / Id_Col / Id_Siz を
持つだけで名称を持たないため、商品マスタ・色サイズ派生テーブル・取引先マスタ(倉庫)を毎回 JOIN する
必要があります。色サイズ名は DerivedShohinColSiz が商品ごとに保持しているので、
商品Id と併せた3項目で突き合わせます。

# example
var sql = $@"
select {StockSql.ShohinCode()}, {StockSql.ColName()}, s.Su
from SummaryRealStock s
{StockSql.JoinSku()}
{StockSql.JoinSoko()}";
 */
namespace CvWpfclient.Helpers;

/// <summary>
/// 在庫集計テーブルの名称解決用SQL断片。既定の別名は s(在庫) / sh(商品) / cs(色サイズ) / so(倉庫)。
/// </summary>
internal static class StockSql {
	/// <summary>商品マスタと色サイズ派生テーブルを結合する。色サイズは商品Idと併せて突き合わせる。</summary>
	internal static string JoinSku(string stock = "s") => $@"
    LEFT JOIN MasterShohin sh ON sh.Id = {stock}.Id_Shohin
    LEFT JOIN DerivedShohinColSiz cs
           ON cs.Id_Shohin = {stock}.Id_Shohin
          AND cs.Id_Col = {stock}.Id_Col
          AND cs.Id_Siz = {stock}.Id_Siz";

	/// <summary>倉庫(MasterTokui の TenType=0)を結合する。</summary>
	internal static string JoinSoko(string stock = "s") =>
		$"    LEFT JOIN MasterTokui so ON so.Id = {stock}.Id_Soko";

	internal static string SokoCode(string a = "so") => $"ifnull({a}.Code,'')";
	internal static string SokoName(string a = "so") => $"ifnull({a}.Name,'')";
	internal static string ShohinCode(string a = "sh") => $"ifnull({a}.Code,'')";
	internal static string ShohinName(string a = "sh") => $"ifnull({a}.Name,'')";
	internal static string ColCode(string a = "cs") => $"ifnull({a}.Code_Col,'')";
	internal static string ColName(string a = "cs") => $"ifnull({a}.Mei_Col,'')";
	internal static string SizCode(string a = "cs") => $"ifnull({a}.Code_Siz,'')";
	internal static string SizName(string a = "cs") => $"ifnull({a}.Mei_Siz,'')";

	/// <summary>原価単価。商品マスタの原価単価。</summary>
	internal static string TankaGenka(string a = "sh") => $"ifnull({a}.TankaGenka,0)";

	/// <summary>
	/// 上代単価。上代一括変更(<see cref="CvBase.DerivedJodai"/>)の適用行があればその価格、無ければ商品マスタの上代。
	/// <para>
	/// 在庫の <c>Id_Soko</c> は倉庫のことも直営店のこともあるため、直営店なら店頭価格、
	/// そうでなければ本部基準の全件行を使う（<see cref="CvBase.DerivedJodai.ResolveSokoSql"/>）。
	/// 適用行が1件も無ければ従来どおり <c>MasterShohin.TankaJodai</c> を返すので、既存の集計値は変わらない。
	/// </para>
	/// </summary>
	/// <param name="stock">在庫テーブルの別名（<c>Id_Shohin</c> / <c>Id_Soko</c> を持つ）</param>
	/// <param name="a">商品マスタの別名</param>
	internal static string TankaJodai(string stock = "s", string a = "sh")
		=> CvBase.DerivedJodai.FinalJodaiSokoSql($"{stock}.Id_Shohin", $"{stock}.Id_Soko", CvBase.DerivedJodai.TodaySql, a);

	/// <summary>上代単価（商品マスタの定価のみ）。上代一括変更を反映したくない箇所で使う。</summary>
	internal static string TankaJodaiMaster(string a = "sh") => $"ifnull({a}.TankaJodai,0)";
}
