using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

/// <summary>
/// 諸掛（原価4項目 詳細設計 §3、Step 6）。<see cref="CostUpdateDb"/>の分割ファイル。
/// <para>
/// 担当は諸掛の商品別集計（§3.4・§3.5、<see cref="SumSundryChargesByShohin"/>）と、生地付属仕入への
/// 入力内容の確認（§3.8、<see cref="PreviewSundryCharges"/>）。諸掛は独立した更新処理を持たない
/// 参照専用画面（§3.8「更新ボタンを持たない参照専用画面」）のため、本ファイルにApply系メソッドは存在しない。
/// 総平均原価更新（Step 7）が実行時に<see cref="SumSundryChargesByShohin"/>を直接呼んで分子へ加算する。
/// </para>
/// </summary>
public partial class CostUpdateDb {
	// ==================================================================
	// 6-1. 商品別集計 SumSundryChargesByShohin（設計書§3.4、§3.5）
	// ==================================================================

	/// <summary>
	/// 対象期間の諸掛を商品別に集計する（設計書§3.5）。
	/// <para>
	/// 抽出条件（設計書§3.4）: <see cref="Tran02Material"/>の<c>DenDay BETWEEN DayFrom AND DayTo</c>、
	/// <c>Kubun IN (10, 20)</c>（10=正、20=負）、明細<c>Id_Shohin &gt; 0</c>の行だけを対象にする。
	/// <c>Kubun=30</c>（値引）と<c>Kubun=99</c>（その他）は対象外とする。<c>Tran02Material</c>の
	/// <c>Kubun=99</c>は<see cref="Tran03Shiire"/>と異なり仕入ではなく消費税へ全額計上する特殊挙動を持つため
	/// （<c>CvBase/BaseDb2Trans.cs</c>の<see cref="Tran02Material"/>クラス注釈）、これを諸掛の金額集計に
	/// 混ぜると税額そのものを原価へ計上してしまう。金額は税抜<c>明細.Kingaku × ヘッダ.CalcFlag</c>のみで、
	/// <c>Tax1/2/3</c>・<c>Total</c>は含めない。
	/// </para>
	/// <para>
	/// 按分・端数処理・差額調整はいずれも行わない（設計書§3.5）。入力された金額をそのまま合計する
	/// 純関数であり、同じ入力（対象期間のデータ）に対して常に同じ結果を返す。何度呼んでも対象期間の
	/// データが変わらない限り値は増減しない（冪等）。
	/// </para>
	/// <para>
	/// 明細は<c>json_each(Jmeisai)</c>経由でSQL1本で集計する。<c>json_type(h.Jmeisai) = 'array'</c>で
	/// 不正JSONを防御する（既存<c>CreateSummaryStockSql</c>等と同じ作法）。商品数ぶんSQLをN回発行しない。
	/// </para>
	/// </summary>
	/// <param name="period">対象期間（<see cref="CostUpdateDb.ResolvePeriod"/>で解決したもの）。</param>
	/// <returns>商品Id→諸掛金額（符号付き）。対象行が無い商品はキーを持たない。</returns>
	public IReadOnlyDictionary<long, long> SumSundryChargesByShohin(ClosingMonthCalculator.KakeMonthPeriod period) {
		var sql = $@"
SELECT CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER) AS Id_Shohin,
       SUM(CAST(json_extract(j.value, '$.Kingaku') AS INTEGER) * h.CalcFlag) AS Amount
FROM {nameof(Tran02Material)} AS h CROSS JOIN json_each(h.Jmeisai) AS j
WHERE h.DenDay BETWEEN @0 AND @1
  AND h.Kubun IN (10, 20)
  AND json_type(h.Jmeisai) = 'array'
  AND CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER) > 0
