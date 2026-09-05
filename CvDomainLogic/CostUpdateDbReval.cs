using System.Data;
using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

/// <summary>
/// 評価替えの抽出・計算・保存・取消（原価4項目 詳細設計 §16、Step 8）。<see cref="CostUpdateDb"/>の分割ファイル。
/// <para>
/// 担当は適用日の解決(§16.4)、対象抽出と計算(§16.5)、一覧(§16.6)、更新(§16.7)、取消(§16.7「再実行と取消」)。
/// 対象期間解決・原価解決・現在原価反映・基準行生成・upsertはStep 4（<c>CostUpdateDb.cs</c>）、
/// 支払計算済み判定・商品まとめ取得はStep 5（<c>CostUpdateDbConsumption.cs</c>）、
/// エラーメッセージ変換・実行社員のVShain解決はStep 7（<c>CostUpdateDbCost.cs</c>）の既存メソッドを
/// そのまま使い、本ファイルでは再実装しない。
/// </para>
/// <para>
/// 設計書§16.10・§2.4-4の「実行履歴の記録・プロセス間排他・<c>CvFlag</c>」はStep 9の担当のため、本ファイルには
/// 含めない。§2.4-4「確認時点からの変化検知」のうち対象商品の<c>Vdu</c>・自社締日・原価方式の再検査だけは
/// 本ファイルの責務とし、<see cref="CostRevaluationParameter.ConfirmedShohinVdu"/>等（<c>Parameters.cs</c>で
/// 追加）を使って実現する（実装判断の詳細はプロパティのコメントを参照）。
/// </para>
/// </summary>
public partial class CostUpdateDb {
	private const string RevaluationLabel = "評価替え";

	// ==================================================================
	// 8-0. 適用日の解決（設計書§16.4）
	// ==================================================================

	/// <summary>適用日解決の結果。<see cref="ResolveRevaluationPeriod"/>が返す。</summary>
	private sealed record RevalPeriodResolution(string SumMonth, string EffectiveDay, ClosingMonthCalculator.KakeMonthPeriod Period);

	/// <summary>
	/// 適用時点(<see cref="EnumCostRevalApplyPoint"/>)に応じて対象計上月・原価適用日を解決する（設計書§16.4）。
	/// <para>
	/// 適用時点=期末(<see cref="EnumCostRevalApplyPoint.FiscalEnd"/>)は、<c>MasterSysman.FiscalStartDate</c>の
	/// 月を期首月とし、入力計上月が属する会計年度の決算期末月（期首月の1か月前）へ<c>SumMonth</c>を読み替える。
	/// 計算は「入力月の絶対月インデックス」から「期首月と同じ剰余を持つ直近の会計年度開始月インデックス」を
	/// 求め、そこに11か月を足して決算期末月を得る（会計年度は12か月固定という前提）。
	/// この式は期首月が1月（会計年度=暦年）の場合も含めて破綻しない（テストで固定する）。
	/// </para>
	/// <para>
	/// 読み替えた決算期末月が現在時刻(<see cref="DateTime.UtcNow"/>基準のyyyyMM)より未来なら入力エラーとする
	/// （設計書§16.4）。<c>substr(DenDay,1,6)</c>による暦月化は行わない（設計書§2.1）。
	/// </para>
	/// </summary>
	private (RevalPeriodResolution? Resolution, string? ErrorMessage) ResolveRevaluationPeriod(CostRevaluationParameter param) {
		if (param.TargetMonth.Length != 6 || !int.TryParse(param.TargetMonth, out _)) {
			return (null, "対象計上月は6桁の年月(yyyyMM)で指定してください。");
		}

		if (param.ApplyPoint == EnumCostRevalApplyPoint.MonthEnd) {
			var period = ResolvePeriod(param.TargetMonth);
			return (new RevalPeriodResolution(param.TargetMonth, period.DayTo, period), null);
		}

		if (param.ApplyPoint != EnumCostRevalApplyPoint.FiscalEnd) {
			return (null, "未定義の適用時点です。");
		}

		var sysman = _db.FirstOrDefault<MasterSysman>($"SELECT * FROM {nameof(MasterSysman)} ORDER BY Id LIMIT 1");
		if (sysman == null || sysman.FiscalStartDate.Length < 6) {
			return (null, "会計年度の期首日(MasterSysman.FiscalStartDate)が未設定です。");
		}

		var fiscalStartMonth = int.Parse(sysman.FiscalStartDate[4..6]);
		var adjustedSumMonth = ResolveFiscalYearEndMonth(param.TargetMonth, fiscalStartMonth);

		var currentYyyyMM = DateTime.UtcNow.ToString("yyyyMM");
		if (string.CompareOrdinal(adjustedSumMonth, currentYyyyMM) > 0) {
			return (null, $"決算期末月({adjustedSumMonth})が未来月のため、評価替えを実行できません。");
		}

		var fiscalPeriod = ResolvePeriod(adjustedSumMonth);
		return (new RevalPeriodResolution(adjustedSumMonth, fiscalPeriod.DayTo, fiscalPeriod), null);
	}

