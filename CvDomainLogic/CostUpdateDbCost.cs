using System.Globalization;
using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

/// <summary>
/// 最終仕入原価更新・総平均原価更新（原価4項目 詳細設計 §5、§6、Step 7）。<see cref="CostUpdateDb"/>の分割ファイル。
/// <para>
/// 担当は最終行の決定・原価計算・<see cref="TranGenka"/>への保存本体
/// (<see cref="PreviewLastPurchaseCost"/>/<see cref="ApplyLastPurchaseCost"/>、
/// <see cref="PreviewTotalAverageCost"/>/<see cref="ApplyTotalAverageCost"/>)。
/// 対象期間解決・原価解決・現在原価反映・基準行生成・upsertはStep 4（<c>CostUpdateDb.cs</c>）、
/// 諸掛集計・総平均の前月在庫/当月仕入取得はStep 6（<c>CostUpdateDbSundry.cs</c>）の既存メソッドをそのまま使い、
/// 本ファイルでは再実装しない。
/// </para>
/// </summary>
public partial class CostUpdateDb {
	private const string LastPurchaseLabel = "最終仕入原価更新";
	private const string TotalAverageLabel = "総平均原価更新";

	/// <summary>予想処理秒数(最終仕入原価更新)。対象1か月ぶんの最終行決定・保存のみのため10分を見込む。</summary>
	private const long ExpectedDurationLastPurchaseSeconds = 600;
	/// <summary>
	/// 予想処理秒数(総平均原価更新)。対象月に加え、§6.6の後続月再計算カスケードが月ごとに走り得るため、
	/// 最終仕入原価更新より長めの20分を見込む。
	/// </summary>
	private const long ExpectedDurationTotalAverageSeconds = 1200;

	// ==================================================================
	// 7-0. 共通ヘルパー
	// ==================================================================

	/// <summary>
	/// <paramref name="yyyyMMdd"/>の前日を返す。総平均原価・最終仕入原価の<c>BeforeCost</c>は
	/// 「対象期間<c>DayFrom</c>より前」（設計書§6.2）の履歴を解決する必要があり、
	/// <see cref="CostUpdateDb.ResolveCostAsOf(long,string,EnumCostMethod,string?)"/>は
	/// <c>EffectiveDay &lt;= asOfDay</c>で解決するため、境界を1日前へずらして「より前」を表現する。
	/// </summary>
	private static string PreviousDay(string yyyyMMdd) =>
		DateTime.ParseExact(yyyyMMdd, "yyyyMMdd", CultureInfo.InvariantCulture)
			.AddDays(-1)
			.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	/// <summary><see cref="EnumCostCalcError"/>を画面表示用の日本語メッセージへ変換する。</summary>
	private static string DescribeCostCalcError(EnumCostCalcError error) => error switch {
		EnumCostCalcError.None => string.Empty,
		EnumCostCalcError.NegativeOpeningQty => "前月在庫数が負です。",
		EnumCostCalcError.NonPositiveBeforeCost => "前月在庫があるのに計算前原価が0円以下です。",
		EnumCostCalcError.NonPositiveDenominator => "数量の合計が0以下です。",
		EnumCostCalcError.NonPositiveNumerator => "金額の合計が0以下です。",
		EnumCostCalcError.NonPositiveAfterCost => "計算後原価が0円以下です。",
		EnumCostCalcError.NoPurchaseInPeriod => "対象期間内に対象となる仕入がありません。",
		EnumCostCalcError.PurchaseAmountWithoutQty => "当月仕入額はあるが数量が0です。",
		EnumCostCalcError.SundryOnlyWithoutBase => "当月仕入が無く諸掛だけがあります。",
		EnumCostCalcError.CostMethodMismatch => "現在の原価方式ではこの更新を実行できません。",
		_ => error.ToString(),
	};

	/// <summary><c>MasterSysman.CostMethod</c>の画面表示名。</summary>
	private static string DescribeCostMethod(EnumCostMethod method) => method switch {
		EnumCostMethod.Fixed => "固定原価",
		EnumCostMethod.LastPurchase => "最終仕入原価",
		EnumCostMethod.TotalAverage => "総平均原価",
		_ => method.ToString(),
	};