GROUP BY Id_Shohin
";
		var rows = _db.FetchDialect<SundryAggRow>(sql, period.DayFrom, period.DayTo);
		return rows.ToDictionary(r => r.Id_Shohin, r => r.Amount);
	}

	/// <summary><see cref="SumSundryChargesByShohin"/>の集計行。</summary>
	private sealed class SundryAggRow {
		public long Id_Shohin { get; set; }
		public long Amount { get; set; }
	}

	// ==================================================================
	// 6-2. 総平均原価の分子・分母（設計書§6.2・§6.3。Step7 総平均原価更新から再利用する）
	// ==================================================================

	/// <summary>
	/// 総平均原価更新の分子・分母（設計書§6.2「前月在庫」・§6.3「当月仕入」）を商品ごとにまとめて取得する。
	/// <b>Step7 の総平均原価更新から再利用する</b>ことを想定した private ヘルパーであり、本ファイル
	/// （諸掛確認画面の§6.5判定・集計行表示）専用にしない。
	/// <para>
	/// 前月在庫（設計書§6.2）は<c>SummaryStock.Su</c>を<c>SumMonth &lt; targetMonth</c>で合算する。
	/// <c>SummaryStock.CumulativeSu</c>は本番経路（<c>SummaryAllAsyncStream</c>）で最新化される保証がないため
	/// 使わない（設計書§6.2、<c>CvDomainLogic/StocktakeDb.cs:550</c>に既記の既知の問題と同じ理由）。
	/// </para>
	/// <para>
	/// 当月仕入（設計書§6.1・§6.3）は<see cref="Tran03Shiire"/>の<c>IsStock=1 AND IsPay=1 AND Kubun IN (10, 20)</c>を
	/// 対象期間で合算する。諸掛（<see cref="SumSundryChargesByShohin"/>）はここに含めない。
	/// 呼び出し側が別途加算する（設計書§6.3「<c>PurchaseAmount += SundryAmount[商品]</c>」）。
	/// </para>
	/// </summary>
	/// <param name="period">対象期間。</param>
	/// <param name="targetMonth">対象計上月 yyyyMM。前月在庫の判定に使う。</param>
	private Dictionary<long, TotalAverageInputs> FetchTotalAverageInputs(ClosingMonthCalculator.KakeMonthPeriod period, string targetMonth) {
		var result = new Dictionary<long, TotalAverageInputs>();

		var purchaseSql = $@"
SELECT CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER) AS Id_Shohin,
       SUM(CAST(json_extract(j.value, '$.Su') AS INTEGER) * h.CalcFlag) AS PurchaseQty,
       SUM(CAST(json_extract(j.value, '$.Kingaku') AS INTEGER) * h.CalcFlag) AS PurchaseAmount
FROM {nameof(Tran03Shiire)} AS h CROSS JOIN json_each(h.Jmeisai) AS j
WHERE h.IsStock = 1 AND h.IsPay = 1 AND h.Kubun IN (10, 20)
  AND h.DenDay BETWEEN @0 AND @1
  AND json_type(h.Jmeisai) = 'array'
