using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CvDomainLogic;

/// <summary>
/// 原価4項目（最終仕入原価更新・総平均原価更新・消化仕入更新・評価替え）のDBアクセス。
/// 正典は `Doc/spec/2026-09-05_原価4項目_詳細設計.md`（以下「設計書」）。
/// <para>
/// 設計書§9.2に列挙された `CostUpdateDb` の担当処理のうち、本ファイル（Step 4）が実装するのは
/// 原価履歴（<see cref="TranGenka"/>）の読み取り側と土台だけである。
/// </para>
/// <list type="bullet">
/// <item><description>本ファイルの担当: 対象期間の解決(<see cref="ResolvePeriod"/>)、原価解決(<see cref="ResolveCostAsOf(long,string,EnumCostMethod,string?)"/>)、
/// 現在原価の反映(<see cref="RefreshCurrentProductCost"/>)、基準行生成(<see cref="EnsureBaselineCostRows"/>)、
/// `TranGenka` のupsert(<see cref="UpsertGenkaRows"/>)、月次状態の算出(<see cref="FetchCostMonthStatus"/>、
/// `ProcessKind=3` のみ)。</description></item>
/// <item><description>Step 5で `CostUpdateDbConsumption.cs`（仮）に追加: `PreviewConsumptionPurchases` /
/// `ApplyConsumptionPurchases`、消化仕入(`ProcessKind=1`)の月次状態算出。</description></item>
/// <item><description>Step 6で追加: `PreviewSundryCharges`、`SumSundryChargesByShohin`（諸掛の商品別集計）。</description></item>
/// <item><description>Step 7で追加: `PreviewLastPurchaseCost` / `ApplyLastPurchaseCost`、
/// `PreviewTotalAverageCost` / `ApplyTotalAverageCost`（`TranGenka` の書き込み側本体）。</description></item>
/// <item><description>Step 8で追加: 評価替えの抽出・計算・保存・取消（`TranGenkaReval` 本体）。</description></item>
/// </list>
/// <para>
/// クラスを <c>partial</c> にしてあるのは、上記Step 5〜8の追加分を同一クラスの別ファイルへ
/// 分割して足すためである（設計書§9.2 の一覧を1クラスへ集約する方針に合わせる）。
/// </para>
/// </summary>
public partial class CostUpdateDb(ExDatabase db) {
	private readonly ExDatabase _db = db;
	private readonly ILogger<CostUpdateDb> _logger = new NLogExtender<CostUpdateDb>();

	/// <summary>
	/// SQLiteのIN句へIdをまとめて埋め込む際の分割単位。既存コード（<see cref="CvDomainLogic.StocktakeDb"/> 等）には
	/// 大量件数のIN句分割の前例が無いため、SQLiteのデフォルト上限(<c>SQLITE_MAX_VARIABLE_NUMBER</c> 等)を
	/// 踏まえて1000件単位に分割する（本ファイルで新設する規約）。
	/// </summary>
	private const int IdChunkSize = 1000;

	private static IEnumerable<List<long>> ChunkIds(IReadOnlyCollection<long> idShohins) {
		var ids = idShohins.Where(x => x > 0).Distinct().ToList();
		for (var i = 0; i < ids.Count; i += IdChunkSize) {
			yield return ids.GetRange(i, Math.Min(IdChunkSize, ids.Count - i));
		}
	}

	// ==================================================================
	// 4-1. 対象期間の解決（設計書§2.1）
	// ==================================================================