	/// <summary>
	/// 原価方式の不一致(設計書§2.3。<c>CostMethod=0</c>はどちらも実行不可、1は最終仕入原価専用、
	/// 2は総平均原価専用)を表す唯一のプレビュー行を作る。商品ごとのエラーではなく更新処理全体が
	/// 実行できないことを示すため、商品を特定する列は空のままにする。
	/// </summary>
	private static CostPreviewRow NewCostMethodMismatchRow(string targetMonth, string label, EnumCostMethod currentCostMethod) => new() {
		SumMonth = targetMonth,
		Error = EnumCostCalcError.CostMethodMismatch,
		ErrorMessage = $"現在の原価方式({DescribeCostMethod(currentCostMethod)})では{label}を実行できません。",
	};

	/// <summary>更新不可(全件エラー・確認と異なる等)を表す<see cref="CostUpdateResult"/>を作る。</summary>
	private static CostUpdateResult Failure(CostUpdateParameter param, long startedAt, long errorCount, string label) => new() {
		IsSuccess = false,
		BatchId = param.BatchId,
		TargetMonth = param.TargetMonth,
		UpdatedCount = 0,
		ErrorCount = errorCount,
		Message = $"エラーが{errorCount}件あるため{label}を更新しませんでした。",
		StartedAt = startedAt,
		FinishedAt = Common.GetVdate(),
	};

	/// <summary>実行社員Idから<see cref="TranGenka.VShain"/>に保存する<see cref="CodeNameView"/>を組み立てる。</summary>
	private CodeNameView ResolveVShain(long idShain) {
		var shain = _db.FirstOrDefault<MasterShain>("WHERE Id=@0", idShain);
		return shain != null ? new CodeNameView(shain.Id, shain.Code, shain.Name) : new CodeNameView();
	}

	// ==================================================================
	// 7-1. 最終仕入原価更新（設計書§5）
	// ==================================================================

	/// <summary><see cref="ComputeLastPurchaseForMonth"/>が抽出する最終仕入候補明細1行。</summary>
	private sealed class LastPurchaseCandidateRow {
		public long Id_Shohin { get; set; }
		public long ShiireId { get; set; }
		public string DenDay { get; set; } = string.Empty;
		public int MeisaiNo { get; set; }
		public int Su { get; set; }
		public long Kingaku { get; set; }
	}

	/// <summary>
	/// 商品ごとに最終仕入原価を計算した結果、実際に<see cref="TranGenka"/>へ保存する内容(設計書§5.3)。
	/// エラー行(<see cref="CostCalculator.CalcLastPurchaseCost"/>が失敗した商品)は本プランを作らない。
	/// </summary>
	private sealed record LastPurchasePlan(MasterShohin Shohin, string EffectiveDay, long ShiireId, int MeisaiNo, long BeforeCost, long AfterCost);

	/// <summary>
	/// 対象月1か月ぶんの最終仕入原価を計算する（設計書§5.1〜§5.3）。プレビュー・更新の両方から呼ぶ
	/// 共通ロジックであり、更新時もここでサーバー側の値を再計算する（設計書§2.4-3、DBは変更しない）。
	/// <c>MasterSysman.CostMethod</c>の一致判定は呼び出し側（<see cref="PreviewLastPurchaseCost"/>/
	/// <see cref="ApplyLastPurchaseCost"/>）が行うため、本メソッドはその判定を含まない。
	/// </summary>
	private (List<CostPreviewRow> Rows, Dictionary<long, LastPurchasePlan> Plans, ClosingMonthCalculator.KakeMonthPeriod Period)
		ComputeLastPurchaseForMonth(string targetMonth) {
		var period = ResolvePeriod(targetMonth);
		var rows = new List<CostPreviewRow>();
		var plans = new Dictionary<long, LastPurchasePlan>();

		// 対象(設計書§5.1): IsStock=1、Kubun=10、明細数量>0、商品ID>0、MasterShohin.IsZaiko=1。
		// 仕入返品(20)・値引(30)・その他(99)・消化仕入(IsStock=0)は対象外。
		var sql = $@"
SELECT CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER) AS Id_Shohin,
       h.Id AS ShiireId, h.DenDay,
       CAST(json_extract(j.value, '$.No') AS INTEGER) AS MeisaiNo,
       CAST(json_extract(j.value, '$.Su') AS INTEGER) AS Su,
       CAST(json_extract(j.value, '$.Kingaku') AS INTEGER) AS Kingaku
FROM {nameof(Tran03Shiire)} AS h CROSS JOIN json_each(h.Jmeisai) AS j
JOIN {nameof(MasterShohin)} AS ms ON ms.Id = CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)
WHERE h.IsStock = 1 AND h.Kubun = 10
  AND h.DenDay BETWEEN @0 AND @1
  AND json_type(h.Jmeisai) = 'array'
  AND CAST(json_extract(j.value, '$.Su') AS INTEGER) > 0
  AND CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER) > 0
  AND ms.IsZaiko = 1