	/// <summary>
	/// 決算期末月への読み替え(設計書§16.4)の純粋な年月演算だけを取り出したもの。現在時刻に依存する
	/// 「未来月」判定を含まないため、単体テストで<c>202608→202703</c>・<c>202702→202703</c>の2例を
	/// 現在時刻に左右されず固定できる（設計書§16.4「この2例をテストで固定すること」）。
	/// <para>
	/// 入力月・期首月をともに「西暦0年1月を0とする絶対月インデックス」へ変換し、期首月と同じ剰余を持つ
	/// 直近(入力月以下)の会計年度開始月インデックスを求める。決算期末月はその11か月後(=開始から12か月目)。
	/// 期首月が1月(会計年度=暦年)の場合も含めて破綻しない。
	/// </para>
	/// </summary>
	/// <param name="targetMonth">入力計上月 yyyyMM。</param>
	/// <param name="fiscalStartMonth">会計年度の期首月(1～12)。</param>
	/// <returns>入力計上月が属する会計年度の決算期末月 yyyyMM。</returns>
	public static string ResolveFiscalYearEndMonth(string targetMonth, int fiscalStartMonth) {
		var year = int.Parse(targetMonth[..4]);
		var month = int.Parse(targetMonth[4..6]);

		var inputIdx = (year * 12) + (month - 1);
		var startMonth0 = fiscalStartMonth - 1;
		var offset = ((inputIdx - startMonth0) % 12 + 12) % 12;
		var fiscalStartIdx = inputIdx - offset;
		var fiscalEndIdx = fiscalStartIdx + 11;
		var fiscalEndYear = fiscalEndIdx / 12;
		var fiscalEndMonth = (fiscalEndIdx % 12) + 1;
		return $"{fiscalEndYear:D4}{fiscalEndMonth:D2}";
	}

	/// <summary>
	/// 期間解決以外の入力検査（設計書§16.9: 指定方式・率・金額・端数単位）。
	/// </summary>
	private static string? ValidateRevaluationInputs(CostRevaluationParameter param) {
		switch (param.Method) {
			case EnumCostRevaluationMethod.ByRate:
				if (param.RatePercent is < 1 or > 100) {
					return "掛率は1～100の範囲で指定してください。";
				}
				break;
			case EnumCostRevaluationMethod.ByFixed:
				if (param.FixedCost < 1) {
					return "指定単価は1円以上で指定してください。";
				}
				break;
			default:
				return "未定義の指定方式です。";
		}

		if (param.RoundingUnit is not (1 or 10 or 100)) {
			return "端数単位は1、10、100円のいずれかで指定してください。";
		}

		return null;
	}

	// ==================================================================
	// 8-1. 抽出条件のSQL構築（設計書§16.4）
	// ==================================================================

	/// <summary>
	/// <see cref="CostRevaluationCondRow.FieldKind"/>に対応する<c>MasterShohin</c>の比較列を返す。
	/// <c>MasterMeisho</c>由来の項目は<c>[SerializedColumn]</c>のJSON列(<c>CodeNameView</c>)の<c>Cd</c>を
	/// <c>json_extract</c>で取り出す。不正JSON対策(<c>json_valid</c>によるガード)は既存<c>CreateSummaryStockSql</c>等と
	/// 同じ作法。
	/// </summary>
	private static string ResolveCondColumn(int fieldKind) => fieldKind switch {
		(int)EnumCostRevalCondField.ShohinCode => "Code",
		(int)EnumCostRevalCondField.MakerCode => "MakerHin",
		(int)EnumCostRevalCondField.Brand => JsonCdColumn("VBrand"),
		(int)EnumCostRevalCondField.Item => JsonCdColumn("VItem"),
		(int)EnumCostRevalCondField.Maker => JsonCdColumn("VMaker"),
		(int)EnumCostRevalCondField.Season => JsonCdColumn("VSeason"),
		(int)EnumCostRevalCondField.Tenji => JsonCdColumn("VTenji"),
		(int)EnumCostRevalCondField.Material => JsonCdColumn("VMaterial"),
		(int)EnumCostRevalCondField.Country => JsonCdColumn("VCountry"),
		_ => throw new ArgumentOutOfRangeException(nameof(fieldKind), fieldKind, "未定義の抽出条件項目です。"),
	};

