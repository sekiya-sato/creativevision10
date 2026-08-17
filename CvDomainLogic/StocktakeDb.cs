using CvAsset;
using CvBase;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// 棚卸の開始処理と確定処理。
/// <para>
/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 8.1 / 8.4 を参照する。
/// 旧CV.netの7段階のうち、システムが行うのは「4. 棚卸開始処理」と「7. 棚卸確定処理」の2つである。
/// </para>
/// <para>
/// 棚卸開始処理は棚卸終了日時点の帳簿在庫を <see cref="SummaryStock.ActualQty"/> の対になる
/// スナップショットとして保存し、棚卸中に伝票が入っても差異表の「帳簿在庫数」が動かないようにする。
/// 棚卸確定処理は実棚数との差を在庫調整伝票(<see cref="Tran61Chosei"/>)として起こす。
/// 集計テーブルへ直接書かないのは「通常更新値 = Rebuild値」を保つためである。
/// </para>
/// </summary>
public class StocktakeDb(ExDatabase db) {
	private readonly ExDatabase _db = db;
	private readonly ILogger<StocktakeDb> _logger = new NLogExtender<StocktakeDb>();

	/// <summary>
	/// 棚卸開始処理をストリーミングで実行する。画面(棚卸開始処理)から `Msg054_StocktakeStart` で呼ばれる。
	/// </summary>
	public IAsyncEnumerable<StreamStepProgress> StartAsyncStream(StocktakeParameter param) =>
		StreamStepProgressRunner.Run(
			[($"棚卸開始処理 : {param.TanaMonth} 帳簿在庫の保存", p => StartStocktake(p.TanaMonth, p.SokoIds))],
			param, _logger, "棚卸開始処理を開始", "棚卸開始処理エラー: {StepName}", "棚卸開始処理を終了");

	/// <summary>
	/// 棚卸確定処理をストリーミングで実行する。画面(棚卸確定処理)から `Msg055_StocktakeFix` で呼ばれる。
	/// <para>
	/// 実棚数の反映と調整伝票の生成を1トランザクションで行う。途中で失敗したら全体を戻す。
	/// </para>
	/// </summary>
	public IAsyncEnumerable<StreamStepProgress> FixAsyncStream(StocktakeParameter param) =>
		StreamStepProgressRunner.Run(
			[($"棚卸確定処理 : {param.TanaMonth} 在庫調整伝票の作成", RunFixInTransaction)],
			param, _logger, "棚卸確定処理を開始", "棚卸確定処理エラー: {StepName}", "棚卸確定処理を終了");