";
		var candidates = _db.FetchDialect<LastPurchaseCandidateRow>(sql, period.DayFrom, period.DayTo);
		if (candidates.Count == 0) {
			// 設計書§5.4「対象期間に通常仕入がない商品は更新しない」。1件も無ければ何もしない。
			return (rows, plans, period);
		}

		var shohinById = FetchShohinByIds([.. candidates.Select(c => c.Id_Shohin).Distinct()]);
		var beforeAsOf = PreviousDay(period.DayFrom);

		foreach (var group in candidates.GroupBy(c => c.Id_Shohin)) {
			if (!shohinById.TryGetValue(group.Key, out var shohin)) {
				continue; // 抽出SQLでMasterShohinをJOIN済みのため通常発生しない
			}

			// 最終行の決定(設計書§5.2): DenDay、Tran03Shiire.Id、Tran99Meisai.Noの降順で1明細を決定する。
			// 自前で比較を書かず、CostCalculator.LastPurchaseKeyの比較規則をそのまま使う。
			var best = candidates
				.Where(c => c.Id_Shohin == group.Key)
				.OrderByDescending(c => new CostCalculator.LastPurchaseKey(c.DenDay, c.ShiireId, c.MeisaiNo))
				.First();

			// BeforeCostは「その直前の有効なTranGenka.AfterCost」(設計書§5.3)。履歴が無ければ
			// 初回基準行の原価(=現在のMasterShohin.TankaGenka)とする。実際の基準行はApply側で
			// EnsureBaselineCostRowsが作るため、Previewではその値をここで模擬する。
			var beforeCost = ResolveCostAsOf(group.Key, beforeAsOf, EnumCostMethod.LastPurchase);
			if (beforeCost <= 0) {
				beforeCost = shohin.TankaGenka;
			}

			var calc = CostCalculator.CalcLastPurchaseCost(best.Kingaku, best.Su);

			rows.Add(new CostPreviewRow {
				SumMonth = targetMonth,
				Id_Shohin = group.Key,
				CodeShohin = shohin.Code,
				MeiShohin = shohin.Name,
				BeforeCost = beforeCost,
				AfterCost = calc.AfterCost,
				SourceTranId = best.ShiireId,
				SourceLineNo = best.MeisaiNo,
				SourceDay = best.DenDay,
				Error = calc.Error,
				ErrorMessage = DescribeCostCalcError(calc.Error),
			});

			if (!calc.IsError) {
				plans[group.Key] = new LastPurchasePlan(shohin, best.DenDay, best.ShiireId, best.MeisaiNo, beforeCost, calc.AfterCost);
			}
		}

		return (rows, plans, period);
	}

	/// <summary>
	/// 最終仕入原価更新の確認（プレビュー）。DBは一切変更しない（設計書§2.4-1）。
	/// <c>MasterSysman.CostMethod</c>が1（最終仕入原価）でなければ、更新不可を表す1行だけを返す（設計書§2.3）。
	/// </summary>
	public IReadOnlyList<CostPreviewRow> PreviewLastPurchaseCost(CostUpdateParameter param) {
		var currentCostMethod = (EnumCostMethod)GetCurrentCostMethod();
		if (currentCostMethod != EnumCostMethod.LastPurchase) {
			return [NewCostMethodMismatchRow(param.TargetMonth, LastPurchaseLabel, currentCostMethod)];
		}
		var (rows, _, _) = ComputeLastPurchaseForMonth(param.TargetMonth);
		return rows;
	}

	/// <summary>
	/// 最終仕入原価更新を実行する（設計書§2.4、§5.4、§10.2）。
	/// <para>
	/// 手順: (1) <c>MasterSysman.CostMethod</c>が1でなければ中断する(設計書§2.3)
	/// (2) サーバー側で計算を再実行し(設計書§2.4-3)、1件でもエラーがあれば何も変更せず失敗を返す
	/// (設計書§2.4-2・§10.2) (3) 対象商品が1件も無ければ何もせず成功を返す(設計書§5.4)
	/// (4) 初回更新の商品へ基準行を作り(<see cref="EnsureBaselineCostRows"/>、設計書§2.6)、
	/// <see cref="TranGenka"/>を置換保存する(<see cref="UpsertGenkaRows"/>、設計書§2.5.3) (5) 現在原価へ反映する
	/// (<see cref="RefreshCurrentProductCost"/>、設計書§2.7)。全体を1つの<c>Serializable</c>トランザクションで行う。
	/// </para>
	/// </summary>
	public CostUpdateResult ApplyLastPurchaseCost(CostUpdateParameter param) {
		var startedAt = Common.GetVdate();

		// マニュアル排他制御(設計書§2.4)。Serializableトランザクションを開始する前に取得する。
		var manualLockDb = new ManualLockDb(_db);
		var lockResult = manualLockDb.TryBegin(LastPurchaseLabel, "最終行決定・保存", ExpectedDurationLastPurchaseSeconds);
		if (!lockResult.IsAcquired) {
			return NewManualLockFailure(param, startedAt, LastPurchaseLabel, lockResult.Blocker);
		}
		using var lockHandle = lockResult.Handle!;

		var started = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			started = true;

			var currentCostMethod = (EnumCostMethod)GetCurrentCostMethod();
			if (currentCostMethod != EnumCostMethod.LastPurchase) {
				_db.AbortTransaction();
				started = false;
				return Failure(param, startedAt, 1, LastPurchaseLabel);
			}

			var (rows, plans, _) = ComputeLastPurchaseForMonth(param.TargetMonth);
			var errorCount = rows.Count(r => r.Error != EnumCostCalcError.None);
			if (errorCount > 0) {
				_db.AbortTransaction();
				started = false;
				return Failure(param, startedAt, errorCount, LastPurchaseLabel);
			}

			if (plans.Count == 0) {
				// 設計書§5.4「対象期間に通常仕入がない商品は更新しない」。対象月に対象商品が1件も無い。
				_db.CompleteTransaction();
				started = false;
				manualLockDb.Complete(lockHandle, 0, 0);
				return new CostUpdateResult {
					IsSuccess = true,
					BatchId = param.BatchId,
					TargetMonth = param.TargetMonth,
					UpdatedCount = 0,
					ErrorCount = 0,
					Message = "対象期間に通常仕入がある商品が無いため、更新対象はありません。",
					StartedAt = startedAt,
					FinishedAt = Common.GetVdate(),
				};
			}

			EnsureBaselineCostRows(plans.Keys, param.BatchId, param.Id_Shain);

			var vShain = ResolveVShain(param.Id_Shain);
			var vdate = Common.GetVdate();
			var genkaRows = plans.Values.Select(p => new TranGenka {
				BatchId = param.BatchId,
				SumMonth = param.TargetMonth,
				EffectiveDay = p.EffectiveDay,
				CostMethod = (int)EnumCostMethod.LastPurchase,
				ChangeKind = (int)EnumCostChangeKind.Monthly,
				SourceRevalId = 0,
				Id_Shohin = p.Shohin.Id,
				VShohin = new CodeNameView(p.Shohin.Id, p.Shohin.Code, p.Shohin.Name),
				BeforeCost = (int)p.BeforeCost,
				AfterCost = (int)p.AfterCost,
				// 最終仕入原価方式では使わないため0で保存する(設計書§5.3・§2.5.3)。
				OpeningQty = 0,
				OpeningAmount = 0,
				PurchaseQty = 0,
				PurchaseAmount = 0,
				SundryAmount = 0,
				SourceTranId = p.ShiireId,
				SourceLineNo = p.MeisaiNo,
				Id_Shain = param.Id_Shain,
				VShain = vShain,
				Vdc = vdate,
				Vdu = vdate,
			}).ToList();

			UpsertGenkaRows(genkaRows);
			RefreshCurrentProductCost(plans.Keys, EnumCostMethod.LastPurchase);

			_db.CompleteTransaction();
			started = false;
			manualLockDb.Complete(lockHandle, 0, genkaRows.Count);
			return new CostUpdateResult {
				IsSuccess = true,
				BatchId = param.BatchId,
				TargetMonth = param.TargetMonth,
				UpdatedCount = genkaRows.Count,
				ErrorCount = 0,
				Message = $"{genkaRows.Count}件の最終仕入原価を更新しました。",
				StartedAt = startedAt,
				FinishedAt = Common.GetVdate(),
			};
		}
		catch {
			if (started) {
				_db.AbortTransaction();
			}
			throw;
		}
	}

	// ==================================================================
	// 7-2. 総平均原価更新（設計書§6）
	// ==================================================================

	/// <summary>
	/// 商品ごとに総平均原価を計算した結果、実際に<see cref="TranGenka"/>へ保存する内容(設計書§6.3・§6.4)。
	/// エラー行(<see cref="CostCalculator.CalcTotalAverageCost"/>が失敗した商品)は本プランを作らない。
	/// </summary>
	private sealed record TotalAveragePlan(
		MasterShohin Shohin, long BeforeCost, long AfterCost,
		long OpeningQty, long OpeningAmount, long PurchaseQty, long PurchaseAmount, long SundryAmount);

	/// <summary>
	/// 対象期間に総平均原価更新の対象となる商品Idを抽出する（設計書§6.1）。
	/// <c>MasterShohin.IsZaiko=1</c>かつ、対象期間に<c>IsStock=1</c>、<c>IsPay=1</c>、<c>Kubun</c>が
	/// 10（仕入）または20（仕入返品）の商品仕入がある商品。<c>Tran03Shiire.EnumShiire</c>の実在値は
	/// 10/20/30/99の4つだけのため、<c>10..29</c>のような範囲表現は使わず実在値を列挙する（設計書§6.1）。
	/// </summary>
	private List<long> FetchTotalAverageTargetIds(ClosingMonthCalculator.KakeMonthPeriod period) {
		var sql = $@"
SELECT DISTINCT CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER) AS Id_Shohin
FROM {nameof(Tran03Shiire)} AS h CROSS JOIN json_each(h.Jmeisai) AS j
JOIN {nameof(MasterShohin)} AS ms ON ms.Id = CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)
WHERE h.IsStock = 1 AND h.IsPay = 1 AND h.Kubun IN (10, 20)
  AND h.DenDay BETWEEN @0 AND @1
  AND json_type(h.Jmeisai) = 'array'
  AND ms.IsZaiko = 1