	/// <summary>
	/// <c>CvWpfclient/ViewModels/07Haibun/ShopHaibunInputViewModel.cs</c>の<c>JsonCd</c>と同じ形の
	/// JSON列アクセス式を組み立てる（設計書§16.4「既存のAddCodeRange + JsonCd()と同じSQL構築を使う」。
	/// <c>CvWpfclient</c>を参照できないため、同等のヘルパーとして本ファイルへ複製する）。
	/// </summary>
	private static string JsonCdColumn(string column) =>
		$"IFNULL(json_extract(CASE WHEN json_valid({column}) THEN {column} ELSE '{{}}' END, '$.Cd'), '')";

	/// <summary>
	/// <c>ShopHaibunInputViewModel.AddCodeRange</c>と同じ形で、1項目のFrom～To条件節を積む。
	/// </summary>
	private static void AddCondRange(List<string> clauses, List<object> args, string column, string? from, string? to) {
		var normalizedFrom = (from ?? string.Empty).Trim();
		var normalizedTo = (to ?? string.Empty).Trim();
		if (normalizedFrom.Length > 0) {
			clauses.Add($"{column} >= @{args.Count}");
			args.Add(normalizedFrom);
		}
		if (normalizedTo.Length > 0) {
			clauses.Add($"{column} <= @{args.Count}");
			args.Add(normalizedTo);
		}
	}

	/// <summary>
	/// 抽出条件(<see cref="CostRevaluationCondition"/>)からWHERE節を組み立てる。0行なら空文字（全在庫商品が対象、
	/// 設計書§16.4）。
	/// </summary>
	private static string BuildConditionSql(CostRevaluationCondition cond, out object[] args) {
		var clauses = new List<string>();
		var argList = new List<object>();
		foreach (var row in cond.Rows) {
			var column = ResolveCondColumn(row.FieldKind);
			AddCondRange(clauses, argList, column, row.CodeFrom, row.CodeTo);
		}
		args = [.. argList];
		return clauses.Count == 0 ? string.Empty : $"AND {string.Join(" AND ", clauses)}";
	}

	// ==================================================================
	// 8-2. 対象抽出と計算（設計書§16.5）
	// ==================================================================

	/// <summary>対象計上月末在庫数(設計書§16.5「Qty」)。<c>SummaryStock.Su</c>を対象計上月以前で合計する。</summary>
	private sealed class StockQtyRow {
		public long Id_Shohin { get; set; }
		public long Qty { get; set; }
	}

	/// <summary>
	/// <paramref name="sumMonth"/>以前の<c>SummaryStock.Su</c>を商品ごとに合計する（設計書§16.5「Qty」）。
	/// <c>CumulativeSu</c>は本番経路で最新化されないため使わない（設計書§16.5、§6.2と同じ理由、
	/// <see cref="FetchTotalAverageInputs"/>と同じ考え方）。
	/// </summary>
	private Dictionary<long, long> FetchStockQtyAsOf(string sumMonth) {
		var sql = $"SELECT Id_Shohin, SUM(Su) AS Qty FROM {nameof(SummaryStock)} WHERE SumMonth <= @0 GROUP BY Id_Shohin";
		return _db.FetchDialect<StockQtyRow>(sql, sumMonth).ToDictionary(r => r.Id_Shohin, r => r.Qty);
	}

	/// <summary>集計単位(<see cref="EnumCostRevalGroupKey"/>)に対応する商品側の軸(コード・名称)を返す。</summary>
	private static (string Code, string Name) ResolveGroupAxis(MasterShohin shohin, EnumCostRevalGroupKey key) => key switch {
		EnumCostRevalGroupKey.Brand => (shohin.VBrand.Cd, shohin.VBrand.Mei),
		EnumCostRevalGroupKey.Item => (shohin.VItem.Cd, shohin.VItem.Mei),
		EnumCostRevalGroupKey.Season => (shohin.VSeason.Cd, shohin.VSeason.Mei),
		EnumCostRevalGroupKey.Maker => (shohin.VMaker.Cd, shohin.VMaker.Mei),
		EnumCostRevalGroupKey.Tenji => (shohin.VTenji.Cd, shohin.VTenji.Mei),
		_ => (string.Empty, string.Empty),
	};

	/// <summary>商品1件ぶんの計算結果。対象・対象外・エラーのいずれも1行として保持する。</summary>
	private sealed record RevalRow(
		MasterShohin Shohin, long Qty, long BeforeCost, long AfterCost,
		bool IsTarget, string ExcludeReason, EnumCostCalcError Error, string ErrorMessage,
		string GroupCode, string GroupName);

