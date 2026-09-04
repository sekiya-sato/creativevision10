using CvAsset;
using CvBase;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

/// <summary>
/// 棚卸の開始処理と確定処理。
/// <para>
/// 仕様は `Doc/spec/archive/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 8.1 / 8.4 を参照する。
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
			// param.DenDay は使わない。調整伝票の計上日は店舗ごとの棚卸基準日になったため(設計書2.4)。
			// パラメータからの削除と「日付補正するか」の確認フラグの追加は Step6 で行う
			var cnt = FixStocktake(param.TanaMonth, param.IdShain, param.SokoIds);
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
	/// 対象店舗の棚卸基準日を解決する(設計書2.1)。店舗ごとの <see cref="Tran60TanaDate.TanaDay"/> を引き、
	/// 未設定の店舗は <paramref name="fallbackMonth"/> の計上月末へフォールバックする。
	/// </summary>
	/// <param name="fallbackMonth">棚卸日が未設定の店舗に使うフォールバック計上月 yyyyMM</param>
	/// <param name="sokoIds">対象倉庫Id。空なら既定の対象倉庫を自動で拾う</param>
	public IReadOnlyList<StocktakeDay> ResolveDays(string fallbackMonth, IEnumerable<long>? sokoIds = null) {
		var shime = new SummaryDb(_db).GetOwnClosingDay();
		var ids = sokoIds?.Where(x => x > 0).Distinct().ToList() ?? [];
		if (ids.Count == 0) {
			ids = FetchDefaultSokoIds(fallbackMonth, shime);
		}
		var tanaDays = FetchTanaDays(ids);
		return [.. ids.Select(id => StocktakeDaySet.Resolve(id, tanaDays.GetValueOrDefault(id), shime, fallbackMonth))];
	}

	/// <summary>
	/// 倉庫指定なしで呼ばれたときの既定の対象倉庫。当該計上月に在庫集計行がある倉庫と、
	/// 計上月の期間内に棚卸入力がある倉庫を拾う。
	/// <para>
	/// 画面からは対象店舗を明示で渡す(設計書2.6)。ここは倉庫指定なしの旧経路向けの既定であり、
	/// 棚卸日が翌計上月へ繰り越される店舗(締日が末日でない場合)は拾えない。
	/// </para>
	/// </summary>
	private List<long> FetchDefaultSokoIds(string fallbackMonth, int shime) {
		var ids = _db.Fetch<long>($"SELECT DISTINCT Id_Soko FROM {nameof(SummaryStock)} WHERE SumMonth = @0", fallbackMonth);
		if (_db.IsExistTable(typeof(Tran60Tana))) {
			var period = ClosingMonthCalculator.GetPeriod(fallbackMonth, shime);
			ids.AddRange(_db.Fetch<long>(
				$"SELECT DISTINCT Id_Soko FROM {nameof(Tran60Tana)} WHERE DenDay BETWEEN @0 AND @1",
				period.DayFrom, period.DayTo));
		}
		return [.. ids.Where(x => x > 0).Distinct().Order()];
	}

	/// <summary>
	/// 店舗別の棚卸日を <see cref="Tran60TanaDate"/> から引く。テーブルが無ければ全店舗未設定として扱う。
	/// 同一店舗に複数行あれば <c>Id</c> の大きい方を採る(一意キーは <c>uk1(Id_Shop)</c> なので通常は1行)。
	/// </summary>
	private Dictionary<long, string> FetchTanaDays(IReadOnlyList<long> sokoIds) {
		if (sokoIds.Count == 0 || !_db.IsExistTable(typeof(Tran60TanaDate))) {
			return [];
		}
		var rows = _db.Fetch<Tran60TanaDate>(
			$"SELECT * FROM {nameof(Tran60TanaDate)} WHERE Id_Shop IN ({string.Join(",", sokoIds)}) ORDER BY Id");
		var map = new Dictionary<long, string>();
		foreach (var row in rows) {
			map[row.Id_Shop] = row.TanaDay;
		}
		return map;
	}

	/// <summary>
	/// 棚卸開始処理。店舗ごとの棚卸基準日を解決してから <see cref="StartStocktake(IReadOnlyList{StocktakeDay})"/> を呼ぶ。
	/// </summary>
	/// <param name="fallbackMonth">棚卸日が未設定の店舗に使うフォールバック計上月 yyyyMM</param>
	/// <param name="sokoIds">対象倉庫Id。空なら既定の対象倉庫</param>
	/// <returns>保存した行数</returns>
	public int StartStocktake(string fallbackMonth, IEnumerable<long>? sokoIds = null) =>
		StartStocktake(ResolveDays(fallbackMonth, sokoIds));

	/// <summary>
	/// 棚卸開始処理。店舗ごとの基準日時点の帳簿在庫を <see cref="SummaryStock"/> へ保存する。
	/// <para>
	/// 帳簿在庫は <see cref="FetchBookQtyAsOf"/> の逆算で求める(設計書2.2)。
	/// 同じ条件で何度でも実行でき、実行のたびに最新の帳簿在庫で上書きする
	/// （旧CV.netも差異調査・伝票修正のあとに再実行する運用だった）。
	/// </para>
	/// <para>
	/// 店舗ごとに基準日が違うので単一SQLで全店舗を捌かず店舗ループにする。
	/// 帳簿在庫は店舗単位の一時表へ吐いてから、行補完(INSERT)と値の書き込み(UPDATE)の2文で反映する。
	/// UPSERT(<c>ON CONFLICT</c>)を使わないのは、素の INSERT/UPDATE なら4方言そのままで通るためである。
	/// </para>
	/// <para>
	/// 在庫再集計(Rebuild)は対象期間の <see cref="SummaryStock"/> を作り直すので、ここで補完した行と
	/// <see cref="SummaryStock.BookQty"/> は失われる。Rebuild のあとは本処理を再実行する運用とする(設計書4)。
	/// </para>
	/// </summary>
	/// <param name="days">解決済みの店舗別棚卸基準日</param>
	/// <returns>補完・更新した行数</returns>
	public int StartStocktake(IReadOnlyList<StocktakeDay> days) {
		if (days.Count == 0) {
			return 0;
		}
		var cnt = 0;
		try {
			_db.Execute($@"
CREATE TEMP TABLE IF NOT EXISTS {TempBookQtyTable} (
  Id_Shohin INTEGER NOT NULL,
  Id_Col INTEGER NOT NULL,
  Id_Siz INTEGER NOT NULL,
  BookQty INTEGER NOT NULL,
  PRIMARY KEY (Id_Shohin, Id_Col, Id_Siz)
);");
			foreach (var day in days) {
				cnt += StartStocktakeOne(day);
			}
		}
		finally {
			try {
				_db.Execute($"DROP TABLE IF EXISTS {TempBookQtyTable}");
			}
			catch (Exception ex) {
				_logger.LogWarning(ex, "一時テーブルの削除に失敗しました: {TableName}", TempBookQtyTable);
			}
		}
		return cnt;
	}

	private const string TempBookQtyTable = "TempStocktakeBookQty";

	/// <summary>店舗1件分の棚卸開始処理。</summary>
	private int StartStocktakeOne(StocktakeDay day) {
		var vdate = Common.GetVdate();
		var soko = day.Id_Shop;
		_db.Execute($"DELETE FROM {TempBookQtyTable}");

		// 1) 基準日時点の帳簿在庫を一時表へ
		_db.ExecuteDialect(
			$"INSERT INTO {TempBookQtyTable} (Id_Shohin, Id_Col, Id_Siz, BookQty){BuildBookQtyAsOfSql(soko)}",
			day.TanaDay, day.DayTo, day.SumMonth);

		// 2) 在庫履歴が無く基準日の棚卸入力にだけ現れるSKUを帳簿在庫0で足す。
		//    これを入れないと「実棚だけあるSKU」が差異に上がらない
		if (_db.IsExistTable(typeof(Tran60Tana))) {
			_db.ExecuteDialect($@"
INSERT INTO {TempBookQtyTable} (Id_Shohin, Id_Col, Id_Siz, BookQty)
SELECT s.Id_Shohin, s.Id_Col, s.Id_Siz, 0
FROM (
  SELECT DISTINCT
    json_extract(j.value, '$.Id_Shohin') AS Id_Shohin,
    json_extract(j.value, '$.Id_Col')    AS Id_Col,
    json_extract(j.value, '$.Id_Siz')    AS Id_Siz
  FROM {nameof(Tran60Tana)} AS t
       CROSS JOIN json_each(t.Jmeisai) AS j
  WHERE t.Id_Soko = {soko}
    AND t.DenDay = @0
    AND json_type(t.Jmeisai) = 'array'
) AS s
WHERE NOT EXISTS (
  SELECT 1 FROM {TempBookQtyTable} AS x
  WHERE x.Id_Shohin = s.Id_Shohin AND x.Id_Col = s.Id_Col AND x.Id_Siz = s.Id_Siz
)
;", day.TanaDay);
		}

		// 3) 当該計上月の行が無いSKUを補完する。当月に動きが無い在庫はこの月の行を持たないため、
		//    UPDATE だけでは帳簿在庫を記録できない
		var inserted = _db.Execute($@"
INSERT INTO {nameof(SummaryStock)}
  (SumMonth, Id_Soko, Id_Shohin, Id_Col, Id_Siz,
   Su, ReserveQty, CumulativeSu, InQty, OutQty, TransitQty, AdjustQty,
   StocktakeDdate, BookQty, ActualQty, Vdc, Vdu)
SELECT @1, {soko}, t.Id_Shohin, t.Id_Col, t.Id_Siz,
   0, 0, 0, 0, 0, 0, 0,
   @0, t.BookQty, t.BookQty, {vdate}, {vdate}
FROM {TempBookQtyTable} AS t
WHERE NOT EXISTS (
  SELECT 1 FROM {nameof(SummaryStock)} AS s
  WHERE s.SumMonth = @1 AND s.Id_Soko = {soko}
    AND s.Id_Shohin = t.Id_Shohin AND s.Id_Col = t.Id_Col AND s.Id_Siz = t.Id_Siz
)
;", day.TanaDay, day.SumMonth);

		// 4) 帳簿在庫と棚卸日(8桁)を書く
		var updated = _db.Execute($@"
UPDATE {nameof(SummaryStock)}
SET BookQty = COALESCE((
      SELECT t.BookQty FROM {TempBookQtyTable} AS t
      WHERE t.Id_Shohin = {nameof(SummaryStock)}.Id_Shohin
        AND t.Id_Col = {nameof(SummaryStock)}.Id_Col
        AND t.Id_Siz = {nameof(SummaryStock)}.Id_Siz
    ), 0),
    StocktakeDdate = @0,
    Vdu = {vdate}
WHERE SumMonth = @1
  AND Id_Soko = {soko}
;", day.TanaDay, day.SumMonth);

		return inserted + updated;
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
	/// <param name="fallbackMonth">棚卸日が未設定の店舗に使うフォールバック計上月 yyyyMM</param>
	/// <param name="idShain">入力社員Id</param>
	/// <param name="sokoIds">対象倉庫Id。空なら既定の対象倉庫</param>
	/// <returns>生成した調整伝票の件数</returns>
	public int FixStocktake(string fallbackMonth, long idShain, IEnumerable<long>? sokoIds = null) =>
		FixStocktake(ResolveDays(fallbackMonth, sokoIds), idShain).SlipCount;

	/// <summary>
	/// 棚卸確定処理。店舗ごとの基準日で実棚数を集計し、帳簿在庫との差を在庫調整伝票へ起こす。
	/// <para>
	/// 基準日以外の日付で入力された棚卸伝票を先に検知する。1件以上あり
	/// <paramref name="alignMisdated"/> が false なら**何も変更せずに中断**し、対象の内訳を返す。
	/// 呼び出し側は「基準日に補正するか」を確認して true で呼び直す(設計書4)。
	/// </para>
	/// <para>
	/// 補正は棚卸伝票の <c>DenDay</c> を書き換えるので <c>Vdu</c> が動く。再確定要否の判定は
	/// <c>DenDay &lt;= 基準日 かつ Vdu &gt; FixDay</c> なので、補正した伝票が直後に「再確定要」として
	/// 現れないよう、補正 → 確定 → <see cref="Tran60TanaDate.FixDay"/> 書き込み の順序を守る。
	/// 呼び出し側は全体を1トランザクションで囲むこと(<see cref="RunFixInTransaction"/>)。
	/// </para>
	/// </summary>
	/// <param name="days">解決済みの店舗別棚卸基準日</param>
	/// <param name="idShain">入力社員Id</param>
	/// <param name="alignMisdated">基準日以外の棚卸入力の <c>DenDay</c> を基準日へ補正してから確定するか</param>
	public StocktakeFixResult FixStocktake(IReadOnlyList<StocktakeDay> days, long idShain, bool alignMisdated = false) {
		var result = new StocktakeFixResult();
		if (days.Count == 0) {
			return result;
		}

		// 0) 基準日以外の棚卸入力を検知する。補正の指示が無ければ何も変更せず中断する
		foreach (var day in days) {
			result.Misdated.AddRange(FetchMisdatedTana(day));
		}
		if (result.Misdated.Count > 0 && !alignMisdated) {
			return result;
		}
		if (result.Misdated.Count > 0) {
			foreach (var day in days) {
				result.AlignedCount += AlignMisdatedTana(day);
			}
		}

		var summaryDb = new SummaryDb(_db);
		foreach (var day in days) {
			result.SlipCount += FixStocktakeOne(day, idShain, summaryDb);
		}
		StoreFixDay(days);
		return result;
	}

	/// <summary>店舗1件分の棚卸確定処理。</summary>
	private int FixStocktakeOne(StocktakeDay day, long idShain, SummaryDb summaryDb) {
		var soko = day.Id_Shop;
		// 1) 前回の棚卸調整を取り消す（在庫も戻す）
		var oldIds = _db.Fetch<long>(
			$"SELECT Id FROM {nameof(Tran61Chosei)} WHERE TanaMonth = @0 AND Kubun = @1 AND Id_Soko = {soko}",
			day.SumMonth, (int)EnumChosei.Tanaoroshi);
		foreach (var id in oldIds) {
			summaryDb.CalcTran2SummaryStock(nameof(Tran61Chosei), nameof(ITranSoko.Id_Soko), id, invertFlag: true);
			_db.Execute($"DELETE FROM {nameof(Tran61Chosei)} WHERE Id = @0", id);
		}

		// 2) 実棚数を SummaryStock.ActualQty へ反映する
		StoreActualQty(day);

		// 3) 帳簿在庫との差を1伝票にまとめて起こす
		var diffs = _db.Fetch<StocktakeDiff>($@"
SELECT Id_Soko, Id_Shohin, Id_Col, Id_Siz, (ActualQty - BookQty) AS Sa
FROM {nameof(SummaryStock)}
WHERE SumMonth = @0 AND Id_Soko = {soko} AND ActualQty <> BookQty
ORDER BY Id_Shohin, Id_Col, Id_Siz
", day.SumMonth);
		if (diffs.Count == 0) {
			return 0;
		}

		var meisai = diffs.Select((d, i) => new Tran99Meisai {
			No = i + 1,
			Id_Shohin = d.Id_Shohin,
			Id_Col = d.Id_Col,
			Id_Siz = d.Id_Siz,
			Su = d.Sa,
		}).ToList();
		var chosei = new Tran61Chosei {
			// 計上日は店舗ごとの棚卸基準日。計上月は既存の自社締日ロジックが DenDay から決めるので
			// SummaryStock.SumMonth と一致する(設計書2.4)
			DenDay = day.TanaDay,
			Id_Soko = soko,
			Id_Shain = idShain,
			EnKubun = EnumChosei.Tanaoroshi,
			TanaMonth = day.SumMonth,
			SuTotal = meisai.Sum(x => x.Su),
			Jmeisai = meisai,
			Memo = $"棚卸確定 {day.TanaDay}",
		};
		_db.Insert(chosei);
		summaryDb.CalcTran2SummaryStock(nameof(Tran61Chosei), nameof(ITranSoko.Id_Soko), chosei.Id, invertFlag: false);
		return 1;
	}

	/// <summary>
	/// 基準日以外の日付で入力された棚卸伝票を日付別に数える。
	/// 対象は計上月の期間内(<c>DayFrom〜DayTo</c>)にあり基準日と一致しないものだけで、
	/// 計上月の外にある棚卸入力は別の月の棚卸なので触らない(設計書4)。
	/// </summary>
	public List<StocktakeMisdated> FetchMisdatedTana(StocktakeDay day) {
		if (!_db.IsExistTable(typeof(Tran60Tana))) {
			return [];
		}
		return _db.Fetch<StocktakeMisdated>($@"
SELECT {day.Id_Shop} AS Id_Soko, DenDay AS DenDay, COUNT(*) AS SlipCount
FROM {nameof(Tran60Tana)}
WHERE Id_Soko = {day.Id_Shop}
  AND DenDay BETWEEN @0 AND @1
  AND DenDay <> @2
GROUP BY DenDay
ORDER BY DenDay
", day.DayFrom, day.DayTo, day.TanaDay);
	}

	/// <summary>基準日以外の棚卸伝票の <c>DenDay</c> を基準日へ補正する。</summary>
	private int AlignMisdatedTana(StocktakeDay day) {
		var vdate = Common.GetVdate();
		return _db.Execute($@"
UPDATE {nameof(Tran60Tana)}
SET DenDay = @2,
    Vdu = {vdate}
WHERE Id_Soko = {day.Id_Shop}
  AND DenDay BETWEEN @0 AND @1
  AND DenDay <> @2
;", day.DayFrom, day.DayTo, day.TanaDay);
	}

	/// <summary>
	/// 店舗ごとの棚卸の進行状況(開始済み／確定済み／再確定要)を返す。画面の店舗一覧に出す(設計書2.5)。
	/// <para>
	/// 再確定要の判定は「確定済みで、かつ基準日以前の伝票が確定後に更新されていること」。
	/// 旧CV.netの「確定処理後に過去の伝票を訂正した場合は再度確定の表示がでます」に対応する。
	/// </para>
	/// <para>
	/// 確定時刻の基準には <see cref="Tran60TanaDate.FixDay"/> ではなく同じ更新で書かれる
	/// <c>Tran60TanaDate.Vdu</c>(UTC Ticks)を使う。<c>FixDay</c> は日付8桁なので、
	/// 「10時に確定して15時に伝票を修正した」同日中の修正を取りこぼすためである。
	/// </para>
	/// <para>
	/// 制約: 棚卸日一括メンテナンスで <see cref="Tran60TanaDate"/> の行を更新すると <c>Vdu</c> が動き、
	/// 判定の基準時刻がリセットされる。棚卸日(<c>TanaDay</c>)を変えたのなら再確定が要るので整合するが、
	/// 自動補充だけ変えた場合は確定後の伝票修正を取りこぼす。
	/// </para>
	/// </summary>
	public List<StocktakeShopStatus> FetchRefixStatus(IReadOnlyList<StocktakeDay> days) {
		var statuses = new List<StocktakeShopStatus>(days.Count);
		var fixInfo = FetchFixInfo(days);
		foreach (var day in days) {
			var status = new StocktakeShopStatus {
				Id_Soko = day.Id_Shop,
				TanaDay = day.TanaDay,
				SumMonth = day.SumMonth,
				IsFallback = day.IsFallback,
				IsStarted = _db.FirstOrDefault<int>($@"
SELECT COUNT(*) FROM {nameof(SummaryStock)}
WHERE SumMonth = @0 AND Id_Soko = {day.Id_Shop} AND StocktakeDdate = @1", day.SumMonth, day.TanaDay) > 0,
			};
			if (fixInfo.TryGetValue(day.Id_Shop, out var info) && !StocktakeDaySet.IsUnset(info.FixDay)) {
				status.FixDay = info.FixDay;
				status.IsFixed = true;
				status.IsRefixRequired = HasSlipChangedAfter(day, info.Vdu);
			}
			statuses.Add(status);
		}
		return statuses;
	}

	/// <summary>店舗別の最終確定日と確定時刻(<c>Vdu</c>)を引く。</summary>
	private Dictionary<long, (string FixDay, long Vdu)> FetchFixInfo(IReadOnlyList<StocktakeDay> days) {
		if (days.Count == 0 || !_db.IsExistTable(typeof(Tran60TanaDate))) {
			return [];
		}
		var ids = string.Join(",", days.Select(x => x.Id_Shop));
		var rows = _db.Fetch<Tran60TanaDate>(
			$"SELECT * FROM {nameof(Tran60TanaDate)} WHERE Id_Shop IN ({ids}) ORDER BY Id");
		var map = new Dictionary<long, (string, long)>();
		foreach (var row in rows) {
			map[row.Id_Shop] = (row.FixDay, row.Vdu);
		}
		return map;
	}

	/// <summary>
	/// 基準日以前の伝票が確定時刻より後に更新されているか。
	/// 在庫に効く伝票に加えて棚卸伝票(<see cref="Tran60Tana"/>)も見る。実棚数を数え直したら再確定が要るためである。
	/// </summary>
	private bool HasSlipChangedAfter(StocktakeDay day, long fixVdu) {
		var sources = StockSuSources
			.Select(x => (x.TableName, x.Axis))
			.Append((TableName: nameof(Tran60Tana), Axis: nameof(ITranSoko.Id_Soko)))
			.Distinct();
		var branches = sources.Select(x => $@"
  SELECT 1 AS Hit FROM {x.TableName} AS t
  WHERE t.{x.Axis} = {day.Id_Shop} AND t.DenDay <= @0 AND t.Vdu > @1");
		var sql = $@"
SELECT COUNT(*) FROM ({string.Join("\n  UNION ALL", branches)}
) AS c";
		return _db.FirstOrDefault<int>(sql, day.TanaDay, fixVdu) > 0;
	}

	/// <summary>
	/// 確定処理の実行日を <see cref="Tran60TanaDate.FixDay"/> へ書く。
	/// 再確定要否の判定(<c>Vdu &gt; FixDay</c>)がこの値を基準にする(設計書2.5)。
	/// 棚卸日を設定していない店舗には行が無いので、その場合は作らない。
	/// </summary>
	private int StoreFixDay(IReadOnlyList<StocktakeDay> days) {
		if (days.Count == 0 || !_db.IsExistTable(typeof(Tran60TanaDate))) {
			return 0;
		}
		var vdate = Common.GetVdate();
		var today = DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
		var ids = string.Join(",", days.Select(x => x.Id_Shop));
		return _db.Execute($@"
UPDATE {nameof(Tran60TanaDate)}
SET FixDay = @0,
    Vdu = {vdate}
WHERE Id_Shop IN ({ids})
;", today);
	}

	/// <summary>
	/// 棚卸入力(<see cref="Tran60Tana"/>)の実棚数を <see cref="SummaryStock.ActualQty"/> へ反映する。
	/// <para>
	/// <see cref="Tran60Tana"/> は在庫を動かさない伝票なので、集計へ取り込むのはこの処理だけである。
	/// 棚卸伝票が無いSKUは実棚0ではなく「数えていない」なので、対象外にして帳簿在庫のままにする。
	/// </para>
	/// <para>
	/// 対象は <c>DenDay</c> が棚卸基準日と**厳密に一致**する伝票だけである(設計書2.3)。
	/// 棚番違いの複数伝票は合計する。基準日以外の日付で入力された伝票は
	/// <see cref="FetchMisdatedTana"/> が確定処理の前に検知する。
	/// </para>
	/// </summary>
	private int StoreActualQty(StocktakeDay day) {
		var vdate = Common.GetVdate();
		var sql = $@"
UPDATE {nameof(SummaryStock)}
SET ActualQty = ifnull((
      SELECT SUM(cast(ifnull(json_extract(m.value,'$.Su'),0) as integer))
      FROM {nameof(Tran60Tana)} h, json_each(h.Jmeisai) m
      WHERE json_valid(h.Jmeisai)
        AND h.DenDay = @1
        AND h.Id_Soko = {nameof(SummaryStock)}.Id_Soko
        AND cast(ifnull(json_extract(m.value,'$.Id_Shohin'),0) as integer) = {nameof(SummaryStock)}.Id_Shohin
        AND cast(ifnull(json_extract(m.value,'$.Id_Col'),0) as integer)    = {nameof(SummaryStock)}.Id_Col
        AND cast(ifnull(json_extract(m.value,'$.Id_Siz'),0) as integer)    = {nameof(SummaryStock)}.Id_Siz
    ), BookQty),
    Vdu = {vdate}
WHERE SumMonth = @0
  AND Id_Soko = {day.Id_Shop}
;
";
		return _db.ExecuteDialect(sql, day.SumMonth, day.TanaDay);
	}

	/// <summary>
	/// 基準日時点の帳簿在庫をSKU別に取得する(設計書2.2の逆算方式)。
	/// <para>
	/// <c>SummaryStock</c> は計上月末時点の在庫しか持たないので、月末累計から
	/// 「基準日より後・計上月末まで」の伝票増減を差し引いて基準日時点へ戻す。
	/// 走査が当該計上月かつ基準日より後に限定されるため、月初から積み上げるより対象行が少ない。
	/// </para>
	/// <para>
	/// <c>SummaryStock.CumulativeSu</c> は使わない。書き手の <c>SummaryDb.CalcSummaryStockCumulative</c> が
	/// 本番経路から呼ばれておらず、実運用で最新化されている保証がないためである。
	/// </para>
	/// </summary>
	/// <param name="day">解決済みの棚卸基準日(<see cref="StocktakeDaySet.Resolve"/>の戻り値)</param>
	/// <returns>帳簿在庫が非0、または基準日より後に動きがあったSKUの行</returns>
	public List<StocktakeBookQty> FetchBookQtyAsOf(StocktakeDay day) =>
		_db.FetchDialect<StocktakeBookQty>(BuildBookQtyAsOfSql(day.Id_Shop), day.TanaDay, day.DayTo, day.SumMonth);

	/// <summary>
	/// 在庫数(<c>Su</c>)に効く伝票と集計軸の組。<see cref="TranCalcBase.GetCalcSoko"/> /
	/// <see cref="TranCalcBase.GetCalcIdosaki"/> が返す符号をそのまま使い、符号表をここで再定義しない。
	/// <para>
	/// 逆算に使うのは在庫数(Item1)だけである。入出庫内訳(<c>InQty</c>/<c>OutQty</c>)・
	/// 移動中(<c>TransitQty</c>)・調整数(<c>AdjustQty</c>)は <c>Su</c> の外側の内訳列であり、
	/// 帳簿在庫の加減算には使わない。とくに移動中の現物は実地棚卸で数えられないため、
	/// 帳簿在庫にも実棚数にも現れず差異を生まない(設計書2.2)。
	/// </para>
	/// </summary>
	private static readonly (string TableName, string Axis, int SuFlag)[] StockSuSources = BuildStockSuSources();

	private static (string TableName, string Axis, int SuFlag)[] BuildStockSuSources() {
		// SummaryDb.CalcSummaryStockRange が走査する7伝票と同じ並び。ここを増減させたら向こうも合わせる
		string[] tableNames = [
			nameof(Tran00Uriage), nameof(Tran01Tenuri), nameof(Tran03Shiire),
			nameof(Tran05Ido), nameof(Tran10IdoOut), nameof(Tran11IdoIn), nameof(Tran61Chosei),
		];
		var sources = new List<(string, string, int)>();
		foreach (var tableName in tableNames) {
			var soko = TranCalcBase.GetCalcSoko(tableName).Item1;
			if (soko != 0) {
				sources.Add((tableName, nameof(ITranSoko.Id_Soko), soko));
			}
			// 移動先軸は ITranIdo を実装しない伝票では 0 が返るので、この判定だけで足りる
			var idosaki = TranCalcBase.GetCalcIdosaki(tableName).Item1;
			if (idosaki != 0) {
				sources.Add((tableName, nameof(ITranIdo.Id_Ido), idosaki));
			}
		}
		return [.. sources];
	}

	/// <summary>
	/// 基準日時点の帳簿在庫を求めるSQLを組む。パラメータは @0=基準日, @1=計上月末日, @2=計上月。
	/// <para>
	/// 明細の展開・<c>CalcFlag</c> の掛け方・<c>IsZaiko</c> の除外条件は
	/// <c>SummaryDb.CreateSummaryStockSql</c> と同一にしてある。片方だけ条件が違うと
	/// 累計と逆算が相殺せず帳簿在庫がずれるためである。
	/// <para>
	/// 実データ(<c>server-user163.db</c>)で倉庫113/253/284の3シナリオ計61,196SKUを、
	/// 逆算と順算(前月末累計＋月初からの積み上げ)で突き合わせて検証した。
	/// 除外条件を外すと不一致0件、付けると <c>MasterShohin.IsZaiko=0</c> の商品だけ不一致になる
	/// (当該DBには <c>IsZaiko=0</c> の商品の <see cref="SummaryStock"/> 行が14,066件残っている)。
	/// これは過去の更新ロジックで作られた行が残っているだけであり、現行仕様では
	/// <c>IsZaiko</c> を見るのが正しい。Rebuildを通せば当該行は落ちて整合する。
	/// つまり不一致は保存済みデータの陳腐化であって、逆算ロジックの誤りではない。
	/// </para>
	/// </para>
	/// <para>
	/// 対象SKU(Keys)は保存済み累計(<c>Cum</c>)だけから採る。基準日より後にしか動きが無いSKUは
	/// 当月の <see cref="SummaryStock"/> 行が必ずあるので <c>Cum</c> に現れる。逆に在庫履歴も
	/// 棚卸入力も無いSKUに帳簿在庫の行を作る必要はない(棚卸入力があるSKUの行補完は棚卸開始処理側で行う)。
	/// </para>
	/// <para>
	/// 内側の分岐では列別名を GROUP BY せず、外側の派生表で1回だけ集約する。
	/// 別名を GROUP BY できない方言があるためである。
	/// </para>
	/// <para>
	/// CTE(<c>WITH</c>)ではなく派生表で書いてある。NPocoの <c>Fetch&lt;T&gt;(sql, args)</c> は
	/// SQLが <c>SELECT</c> で始まらないと <c>SELECT ... FROM &lt;テーブル名&gt;</c> を前置するため、
	/// <c>WITH</c> 始まりのSQLは壊れて `near "Cum": syntax error` になる。
	/// 派生表なら <c>SELECT</c> 始まりになり、CTEをサブクエリに書けるかの方言差も避けられる。
	/// </para>
	/// </summary>
	/// <param name="idSoko">対象倉庫Id。long なのでSQLへ直接埋め込む(<see cref="BuildSokoWhere"/>と同じ理由)</param>
	private static string BuildBookQtyAsOfSql(long idSoko) {
		var branches = StockSuSources.Select(x => $@"
  SELECT
    json_extract(j.value, '$.Id_Shohin') AS Id_Shohin,
    json_extract(j.value, '$.Id_Col')    AS Id_Col,
    json_extract(j.value, '$.Id_Siz')    AS Id_Siz,
    json_extract(j.value, '$.Su')*t.CalcFlag*{x.SuFlag} AS Su
  FROM {x.TableName} AS t
       CROSS JOIN json_each(t.Jmeisai) AS j
       LEFT JOIN MasterTokui AS mt ON mt.Id = t.{x.Axis}
       LEFT JOIN MasterShohin AS ms ON ms.Id = json_extract(j.value, '$.Id_Shohin')
  WHERE t.{x.Axis} = {idSoko}
    AND t.DenDay > @0
    AND t.DenDay <= @1
    AND json_type(t.Jmeisai) = 'array'
    AND COALESCE(mt.IsZaiko, 1) = 1
    AND COALESCE(ms.IsZaiko, 1) = 1");
		return $@"
SELECT
  c.Id_Shohin AS Id_Shohin,
  c.Id_Col    AS Id_Col,
  c.Id_Siz    AS Id_Siz,
  c.Su - COALESCE(v.Su, 0) AS BookQty
FROM (
  SELECT Id_Shohin, Id_Col, Id_Siz, SUM(Su) AS Su
  FROM SummaryStock
  WHERE Id_Soko = {idSoko}
    AND SumMonth <= @2
  GROUP BY Id_Shohin, Id_Col, Id_Siz
) AS c
LEFT JOIN (
  SELECT Id_Shohin, Id_Col, Id_Siz, SUM(Su) AS Su
  FROM ({string.Join("\n  UNION ALL", branches)}
  ) AS d
  GROUP BY Id_Shohin, Id_Col, Id_Siz
) AS v ON v.Id_Shohin = c.Id_Shohin AND v.Id_Col = c.Id_Col AND v.Id_Siz = c.Id_Siz
ORDER BY c.Id_Shohin, c.Id_Col, c.Id_Siz
";
	}

	/// <summary>倉庫Idの絞り込み。Idは long なのでSQLへ直接埋め込む(パラメータでは動的型比較で一致しない)</summary>
	private static string BuildSokoWhere(IEnumerable<long>? sokoIds, string column) {
		var ids = sokoIds?.Where(x => x > 0).Distinct().ToList();
		return ids == null || ids.Count == 0 ? string.Empty : $" AND {column} IN ({string.Join(",", ids)})";
	}

	/// <summary>店舗1件の棚卸の進行状況(<see cref="FetchRefixStatus"/>の戻り)</summary>
	public sealed class StocktakeShopStatus {
		/// <summary>店舗Id</summary>
		public long Id_Soko { get; set; }
		/// <summary>棚卸基準日 yyyyMMdd</summary>
		public string TanaDay { get; set; } = string.Empty;
		/// <summary>計上月 yyyyMM</summary>
		public string SumMonth { get; set; } = string.Empty;
		/// <summary>最終確定日 yyyyMMdd。未確定なら <see cref="StocktakeDaySet.UnsetDay"/></summary>
		public string FixDay { get; set; } = StocktakeDaySet.UnsetDay;
		/// <summary>棚卸日が未設定で計上月末へフォールバックしたか</summary>
		public bool IsFallback { get; set; }
		/// <summary>この基準日で棚卸開始処理が済んでいるか</summary>
		public bool IsStarted { get; set; }
		/// <summary>棚卸確定処理が済んでいるか</summary>
		public bool IsFixed { get; set; }
		/// <summary>確定後に基準日以前の伝票が修正され、再確定が必要か</summary>
		public bool IsRefixRequired { get; set; }
	}

	/// <summary>
	/// 基準日以外の日付で入力された棚卸伝票の1行(<see cref="FetchMisdatedTana"/>の戻り)
	/// </summary>
	public sealed class StocktakeMisdated {
		/// <summary>店舗Id</summary>
		public long Id_Soko { get; set; }
		/// <summary>棚卸入力の計上日 yyyyMMdd</summary>
		public string DenDay { get; set; } = string.Empty;
		/// <summary>その日付の棚卸伝票の件数</summary>
		public int SlipCount { get; set; }
	}

	/// <summary>棚卸確定処理の結果</summary>
	public sealed class StocktakeFixResult {
		/// <summary>生成した在庫調整伝票の件数</summary>
		public int SlipCount { get; set; }
		/// <summary>
		/// 基準日以外の日付で入力された棚卸伝票。<see cref="IsConfirmationRequired"/> が true のときは
		/// 確定処理を実行していない(何も変更していない)。
		/// </summary>
		public List<StocktakeMisdated> Misdated { get; set; } = [];
		/// <summary>基準日へ補正した棚卸伝票の件数</summary>
		public int AlignedCount { get; set; }
		/// <summary>日付補正の確認が必要で確定処理を中断したか</summary>
		public bool IsConfirmationRequired => Misdated.Count > 0 && AlignedCount == 0 && SlipCount == 0;
	}

	/// <summary>基準日時点の帳簿在庫の1行(<see cref="FetchBookQtyAsOf"/>の戻り)</summary>
	public sealed class StocktakeBookQty {
		public long Id_Shohin { get; set; }
		public long Id_Col { get; set; }
		public long Id_Siz { get; set; }
		public int BookQty { get; set; }
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
