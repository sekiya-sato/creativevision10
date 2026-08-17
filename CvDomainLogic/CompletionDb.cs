using CvAsset;
using CvBase;

namespace CvDomainLogic;

/// <summary>
/// 発注残・受注残の完了フラグ(<c>Tran13Hachu.EndFlag</c> / <c>Tran12Jyuchu.EndFlag</c>)を、
/// 紐付く仕入・出荷の実績から自動判定して立てる。
/// <para>
/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 4.2 / 4.3 / 4.3.1 を参照する。
/// </para>
/// <para>
/// 判定は次の3点で決まっている。
/// <list type="number">
/// <item>完了は<b>伝票単位</b>で持つ(G0)。明細は <c>Jmeisai</c>(JSON)にありSQLで絞れないため。</item>
/// <item>自動完了の条件は<b>明細単位で全SKUが充足</b>すること(G0-c)。伝票合計の比較では判定しない。</item>
/// <item>いったん <c>EndFlag=1</c> になったものは、実績が減っても<b>自動では 0 へ戻さない</b>(4.3.1)。
/// 戻すのは残完了設定画面からの手動操作だけで、代わりに編集時ワーニングで気付けるようにする。</item>
/// </list>
/// </para>
/// <para>
/// 呼び出し元(<c>WriteEffectRunner</c>)が張ったトランザクション内で実行される前提。
/// </para>
/// </summary>
public class CompletionDb(ExDatabase db) {
	private readonly ExDatabase _db = db;

	/// <summary>明細JSONの展開。エイリアスは h(ヘッダ) / m(明細) 固定</summary>
	private const string MeisaiFrom = "json_each(h.Jmeisai) m";
	/// <summary>不正JSONへ json_extract を当てると SQLite が例外を投げるためのガード</summary>
	private const string MeisaiGuard = "json_valid(h.Jmeisai)";

	private static string Num(string property) =>
		$"cast(ifnull(json_extract(m.value,'$.{property}'),0) as integer)";

	/// <summary>
	/// 発注(<see cref="Tran13Hachu"/>)の完了フラグを、紐付く仕入(<see cref="Tran03Shiire"/>)から判定する。
	/// <para>
	/// 紐付けは仕入の <c>RelateNo1</c> に発注Idを入れる規約による(旧CV.netの「関連伝票NO1」と同じ)。
	/// 仕入数は <c>CalcFlag</c> を掛けた符号付きで数えるため、仕入返品を登録すると充足が取り消される。
	/// </para>
	/// </summary>
	/// <param name="hachuIds">判定対象の発注Id。空なら何もしない</param>
	/// <returns>完了フラグを立てた伝票数</returns>
	public int CalcHachuEndFlag(IEnumerable<long> hachuIds) =>
		CalcEndFlag(nameof(Tran13Hachu), nameof(Tran03Shiire), hachuIds, actualJoin: "");

	/// <summary>
	/// 受注(<see cref="Tran12Jyuchu"/>)の完了フラグを、紐付く出荷売上(<see cref="Tran00Uriage"/>)から判定する。
	/// <para>
	/// 紐付けは出荷売上の <c>RelateNo1</c> に受注Idを入れる規約による。
	/// 対象とする出荷は出荷先の店種区分が<b>卸先(1) または 売仕店(3)</b> のものだけである(決定 G4)。
	/// 旧CV.netの受注残管理表も「受注Noに受注伝票Noが登録されていないものは対象外」としていた。
	/// </para>
	/// </summary>
	/// <param name="juchuIds">判定対象の受注Id。空なら何もしない</param>
	/// <returns>完了フラグを立てた伝票数</returns>
	public int CalcJuchuEndFlag(IEnumerable<long> juchuIds) =>
		CalcEndFlag(nameof(Tran12Jyuchu), nameof(Tran00Uriage), juchuIds,
			actualJoin: $"INNER JOIN {nameof(MasterTokui)} t ON t.Id = h.Id_Tokui "
				+ $"AND t.TenType IN ({TranCalcBase.ShukkaTenTypes})");

	/// <summary>
	/// 未完了の対象伝票のうち、明細の全SKUが実績で充足したものへ <c>EndFlag=1</c> を立てる。
	/// <para>
	/// 「充足していないSKUが1件も無い」を <c>NOT EXISTS</c> で判定する。
	/// 実績が1件も無い伝票は最初のSKUで不足になるため対象外になる。
	/// </para>
	/// </summary>
	private int CalcEndFlag(string zanTable, string actualTable, IEnumerable<long> ids, string actualJoin) {
		var idList = ids.Where(x => x > 0).Distinct().ToList();
		if (idList.Count == 0) {
			return 0;
		}
		// Id は long で数値以外を含み得ないためSQLへ直接埋め込む(パラメータでは動的型比較になり一致しない)
		var inIds = string.Join(",", idList);
		var vdate = Common.GetVdate();
		var sql = $@"
UPDATE {zanTable}
SET EndFlag = 1, Vdu = {vdate}
WHERE Id IN ({inIds})
  AND EndFlag = 0
  AND EXISTS (SELECT 1 FROM json_each({zanTable}.Jmeisai) WHERE json_valid({zanTable}.Jmeisai))
  AND NOT EXISTS (
    /* 発注(受注)明細を SKU 単位に畳んで、実績合計が足りない SKU を探す */
    SELECT 1
    FROM (
      SELECT
        {Num("Id_Shohin")} AS Id_Shohin,
        {Num("Id_Col")}    AS Id_Col,
        {Num("Id_Siz")}    AS Id_Siz,
        SUM({Num("Su")})   AS Su
      FROM {zanTable} h, {MeisaiFrom}
      WHERE h.Id = {zanTable}.Id AND {MeisaiGuard}
      GROUP BY 1, 2, 3
    ) zan
    WHERE zan.Su > ifnull((
      SELECT SUM({Num("Su")} * h.CalcFlag)
      FROM {actualTable} h, {MeisaiFrom}
      {actualJoin}
      WHERE h.RelateNo1 = {zanTable}.Id AND {MeisaiGuard}
        AND {Num("Id_Shohin")} = zan.Id_Shohin
        AND {Num("Id_Col")}    = zan.Id_Col
        AND {Num("Id_Siz")}    = zan.Id_Siz
    ), 0)
  );
";
		return _db.Execute(sql);
	}

	/// <summary>
	/// 指定した伝票のうち <c>EndFlag=1</c>(完了済み)のIdを返す。
	/// <para>
	/// 完了済みの発注・受注に紐付く仕入・出荷を編集したときのワーニング表示に使う(4.3.1)。
	/// 完了は自動では解除しないため、利用者が気付けるようにこの一覧を画面へ出す。
	/// </para>
	/// </summary>
	/// <param name="zanTableType">対象の型(<see cref="Tran13Hachu"/> / <see cref="Tran12Jyuchu"/>)</param>
	/// <param name="ids">確認する伝票Id</param>
	public IReadOnlyList<long> FindCompleted(Type zanTableType, IEnumerable<long> ids) {
		var idList = ids.Where(x => x > 0).Distinct().ToList();
		if (idList.Count == 0) {
			return [];
		}
		return _db.Fetch<long>(
			$"SELECT Id FROM {zanTableType.Name} WHERE Id IN ({string.Join(",", idList)}) AND EndFlag = 1");
	}
}