	/// <summary><see cref="ComputeRevaluation"/>の計算結果全体。</summary>
	private sealed class RevalComputation {
		/// <summary>入力段階のエラー。非nullなら他のプロパティは参照しない（設計書§16.9）。</summary>
		public string? InputError { get; init; }
		public List<RevalRow> Rows { get; init; } = [];
		public string SumMonth { get; init; } = string.Empty;
		public string EffectiveDay { get; init; } = string.Empty;
		public ClosingMonthCalculator.KakeMonthPeriod Period { get; init; }
		public int CurrentCostMethod { get; init; }
		public int CurrentShimeBi { get; init; }
		/// <summary>抽出条件(IsZaiko=1、PurchaseType=0、§16.4の条件行)に一致した商品数。0件は「データが存在しません」。</summary>
		public int MatchedCount { get; init; }
		public List<string> InfoMessages { get; init; } = [];
	}

	/// <summary>
	/// 評価替えの対象抽出・計算本体（設計書§16.4・§16.5）。<see cref="PreviewRevaluation"/>・
	/// <see cref="ApplyRevaluation"/>の両方から呼ぶ共通ロジックであり、更新時もここでサーバー側の値を
	/// 再計算する（設計書§2.4-3、DBは変更しない）。
	/// </summary>
	private RevalComputation ComputeRevaluation(CostRevaluationParameter param) {
		var (resolution, periodError) = ResolveRevaluationPeriod(param);
		var validationError = periodError ?? ValidateRevaluationInputs(param);
		if (validationError != null || resolution == null) {
			return new RevalComputation { InputError = validationError ?? "入力値が不正です。" };
		}

		var currentCostMethod = GetCurrentCostMethod();
		var currentShimeBi = new SummaryDb(_db).GetOwnClosingDay();

		var condSql = BuildConditionSql(param.Cond, out var condArgs);
		var matchWhere = $"IsZaiko = 1 AND PurchaseType = {(int)EnumPurchaseType.Normal} {condSql}";
		var matchedIds = _db.FetchDialect<long>($"SELECT Id FROM {nameof(MasterShohin)} WHERE {matchWhere}", condArgs);

		if (matchedIds.Count == 0) {
			return new RevalComputation {
				SumMonth = resolution.SumMonth,
				EffectiveDay = resolution.EffectiveDay,
				Period = resolution.Period,
				CurrentCostMethod = currentCostMethod,
				CurrentShimeBi = currentShimeBi,
				MatchedCount = 0,
				InfoMessages = ["データが存在しません。"],
			};
		}

		var shohinById = FetchShohinByIds(matchedIds);
		var qtyMap = FetchStockQtyAsOf(resolution.SumMonth);
		// 評価替えのBeforeCostは対象計上月時点の解決原価。再実行時の増分適用を防ぐため、同一SumMonthかつ
		// ChangeKind=Revalの行を除外して解決する(設計書§16.5・§2.7)。BeforeCost=0は対象外(§16.5-4)であり、
		// 最終仕入原価・総平均原価のような MasterShohin.TankaGenka へのフォールバックは行わない
		// (§16.9「BeforeCost=0の商品は対象外であり、エラーにはしない」を、母数の水増しなしにそのまま適用するため)。
		var beforeCostMap = ResolveCostAsOf(matchedIds, resolution.EffectiveDay, (EnumCostMethod)currentCostMethod, resolution.SumMonth);

		var rows = new List<RevalRow>();
		var excludeReasonCounts = new Dictionary<string, int>();
		foreach (var id in matchedIds) {
			if (!shohinById.TryGetValue(id, out var shohin)) {
				continue; // 抽出SQLがMasterShohin自身へのSELECTのため通常発生しない
			}

			var qty = qtyMap.GetValueOrDefault(id, 0L);
			var beforeCost = beforeCostMap.GetValueOrDefault(id, 0L);
			var (groupCode, groupName) = ResolveGroupAxis(shohin, param.GroupKey);

			if (qty <= 0) {
				AddExclude(rows, excludeReasonCounts, shohin, qty, beforeCost, 0, "在庫0", groupCode, groupName);
				continue;
			}
			if (beforeCost <= 0) {
				AddExclude(rows, excludeReasonCounts, shohin, qty, beforeCost, 0, "原価0", groupCode, groupName);
				continue;
			}

			var calc = param.Method == EnumCostRevaluationMethod.ByRate
				? CostCalculator.CalcRevalCostByRate(beforeCost, param.RatePercent, param.RoundingUnit, param.Rounding)
				: CostCalculator.CalcRevalCostByFixed(beforeCost, param.FixedCost, param.RoundingUnit, param.Rounding);

			if (calc.IsError) {
				// AfterCost<=0はエラー行(設計書§16.5・§16.9)。対象外ではない。
				rows.Add(new RevalRow(shohin, qty, beforeCost, 0, false, string.Empty, calc.Error, DescribeCostCalcError(calc.Error), groupCode, groupName));
				continue;
			}

			if (!CostCalculator.IsRevalTarget(beforeCost, calc.AfterCost)) {
				// AfterCost>=BeforeCostは対象外(エラーではない、設計書§16.5-5・§16.9)。
				AddExclude(rows, excludeReasonCounts, shohin, qty, beforeCost, calc.AfterCost, "引き下げにならない", groupCode, groupName);
				continue;
			}

			rows.Add(new RevalRow(shohin, qty, beforeCost, calc.AfterCost, true, string.Empty, EnumCostCalcError.None, string.Empty, groupCode, groupName));
		}

		var targetCount = rows.Count(r => r.IsTarget);
		var infoMessages = new List<string>();
		if (targetCount == 0) {
			var reasonSummary = string.Join("、", excludeReasonCounts.Select(kv => $"{kv.Key}{kv.Value}件"));
			infoMessages.Add($"更新対象がありませんでした。(対象外内訳: {reasonSummary})");
		}

		return new RevalComputation {
			Rows = rows,
			SumMonth = resolution.SumMonth,
			EffectiveDay = resolution.EffectiveDay,
			Period = resolution.Period,
			CurrentCostMethod = currentCostMethod,
			CurrentShimeBi = currentShimeBi,
			MatchedCount = matchedIds.Count,
			InfoMessages = infoMessages,
		};
	}