	/// <summary>
	/// 画面入力の対象計上月(<paramref name="targetMonth"/> yyyyMM)から、自社締日基準の対象期間を解決する
	/// （設計書§2.1）。
	/// <para>
	/// 自社締日は <see cref="SummaryDb.GetOwnClosingDay"/> と同じ取得経路
	/// （<c>MasterSysman.ShimeBi</c> を <c>ORDER BY Id LIMIT 1</c> で取得）を使う。既存の棚卸処理
	/// （<see cref="StocktakeDb.ResolveDays"/>）も同じ経路を再利用しており、本メソッドもそれに倣う。
	/// </para>
	/// <para>
	/// <c>substr(DenDay,1,6)</c> による暦月化は行わない（設計書§2.1で明示的に禁止）。対象期間の算出は
	/// 既存の <see cref="ClosingMonthCalculator.GetPeriod"/> に委譲する。
	/// </para>
	/// </summary>
	public ClosingMonthCalculator.KakeMonthPeriod ResolvePeriod(string targetMonth) {
		var shime = new SummaryDb(_db).GetOwnClosingDay();
		return ClosingMonthCalculator.GetPeriod(targetMonth, shime);
	}

	/// <summary>
	/// <c>MasterSysman.CostMethod</c> を取得する（設計書§2.5.7）。取得経路は
	/// <see cref="SummaryDb.GetOwnClosingDay"/> の <c>ShimeBi</c> 取得と同じ流儀（<c>ORDER BY Id LIMIT 1</c>）に揃える。
	/// </summary>
	private int GetCurrentCostMethod() {
		if (!_db.IsExistTable(typeof(MasterSysman))) {
			throw new InvalidOperationException("MasterSysmanが存在しないため、原価方式を取得できません。");
		}
		return _db.FirstOrDefault<int>($"SELECT CostMethod FROM {nameof(MasterSysman)} ORDER BY Id LIMIT 1");
	}

	// ==================================================================
	// 4-2. 原価解決 ResolveCostAsOf（設計書§2.7、§4.4、§16.5）
	// ==================================================================

	/// <summary>
	/// 商品1件の指定日時点の解決原価を返す（設計書§2.7）。内部的には複数商品版
	/// (<see cref="ResolveCostAsOf(IReadOnlyCollection{long},string,EnumCostMethod,string?)"/>)を1件で呼ぶ薄いラッパ。
	/// </summary>
	/// <param name="idShohin">対象商品Id。</param>
	/// <param name="asOfDay">解決基準日(yyyyMMdd)。<c>TranGenka.EffectiveDay &lt;= asOfDay</c> の履歴だけを見る。</param>
	/// <param name="method">解決に使う原価方式。履歴が無ければ<c>CostMethod=0</c>の基準行へフォールバックする。</param>
	/// <param name="excludeRevalSumMonth">
	/// 非nullのとき、<c>SumMonth = excludeRevalSumMonth AND ChangeKind = Reval</c> の行を解決対象から除外する。
	/// 評価替えの対象抽出(<see cref="ResolveCostAsOf"/>の呼び出し元、設計書§16.5)が、自分自身が今まさに
	/// 書こうとしている当月の評価替え結果を「前回実行済みの結果」として読み込んでしまうと、再実行のたびに
	/// 原価が下がり続ける（評価替え後の原価に対してさらに掛率を適用してしまう）。これを防ぐため、
	/// 評価替えの対象抽出時だけ当月の評価替え行を除外して解決する（設計書§16.5、§2.7）。
	/// </param>
	/// <returns>解決した<c>AfterCost</c>。基準行を含め履歴が1行も無い商品は0を返す
	/// （呼び出し側が「変更しない」と判断できるようにするため、設計書§2.7）。</returns>
	public long ResolveCostAsOf(long idShohin, string asOfDay, EnumCostMethod method, string? excludeRevalSumMonth = null) {
		var map = ResolveCostAsOf([idShohin], asOfDay, method, excludeRevalSumMonth);
		return map.GetValueOrDefault(idShohin, 0L);
	}

