using CvAsset;
using CvBase;

namespace CvDomainLogic;

/// <summary>
/// 仕入配分（区分 <see cref="EnumHaibun.Hatsukai"/>(0)）の入荷割当。
/// <para>
/// 発注×SKU ごとに「入荷数 − 確定済みの仕入配分の確定数」を、その発注の未完了の仕入配分へ
/// 配分先の店舗コード順（同じ店舗内は行のId順）に割り当て、<see cref="TranHaibun.ArrivedSu"/>（入荷済み数）を更新する。
/// 区分0はこの入荷済み数だけが引当・確定の対象になる（決定 D1）。
/// 入荷数は、配分の出庫元と同じ倉庫の仕入(<see cref="Tran03Shiire"/>.RelateNo1 = 発注Id)の明細数量に
/// <c>CalcFlag</c> を掛けた合計で、仕入返品は差し引かれる。計算は冪等で、何度呼んでも同じ結果になる。
/// </para>
/// <para>
/// 呼び出し元が張ったトランザクション内で実行される前提。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step4_仕入配分入力_詳細設計.md` 3.2 を参照する。
/// </para>
/// </summary>
public class ArrivalDb(ExDatabase db) {
	private readonly ExDatabase _db = db;

	/// <summary>
	/// 指定した発注の入荷割当を計算し直す。入荷済み数が変わった行だけを更新し、その倉庫+SKUの引当を引き直す。
	/// </summary>
	/// <param name="hachuIds">発注Id（仕入配分の RelateNo1）。0以下は無視する</param>
	/// <param name="recalcReserve">false なら引当を引き直さない（全件再集計で後から全件引き直す場合）</param>
	/// <returns>入荷済み数を更新した配分行数</returns>
	public int Recalc(IEnumerable<long> hachuIds, bool recalcReserve = true) {
		var ids = hachuIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0) {
			return 0;
		}
		// Id は long で数値以外を含み得ないためSQLへ直接埋め込む（CompletionDb と同じ）
		var inIds = string.Join(",", ids);
		var rows = _db.FetchDialect<TranHaibun>(
			$"SELECT * FROM {nameof(TranHaibun)} WHERE Kubun = {(int)EnumHaibun.Hatsukai} AND RelateNo1 IN ({inIds})");
		if (rows.Count == 0) {
			return 0;
		}
		var received = _db.FetchDialect<ReceivedRow>($@"
SELECT h.RelateNo1 AS Id_Hachu, h.Id_Soko AS Id_Soko,
       cast(ifnull(json_extract(m.value,'$.Id_Shohin'),0) as integer) AS Id_Shohin,
       cast(ifnull(json_extract(m.value,'$.Id_Col'),0) as integer) AS Id_Col,
       cast(ifnull(json_extract(m.value,'$.Id_Siz'),0) as integer) AS Id_Siz,
       SUM(cast(ifnull(json_extract(m.value,'$.Su'),0) as integer) * h.CalcFlag) AS Su
FROM {nameof(Tran03Shiire)} h, json_each(h.Jmeisai) m
WHERE json_valid(h.Jmeisai) AND h.CalcFlag <> 0 AND h.RelateNo1 IN ({inIds})
GROUP BY h.RelateNo1, h.Id_Soko, Id_Shohin, Id_Col, Id_Siz")
			.ToDictionary(x => (x.Id_Hachu, x.Id_Soko, x.Id_Shohin, x.Id_Col, x.Id_Siz), x => x.Su);
		var tenpoIds = rows.Select(x => x.Id_Tenpo).Where(x => x > 0).Distinct().ToList();
		var codeByTenpo = tenpoIds.Count == 0 ? [] : _db.FetchDialect<MasterTokui>(
				$"SELECT * FROM {nameof(MasterTokui)} WHERE Id IN ({string.Join(",", tenpoIds)})")
			.ToDictionary(x => x.Id, x => x.Code);

		var changed = new List<TranHaibun>();
		foreach (var group in rows.GroupBy(x => ((long)x.RelateNo1, x.Id_Soko, x.Id_Shohin, x.Id_Col, x.Id_Siz))) {
			var consumed = group.Where(x => x.EndFlag != 0).Sum(x => x.JitsuSu);
			var available = Math.Max(received.GetValueOrDefault(group.Key) - consumed, 0);
			var open = group.Where(x => x.EndFlag == 0)
				.OrderBy(x => codeByTenpo.GetValueOrDefault(x.Id_Tenpo, string.Empty), StringComparer.Ordinal)
				.ThenBy(x => x.Id)
				.ToList();
			var arrived = HaibunOrderDistributor.FillByStock(available, [.. open.Select(x => x.Su)]);
			for (int i = 0; i < open.Count; i++) {
				if (open[i].ArrivedSu == arrived[i]) continue;
				open[i].ArrivedSu = arrived[i];
				changed.Add(open[i]);
			}
		}
		if (changed.Count == 0) {
			return 0;
		}
		var vdate = Common.GetVdate();
		foreach (var row in changed) {
			_db.ExecuteDialect($"UPDATE {nameof(TranHaibun)} SET ArrivedSu = @0, Vdu = {vdate} WHERE Id = @1", row.ArrivedSu, row.Id);
		}
		if (recalcReserve) {
			new SummaryDb(_db).CalcHaibun2Reserve(changed.Select(ReserveKey.From).ToHashSet());
		}
		return changed.Count;
	}

	/// <summary>
	/// 未完了の仕入配分を持つすべての発注の入荷割当を計算し直す（全件再集計で使う）。
	/// </summary>
	public int RecalcAll(bool recalcReserve = true) {
		var ids = _db.FetchDialect<long>(
			$"SELECT DISTINCT RelateNo1 FROM {nameof(TranHaibun)} WHERE Kubun = {(int)EnumHaibun.Hatsukai} AND EndFlag = 0 AND RelateNo1 > 0");
		return Recalc(ids, recalcReserve);
	}

	/// <summary>発注×倉庫×SKU の入荷数の集計行</summary>
	private sealed class ReceivedRow {
		public long Id_Hachu { get; set; }
		public long Id_Soko { get; set; }
		public long Id_Shohin { get; set; }
		public long Id_Col { get; set; }
		public long Id_Siz { get; set; }
		public int Su { get; set; }
	}
}