	private static void AddExclude(
		List<RevalRow> rows, Dictionary<string, int> reasonCounts, MasterShohin shohin,
		long qty, long beforeCost, long afterCost, string reason, string groupCode, string groupName) {
		rows.Add(new RevalRow(shohin, qty, beforeCost, afterCost, false, reason, EnumCostCalcError.None, string.Empty, groupCode, groupName));
		reasonCounts[reason] = reasonCounts.GetValueOrDefault(reason) + 1;
	}

	// ==================================================================
	// 8-3. 一覧（設計書§16.6）
	// ==================================================================

	private static RevaluationDetailRow ToDetailRow(RevalRow r) => new() {
		Id_Shohin = r.Shohin.Id,
		CodeShohin = r.Shohin.Code,
		MeiShohin = r.Shohin.Name,
		MeiSeason = r.Shohin.VSeason.Mei,
		MeiBrand = r.Shohin.VBrand.Mei,
		MeiItem = r.Shohin.VItem.Mei,
		Jodai = r.Shohin.TankaJodai,
		Qty = r.Qty,
		BeforeCost = r.BeforeCost,
		AfterCost = r.AfterCost,
		BeforeAmount = r.BeforeCost * r.Qty,
		AfterAmount = r.AfterCost * r.Qty,
		IsTarget = r.IsTarget,
		ExcludeReason = r.ExcludeReason,
		Error = r.Error,
		ErrorMessage = r.ErrorMessage,
	};

	private static RevaluationSummaryRow NewSummaryRow(string code, string name, IReadOnlyCollection<RevalRow> rows) => new() {
		GroupCode = code,
		GroupName = name,
		TargetCount = rows.Count,
		Qty = rows.Sum(r => r.Qty),
		// 元上代金額・在庫金額・評価減後金額は品番単位で算出済みの値を合計する(設計書§16.5「旧実装との丸め位置の差」、§13 U-22)。
		JodaiAmount = rows.Sum(r => r.Shohin.TankaJodai * r.Qty),
		BeforeAmount = rows.Sum(r => r.BeforeCost * r.Qty),
		AfterAmount = rows.Sum(r => r.AfterCost * r.Qty),
	};