	/// <summary>
	/// 複数商品の指定日時点の解決原価をまとめて返す（設計書§2.7）。商品数が多くても
	/// SQL発行は<see cref="IdChunkSize"/>件ごとに1回であり、商品1件ずつSQLを発行しない。
	/// <para>
	/// 解決順は設計書§2.7のとおり
	/// <c>ORDER BY EffectiveDay DESC, SumMonth DESC, ChangeKind DESC, Vdu DESC, Id DESC</c>。
	/// <c>ChangeKind DESC</c> を <c>Vdu DESC</c> より前に置くのは、同一計上月に月次原価計算行と
	/// 評価替え行が並ぶ場合、実行順によらず常に評価替え行を優先させるためである（§13 U-19）。
	/// </para>
	/// <para>
	/// 実装方針: 選択中の方式(<paramref name="method"/>)と基準方式(<c>CostMethod=0</c>)の両方の履歴を
	/// 1回のSQLでまとめて取得し、商品ごとに「選択中の方式の履歴が1件でもあればそれだけを解決対象にし、
	/// 無ければ基準方式の履歴を解決対象にする」というフォールバック規則(設計書§2.6・§2.7)をC#側で適用する。
	/// フォールバックをSQLだけで表現するとウィンドウ関数か2階層の相関サブクエリが要り、
	/// 方言変換(<c>ExecuteDialect</c>/<c>FetchDialect</c>)を通す対象が増えて4方言ぶんの検証範囲が広がる。
	/// 解決順(§2.7)は純粋な比較規則で移植の必要が無いため、1回のSELECTで両方式ぶんの候補行を取得し、
	/// フォールバック判定と最終行の決定はメモリ上のLINQで行う。対象は商品数ぶんの原価履歴に限られ、
	/// 件数は<see cref="IdChunkSize"/>件ごとに区切られるためメモリ上の処理で問題にならない。
	/// </para>
	/// </summary>
	public IReadOnlyDictionary<long, long> ResolveCostAsOf(IReadOnlyCollection<long> idShohins, string asOfDay, EnumCostMethod method, string? excludeRevalSumMonth = null) {
		var result = new Dictionary<long, long>();
		foreach (var chunk in ChunkIds(idShohins)) {
			if (chunk.Count == 0) {
				continue;
			}
			var excludeClause = excludeRevalSumMonth is null
				? string.Empty
				: $" AND NOT (SumMonth = @2 AND ChangeKind = {(int)EnumCostChangeKind.Reval})";
			var sql = $@"
SELECT Id_Shohin, EffectiveDay, SumMonth, ChangeKind, Vdu, Id, CostMethod, AfterCost
FROM {nameof(TranGenka)}
WHERE Id_Shohin IN ({string.Join(",", chunk)})
  AND EffectiveDay <= @0
  AND CostMethod IN (@1, {(int)EnumCostMethod.Fixed})
  {excludeClause}
";
			var args = excludeRevalSumMonth is null
				? [asOfDay, (int)method]
				: new object[] { asOfDay, (int)method, excludeRevalSumMonth };
			var rows = _db.FetchDialect<ResolveCandidateRow>(sql, args);
			foreach (var group in rows.GroupBy(x => x.Id_Shohin)) {
				var methodRows = group.Where(x => x.CostMethod == (int)method).ToList();
				var candidates = methodRows.Count > 0
					? methodRows
					: group.Where(x => x.CostMethod == (int)EnumCostMethod.Fixed).ToList();
				if (candidates.Count == 0) {
					continue;
				}
				var best = candidates
					.OrderByDescending(x => x.EffectiveDay, StringComparer.Ordinal)
					.ThenByDescending(x => x.SumMonth, StringComparer.Ordinal)
					.ThenByDescending(x => x.ChangeKind)
					.ThenByDescending(x => x.Vdu)
					.ThenByDescending(x => x.Id)
					.First();
				result[group.Key] = best.AfterCost;
			}
		}
		return result;
	}

	/// <summary><see cref="ResolveCostAsOf(IReadOnlyCollection{long},string,EnumCostMethod,string?)"/>の候補行1件。</summary>
	private sealed class ResolveCandidateRow {
		public long Id_Shohin { get; set; }
		public string EffectiveDay { get; set; } = string.Empty;
		public string SumMonth { get; set; } = string.Empty;
		public int ChangeKind { get; set; }
		public long Vdu { get; set; }
		public long Id { get; set; }
		public int CostMethod { get; set; }
		public long AfterCost { get; set; }
	}

