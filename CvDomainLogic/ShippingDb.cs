using CvAsset;
using CvBase;

namespace CvDomainLogic;

/// <summary>出荷指示確定でエラーになった1行（有効在庫割れ）</summary>
/// <param name="Id_Soko">出庫元倉庫</param>
/// <param name="Id_Shohin">商品</param>
/// <param name="Id_Col">色</param>
/// <param name="Id_Siz">サイズ</param>
/// <param name="Shiji">確定しようとした指示数の合計</param>
/// <param name="Yuko">確定前の有効在庫（実在庫 − 引当数）</param>
public readonly record struct ShippingConfirmError(long Id_Soko, long Id_Shohin, long Id_Col, long Id_Siz, int Shiji, int Yuko);

/// <summary>
/// 配分の出荷指示確定と出荷処理（伝票作成）。
/// <para>
/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 5.2.4 / I2 / I3 / I4 / I5 を参照する。
/// 旧CV.netと同じ2段階で、確定と完了は独立した軸である。
/// </para>
/// <list type="number">
/// <item><b>出荷指示確定</b> … <see cref="TranHaibun.KakuteiDay"/> を立てる。
/// 割り当てた配分数が有効在庫を下回る場合はエラーにして1件も確定しない。</item>
/// <item><b>出荷処理</b> … 確定済みの配分から出荷売上伝票または移動伝票を作り、
/// <see cref="TranHaibun.RelateNo2"/> へ伝票Idを書いて <see cref="TranHaibun.EndFlag"/>=1 で引当を解除する。</item>
/// </list>
/// <para>
/// 呼び出し元が張ったトランザクション内で実行される前提。
/// </para>
/// </summary>
public class ShippingDb(ExDatabase db) {
	private readonly ExDatabase _db = db;

	/// <summary>
	/// 出荷指示を確定する。対象は未確定(<see cref="TranHaibun.KakuteiDay"/> が空)かつ未完了の行。
	/// <para>
	/// 旧CV.netは「有効在庫数 − 入力した予指示が正の場合のみ確定できる」としていた。
	/// 同じ検証をここで行い、1SKUでも割れていれば <paramref name="errors"/> を返して**何も確定しない**。
	/// 部分的に確定すると倉庫への指示が中途半端になるためである。
	/// </para>
	/// <para>
	/// 有効在庫は「実在庫 − 引当数」で、引当数には未確定の指示も含まれる(決定 I1-x)。
	/// 自分自身の指示ぶんは既に引当へ入っているため、確定してもここでは在庫は動かない。
	/// </para>
	/// </summary>
	/// <param name="haibunIds">確定する配分行のId</param>
	/// <param name="kakuteiDay">確定日 yyyyMMdd</param>
	/// <param name="errors">有効在庫を割ったSKUの一覧。空なら確定成功</param>
	/// <returns>確定した行数。エラー時は0</returns>
	public int ConfirmShipping(IEnumerable<long> haibunIds, string kakuteiDay, out IReadOnlyList<ShippingConfirmError> errors) {
		errors = [];
		var ids = haibunIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0 || string.IsNullOrWhiteSpace(kakuteiDay)) {
			return 0;
		}
		var inIds = string.Join(",", ids);
		// 有効在庫は「実在庫 - 引当数」。引当には自分自身の指示も入っているので、
		// 「実在庫 < 自分以外の引当 + 自分の指示」すなわち「実在庫 - 引当数 < 0」で割れを判定する
		var found = _db.Fetch<ShippingConfirmErrorRow>($@"
SELECT h.Id_Soko, h.Id_Shohin, h.Id_Col, h.Id_Siz,
       SUM(h.Su) AS Shiji,
       ifnull(MAX(r.Su - r.ReserveQty), 0) AS Yuko
FROM {nameof(TranHaibun)} h
LEFT JOIN {nameof(SummaryRealStock)} r
  ON r.Id_Soko = h.Id_Soko AND r.Id_Shohin = h.Id_Shohin AND r.Id_Col = h.Id_Col AND r.Id_Siz = h.Id_Siz
WHERE h.Id IN ({inIds}) AND h.EndFlag = 0 AND ifnull(h.KakuteiDay,'') = ''
GROUP BY h.Id_Soko, h.Id_Shohin, h.Id_Col, h.Id_Siz
HAVING ifnull(MAX(r.Su - r.ReserveQty), 0) < 0
ORDER BY h.Id_Soko, h.Id_Shohin, h.Id_Col, h.Id_Siz
");
		if (found.Count > 0) {
			errors = [.. found.Select(x => new ShippingConfirmError(x.Id_Soko, x.Id_Shohin, x.Id_Col, x.Id_Siz, x.Shiji, x.Yuko))];
			return 0;
		}
		return _db.Execute(
			$"UPDATE {nameof(TranHaibun)} SET KakuteiDay = @0, Vdu = {Common.GetVdate()} "
			+ $"WHERE Id IN ({inIds}) AND EndFlag = 0 AND ifnull(KakuteiDay,'') = ''", kakuteiDay);
	}