	/// <summary>
	/// 計算結果(<see cref="RevalComputation"/>)から確認一覧(<see cref="RevaluationPreviewResult"/>)を組み立てる。
	/// </summary>
	private RevaluationPreviewResult BuildPreviewResult(RevalComputation computation) {
		if (computation.InputError != null) {
			return new RevaluationPreviewResult { InfoMessages = [computation.InputError], ErrorCount = 1 };
		}

		var detailRows = computation.Rows.Select(ToDetailRow).ToList();
		var errorCount = detailRows.Count(r => r.Error != EnumCostCalcError.None);
		var targetRows = computation.Rows.Where(r => r.IsTarget).ToList();

		var summaryRows = targetRows
			.GroupBy(r => (r.GroupCode, r.GroupName))
			.Select(g => NewSummaryRow(g.Key.GroupCode, g.Key.GroupName, [.. g]))
			.OrderBy(r => r.GroupCode, StringComparer.Ordinal)
			.ToList();

		var total = NewSummaryRow(string.Empty, "合計", targetRows);

		return new RevaluationPreviewResult {
			SummaryRows = summaryRows,
			DetailRows = detailRows,
			Total = total,
			ErrorCount = errorCount,
			InfoMessages = computation.InfoMessages,
			ConfirmedShohinVdu = targetRows.ToDictionary(r => r.Shohin.Id, r => r.Shohin.Vdu),
			ConfirmedShimeBi = computation.CurrentShimeBi,
			ConfirmedCostMethod = computation.CurrentCostMethod,
		};
	}

	/// <summary>
	/// 評価替えの確認（プレビュー）。DBは一切変更しない（設計書§2.4-1）。
	/// </summary>
	public RevaluationPreviewResult PreviewRevaluation(CostRevaluationParameter param) => BuildPreviewResult(ComputeRevaluation(param));

	// ==================================================================
	// 8-4. 更新 ApplyRevaluation（設計書§16.7、§2.4）
	// ==================================================================

	private static CostUpdateResult NewRevalFailure(CostRevaluationParameter param, long startedAt, long errorCount, string message) => new() {
		IsSuccess = false,
		BatchId = param.BatchId,
		TargetMonth = param.TargetMonth,
		UpdatedCount = 0,
		ErrorCount = errorCount,
		Message = message,
		StartedAt = startedAt,
		FinishedAt = Common.GetVdate(),
	};

	/// <summary>
	/// 確認時点からの変化を検知する（設計書§2.4-4）。<see cref="CostRevaluationParameter.ConfirmedShohinVdu"/>等が
	/// 指定されていない（<c>null</c>）場合はこの再検査を省略する。
	/// </summary>
	private static string? DetectConfirmMismatch(CostRevaluationParameter param, RevalComputation computation) {
		if (param.ConfirmedShimeBi is int confirmedShimeBi && confirmedShimeBi != computation.CurrentShimeBi) {
			return "確認後に自社締日が変更されました。再確認してください。";
		}
		if (param.ConfirmedCostMethod is int confirmedCostMethod && confirmedCostMethod != computation.CurrentCostMethod) {
			return "確認後に原価方式が変更されました。再確認してください。";
		}
		if (param.ConfirmedShohinVdu is { Count: > 0 } confirmedVdu) {
			var currentVdu = computation.Rows.ToDictionary(r => r.Shohin.Id, r => r.Shohin.Vdu);
			foreach (var (idShohin, expectedVdu) in confirmedVdu) {
				if (!currentVdu.TryGetValue(idShohin, out var vdu) || vdu != expectedVdu) {
					return "確認後に対象商品が更新されました。再確認してください。";
				}
			}
		}
		return null;
	}