	// ==================================================================
	// 4-3. 現在原価の反映 RefreshCurrentProductCost（設計書§2.7）
	// ==================================================================

	/// <summary>
	/// 対象商品の現在原価(<c>MasterShohin.TankaGenka</c>)を、設計書§2.7の解決順で求めた最新有効行へ反映する。
	/// <para>
	/// 解決結果が0（履歴が1行も無い）商品は<c>TankaGenka</c>を変更しない（設計書§2.7）。
	/// 過去年月を再実行しても、より新しい<c>EffectiveDay</c>の履歴があれば現在値を過去原価へ戻さない。
	/// これは解決順が<c>EffectiveDay DESC</c>を先頭に置くことで自然に満たされる（テストで固定する）。
	/// </para>
	/// <para>
	/// 更新は商品ごとにUPDATEをN回発行するのではなく、SQLiteの相関サブクエリを使った1本のUPDATE文で行う。
	/// <c>COALESCE</c>で「選択中の方式の最新行」→「基準方式(<c>CostMethod=0</c>)の最新行」の順にフォールバックし、
	/// 対象商品に該当行が1件も無い場合は<c>EXISTS</c>句がfalseになりその行は更新対象から外れる
	/// （<c>UPDATE</c>の<c>WHERE</c>に<c>EXISTS</c>を明示することで、値だけを見ても意図が読めるようにしている）。
	/// </para>
	/// <b>MasterShohinの他の列は書き換えない。</b>
	/// </summary>
	/// <param name="idShohins">対象商品Id。</param>
	/// <param name="method">現在の<c>MasterSysman.CostMethod</c>。呼び出し側が渡す。</param>
	/// <returns>更新した行数。</returns>
	public int RefreshCurrentProductCost(IReadOnlyCollection<long> idShohins, EnumCostMethod method) {
		var vdate = Common.GetVdate();
		var updated = 0;
		foreach (var chunk in ChunkIds(idShohins)) {
			if (chunk.Count == 0) {
				continue;
			}
			var idsCsv = string.Join(",", chunk);
			var sql = $@"
UPDATE {nameof(MasterShohin)}
SET TankaGenka = COALESCE(
      (SELECT g.AfterCost FROM {nameof(TranGenka)} AS g
       WHERE g.Id_Shohin = {nameof(MasterShohin)}.Id AND g.CostMethod = @0
       ORDER BY g.EffectiveDay DESC, g.SumMonth DESC, g.ChangeKind DESC, g.Vdu DESC, g.Id DESC
       LIMIT 1),
      (SELECT g.AfterCost FROM {nameof(TranGenka)} AS g
       WHERE g.Id_Shohin = {nameof(MasterShohin)}.Id AND g.CostMethod = {(int)EnumCostMethod.Fixed}
       ORDER BY g.EffectiveDay DESC, g.SumMonth DESC, g.ChangeKind DESC, g.Vdu DESC, g.Id DESC
       LIMIT 1)
    ),
    Vdu = @1
WHERE {nameof(MasterShohin)}.Id IN ({idsCsv})
  AND EXISTS (
      SELECT 1 FROM {nameof(TranGenka)} AS g2
      WHERE g2.Id_Shohin = {nameof(MasterShohin)}.Id
        AND g2.CostMethod IN (@0, {(int)EnumCostMethod.Fixed})
  )
;";
			updated += _db.ExecuteDialect(sql, (int)method, vdate);
		}
		return updated;
	}

	// ==================================================================
	// 4-4. 基準行の生成（設計書§2.6）
	// ==================================================================