	/// <summary>
	/// 出荷指示の確定を取り消す。まだ伝票を作っていない行だけを対象にする。
	/// </summary>
	/// <returns>取り消した行数</returns>
	public int CancelConfirm(IEnumerable<long> haibunIds) {
		var ids = haibunIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0) {
			return 0;
		}
		return _db.Execute(
			$"UPDATE {nameof(TranHaibun)} SET KakuteiDay = '', Vdu = {Common.GetVdate()} "
			+ $"WHERE Id IN ({string.Join(",", ids)}) AND EndFlag = 0 AND RelateNo2 = 0");
	}

	/// <summary>
	/// 出荷処理。確定済みの配分から伝票を作り、引当を解除する。
	/// <para>
	/// まとめる単位は仮想ヘッダのキー
	/// <c>DenDay + NouhinDay + Id_Soko + Id_Tenpo + Kubun + RelateNo1</c>（決定 I5）。
	/// 旧CV.netの配分伝票NO（1出庫元 ⇒ 1出荷先）と同じ括りになる。
	/// </para>
	/// <para>
	/// 生成する伝票は出荷先の店種区分で分かれる（決定 I4）。
	/// 卸先(1)・売仕店(3) は出荷売上伝票 <see cref="Tran00Uriage"/>、
	/// 倉庫(0)・直営店(6) は移動出庫伝票 <see cref="Tran10IdoOut"/> になる。
	/// </para>
	/// <para>
	/// 数量は確定数 <see cref="TranHaibun.JitsuSu"/> を使う。欠品(<see cref="TranHaibun.ShortSu"/>)は出荷しない。
	/// 全量欠品の行は伝票を作らずに完了だけ立てて引当から外す。
	/// </para>
	/// </summary>
	/// <param name="haibunIds">出荷処理する配分行のId</param>
	/// <param name="denDay">生成する伝票の在庫計上日 yyyyMMdd</param>
	/// <param name="idShain">入力社員Id</param>
	/// <returns>生成した伝票Idの一覧</returns>
	public IReadOnlyList<long> CreateShippingSlips(IEnumerable<long> haibunIds, string denDay, long idShain) {
		var ids = haibunIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0) {
			return [];
		}
		var rows = _db.Fetch<TranHaibun>(
			$"where Id in ({string.Join(",", ids)}) and EndFlag = 0 and ifnull(KakuteiDay,'') <> '' "
			+ $"order by {HaibunHeaderKey.KeyColumnsSql()}, Id");
		if (rows.Count == 0) {
			return [];
		}
		var tenTypes = LoadTenTypes(rows.Select(x => x.Id_Tenpo));
		var summaryDb = new SummaryDb(_db);
		var created = new List<long>();

		// 仮想ヘッダ単位でまとめる(決定 I5)。キーの定義は HaibunHeaderKey に集約している
		foreach (var group in rows.GroupBy(HaibunHeaderKey.From)) {
			var shipped = group.Where(x => x.JitsuSu > 0).ToList();
			long slipId = 0;
			if (shipped.Count > 0) {
				var meisai = shipped.Select((h, i) => new Tran99Meisai {
					No = i + 1,
					Id_Shohin = h.Id_Shohin,
					Id_Col = h.Id_Col,
					Id_Siz = h.Id_Siz,
					Su = h.JitsuSu,
					Tanka = h.Tanka,
					Kingaku = h.JitsuSu * h.Tanka,
					Jodai = h.Jodai,
					Gedai = h.Gedai,
				}).ToList();
				var tenType = tenTypes.GetValueOrDefault(group.Key.Id_Tenpo, 0);
				slipId = IsShukka(tenType)
					? CreateUriage(group.Key, meisai, idShain, denDay)
					: CreateIdoOut(group.Key, meisai, idShain, denDay);
				created.Add(slipId);
				// 生成した伝票の在庫を反映する。バッチ処理なので gRPC を往復せず直接呼ぶ
				summaryDb.CalcTran2SummaryStock(
					IsShukka(tenType) ? nameof(Tran00Uriage) : nameof(Tran10IdoOut),
					nameof(ITranSoko.Id_Soko), slipId, invertFlag: false);
				if (!IsShukka(tenType)) {
					summaryDb.CalcTran2SummaryStock(nameof(Tran10IdoOut), nameof(ITranIdo.Id_Ido), slipId, invertFlag: false);
				}
			}
			// 出荷した行も全量欠品の行も完了にして引当から外す
			var vdate = Common.GetVdate();
			_db.Execute(
				$"UPDATE {nameof(TranHaibun)} SET EndFlag = 1, RelateNo2 = @0, Vdu = {vdate} "
				+ $"WHERE Id IN ({string.Join(",", group.Select(x => x.Id))})", (int)slipId);
		}
		// 引当は EndFlag が変わったキーぶんを引き直す
		summaryDb.CalcHaibun2Reserve(rows.Select(ReserveKey.From).ToHashSet());
		return created;
	}

	/// <summary>
	/// 出荷処理。確定済み配分に実数量を入れてから伝票を作成する。ハンディ廃止(決定 I6)により、
	/// 実数量(<see cref="TranHaibun.JitsuSu"/>)・欠品(<see cref="TranHaibun.ShortSu"/>)は
	/// 出荷処理入力の画面で確定する。
	/// <para>
	/// 楽観排他は行単位で<b>先に全行を検証</b>する。対象(<c>EndFlag=0</c> かつ確定済み)に無い行や
	/// <see cref="TranHaibun.Vdu"/> が一覧取得時点と食い違う行が1件でもあれば、<b>何も書かずに</b>
	/// <paramref name="concurrencyConflict"/> を <c>true</c> にして返す。呼び出し元がトランザクションを
	/// 戻して再取得を促す前提。
	/// </para>
	/// </summary>
	/// <param name="rows">出荷処理する行（Id・一覧取得時点のVdu・実数量）</param>
	/// <param name="denDay">生成する伝票の在庫計上日 yyyyMMdd</param>
	/// <param name="idShain">入力社員Id</param>
	/// <param name="concurrencyConflict">競合を検知したら true。true のときは何も書き込んでいない</param>
	/// <returns>作成した伝票Idの一覧。競合時は空</returns>
	public IReadOnlyList<long> ProcessShipping(
		IReadOnlyCollection<(long Id, long ExpectedVdu, int JitsuSu)> rows, string denDay, long idShain, out bool concurrencyConflict) {
		concurrencyConflict = false;
		var ids = rows.Where(r => r.Id > 0).Select(r => r.Id).Distinct().ToList();
		if (ids.Count == 0) {
			return [];
		}
		// 対象は確定済み(KakuteiDay有効)かつ未完了(EndFlag=0)の行だけ
		var current = _db.Fetch<TranHaibun>(
			$"where Id in ({string.Join(",", ids)}) and EndFlag = 0 and ifnull(KakuteiDay,'') <> ''")
			.ToDictionary(x => x.Id);
		// 1件でも「対象に無い」「Vdu不一致」があれば何も書かずに競合として返す(fail-fast)
		foreach (var r in rows.Where(r => r.Id > 0)) {
			if (!current.TryGetValue(r.Id, out var h) || h.Vdu != r.ExpectedVdu) {
				concurrencyConflict = true;
				return [];
			}
		}
		var vdate = Common.GetVdate();
		foreach (var r in rows.Where(r => r.Id > 0)) {
			var h = current[r.Id];
			var jitsu = Math.Clamp(r.JitsuSu, 0, h.Su);
			_db.Execute(
				$"update {nameof(TranHaibun)} set {nameof(TranHaibun.JitsuSu)} = @0, "
				+ $"{nameof(TranHaibun.ShortSu)} = @1, {nameof(TranHaibun.Vdu)} = {vdate} where {nameof(TranHaibun.Id)} = @2",
				jitsu, h.Su - jitsu, r.Id);
		}
		return CreateShippingSlips(ids, denDay, idShain);
	}

	/// <summary>出荷売上とみなす店種区分。1=卸先 / 3=売仕店（決定 I4 / G4）</summary>
	public static bool IsShukka(int tenType) => tenType is 1 or 3;

	private long CreateUriage(HaibunHeaderKey key,
		List<Tran99Meisai> meisai, long idShain, string denDay) {
		var slip = new Tran00Uriage {
			DenDay = denDay,
			KakeDay = denDay,
			Id_Soko = key.Id_Soko,
			Id_Tokui = key.Id_Tenpo,
			Id_Shain = idShain,
			RelateNo1 = key.RelateNo1,
			IsPay = 1,
			SuTotal = meisai.Sum(x => x.Su),
			KingakuTotal = meisai.Sum(x => x.Kingaku),
			Jmeisai = meisai,
			Memo = "配分出荷",
		};
		_db.Insert(slip);
		return slip.Id;
	}

	private long CreateIdoOut(HaibunHeaderKey key,
		List<Tran99Meisai> meisai, long idShain, string denDay) {
		var slip = new Tran10IdoOut {
			DenDay = denDay,
			Id_Soko = key.Id_Soko,
			Id_Ido = key.Id_Tenpo,
			Id_Shain = idShain,
			SuTotal = meisai.Sum(x => x.Su),
			KingakuTotal = meisai.Sum(x => x.Kingaku),
			Jmeisai = meisai,
			Memo = "配分出荷",
		};
		_db.Insert(slip);
		return slip.Id;
	}

	private Dictionary<long, int> LoadTenTypes(IEnumerable<long> tenpoIds) {
		var ids = tenpoIds.Where(x => x > 0).Distinct().ToList();
		if (ids.Count == 0) {
			return [];
		}
		return _db.Fetch<MasterTokui>($"where Id in ({string.Join(",", ids)})")
			.ToDictionary(x => x.Id, x => x.TenType);
	}

	/// <summary>有効在庫割れの集計行</summary>
	private sealed class ShippingConfirmErrorRow {
		public long Id_Soko { get; set; }
		public long Id_Shohin { get; set; }
		public long Id_Col { get; set; }
		public long Id_Siz { get; set; }
		public int Shiji { get; set; }
		public int Yuko { get; set; }
	}
}