";
		return _db.FetchDialect<long>(sql, period.DayFrom, period.DayTo);
	}

	/// <summary>
	/// 対象月より後に既存の総平均原価(<c>CostMethod=2</c>、<c>ChangeKind=0</c>)履歴を持つ計上月を
	/// 昇順で返す（設計書§6.6「Mより後のCostMethod=2履歴が存在する場合は、後続する履歴月を古い順に再計算する」）。
	/// </summary>
	private List<string> FetchSuccessorMonths(string targetMonth) {
		var sql = $@"
SELECT DISTINCT SumMonth
FROM {nameof(TranGenka)}
WHERE CostMethod = {(int)EnumCostMethod.TotalAverage} AND ChangeKind = {(int)EnumCostChangeKind.Monthly}
  AND SumMonth > @0
ORDER BY SumMonth ASC
";
		return _db.Fetch<string>(sql, targetMonth);
	}

	/// <summary>
	/// 対象月1か月ぶんの総平均原価を計算する（設計書§6.1〜§6.5）。プレビュー・更新の両方、および
	/// §6.6の後続月再計算カスケードから呼ぶ共通ロジックである。
	/// <para>
	/// <paramref name="beforeCostOverrides"/>は、同一カスケード内でまだDBへ書き込んでいない先行月の
	/// 計算結果を「その商品の直前原価」として使うための上書き値である。プレビュー(<see cref="PreviewTotalAverageCost"/>)は
	/// DBを一切変更しないため、先行月の新しい結果をこの辞書に積み上げてから次の月の計算に渡す。
	/// 更新(<see cref="ApplyTotalAverageCost"/>)は月ごとに<see cref="UpsertGenkaRows"/>で実際に書き込んでから
	/// 次の月へ進むため、この辞書は不要（<c>null</c>を渡し、<see cref="ResolveCostAsOf(long,string,EnumCostMethod,string?)"/>が
	/// 直前に書き込んだ実データをそのまま解決する）。
	/// </para>
	/// </summary>
	private (List<CostPreviewRow> Rows, Dictionary<long, TotalAveragePlan> Plans, ClosingMonthCalculator.KakeMonthPeriod Period)
		ComputeTotalAverageForMonth(string sumMonth, IReadOnlyDictionary<long, long>? beforeCostOverrides) {
		var period = ResolvePeriod(sumMonth);
		var rows = new List<CostPreviewRow>();
		var plans = new Dictionary<long, TotalAveragePlan>();

		var targetIds = FetchTotalAverageTargetIds(period);
		if (targetIds.Count == 0) {
			// 設計書§6.5「当月仕入なし → 対象外。前原価を維持」。対象抽出(§6.1)の時点で外れるため
			// エラーにはしない。
			return (rows, plans, period);
		}

		var shohinById = FetchShohinByIds(targetIds);
		// 前月在庫(§6.2)・当月仕入(§6.3)はStep6で切り出し済みのFetchTotalAverageInputsを再利用する。
		var inputs = FetchTotalAverageInputs(period, sumMonth);
		// 諸掛の商品別集計(§3.5)もStep6のSumSundryChargesByShohinを再利用する。
		var sundry = SumSundryChargesByShohin(period);
		var beforeAsOf = PreviousDay(period.DayFrom);

		foreach (var idShohin in targetIds) {
			if (!shohinById.TryGetValue(idShohin, out var shohin)) {
				continue; // 抽出SQLでMasterShohinをJOIN済みのため通常発生しない
			}

			long beforeCost;
			if (beforeCostOverrides != null && beforeCostOverrides.TryGetValue(idShohin, out var overridden)) {
				beforeCost = overridden;
			}
			else {
				// BeforeCostは対象期間DayFromより前の最新TranGenka.AfterCost(設計書§6.2)。
				// 履歴が無ければ初回基準行の原価(=現在のMasterShohin.TankaGenka)とする。
				beforeCost = ResolveCostAsOf(idShohin, beforeAsOf, EnumCostMethod.TotalAverage);
			}
			if (beforeCost <= 0) {
				beforeCost = shohin.TankaGenka;
			}

			var input = inputs.GetValueOrDefault(idShohin);
			var sundryAmount = sundry.GetValueOrDefault(idShohin, 0L);
			var openingAmount = input.OpeningQty * beforeCost;
			// TotalAverageInput.PurchaseAmountには諸掛を含めない。CalcTotalAverageCost側がSundryAmountを
			// 分子へ加算する(設計書§6.3、CostCalculator.csのコメント参照)。TQは加算しない(設計書§6.4)。
			var taInput = new CostCalculator.TotalAverageInput(input.OpeningQty, openingAmount, input.PurchaseQty, input.PurchaseAmount, sundryAmount);
			var calc = CostCalculator.CalcTotalAverageCost(taInput, beforeCost);

			rows.Add(new CostPreviewRow {
				SumMonth = sumMonth,
				Id_Shohin = idShohin,
				CodeShohin = shohin.Code,
				MeiShohin = shohin.Name,
				BeforeCost = beforeCost,
				AfterCost = calc.AfterCost,
				OpeningQty = input.OpeningQty,
				OpeningAmount = openingAmount,
				PurchaseQty = input.PurchaseQty,
				PurchaseAmount = input.PurchaseAmount,
				SundryAmount = sundryAmount,
				Error = calc.Error,
				ErrorMessage = DescribeCostCalcError(calc.Error),
			});

			if (!calc.IsError) {
				plans[idShohin] = new TotalAveragePlan(shohin, beforeCost, calc.AfterCost, input.OpeningQty, openingAmount, input.PurchaseQty, input.PurchaseAmount, sundryAmount);
			}
		}

		return (rows, plans, period);
	}

	/// <summary>
	/// 総平均原価更新の確認（プレビュー）。DBは一切変更しない（設計書§2.4-1）。
	/// <para>
	/// <c>MasterSysman.CostMethod</c>が2（総平均原価）でなければ、更新不可を表す1行だけを返す（設計書§2.3）。
	/// 対象月Mより後に総平均原価の履歴月があれば、古い順に再計算した結果も併せて返す（設計書§6.6）。
	/// DBへは書き込まないため、先行月の計算結果は<c>overrides</c>辞書へ積み上げて次の月の
	/// <c>BeforeCost</c>解決に使う。途中の月でエラーが出た場合はそこで打ち切り、それ以降の月は計算しない
	/// （設計書§6.6「途中月の伝票・在庫が不足またはエラーなら全更新を中断する」）。
	/// </para>
	/// </summary>
	public IReadOnlyList<CostPreviewRow> PreviewTotalAverageCost(CostUpdateParameter param) {
		var currentCostMethod = (EnumCostMethod)GetCurrentCostMethod();
		if (currentCostMethod != EnumCostMethod.TotalAverage) {
			return [NewCostMethodMismatchRow(param.TargetMonth, TotalAverageLabel, currentCostMethod)];
		}

		var months = new List<string> { param.TargetMonth };
		months.AddRange(FetchSuccessorMonths(param.TargetMonth));

		var rows = new List<CostPreviewRow>();
		var overrides = new Dictionary<long, long>();
		foreach (var month in months) {
			var (monthRows, plans, _) = ComputeTotalAverageForMonth(month, overrides);
			rows.AddRange(monthRows);
			if (monthRows.Any(r => r.Error != EnumCostCalcError.None)) {
				break;
			}
			foreach (var (idShohin, plan) in plans) {
				overrides[idShohin] = plan.AfterCost;
			}
		}
		return rows;
	}

	/// <summary>
	/// 総平均原価更新を実行する（設計書§2.4、§6.6、§10.2）。
	/// <para>
	/// 手順: (1) <c>MasterSysman.CostMethod</c>が2でなければ中断する(設計書§2.3) (2) 対象月Mと、
	/// Mより後に既存の総平均原価履歴を持つ月を古い順に列挙する(設計書§6.6) (3) 月ごとにサーバー側で
	/// 計算を再実行し(設計書§2.4-3)、1件でもエラーがあればその時点で全体をロールバックして失敗を返す
	/// (設計書§2.4-2・§6.6・§10.2) (4) エラーが無い月は直ちに<see cref="UpsertGenkaRows"/>で保存し、
	/// 同一トランザクション内で次の月の計算がこの結果を<c>BeforeCost</c>として解決できるようにする
	/// (5) 全ての月が成功したら、更新した商品全体を<see cref="RefreshCurrentProductCost"/>で現在原価へ反映する
	/// (設計書§2.7)。全体を1つの<c>Serializable</c>トランザクションで行う。
	/// </para>
	/// </summary>
	public CostUpdateResult ApplyTotalAverageCost(CostUpdateParameter param) {
		var startedAt = Common.GetVdate();

		// マニュアル排他制御(設計書§2.4)。Serializableトランザクションを開始する前に取得する。
		var manualLockDb = new ManualLockDb(_db);
		var lockResult = manualLockDb.TryBegin(TotalAverageLabel, "対象月決定", ExpectedDurationTotalAverageSeconds);
		if (!lockResult.IsAcquired) {
			return NewManualLockFailure(param, startedAt, TotalAverageLabel, lockResult.Blocker);
		}
		using var lockHandle = lockResult.Handle!;

		var started = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			started = true;

			var currentCostMethod = (EnumCostMethod)GetCurrentCostMethod();
			if (currentCostMethod != EnumCostMethod.TotalAverage) {
				_db.AbortTransaction();
				started = false;
				return Failure(param, startedAt, 1, TotalAverageLabel);
			}

			var months = new List<string> { param.TargetMonth };
			months.AddRange(FetchSuccessorMonths(param.TargetMonth));

			var vShain = ResolveVShain(param.Id_Shain);
			var touchedIds = new HashSet<long>();
			var updatedCount = 0L;
			var totalErrorCount = 0L;

			for (var monthIndex = 0; monthIndex < months.Count; monthIndex++) {
				var month = months[monthIndex];
				// §6.6の後続月再計算カスケードは長時間になり得るため、月ごとに進捗(ColumnName=計上月)を書く。
				// Vduが前進しないと監視タスク(設計書§3.4)に異常と判定されるための対応。
				manualLockDb.Progress(lockHandle, $"総平均原価再計算: {month}", monthIndex + 1);

				var (rows, plans, period) = ComputeTotalAverageForMonth(month, beforeCostOverrides: null);
				var errorCount = rows.Count(r => r.Error != EnumCostCalcError.None);
				if (errorCount > 0) {
					totalErrorCount += errorCount;
					_db.AbortTransaction();
					started = false;
					return Failure(param, startedAt, totalErrorCount, TotalAverageLabel);
				}

				if (plans.Count == 0) {
					continue; // この月は対象商品が無い(対象月以外では通常起こらないが安全のため)
				}

				EnsureBaselineCostRows(plans.Keys, param.BatchId, param.Id_Shain);

				var vdate = Common.GetVdate();
				var genkaRows = plans.Values.Select(p => new TranGenka {
					BatchId = param.BatchId,
					SumMonth = month,
					// EffectiveDayは各計上月のDayFrom(設計書§6.6)。
					EffectiveDay = period.DayFrom,
					CostMethod = (int)EnumCostMethod.TotalAverage,
					ChangeKind = (int)EnumCostChangeKind.Monthly,
					SourceRevalId = 0,
					Id_Shohin = p.Shohin.Id,
					VShohin = new CodeNameView(p.Shohin.Id, p.Shohin.Code, p.Shohin.Name),
					BeforeCost = (int)p.BeforeCost,
					AfterCost = (int)p.AfterCost,
					OpeningQty = p.OpeningQty,
					OpeningAmount = p.OpeningAmount,
					PurchaseQty = p.PurchaseQty,
					PurchaseAmount = p.PurchaseAmount,
					SundryAmount = p.SundryAmount,
					// 総平均原価方式では最終仕入根拠を持たないため0で保存する(設計書§2.5.3)。
					SourceTranId = 0,
					SourceLineNo = 0,
					Id_Shain = param.Id_Shain,
					VShain = vShain,
					Vdc = vdate,
					Vdu = vdate,
				}).ToList();

				UpsertGenkaRows(genkaRows);
				updatedCount += genkaRows.Count;
				foreach (var idShohin in plans.Keys) {
					touchedIds.Add(idShohin);
				}
			}

			if (touchedIds.Count > 0) {
				RefreshCurrentProductCost(touchedIds, EnumCostMethod.TotalAverage);
			}

			_db.CompleteTransaction();
			started = false;
			manualLockDb.Complete(lockHandle, 0, (int)updatedCount);
			var followUpCount = months.Count - 1;
			var message = followUpCount > 0
				? $"{updatedCount}件の総平均原価を更新しました（対象月と再計算した後続{followUpCount}か月分の合計）。"
				: $"{updatedCount}件の総平均原価を更新しました。";
			return new CostUpdateResult {
				IsSuccess = true,
				BatchId = param.BatchId,
				TargetMonth = param.TargetMonth,
				UpdatedCount = updatedCount,
				ErrorCount = 0,
				Message = message,
				StartedAt = startedAt,
				FinishedAt = Common.GetVdate(),
			};
		}
		catch {
			if (started) {
				_db.AbortTransaction();
			}
			throw;
		}
	}
}