	/// <summary>
	/// 対象商品に<c>TranGenka</c>の履歴が1行も無ければ、実行前の<c>MasterShohin.TankaGenka</c>を
	/// <c>CostMethod=0</c>・<c>ChangeKind=0</c>・<c>SumMonth="190101"</c>・<c>EffectiveDay="19010101"</c>の
	/// 基準行として1行INSERTする（設計書§2.6）。
	/// <para>
	/// 既に履歴がある商品には作らない（冪等）。判定は<c>SumMonth="190101"</c>の一意キーだけでなく、
	/// 対象商品の<c>TranGenka</c>行の有無そのもので行う。基準行以外の月に別方式の履歴を持つ商品へ
	/// 誤って基準行を重ねて作らないようにするためである。
	/// </para>
	/// <para>
	/// 呼び出し側（Step 7の最終仕入原価更新・総平均原価更新）が初回更新時に呼ぶ想定である。
	/// </para>
	/// </summary>
	/// <param name="idShohins">対象商品Id。</param>
	/// <param name="batchId">更新実行Id。</param>
	/// <param name="idShain">実行社員Id。</param>
	/// <returns>作成した基準行数。</returns>
	public int EnsureBaselineCostRows(IReadOnlyCollection<long> idShohins, string batchId, long idShain) {
		var vdate = Common.GetVdate();
		var shain = _db.FirstOrDefault<MasterShain>("WHERE Id=@0", idShain);
		var vShain = shain != null ? new CodeNameView(shain.Id, shain.Code, shain.Name) : new CodeNameView();
		var inserted = 0;
		foreach (var chunk in ChunkIds(idShohins)) {
			if (chunk.Count == 0) {
				continue;
			}
			var idsCsv = string.Join(",", chunk);
			var existingIds = _db.Fetch<long>(
				$"SELECT DISTINCT Id_Shohin FROM {nameof(TranGenka)} WHERE Id_Shohin IN ({idsCsv})").ToHashSet();
			var shohins = _db.Fetch<MasterShohin>($"WHERE Id IN ({idsCsv})");
			foreach (var shohin in shohins) {
				if (existingIds.Contains(shohin.Id)) {
					continue;
				}
				var row = new TranGenka {
					BatchId = batchId,
					SumMonth = "190101",
					EffectiveDay = "19010101",
					CostMethod = (int)EnumCostMethod.Fixed,
					ChangeKind = (int)EnumCostChangeKind.Monthly,
					SourceRevalId = 0,
					Id_Shohin = shohin.Id,
					VShohin = new CodeNameView(shohin.Id, shohin.Code, shohin.Name),
					BeforeCost = 0,
					AfterCost = shohin.TankaGenka,
					Id_Shain = idShain,
					VShain = vShain,
					Vdc = vdate,
					Vdu = vdate,
				};
				_db.Insert(row);
				inserted++;
			}
		}
		return inserted;
	}

	// ==================================================================
	// 4-5. TranGenka の upsert（設計書§2.5.3）
	// ==================================================================