	/// <summary>
	/// 評価替えを実行する（設計書§16.7、§2.4）。
	/// <para>
	/// 手順: (1) 入力検査(§16.4・§16.9) (2) 対象期間が支払計算済みなら<see cref="CostRevaluationPaidPeriodException"/>で
	/// 中断(§4.6と同じ扱い) (3) サーバー側で対象抽出・計算を再実行する(§2.4-3) (4) 確認時点からの変化を検知したら中断する
	/// (§2.4-4) (5) エラー行が1件でもあれば更新しない(§2.4-2・§16.9) (6) 条件一致0件・対象0件ならそれぞれの
	/// メッセージで中断する(§16.9) (7) <see cref="TranGenkaReval"/>ヘッダを1行作成し、<see cref="TranGenka"/>を
	/// 一括upsertし、<see cref="RefreshCurrentProductCost"/>で現在原価へ反映する(§2.7)。全体を1つの
	/// <c>Serializable</c>トランザクションで行い、部分成功を許可しない。
	/// </para>
	/// </summary>
	public CostUpdateResult ApplyRevaluation(CostRevaluationParameter param) {
		var startedAt = Common.GetVdate();
		var started = false;
		try {
			_db.BeginTransaction(IsolationLevel.Serializable);
			started = true;

			var computation = ComputeRevaluation(param);
			if (computation.InputError != null) {
				_db.AbortTransaction();
				started = false;
				return NewRevalFailure(param, startedAt, 1, computation.InputError);
			}

			if (IsPeriodAlreadyPaid(computation.Period)) {
				// 消化仕入更新(§4.6)と同じ扱い。catchブロックがstartedを見てAbortTransactionする。
				throw new CostRevaluationPaidPeriodException(param.TargetMonth);
			}

			var mismatch = DetectConfirmMismatch(param, computation);
			if (mismatch != null) {
				_db.AbortTransaction();
				started = false;
				return NewRevalFailure(param, startedAt, 0, mismatch);
			}

			if (computation.MatchedCount == 0) {
				_db.AbortTransaction();
				started = false;
				return NewRevalFailure(param, startedAt, 0, "データが存在しません。");
			}

			var errorCount = computation.Rows.Count(r => r.Error != EnumCostCalcError.None);
			if (errorCount > 0) {
				_db.AbortTransaction();
				started = false;
				return NewRevalFailure(param, startedAt, errorCount, $"エラーが{errorCount}件あるため{RevaluationLabel}を更新しませんでした。");
			}

			var targetRows = computation.Rows.Where(r => r.IsTarget).ToList();
			if (targetRows.Count == 0) {
				_db.AbortTransaction();
				started = false;
				var message = computation.InfoMessages.Count > 0 ? computation.InfoMessages[0] : "更新対象がありませんでした。";
				return NewRevalFailure(param, startedAt, 0, message);
			}

			var vShain = ResolveVShain(param.Id_Shain);
			var vdate = Common.GetVdate();

			var header = new TranGenkaReval {
				BatchId = param.BatchId,
				SumMonth = computation.SumMonth,
				EffectiveDay = computation.EffectiveDay,
				ApplyPoint = (int)param.ApplyPoint,
				CostMethod = computation.CurrentCostMethod,
				Method = (int)param.Method,
				RatePercent = param.Method == EnumCostRevaluationMethod.ByRate ? param.RatePercent : 0,
				FixedCost = param.Method == EnumCostRevaluationMethod.ByFixed ? param.FixedCost : 0,
				RoundingUnit = param.RoundingUnit,
				Rounding = (int)param.Rounding,
				GroupKey = (int)param.GroupKey,
				JCond = param.Cond,
				TargetCount = targetRows.Count,
				TargetQty = targetRows.Sum(r => r.Qty),
				JodaiAmount = targetRows.Sum(r => r.Shohin.TankaJodai * r.Qty),
				BeforeAmount = targetRows.Sum(r => r.BeforeCost * r.Qty),
				AfterAmount = targetRows.Sum(r => r.AfterCost * r.Qty),
				Status = (int)EnumCostRevalStatus.Active,
				Id_Shain = param.Id_Shain,
				VShain = vShain,
				Vdc = vdate,
				Vdu = vdate,
			};
			_db.Insert(header);

			var targetIds = targetRows.Select(r => r.Shohin.Id).ToList();
			// 履歴が1行も無い商品への基準行作成(設計書§2.6)。BeforeCost>0の対象行は既に(選択方式または
			// 基準方式の)履歴を持つため通常は何もしないが、既存のApply系メソッドと同じ手順として呼んでおく。
			EnsureBaselineCostRows(targetIds, param.BatchId, param.Id_Shain);

			var genkaRows = targetRows.Select(r => new TranGenka {
				BatchId = param.BatchId,
				SumMonth = computation.SumMonth,
				EffectiveDay = computation.EffectiveDay,
				CostMethod = computation.CurrentCostMethod,
				ChangeKind = (int)EnumCostChangeKind.Reval,
				SourceRevalId = header.Id,
				Id_Shohin = r.Shohin.Id,
				VShohin = new CodeNameView(r.Shohin.Id, r.Shohin.Code, r.Shohin.Name),
				BeforeCost = (int)r.BeforeCost,
				AfterCost = (int)r.AfterCost,
				// 評価替えは対象計上月末の在庫数・在庫金額をOpeningQty/OpeningAmountへ保持する(設計書§16.7)。
				// それ以外の計算根拠列(PurchaseQty等)は使わないため0で保存する。
				OpeningQty = r.Qty,
				OpeningAmount = r.BeforeCost * r.Qty,
				PurchaseQty = 0,
				PurchaseAmount = 0,
				SundryAmount = 0,
				SourceTranId = 0,
				SourceLineNo = 0,
				Id_Shain = param.Id_Shain,
				VShain = vShain,
				Vdc = vdate,
				Vdu = vdate,
			}).ToList();

			UpsertGenkaRows(genkaRows);
			RefreshCurrentProductCost(targetIds, (EnumCostMethod)computation.CurrentCostMethod);

			_db.CompleteTransaction();
			started = false;
			return new CostUpdateResult {
				IsSuccess = true,
				BatchId = param.BatchId,
				TargetMonth = param.TargetMonth,
				UpdatedCount = genkaRows.Count,
				ErrorCount = 0,
				Message = $"{genkaRows.Count}件の評価替えを更新しました。",
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
	// 8-5. 取消 CancelRevaluation（設計書§16.7「再実行と取消」）
	// ==================================================================

	/// <summary>
	/// 評価替えを取り消す（設計書§16.7「再実行と取消」、§13 U-23）。<c>TranGenkaReval.Status</c>を1(取消)にし、
	/// 当該ヘッダの<c>TranGenka</c>行を削除したうえで<see cref="RefreshCurrentProductCost"/>を再実行する。
	/// ヘッダ行は監査のため残す（設計書§2.5.11）。
	/// <para>
	/// 取消は、当該ヘッダより新しい評価替え（<c>Id</c>がより大きい、かつ<c>Status=Active</c>）が対象商品に
	/// 1件も無い場合のみ許可する。新しい評価替えがある場合は、更新せず失敗を表す
	/// <see cref="CostUpdateResult"/>（<c>IsSuccess=false</c>）を返す（既存の<c>Failure</c>系メソッドと同じ作法。
	/// 支払計算済み中断のような「中断」ではなく、業務規則による拒否のため例外にはしない）。
	/// </para>
	/// </summary>
	public CostUpdateResult CancelRevaluation(long revalId, long idShain) {
		var startedAt = Common.GetVdate();
		var started = false;
		try {
			_db.BeginTransaction(IsolationLevel.Serializable);
			started = true;

			var header = _db.FirstOrDefault<TranGenkaReval>("WHERE Id=@0", revalId);
			if (header == null) {
				_db.AbortTransaction();
				started = false;
				return NewCancelFailure(revalId, startedAt, "対象の評価替え実行が見つかりません。");
			}
			if (header.Status == (int)EnumCostRevalStatus.Canceled) {
				_db.AbortTransaction();
				started = false;
				return NewCancelFailure(revalId, startedAt, "既に取消済みです。");
			}

			var targetIds = _db.Fetch<long>($"SELECT DISTINCT Id_Shohin FROM {nameof(TranGenka)} WHERE SourceRevalId=@0", revalId);

			if (targetIds.Count > 0) {
				var idsCsv = string.Join(",", targetIds);
				// 「当該ヘッダより新しい評価替え」= 対象商品のいずれかを触っている、Status=Activeかつ
				// Idがこのヘッダより大きい(=より後に作成された)TranGenkaReval。1件でもあれば取消不可(設計書§16.7)。
				var newerCount = _db.FirstOrDefault<int>($@"
SELECT COUNT(DISTINCT r.Id)
FROM {nameof(TranGenka)} g
JOIN {nameof(TranGenkaReval)} r ON r.Id = g.SourceRevalId
WHERE g.ChangeKind = {(int)EnumCostChangeKind.Reval}
  AND g.Id_Shohin IN ({idsCsv})
  AND r.Status = {(int)EnumCostRevalStatus.Active}
  AND r.Id > @0
", header.Id);
				if (newerCount > 0) {
					_db.AbortTransaction();
					started = false;
					return NewCancelFailure(revalId, startedAt, "この評価替えより新しい評価替えが対象商品にあるため取消できません。");
				}
			}

			var vdate = Common.GetVdate();
			header.Status = (int)EnumCostRevalStatus.Canceled;
			header.Vdu = vdate;
			_db.Update(header, ["Status", "Vdu"]);

			if (targetIds.Count > 0) {
				_db.ExecuteDialect($"DELETE FROM {nameof(TranGenka)} WHERE SourceRevalId=@0", revalId);
				RefreshCurrentProductCost(targetIds, (EnumCostMethod)GetCurrentCostMethod());
			}

			_db.CompleteTransaction();
			started = false;
			return new CostUpdateResult {
				IsSuccess = true,
				BatchId = header.BatchId,
				TargetMonth = header.SumMonth,
				UpdatedCount = targetIds.Count,
				ErrorCount = 0,
				Message = $"評価替え(BatchId={header.BatchId})を取り消しました。",
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

	private static CostUpdateResult NewCancelFailure(long revalId, long startedAt, string message) => new() {
		IsSuccess = false,
		BatchId = string.Empty,
		TargetMonth = string.Empty,
		UpdatedCount = 0,
		ErrorCount = 1,
		Message = message,
		StartedAt = startedAt,
		FinishedAt = Common.GetVdate(),
	};
}