GROUP BY Id_Shohin
";
		foreach (var row in _db.FetchDialect<PurchaseAggRow>(purchaseSql, period.DayFrom, period.DayTo)) {
			result[row.Id_Shohin] = new TotalAverageInputs(row.PurchaseQty, row.PurchaseAmount, 0);
		}

		var openingSql = $"SELECT Id_Shohin, SUM(Su) AS OpeningQty FROM {nameof(SummaryStock)} WHERE SumMonth < @0 GROUP BY Id_Shohin";
		foreach (var row in _db.FetchDialect<OpeningAggRow>(openingSql, targetMonth)) {
			result[row.Id_Shohin] = result.GetValueOrDefault(row.Id_Shohin) with { OpeningQty = row.OpeningQty };
		}
		return result;
	}

	/// <summary><see cref="FetchTotalAverageInputs"/>が商品ごとに返す当月仕入・前月在庫。既定値は全て0。</summary>
	private readonly record struct TotalAverageInputs(long PurchaseQty, long PurchaseAmount, long OpeningQty);

	/// <summary><see cref="FetchTotalAverageInputs"/>の当月仕入集計行。</summary>
	private sealed class PurchaseAggRow {
		public long Id_Shohin { get; set; }
		public long PurchaseQty { get; set; }
		public long PurchaseAmount { get; set; }
	}

	/// <summary><see cref="FetchTotalAverageInputs"/>の前月在庫集計行。</summary>
	private sealed class OpeningAggRow {
		public long Id_Shohin { get; set; }
		public long OpeningQty { get; set; }
	}

	// ==================================================================
	// 6-3. 確認 PreviewSundryCharges（設計書§3.8、§8.2）
	// ==================================================================

	/// <summary><see cref="PreviewSundryCharges"/>が使う<see cref="Tran02Material"/>ヘッダの軽量行。</summary>
	private sealed class MaterialHeaderProbe {
		public long Id { get; set; }
		public string DenDay { get; set; } = string.Empty;
		public int Kubun { get; set; }
		public long Id_Shiire { get; set; }
		public int ValidJson { get; set; }
	}

	/// <summary>判定重みの大小比較。複数条件に該当する行・商品は最も重い区分だけを表示区分に採る。</summary>
	private static EnumSundryCheckSeverity MaxSeverity(EnumSundryCheckSeverity a, EnumSundryCheckSeverity b) =>
		(EnumSundryCheckSeverity)Math.Max((int)a, (int)b);

	/// <summary>
	/// 諸掛の確認（設計書§3.8、§8.2）。DBを一切変更しない参照専用画面であり、更新(Apply)に相当する
	/// メソッドは存在しない（設計書§3.8「更新ボタンを持たない参照専用画面」）。
	/// <para>
	/// 判定順序: (1) <c>Tran02Material</c>を対象期間で抽出し、不正JSONの伝票をエラー行にする
	/// (2) <c>Kubun</c>10・20の明細で<c>Id_Shohin</c>を集計単位ごとに集約する（設計書§3.4の対象範囲）
	/// (3) 商品単位で「商品マスタに存在しない」「<c>IsZaiko=0</c>」「対象月の在庫加算仕入も前月在庫も無い」
	/// （設計書§6.5と同じ判定、<see cref="FetchTotalAverageInputs"/>を再利用）をエラーとして判定する
	/// (4) 明細単位で「<c>Id_Shohin=0</c>」「<c>Kingaku=0</c>」「<c>Kubun=30/99</c>への<c>Id_Shohin</c>入力」を
	/// 警告として判定する (5) 商品単位のエラーと明細単位の警告を合成して明細行の表示区分にする
	/// (6) 現在の原価方式・対象月の諸掛明細0件を情報メッセージにする。
	/// </para>
	/// </summary>
	public SundryChargeCheckResult PreviewSundryCharges(CostUpdateParameter param) {
		var period = ResolvePeriod(param.TargetMonth);
		var costMethod = (EnumCostMethod)GetCurrentCostMethod();

		// json_valid()による防御(設計書§2.5.2、既存CreateSummaryStockSql等と同じ作法)。
		var probes = _db.FetchDialect<MaterialHeaderProbe>($@"
SELECT Id, DenDay, Kubun, Id_Shiire, json_valid(Jmeisai) AS ValidJson
FROM {nameof(Tran02Material)}
WHERE DenDay BETWEEN @0 AND @1", period.DayFrom, period.DayTo);

		var detailRows = new List<SundryChargeDetailRow>();
		foreach (var bad in probes.Where(p => p.ValidJson != 1)) {
			detailRows.Add(new SundryChargeDetailRow {
				Id_Material_Slip = bad.Id,
				DenNo = bad.Id,
				DenDay = bad.DenDay,
				Kubun = bad.Kubun,
				Id_Shiire = bad.Id_Shiire,
				Severity = EnumSundryCheckSeverity.Error,
				ErrorMessage = "明細データ(Jmeisai)が不正なJSONです。",
			});
		}

		var validIds = probes.Where(p => p.ValidJson == 1).Select(p => p.Id).ToList();
		var headers = new List<Tran02Material>();
		foreach (var chunk in ChunkIds(validIds)) {
			headers.AddRange(_db.Fetch<Tran02Material>($"WHERE Id IN ({string.Join(",", chunk)})"));
		}

		var shiireById = FetchShiireByIds([.. headers.Select(h => h.Id_Shiire).Where(id => id > 0).Distinct()]);
		var materialById = FetchMaterialByIds([.. headers.SelectMany(h => h.Jmeisai ?? []).Select(m => m.Id_Material).Where(id => id > 0).Distinct()]);
		var shohinById = FetchShohinByIds([.. headers.SelectMany(h => h.Jmeisai ?? []).Select(m => m.Id_Shohin).Where(id => id > 0).Distinct()]);

		// 明細単位の一次判定(Id_Shohin=0、Kingaku=0、Kubun=30/99への入力)と、Kubun 10/20・Id_Shohin>0の
		// 行だけを対象にした商品別集計(§3.4・§3.5と同じ範囲)を1回のループでまとめて行う。
		var pending = new List<(Tran02Material Header, Tran99MaterialMeisai Line, int LineNo, EnumSundryCheckSeverity Severity, List<string> Messages)>();
		var perProduct = new Dictionary<long, (long Count, long Amount)>();

		foreach (var header in headers) {
			var lines = header.Jmeisai ?? [];
			// 設計書§3.8は「諸掛費目の明細で Id_Shohin=0」を警告とするが、CV10のデータ構造には
			// 「その明細が諸掛費目かどうか」を示すフラグが無い(§3.4は Id_Shohin>0 の行だけを諸掛と定め、
			// Id_Shohin=0 は通常の生地付属仕入として対象外とする)。Id_Shohin=0 を無条件に警告すると、
			// 諸掛と無関係な通常の資材購入まで全件が警告になり、本当の入力漏れが埋もれる。
			// そこで「同一伝票に Id_Shohin>0 の明細が1行でもある」ことを諸掛伝票の判定に使い、
			// その伝票の中の Id_Shohin=0 行だけを入力漏れの疑いとして警告する。
			// 消化仕入(§4.3)の対象売上判定で、消化仕入商品の明細を1行でも含むヘッダだけを検査対象に
			// している(CostUpdateDbConsumption.cs)のと同じ考え方である。
			var isSundrySlip = lines.Any(x => x.Id_Shohin > 0);
			for (var i = 0; i < lines.Count; i++) {
				var line = lines[i];
				var lineNo = line.No > 0 ? line.No : i + 1;
				var isTargetKubun = header.Kubun is 10 or 20;
				var isOutOfScopeKubun = header.Kubun is 30 or 99;
				var severity = EnumSundryCheckSeverity.Info;
				var messages = new List<string>();

				if (isTargetKubun) {
					if (line.Id_Shohin <= 0) {
						if (isSundrySlip) {
							severity = MaxSeverity(severity, EnumSundryCheckSeverity.Warning);
							messages.Add("費用を負担する商品が未入力です(入力漏れの可能性)。集計対象外です。");
						}
					}
					else {
						var agg = perProduct.GetValueOrDefault(line.Id_Shohin);
						perProduct[line.Id_Shohin] = (agg.Count + 1, agg.Amount + line.Kingaku * header.CalcFlag);
						if (line.Kingaku == 0) {
							severity = MaxSeverity(severity, EnumSundryCheckSeverity.Warning);
							messages.Add("金額が0円です。");
						}
					}
				}
				else if (isOutOfScopeKubun && line.Id_Shohin > 0) {
					severity = MaxSeverity(severity, EnumSundryCheckSeverity.Warning);
					messages.Add("値引・その他の区分のため諸掛の対象外です。");
				}

				pending.Add((header, line, lineNo, severity, messages));
			}
		}

		// 商品単位のエラー判定(設計書§3.8「商品マスタに存在しない」「IsZaiko=0」「対象月の在庫加算仕入も
		// 前月在庫も無い」(§6.5と同じ理由=総平均原価更新の分母が0のまま分子だけ増える))。
		var inputs = FetchTotalAverageInputs(period, param.TargetMonth);
		var productSeverity = new Dictionary<long, (EnumSundryCheckSeverity Severity, List<string> Messages)>();
		foreach (var idShohin in perProduct.Keys) {
			var severity = EnumSundryCheckSeverity.Info;
			var messages = new List<string>();
			if (!shohinById.TryGetValue(idShohin, out var shohin)) {
				severity = MaxSeverity(severity, EnumSundryCheckSeverity.Error);
				messages.Add("商品マスタに存在しません。");
			}
			else if (shohin.IsZaiko == 0) {
				severity = MaxSeverity(severity, EnumSundryCheckSeverity.Error);
				messages.Add("商品が在庫管理対象外(IsZaiko=0)のため諸掛を計上できません。");
			}
			var input = inputs.GetValueOrDefault(idShohin);
			if (input.PurchaseQty == 0 && input.OpeningQty == 0) {
				severity = MaxSeverity(severity, EnumSundryCheckSeverity.Error);
				messages.Add("対象月の在庫加算仕入も前月在庫も無いため、総平均原価更新の分母が0になります。");
			}
			productSeverity[idShohin] = (severity, messages);
		}

		foreach (var (header, line, lineNo, lineSeverity, lineMessages) in pending) {
			var severity = lineSeverity;
			var messages = new List<string>(lineMessages);
			if (line.Id_Shohin > 0 && productSeverity.TryGetValue(line.Id_Shohin, out var prod)) {
				severity = MaxSeverity(severity, prod.Severity);
				messages.AddRange(prod.Messages);
			}
			shiireById.TryGetValue(header.Id_Shiire, out var shiire);
			materialById.TryGetValue(line.Id_Material, out var material);
			shohinById.TryGetValue(line.Id_Shohin, out var shohin);

			detailRows.Add(new SundryChargeDetailRow {
				Id_Material_Slip = header.Id,
				DenNo = header.Id,
				DenDay = header.DenDay,
				Kubun = header.Kubun,
				Id_Shiire = header.Id_Shiire,
				MeiShiire = shiire?.Name ?? string.Empty,
				MeisaiNo = lineNo,
				Id_Material = line.Id_Material,
				MeiMaterial = material?.Name ?? line.Mei_Material,
				Id_Shohin = line.Id_Shohin,
				CodeShohin = shohin?.Code ?? line.Code_Shohin,
				MeiShohin = shohin?.Name ?? line.Mei_Shohin,
				Su = line.Su,
				Kingaku = line.Kingaku * header.CalcFlag,
				Severity = severity,
				ErrorMessage = string.Join(" ", messages),
			});
		}

		var summaryRows = new List<SundryChargeSummaryRow>();
		foreach (var (idShohin, agg) in perProduct) {
			shohinById.TryGetValue(idShohin, out var shohin);
			var input = inputs.GetValueOrDefault(idShohin);
			var (severity, messages) = productSeverity.GetValueOrDefault(idShohin, (EnumSundryCheckSeverity.Info, []));
			summaryRows.Add(new SundryChargeSummaryRow {
				Id_Shohin = idShohin,
				CodeShohin = shohin?.Code ?? string.Empty,
				MeiShohin = shohin?.Name ?? string.Empty,
				SundryCount = agg.Count,
				SundryAmount = agg.Amount,
				PurchaseQty = input.PurchaseQty,
				PurchaseAmount = input.PurchaseAmount,
				OpeningQty = input.OpeningQty,
				Severity = severity,
				ErrorMessage = string.Join(" ", messages),
			});
		}

		var infoMessages = new List<string>();
		if (costMethod == EnumCostMethod.LastPurchase) {
			infoMessages.Add("現在の原価方式は最終仕入原価です。入力された諸掛は原価に反映されません(総平均原価方式でのみ算入されます)。");
		}
		if (perProduct.Count == 0) {
			infoMessages.Add("対象月に諸掛明細がありません。");
		}

		return new SundryChargeCheckResult {
			DetailRows = detailRows,
			SummaryRows = summaryRows,
			InfoMessages = infoMessages,
			ErrorCount = detailRows.Count(r => r.Severity == EnumSundryCheckSeverity.Error),
			WarningCount = detailRows.Count(r => r.Severity == EnumSundryCheckSeverity.Warning),
		};
	}

	/// <summary>商品Id集合からMasterMaterialをまとめて取得する。<see cref="PreviewSundryCharges"/>専用。</summary>
	private Dictionary<long, MasterMaterial> FetchMaterialByIds(IReadOnlyCollection<long> ids) {
		var result = new Dictionary<long, MasterMaterial>();
		foreach (var chunk in ChunkIds(ids)) {
			foreach (var m in _db.Fetch<MasterMaterial>($"WHERE Id IN ({string.Join(",", chunk)})")) {
				result[m.Id] = m;
			}
		}
		return result;
	}
}