	/// <summary>
	/// 一意キー<c>(SumMonth, Id_Shohin, CostMethod, ChangeKind)</c>に一致する行を置換して<c>TranGenka</c>へ保存する
	/// （設計書§2.5.3「同月・同方式・同ChangeKindの再実行は、一意キーに一致する行を同一トランザクションで置換する」）。
	/// <para>
	/// <b>トランザクションはこの関数では張らない。</b>呼び出し側（Step 7の最終仕入原価更新・総平均原価更新、
	/// Step 8の評価替え）が張った<c>Serializable</c>トランザクションの中で呼ばれる前提である。
	/// </para>
	/// <para>
	/// SQLiteの<c>ON CONFLICT(...) DO UPDATE</c>を使う。<see cref="SummaryDb"/>の既存upsert
	/// （<c>CreateSummaryStockSql</c>等）と同じ作法で、新規行は<c>Vdc</c>/<c>Vdu</c>とも現在時刻を入れ、
	/// 競合時（置換）は<c>Vdc</c>を<c>ON CONFLICT ... DO UPDATE</c>のSET句に含めないことで既存行の値を保ち、
	/// <c>Vdu</c>だけを更新する。
	/// </para>
	/// <para>
	/// <see cref="CodeNameView"/>型の<c>VShohin</c>/<c>VShain</c>は<c>[SerializedColumn]</c>（NPoco）により
	/// <see cref="ExDatabase.Insert{T}(T)"/>等の型付きAPIでは自動的にJSONへ変換されるが、本メソッドは
	/// <c>ON CONFLICT</c>を使うため生SQL（<see cref="ExDatabase.ExecuteDialect"/>）で発行する必要があり、
	/// その経路では自動変換が効かない。そのため<c>JsonConvert.SerializeObject</c>（NPocoの
	/// <c>SerializedColumn</c>が内部で使うのと同じNewtonsoft.Jsonの既定設定）で明示的にシリアライズする。
	/// </para>
	/// </summary>
	/// <param name="rows">保存する<c>TranGenka</c>行。</param>
	/// <returns>保存（置換またはINSERT）した行数。</returns>
	public int UpsertGenkaRows(IReadOnlyCollection<TranGenka> rows) {
		if (rows.Count == 0) {
			return 0;
		}
		var vdate = Common.GetVdate();
		var affected = 0;
		foreach (var row in rows) {
			var sql = $@"
INSERT INTO {nameof(TranGenka)}
  (BatchId, SumMonth, EffectiveDay, CostMethod, ChangeKind, SourceRevalId,
   Id_Shohin, VShohin, BeforeCost, AfterCost, OpeningQty, OpeningAmount,
   PurchaseQty, PurchaseAmount, SundryAmount, SourceTranId, SourceLineNo,
   Id_Shain, VShain, Vdc, Vdu)
VALUES
  (@0, @1, @2, @3, @4, @5, @6, @7, @8, @9, @10, @11, @12, @13, @14, @15, @16, @17, @18, @19, @19)
ON CONFLICT(SumMonth, Id_Shohin, CostMethod, ChangeKind) DO UPDATE SET
  BatchId = excluded.BatchId,
  EffectiveDay = excluded.EffectiveDay,
  SourceRevalId = excluded.SourceRevalId,
  VShohin = excluded.VShohin,
  BeforeCost = excluded.BeforeCost,
  AfterCost = excluded.AfterCost,
  OpeningQty = excluded.OpeningQty,
  OpeningAmount = excluded.OpeningAmount,
  PurchaseQty = excluded.PurchaseQty,
  PurchaseAmount = excluded.PurchaseAmount,
  SundryAmount = excluded.SundryAmount,
  SourceTranId = excluded.SourceTranId,
  SourceLineNo = excluded.SourceLineNo,
  Id_Shain = excluded.Id_Shain,
  VShain = excluded.VShain,
  Vdu = excluded.Vdu
;";
			affected += _db.ExecuteDialect(sql,
				row.BatchId, row.SumMonth, row.EffectiveDay, row.CostMethod, row.ChangeKind, row.SourceRevalId,
				row.Id_Shohin, JsonConvert.SerializeObject(row.VShohin), row.BeforeCost, row.AfterCost,
				row.OpeningQty, row.OpeningAmount, row.PurchaseQty, row.PurchaseAmount, row.SundryAmount,
				row.SourceTranId, row.SourceLineNo, row.Id_Shain, JsonConvert.SerializeObject(row.VShain), vdate);
		}
		return affected;
	}

	// ==================================================================
	// 4-6. 月次状態の都度算出（設計書§2.5.6、U-13の中核）
	// ==================================================================

	/// <summary>
	/// 指定した処理区分1件の月次状態を算出する（設計書§2.5.6）。状態テーブルは存在しない（U-13で廃止）ため、
	/// 成果テーブルから都度算出する。
	/// </summary>
	public CostMonthStatus FetchCostMonthStatus(string sumMonth, EnumCostProcessKind processKind) => processKind switch {
		EnumCostProcessKind.ConsumptionPurchase => FetchConsumptionStatus(sumMonth),
		EnumCostProcessKind.CostUpdate => FetchCostUpdateStatus(sumMonth),
		_ => throw new ArgumentOutOfRangeException(nameof(processKind), processKind, "未定義の原価処理区分です。"),
	};