	private int RunFixInTransaction(StocktakeParameter param) {
		var started = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			started = true;
			var cnt = FixStocktake(param.TanaMonth, param.DenDay, param.IdShain, param.SokoIds);
			_db.CompleteTransaction();
			started = false;
			return cnt;
		}
		catch {
			if (started) {
				_db.AbortTransaction();
			}
			throw;
		}
	}

	/// <summary>
	/// 棚卸開始処理。対象倉庫×SKU について、棚卸終了日時点の帳簿在庫を <see cref="SummaryStock"/> へ保存する。
	/// <para>
	/// 帳簿在庫は「対象年月以前の <see cref="SummaryStock.Su"/> の累計」で求める。
	/// 同じ条件で何度でも実行でき、実行のたびに最新の帳簿在庫で上書きする
	/// （旧CV.netも差異調査・伝票修正のあとに再実行する運用だった）。
	/// </para>
	/// </summary>
	/// <param name="tanaMonth">棚卸年月 yyyyMM</param>
	/// <param name="sokoIds">対象倉庫Id。空なら全倉庫</param>
	/// <returns>保存した行数</returns>
	public int StartStocktake(string tanaMonth, IEnumerable<long>? sokoIds = null) {
		var vdate = Common.GetVdate();
		var sokoWhere = BuildSokoWhere(sokoIds, "s.Id_Soko");
		var sql = $@"
UPDATE SummaryStock
SET BookQty = ifnull((
      SELECT SUM(p.Su) FROM SummaryStock p
      WHERE p.Id_Soko = SummaryStock.Id_Soko
        AND p.Id_Shohin = SummaryStock.Id_Shohin
        AND p.Id_Col = SummaryStock.Id_Col
        AND p.Id_Siz = SummaryStock.Id_Siz
        AND p.SumMonth <= SummaryStock.SumMonth
    ), 0),
    StocktakeDdate = @0,
    Vdu = {vdate}
WHERE SumMonth = @0
  {sokoWhere.Replace("s.Id_Soko", "Id_Soko")}
;
";
		return _db.Execute(sql, tanaMonth);
	}

	/// <summary>
	/// 棚卸確定処理。実棚数(<see cref="Tran60Tana"/>)を集計して <see cref="SummaryStock.ActualQty"/> へ入れ、
	/// 帳簿在庫との差を在庫調整伝票(<see cref="Tran61Chosei"/>)として起こす。
	/// <para>
	/// 差が0のSKUは伝票を作らない。生成した伝票の在庫反映は呼び出し元
	/// (<c>CoreService</c> / <c>WriteEffectRunner</c>)ではなく、ここで <see cref="SummaryDb"/> を直接呼ぶ。
	/// バッチ処理であり1件ずつgRPCを往復しないためである。
	/// </para>
	/// <para>
	/// 再確定に対応する。同じ年月・倉庫で再実行すると、前回この処理が作った調整伝票
	/// (<c>Kubun = EnumChosei.Tanaoroshi</c> かつ <c>TanaMonth</c> 一致)を削除してから作り直す。
	/// 旧CV.netも「確定処理後に過去の伝票を訂正した場合は再確定する」運用だった。
	/// </para>
	/// </summary>
	/// <param name="tanaMonth">棚卸年月 yyyyMM</param>
	/// <param name="denDay">生成する調整伝票の在庫計上日 yyyyMMdd</param>
	/// <param name="idShain">入力社員Id</param>
	/// <param name="sokoIds">対象倉庫Id。空なら全倉庫</param>
	/// <returns>生成した調整伝票の件数</returns>
	public int FixStocktake(string tanaMonth, string denDay, long idShain, IEnumerable<long>? sokoIds = null) {
		var summaryDb = new SummaryDb(_db);
		// 1) 前回の棚卸調整を取り消す（在庫も戻す）
		var oldIds = _db.Fetch<long>(
			$"SELECT Id FROM {nameof(Tran61Chosei)} WHERE TanaMonth = @0 AND Kubun = @1 {BuildSokoWhere(sokoIds, "Id_Soko")}",
			tanaMonth, (int)EnumChosei.Tanaoroshi);
		foreach (var id in oldIds) {
			summaryDb.CalcTran2SummaryStock(nameof(Tran61Chosei), nameof(ITranSoko.Id_Soko), id, invertFlag: true);
			_db.Execute($"DELETE FROM {nameof(Tran61Chosei)} WHERE Id = @0", id);
		}

		// 2) 実棚数を SummaryStock.ActualQty へ反映する
		StoreActualQty(tanaMonth, sokoIds);

		// 3) 帳簿在庫との差を倉庫単位の調整伝票にまとめて起こす
		var diffs = _db.Fetch<StocktakeDiff>($@"
SELECT Id_Soko, Id_Shohin, Id_Col, Id_Siz, (ActualQty - BookQty) AS Sa
FROM SummaryStock
WHERE SumMonth = @0 AND ActualQty <> BookQty
  {BuildSokoWhere(sokoIds, "Id_Soko")}
ORDER BY Id_Soko, Id_Shohin, Id_Col, Id_Siz
", tanaMonth);

		var cnt = 0;
		foreach (var group in diffs.GroupBy(x => x.Id_Soko)) {
			var meisai = group.Select((d, i) => new Tran99Meisai {
				No = i + 1,
				Id_Shohin = d.Id_Shohin,
				Id_Col = d.Id_Col,
				Id_Siz = d.Id_Siz,
				Su = d.Sa,
			}).ToList();
			var chosei = new Tran61Chosei {
				DenDay = denDay,
				Id_Soko = group.Key,
				Id_Shain = idShain,
				EnKubun = EnumChosei.Tanaoroshi,
				TanaMonth = tanaMonth,
				SuTotal = meisai.Sum(x => x.Su),
				Jmeisai = meisai,
				Memo = $"棚卸確定 {tanaMonth}",
			};
			_db.Insert(chosei);
			summaryDb.CalcTran2SummaryStock(nameof(Tran61Chosei), nameof(ITranSoko.Id_Soko), chosei.Id, invertFlag: false);
			cnt++;
		}
		return cnt;
	}

	/// <summary>
	/// 棚卸入力(<see cref="Tran60Tana"/>)の実棚数を <see cref="SummaryStock.ActualQty"/> へ反映する。
	/// <para>
	/// <see cref="Tran60Tana"/> は在庫を動かさない伝票なので、集計へ取り込むのはこの処理だけである。
	/// 棚卸伝票が無いSKUは実棚0ではなく「数えていない」なので、対象外にして帳簿在庫のままにする。
	/// </para>
	/// </summary>
	private int StoreActualQty(string tanaMonth, IEnumerable<long>? sokoIds) {
		var vdate = Common.GetVdate();
		var sql = $@"
UPDATE SummaryStock
SET ActualQty = ifnull((
      SELECT SUM(cast(ifnull(json_extract(m.value,'$.Su'),0) as integer))
      FROM {nameof(Tran60Tana)} h, json_each(h.Jmeisai) m
      WHERE json_valid(h.Jmeisai)
        AND substr(h.DenDay, 1, 6) = SummaryStock.SumMonth
        AND h.Id_Soko = SummaryStock.Id_Soko
        AND cast(ifnull(json_extract(m.value,'$.Id_Shohin'),0) as integer) = SummaryStock.Id_Shohin
        AND cast(ifnull(json_extract(m.value,'$.Id_Col'),0) as integer)    = SummaryStock.Id_Col
        AND cast(ifnull(json_extract(m.value,'$.Id_Siz'),0) as integer)    = SummaryStock.Id_Siz
    ), BookQty),
    Vdu = {vdate}
WHERE SumMonth = @0
  {BuildSokoWhere(sokoIds, "Id_Soko")}
;
";
		return _db.Execute(sql, tanaMonth);
	}

	/// <summary>倉庫Idの絞り込み。Idは long なのでSQLへ直接埋め込む(パラメータでは動的型比較で一致しない)</summary>
	private static string BuildSokoWhere(IEnumerable<long>? sokoIds, string column) {
		var ids = sokoIds?.Where(x => x > 0).Distinct().ToList();
		return ids == null || ids.Count == 0 ? string.Empty : $" AND {column} IN ({string.Join(",", ids)})";
	}

	/// <summary>棚卸差異の1行</summary>
	private sealed class StocktakeDiff {
		public long Id_Soko { get; set; }
		public long Id_Shohin { get; set; }
		public long Id_Col { get; set; }
		public long Id_Siz { get; set; }
		public int Sa { get; set; }
	}
}