	/// <summary>対象月の2区分（消化仕入・原価更新）の月次状態をまとめて返す。画面表示は1回の呼び出しで済ませる。</summary>
	public IReadOnlyList<CostMonthStatus> FetchCostMonthStatuses(string sumMonth) => [
		FetchCostMonthStatus(sumMonth, EnumCostProcessKind.ConsumptionPurchase),
		FetchCostMonthStatus(sumMonth, EnumCostProcessKind.CostUpdate),
	];

	/// <summary>
	/// 原価更新(<c>ProcessKind=3</c>)の月次状態を設計書§2.5.6の算出方法1〜4のとおりに算出する。
	/// <para>
	/// <b>件数比較の方式（設計書が算出方法まで明記していないため実装判断が必要な箇所）</b>:
	/// 削除だけが起きた場合は最大<c>Vdu</c>が前進しないため検出できない、と設計書は指摘する一方、
	/// <c>SourceCount</c>を永続化する専用列は無い。本実装では
	/// <b>「その月の最終成功実行が<c>TranGenka</c>へ書いた商品数（＝<c>TranGenka</c>の行数）」</b>を
	/// <c>CostMonthStatus.SourceCount</c>として保存済みの指紋に使い、これを
	/// <b>「現時点で同じ抽出条件を満たす対象商品数」</b>（最終仕入原価は設計書§5.1のとおり
	/// <c>IsStock=1 AND Kubun=10</c>、総平均原価は§6.1のとおり<c>IsStock=1 AND IsPay=1 AND Kubun IN (10,20)</c>の
	/// 仕入がある<c>IsZaiko=1</c>商品の数）と比較する。両者が一致しなければ状態2とする。
	/// 対象商品の仕入明細が丸ごと削除されればこの対象商品数が減るため、Vduの前進が無くても検出できる。
	/// 逆に新規仕入行の追加は通常Vduの前進でも検出されるため、件数比較は主に削除検出の保険として働く。
	/// </para>
	/// </summary>
	private CostMonthStatus FetchCostUpdateStatus(string sumMonth) {
		var status = new CostMonthStatus { SumMonth = sumMonth, ProcessKind = EnumCostProcessKind.CostUpdate };

		var summary = _db.FirstOrDefault<GenkaMonthSummary>($@"
SELECT COUNT(*) AS Cnt, IFNULL(MAX(Vdu), 0) AS MaxVdu
FROM {nameof(TranGenka)}
WHERE SumMonth = @0 AND ChangeKind = @1", sumMonth, (int)EnumCostChangeKind.Monthly);
		if (summary == null || summary.Cnt == 0) {
			status.Status = EnumCostProcessStatus.NotRun;
			return status;
		}

		var latest = _db.FirstOrDefault<GenkaMonthLatest>($@"
SELECT BatchId, CostMethod
FROM {nameof(TranGenka)}
WHERE SumMonth = @0 AND ChangeKind = @1
ORDER BY Vdu DESC, Id DESC
LIMIT 1", sumMonth, (int)EnumCostChangeKind.Monthly);

		var lastRunAt = summary.MaxVdu;
		var costMethod = latest?.CostMethod ?? (int)EnumCostMethod.Fixed;
		status.LastRunAt = lastRunAt;
		status.BatchId = latest?.BatchId ?? string.Empty;
		status.CostMethod = (EnumCostMethod)costMethod;
		status.SourceCount = summary.Cnt;

		var period = ResolvePeriod(sumMonth);
		var currentCostMethod = GetCurrentCostMethod();
		var sourceChanged = HasSourceChangedAfter(period, sumMonth, lastRunAt);
		var currentEligibleCount = FetchEligibleProductCount(period, costMethod);
		var countMismatch = currentEligibleCount != summary.Cnt;

		// 消化仕入(ProcessKind=1)が「再実行要」なら原価更新も無効化する連鎖(設計書§2.5.6手順3、§7の表)。
		// 消化仕入は在庫加算しない(IsStock=0)ため原価更新の対象抽出そのものには含まれないが、
		// 消化仕入の再生成で買掛が変わりうるため、先行処理として無効化する。
		var consumptionRerunRequired = FetchConsumptionStatus(sumMonth).Status == EnumCostProcessStatus.RerunRequired;

		status.Status = currentCostMethod != costMethod || sourceChanged || countMismatch || consumptionRerunRequired
			? EnumCostProcessStatus.RerunRequired
			: EnumCostProcessStatus.Completed;
		return status;
	}

	/// <summary>
	/// 対象期間の在庫加算仕入(<see cref="Tran03Shiire"/>、<c>IsStock=1</c>)・売上(<see cref="Tran00Uriage"/>、
	/// <see cref="Tran01Tenuri"/>)・諸掛明細(<see cref="Tran02Material"/>)、および対象月の
	/// <see cref="SummaryStock"/>に、最終成功時刻<paramref name="lastRunVdu"/>より新しい<c>Vdu</c>の行があるか
	/// （設計書§2.5.6「原価更新」手順3の2番目・5番目の条件）。
	/// </summary>
	private bool HasSourceChangedAfter(ClosingMonthCalculator.KakeMonthPeriod period, string sumMonth, long lastRunVdu) {
		var sql = $@"
SELECT COUNT(*) FROM (
  SELECT 1 FROM {nameof(Tran03Shiire)} WHERE IsStock = 1 AND DenDay BETWEEN @0 AND @1 AND Vdu > @2
  UNION ALL
  SELECT 1 FROM {nameof(Tran00Uriage)} WHERE DenDay BETWEEN @0 AND @1 AND Vdu > @2
  UNION ALL
  SELECT 1 FROM {nameof(Tran01Tenuri)} WHERE DenDay BETWEEN @0 AND @1 AND Vdu > @2
  UNION ALL
  SELECT 1 FROM {nameof(Tran02Material)} WHERE DenDay BETWEEN @0 AND @1 AND Vdu > @2
  UNION ALL
  SELECT 1 FROM {nameof(SummaryStock)} WHERE SumMonth = @3 AND Vdu > @2
) AS c
";
		return _db.FirstOrDefault<int>(sql, period.DayFrom, period.DayTo, lastRunVdu, sumMonth) > 0;
	}

	/// <summary>
	/// 現時点で<paramref name="costMethod"/>の対象抽出条件（設計書§5.1・§6.1）を満たす商品数を数える。
	/// <see cref="FetchCostUpdateStatus"/>の件数比較（削除検出）に使う。
	/// </summary>
	private long FetchEligibleProductCount(ClosingMonthCalculator.KakeMonthPeriod period, int costMethod) {
		var where = costMethod switch {
			(int)EnumCostMethod.LastPurchase => "h.IsStock = 1 AND h.Kubun = 10",
			(int)EnumCostMethod.TotalAverage => "h.IsStock = 1 AND h.IsPay = 1 AND h.Kubun IN (10, 20)",
			// 固定原価等、原価更新の対象になり得ないMがTranGenkaへ月次原価計算行として残っていた場合の保険。
			// 在庫加算仕入の総数で代用する（想定外の状態であり、そもそもRerunRequiredになりやすい方へ倒す）。
			_ => "h.IsStock = 1",
		};
		var sql = $@"
SELECT COUNT(DISTINCT x.Id_Shohin) FROM (
  SELECT CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER) AS Id_Shohin
  FROM {nameof(Tran03Shiire)} AS h CROSS JOIN json_each(h.Jmeisai) AS j
  WHERE {where}
    AND h.DenDay BETWEEN @0 AND @1
    AND json_type(h.Jmeisai) = 'array'
) AS x
JOIN {nameof(MasterShohin)} AS ms ON ms.Id = x.Id_Shohin
WHERE ms.IsZaiko = 1
";
		return _db.FirstOrDefault<long>(sql, period.DayFrom, period.DayTo);
	}

	private sealed class GenkaMonthSummary {
		public int Cnt { get; set; }
		public long MaxVdu { get; set; }
	}

	private sealed class GenkaMonthLatest {
		public string BatchId { get; set; } = string.Empty;
		public int CostMethod { get; set; }
	}
}
