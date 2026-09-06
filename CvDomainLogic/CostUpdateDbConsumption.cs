using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

/// <summary>
/// 消化仕入更新（原価4項目 詳細設計 §4、Step 5）。<see cref="CostUpdateDb"/>の分割ファイル。
/// <para>
/// 担当は対象売上の抽出(§4.3)、生成単価の算出(§4.4)、生成単位ごとの<see cref="Tran03Shiire"/>/
/// <see cref="TranConsumptionPurchaseLink"/>の生成(§4.5)、税額の確定(§4.7)、確認・更新(§2.4・§4.6・§10.2)、
/// 消化仕入(<c>ProcessKind=1</c>)の月次状態算出(§2.5.6)。
/// </para>
/// </summary>
public partial class CostUpdateDb {
	/// <summary>
	/// マニュアル排他制御（`Doc/spec/2026-09-06_マニュアル排他制御_詳細設計.md`§2.4）の一連処理名。
	/// 設計書§2.4の表の値をそのまま使う。
	/// </summary>
	private const string ConsumptionLabel = "消化仕入更新";
	/// <summary>予想処理秒数。対象1か月ぶんの売上抽出と仕入生成のみのため10分を見込む。</summary>
	private const long ExpectedDurationConsumptionSeconds = 600;

	// ==================================================================
	// 5-1. 対象売上の抽出・生成計画（設計書§4.3〜§4.5、§4.7、§4.8）
	// ==================================================================

	/// <summary>
	/// 対象期間の消化仕入対象売上明細1行ぶんの計算結果（正常行）。<see cref="ComputeConsumptionPurchases"/>の
	/// 内部結果であり、Applyが実際の<see cref="Tran03Shiire"/>／<see cref="TranConsumptionPurchaseLink"/>を
	/// 組み立てる材料になる。
	/// </summary>
	private sealed class ConsumptionLinePlan {
		public required EnumConsumptionSourceType SourceType;
		public required long SourceId;
		public required int SourceLineNo;
		public required string SourceDay;
		public required long SourceVdu;
		public required long IdSoko;
		/// <summary>正負区分。売上Kubun 10/11=1、20/21=-1（設計書§4.3）。</summary>
		public required int Sign;
		public required MasterShohin Shohin;
		public required Tran99Meisai SourceMeisai;
		public required long UnitCost;
	}

	/// <summary>
	/// 生成単位（設計書§4.5「売上テーブル種別 + 売上ヘッダID + 仕入先ID + 倉庫ID + 正負区分」）のキー。
	/// 倉庫IDと正負区分は売上ヘッダに対して一意に決まるため、実質的なグループ化の単位は
	/// (SourceType, SourceId, IdShiire)である。
	/// </summary>
	private readonly record struct ConsumptionGroupKey(EnumConsumptionSourceType SourceType, long SourceId, long IdShiire);

	/// <summary>
	/// <see cref="ComputeConsumptionPurchases"/>が返す計算結果全体。
	/// </summary>
	private sealed class ConsumptionComputation {
		public required List<ConsumptionPreviewRow> Rows;
		public required Dictionary<ConsumptionGroupKey, List<ConsumptionLinePlan>> Groups;
		public required ClosingMonthCalculator.KakeMonthPeriod Period;
	}

	/// <summary>
	/// 対象期間の消化仕入対象売上を抽出し、生成単価まで計算する（設計書§4.3・§4.4・§4.8）。
	/// プレビュー(<see cref="PreviewConsumptionPurchases"/>)・更新(<see cref="ApplyConsumptionPurchases"/>)の
	/// 両方から呼ぶ共通ロジックであり、更新時も必ずここでサーバー側の値を再計算する（設計書§2.4-3、DBは変更しない）。
	/// </summary>
	private ConsumptionComputation ComputeConsumptionPurchases(string targetMonth) {
		var period = ResolvePeriod(targetMonth);
		var sysman = _db.FirstOrDefault<MasterSysman>($"SELECT * FROM {nameof(MasterSysman)} ORDER BY Id LIMIT 1");
		var costMethod = (EnumCostMethod)(sysman?.CostMethod ?? (int)EnumCostMethod.Fixed);

		var rows = new List<ConsumptionPreviewRow>();
		var resolved = new List<ConsumptionLinePlan>();

		ProcessSalesTable(EnumConsumptionSourceType.Uriage, nameof(Tran00Uriage), period, costMethod, rows, resolved);
		ProcessSalesTable(EnumConsumptionSourceType.Tenuri, nameof(Tran01Tenuri), period, costMethod, rows, resolved);

		AppendExistingLinkIssues(period, rows);

		var groups = new Dictionary<ConsumptionGroupKey, List<ConsumptionLinePlan>>();
		foreach (var plan in resolved) {
			var key = new ConsumptionGroupKey(plan.SourceType, plan.SourceId, plan.Shohin.Id_ConsignmentShiire);
			if (!groups.TryGetValue(key, out var list)) {
				groups[key] = list = [];
			}
			list.Add(plan);
		}

		return new ConsumptionComputation { Rows = rows, Groups = groups, Period = period };
	}

	/// <summary>
	/// 消化仕入対象売上ヘッダのJSON妥当性だけを見る軽量行（<c>Jmeisai</c>のデシリアライズはしない）。
	/// 不正JSONの行を先に安全に検出するために使う（設計書§4.3、既存<c>CreateSummaryStockSql</c>と同じ
	/// <c>json_valid()</c>・<c>json_type()</c>による防御の作法）。
	/// </summary>
	private sealed class SalesHeaderProbe {
		public long Id { get; set; }
		public string DenDay { get; set; } = string.Empty;
		public int Kubun { get; set; }
		public long Vdu { get; set; }
		public int ValidJson { get; set; }
	}

	/// <summary>
	/// 1テーブル（<see cref="Tran00Uriage"/>または<see cref="Tran01Tenuri"/>）ぶんの対象売上を抽出し、
	/// エラー行を<paramref name="rows"/>へ、計算に成功した行を<paramref name="resolved"/>へ積む。
	/// <para>
	/// <b>実装判断（設計書に算出方法の明記が無い箇所）</b>: 「対象売上」であるかどうかは、まずヘッダ単位で
	/// 判定する（そのヘッダの明細のいずれかが<c>MasterShohin.PurchaseType=3</c>の商品を参照しているか）。
	/// 対象でないヘッダ（消化仕入商品を1行も含まない通常売上）は一切見ない。対象ヘッダに限り、
	/// 設計書§4.3「値引・その他だけの明細、商品ID=0、不正JSONは対象外ではなくエラーにする」を、
	/// ヘッダ内の全明細に対して適用する。対象でない通常商品の行は無関係のため無視する。
	/// </para>
	/// </summary>
	private void ProcessSalesTable(
		EnumConsumptionSourceType sourceType,
		string tableName,
		ClosingMonthCalculator.KakeMonthPeriod period,
		EnumCostMethod costMethod,
		List<ConsumptionPreviewRow> rows,
		List<ConsumptionLinePlan> resolved) {

		var probes = _db.FetchDialect<SalesHeaderProbe>($@"
SELECT Id, DenDay, Kubun, Vdu, json_valid(Jmeisai) AS ValidJson
FROM {tableName}
WHERE DenDay BETWEEN @0 AND @1", period.DayFrom, period.DayTo);

		foreach (var bad in probes.Where(p => p.ValidJson != 1)) {
			rows.Add(NewErrorRow(sourceType, bad.Id, 0, bad.DenDay, 0, "明細データ(Jmeisai)が不正なJSONです。"));
		}

		var validIds = probes.Where(p => p.ValidJson == 1).Select(p => p.Id).ToList();
		if (validIds.Count == 0) {
			return;
		}

		// (Id, DenDay, Kubun, Vdu, Id_Soko, Jmeisai) へ共通化する。Tran00Uriage/Tran01Tenuriは
		// Kubunの意味・値集合(EnumUri00/EnumUri01)が別々でも、ここで使う列の形は同じ
		var headers = new List<(long Id, string DenDay, int Kubun, long Vdu, long IdSoko, List<Tran99Meisai> Jmeisai)>();
		foreach (var chunk in ChunkIds(validIds)) {
			var idsCsv = string.Join(",", chunk);
			if (sourceType == EnumConsumptionSourceType.Uriage) {
				foreach (var h in _db.Fetch<Tran00Uriage>($"WHERE Id IN ({idsCsv})")) {
					headers.Add((h.Id, h.DenDay, h.Kubun, h.Vdu, h.Id_Soko, h.Jmeisai ?? []));
				}
			}
			else {
				foreach (var h in _db.Fetch<Tran01Tenuri>($"WHERE Id IN ({idsCsv})")) {
					headers.Add((h.Id, h.DenDay, h.Kubun, h.Vdu, h.Id_Soko, h.Jmeisai ?? []));
				}
			}
		}

		var shohinIds = headers.SelectMany(h => h.Jmeisai).Select(m => m.Id_Shohin).Where(id => id > 0).Distinct().ToList();
		var shohinById = FetchShohinByIds(shohinIds);
		var shiireById = FetchShiireByIds([.. shohinById.Values.Select(s => s.Id_ConsignmentShiire).Where(id => id > 0).Distinct()]);
		var costAsOfCache = new Dictionary<(long ShohinId, string Day), long>();

		foreach (var header in headers) {
			// 対象Kubunは範囲比較を使わず明示列挙する（設計書§4.3）。10・11=仕入生成、20・21=仕入返品生成、
			// 30・99=値引・その他(消化仕入対象外だが、消化仕入対象商品を含んでいればエラー)。
			var isTargetKubun = header.Kubun is 10 or 11 or 20 or 21;
			var isErrorKubun = header.Kubun is 30 or 99;
			if (!isTargetKubun && !isErrorKubun) {
				continue; // 未定義のKubun。EnumUri00/EnumUri01の定義に無い値のため防御的にスキップする
			}
			var sign = header.Kubun is 10 or 11 ? 1 : -1;

			// このヘッダが消化仕入と無関係(消化仕入対象商品の明細を1行も含まない)なら、他の明細の
			// 商品ID=0等を含め一切エラーにしない(通常売上の大多数を誤って対象にしないため)。
			var touchesConsumption = header.Jmeisai.Any(m =>
				m.Id_Shohin > 0 && shohinById.TryGetValue(m.Id_Shohin, out var sh) && sh.PurchaseType == (int)EnumPurchaseType.Consumption);
			if (!touchesConsumption) {
				continue;
			}

			foreach (var line in header.Jmeisai) {
				var lineNo = line.No > 0 ? line.No : header.Jmeisai.IndexOf(line) + 1;
				if (line.Id_Shohin <= 0) {
					rows.Add(NewErrorRow(sourceType, header.Id, lineNo, header.DenDay, header.Vdu, "商品ID=0の明細は消化仕入対象にできません。"));
					continue;
				}
				if (!shohinById.TryGetValue(line.Id_Shohin, out var shohin) || shohin.PurchaseType != (int)EnumPurchaseType.Consumption) {
					continue; // 消化仕入対象外の通常商品行。この機能とは無関係のため無視する
				}
				if (isErrorKubun) {
					rows.Add(NewErrorRow(sourceType, header.Id, lineNo, header.DenDay, header.Vdu,
						"値引・その他の明細は消化仕入対象にできません。", shohin));
					continue;
				}

				// 設計書§4.2の保存時検査（本来はMasterShohin保存時に検査済みのはずだが、消化仕入更新実行時にも
				// 再検査する。設計書§2.4-3「更新時はサーバーで同じ計算を再実行する」を保存条件にも適用する）。
				if (shohin.Id_ConsignmentShiire <= 0) {
					rows.Add(NewErrorRow(sourceType, header.Id, lineNo, header.DenDay, header.Vdu, "消化仕入先が未設定です。", shohin));
					continue;
				}
				if (!shiireById.TryGetValue(shohin.Id_ConsignmentShiire, out var shiire)) {
					rows.Add(NewErrorRow(sourceType, header.Id, lineNo, header.DenDay, header.Vdu, "消化仕入先が無効です。", shohin));
					continue;
				}
				if (line.Su <= 0) {
					rows.Add(NewErrorRow(sourceType, header.Id, lineNo, header.DenDay, header.Vdu, "売上明細数量が0以下です。", shohin));
					continue;
				}

				long unitCost;
				var calcType = (EnumConsumptionCalcType)shohin.ConsumptionCalcType;
				if (calcType == EnumConsumptionCalcType.CostBased) {
					if (shohin.TankaShiire <= 0 && shohin.TankaGenka <= 0) {
						rows.Add(NewErrorRow(sourceType, header.Id, lineNo, header.DenDay, header.Vdu,
							"消化仕入の計算条件(仕入単価/原価)が未設定です。", shohin));
						continue;
					}
					if (shohin.TankaShiire > 0) {
						unitCost = shohin.TankaShiire;
					}
					else {
						var cacheKey = (shohin.Id, header.DenDay);
						if (!costAsOfCache.TryGetValue(cacheKey, out unitCost)) {
							unitCost = ResolveCostAsOf(shohin.Id, header.DenDay, costMethod);
							costAsOfCache[cacheKey] = unitCost;
						}
						// 履歴が無ければMasterShohin.TankaGenkaへフォールバックする(設計書§4.4)
						if (unitCost <= 0) {
							unitCost = shohin.TankaGenka;
						}
					}
					if (unitCost <= 0) {
						rows.Add(NewErrorRow(sourceType, header.Id, lineNo, header.DenDay, header.Vdu,
							"計算単価が0以下です。", shohin, EnumCostCalcError.NonPositiveAfterCost));
						continue;
					}
				}
				else if (calcType == EnumConsumptionCalcType.RateBased) {
					var calc = CostCalculator.CalcConsumptionUnitCostByRate(
						line.Tanka, shohin.ConsumptionRateBasisPoints, shohin.ConsumptionRoundingUnit, (EnumRounding)shohin.ConsumptionRounding);
					if (calc.IsError) {
						rows.Add(NewErrorRow(sourceType, header.Id, lineNo, header.DenDay, header.Vdu,
							calc.Error == EnumCostCalcError.InvalidRate ? "掛率が範囲外です。" : "計算単価が0以下または上限超過です。",
							shohin, calc.Error));
						continue;
					}
					unitCost = calc.AfterCost;
				}
				else {
					rows.Add(NewErrorRow(sourceType, header.Id, lineNo, header.DenDay, header.Vdu, "消化仕入計算区分が不正です。", shohin));
					continue;
				}

				var kingaku = (long)line.Su * unitCost;
				rows.Add(new ConsumptionPreviewRow {
					SourceType = sourceType,
					SourceId = header.Id,
					SourceLineNo = lineNo,
					SourceDay = header.DenDay,
					Id_Shohin = shohin.Id,
					CodeShohin = shohin.Code,
					MeiShohin = shohin.Name,
					Su = line.Su,
					Id_Shiire = shiire.Id,
					MeiShiire = shiire.Name,
					CalcType = calcType,
					RateBasisPoints = calcType == EnumConsumptionCalcType.RateBased ? shohin.ConsumptionRateBasisPoints : 0,
					UnitCost = unitCost,
					Kingaku = kingaku,
					Error = EnumCostCalcError.None,
					ErrorMessage = string.Empty,
				});

				resolved.Add(new ConsumptionLinePlan {
					SourceType = sourceType,
					SourceId = header.Id,
					SourceLineNo = lineNo,
					SourceDay = header.DenDay,
					SourceVdu = header.Vdu,
					IdSoko = header.IdSoko,
					Sign = sign,
					Shohin = shohin,
					SourceMeisai = line,
					UnitCost = unitCost,
				});
			}
		}
	}

	private static ConsumptionPreviewRow NewErrorRow(
		EnumConsumptionSourceType sourceType, long sourceId, int sourceLineNo, string sourceDay, long sourceVdu,
		string message, MasterShohin? shohin = null, EnumCostCalcError error = EnumCostCalcError.None) => new() {
		SourceType = sourceType,
		SourceId = sourceId,
		SourceLineNo = sourceLineNo,
		SourceDay = sourceDay,
		Id_Shohin = shohin?.Id ?? 0,
		CodeShohin = shohin?.Code ?? string.Empty,
		MeiShohin = shohin?.Name ?? string.Empty,
		Error = error,
		ErrorMessage = message,
	};

	/// <summary>
	/// 設計書§4.8の残り2条件（同一売上明細に複数の既存リンクがある、生成仕入が通常画面で変更されリンク内容と
	/// 一致しない）を、対象期間の既存<see cref="TranConsumptionPurchaseLink"/>から検出して<paramref name="rows"/>へ追加する。
	/// 新規計算(<see cref="ProcessSalesTable"/>)とは独立した「既存データの整合性」チェックであり、
	/// 再実行で削除する前の状態を見て判定する。
	/// </summary>
	private void AppendExistingLinkIssues(ClosingMonthCalculator.KakeMonthPeriod period, List<ConsumptionPreviewRow> rows) {
		if (!_db.IsExistTable(typeof(TranConsumptionPurchaseLink))) {
			return;
		}
		var links = _db.Fetch<TranConsumptionPurchaseLink>(
			$"WHERE SourceDay BETWEEN @0 AND @1", period.DayFrom, period.DayTo);
		if (links.Count == 0) {
			return;
		}

		foreach (var dup in links.GroupBy(l => (l.SourceType, l.SourceId, l.SourceLineNo)).Where(g => g.Count() > 1)) {
			foreach (var link in dup) {
				rows.Add(NewErrorRow((EnumConsumptionSourceType)link.SourceType, link.SourceId, link.SourceLineNo, link.SourceDay, link.SourceVdu,
					"同一売上明細に複数の既存リンクがあります。"));
			}
		}

		var generatedIds = links.Select(l => l.GeneratedShiireId).Where(id => id > 0).Distinct().ToList();
		if (generatedIds.Count == 0) {
			return;
		}
		var generated = new Dictionary<long, Tran03Shiire>();
		foreach (var chunk in ChunkIds(generatedIds)) {
			foreach (var g in _db.Fetch<Tran03Shiire>($"WHERE Id IN ({string.Join(",", chunk)})")) {
				generated[g.Id] = g;
			}
		}
		foreach (var link in links) {
			if (link.GeneratedShiireId <= 0) {
				continue;
			}
			// 生成仕入が既に削除されている場合はエラーにしない(再実行で正しく再生成されるため)。
			// 残っているのに GeneratedKind/IsStock が変わっている場合だけ、通常画面での改変とみなす
			if (generated.TryGetValue(link.GeneratedShiireId, out var shiire)
				&& (shiire.GeneratedKind != (int)EnumGeneratedKind.ConsumptionPurchase || shiire.IsStock != 0)) {
				rows.Add(NewErrorRow((EnumConsumptionSourceType)link.SourceType, link.SourceId, link.SourceLineNo, link.SourceDay, link.SourceVdu,
					"生成仕入が通常の仕入画面で変更されています。リンク内容と一致しません。"));
			}
		}
	}

	private Dictionary<long, MasterShohin> FetchShohinByIds(IReadOnlyCollection<long> ids) {
		var result = new Dictionary<long, MasterShohin>();
		foreach (var chunk in ChunkIds(ids)) {
			foreach (var s in _db.Fetch<MasterShohin>($"WHERE Id IN ({string.Join(",", chunk)})")) {
				result[s.Id] = s;
			}
		}
		return result;
	}

	private Dictionary<long, MasterShiire> FetchShiireByIds(IReadOnlyCollection<long> ids) {
		var result = new Dictionary<long, MasterShiire>();
		foreach (var chunk in ChunkIds(ids)) {
			foreach (var s in _db.Fetch<MasterShiire>($"WHERE Id IN ({string.Join(",", chunk)})")) {
				result[s.Id] = s;
			}
		}
		return result;
	}

	// ==================================================================
	// 5-2. プレビュー・更新（設計書§2.4、§4.6、§10.2）
	// ==================================================================

	/// <summary>
	/// 消化仕入更新の確認（プレビュー）。DBは一切変更しない（設計書§2.4-1）。
	/// </summary>
	public IReadOnlyList<ConsumptionPreviewRow> PreviewConsumptionPurchases(CostUpdateParameter param) =>
		ComputeConsumptionPurchases(param.TargetMonth).Rows;

	/// <summary>
	/// 消化仕入更新を実行する（設計書§4.6、§10.2）。
	/// <para>
	/// 手順: (1) 対象期間が支払計算済み範囲に含まれれば<see cref="ConsumptionPurchasePaidPeriodException"/>で中断する
	/// (2) サーバー側で計算を再実行し(設計書§2.4-3、プレビュー結果は信用しない)、1件でもエラーがあれば
	/// 何も変更せず失敗を返す(設計書§2.4-2・§10.2) (3) 対象期間内の既存リンクと生成仕入(<c>GeneratedKind=1</c>)を
	/// 削除してから現在の売上で再生成する(設計書§4.6) (4) 影響する買掛月次を<see cref="SummaryDb.CalcSummaryKaiKake"/>で
	/// 再計算する。全体を1つの<c>Serializable</c>トランザクションで行い、部分成功を許可しない。
	/// </para>
	/// </summary>
	public CostUpdateResult ApplyConsumptionPurchases(CostUpdateParameter param) {
		var startedAt = Common.GetVdate();

		// マニュアル排他制御(設計書§2.4)。Serializableトランザクションを開始する前に取得する。
		// 取得できなければ例外にせず失敗を返す(業務エラーではなく「今は実行できない」ため)。
		var manualLockDb = new ManualLockDb(_db);
		var lockResult = manualLockDb.TryBegin(ConsumptionLabel, "対象抽出・計算", ExpectedDurationConsumptionSeconds);
		if (!lockResult.IsAcquired) {
			return NewManualLockFailure(param, startedAt, ConsumptionLabel, lockResult.Blocker);
		}
		using var lockHandle = lockResult.Handle!;

		var started = false;
		try {
			_db.BeginTransaction(System.Data.IsolationLevel.Serializable);
			started = true;

			var computation = ComputeConsumptionPurchases(param.TargetMonth);
			if (IsPeriodAlreadyPaid(computation.Period)) {
				throw new ConsumptionPurchasePaidPeriodException(param.TargetMonth);
			}

			var errorCount = computation.Rows.Count(r => r.Error != EnumCostCalcError.None || !string.IsNullOrEmpty(r.ErrorMessage));
			if (errorCount > 0) {
				_db.AbortTransaction();
				started = false;
				return new CostUpdateResult {
					IsSuccess = false,
					BatchId = param.BatchId,
					TargetMonth = param.TargetMonth,
					UpdatedCount = 0,
					ErrorCount = errorCount,
					Message = $"エラーが{errorCount}件あるため更新しませんでした。",
					StartedAt = startedAt,
					FinishedAt = Common.GetVdate(),
				};
			}

			DeleteExistingGenerated(computation.Period);
			var updatedCount = InsertGenerated(computation.Groups, param.BatchId, param.Id_Shain);
			RecalcAffectedKaiKake(computation.Period);

			_db.CompleteTransaction();
			started = false;
			// マニュアル排他制御の終了記録(設計書§2.3)。正常に最後まで到達したときだけ呼ぶ。
			manualLockDb.Complete(lockHandle, 0, updatedCount);
			return new CostUpdateResult {
				IsSuccess = true,
				BatchId = param.BatchId,
				TargetMonth = param.TargetMonth,
				UpdatedCount = updatedCount,
				ErrorCount = 0,
				Message = $"{updatedCount}件の消化仕入を生成しました。",
				StartedAt = startedAt,
				FinishedAt = Common.GetVdate(),
			};
		}
		catch {
			if (started) {
				_db.AbortTransaction();
			}
			// マニュアル排他制御: Completeを呼ばず、行はusing(lockHandle)のDisposeに任せて残す
			// (異常終了として監視タスクまたは強制クリアで解放される。設計書§2.1〜§2.3、ManualLockHandle参照)。
			throw;
		}
	}

	/// <summary>
	/// 対象期間が支払計算済み範囲に含まれるかを判定する（設計書§4.6）。
	/// <para>
	/// 既存の締日整合チェック<see cref="SummaryRebuildClosingCheck.FindMismatches"/>が
	/// 「保存済み<c>SummaryKaiShi.DayTo</c>と現在の自社締日を突合する」方式を採っているのに倣い、
	/// ここでも<see cref="SummaryKaiShi"/>という「支払計算の実行結果を保存した集計テーブル」を都度検査する
	/// 同じ方式を使う。判定は<c>SummaryKaiShi.DayFrom〜DayTo</c>の期間が対象期間と重なる行が1件でもあるか、
	/// という単純な期間重なり判定にする（支払計算はこのテーブルへ1行以上の締請求期間として結果を残すため、
	/// 1件でもあれば「その期間の支払計算が実行済み」とみなせる）。
	/// </para>
	/// </summary>
	private bool IsPeriodAlreadyPaid(ClosingMonthCalculator.KakeMonthPeriod period) {
		if (!_db.IsExistTable(typeof(SummaryKaiShi))) {
			return false;
		}
		var sql = $"SELECT COUNT(*) FROM {nameof(SummaryKaiShi)} WHERE DayFrom <= @1 AND DayTo >= @0";
		return _db.FirstOrDefault<int>(sql, period.DayFrom, period.DayTo) > 0;
	}

	/// <summary>
	/// 対象期間内の既存<see cref="TranConsumptionPurchaseLink"/>と、それが指す<c>GeneratedKind=1</c>の生成仕入を削除する
	/// （設計書§4.6）。売上が既に削除されていてもリンクの<c>SourceDay</c>が残っているため、対象期間の
	/// 古い生成仕入を除去できる。
	/// </summary>
	private void DeleteExistingGenerated(ClosingMonthCalculator.KakeMonthPeriod period) {
		var links = _db.Fetch<TranConsumptionPurchaseLink>(
			$"WHERE SourceDay BETWEEN @0 AND @1", period.DayFrom, period.DayTo);
		if (links.Count == 0) {
			return;
		}
		var shiireIds = links.Select(l => l.GeneratedShiireId).Where(id => id > 0).Distinct().ToList();
		foreach (var chunk in ChunkIds(shiireIds)) {
			_db.ExecuteDialect($"DELETE FROM {nameof(Tran03Shiire)} WHERE Id IN ({string.Join(",", chunk)}) AND GeneratedKind = {(int)EnumGeneratedKind.ConsumptionPurchase}");
		}
		var linkIds = links.Select(l => l.Id).ToList();
		foreach (var chunk in ChunkIds(linkIds)) {
			_db.ExecuteDialect($"DELETE FROM {nameof(TranConsumptionPurchaseLink)} WHERE Id IN ({string.Join(",", chunk)})");
		}
	}

	/// <summary>
	/// 計算済みの生成単位ごとに<see cref="Tran03Shiire"/>と<see cref="TranConsumptionPurchaseLink"/>を作成する
	/// （設計書§4.5・§4.7）。税額は生成単位（ヘッダ1件）ごとに<see cref="TaxCalculator.Apply"/>を1回だけ呼ぶ
	/// （設計書§4.7、<c>TaxCalculator.cs:82</c>の前提どおり）。
	/// </summary>
	private int InsertGenerated(Dictionary<ConsumptionGroupKey, List<ConsumptionLinePlan>> groups, string batchId, long idShain) {
		if (groups.Count == 0) {
			return 0;
		}
		var vdate = Common.GetVdate();
		var shain = _db.FirstOrDefault<MasterShain>("WHERE Id=@0", idShain);
		var vShain = shain != null ? new CodeNameView(shain.Id, shain.Code, shain.Name) : new CodeNameView();
		var shiireById = FetchShiireByIds([.. groups.Keys.Select(k => k.IdShiire).Distinct()]);
		var sysman = _db.FirstOrDefault<MasterSysman>($"SELECT * FROM {nameof(MasterSysman)} ORDER BY Id LIMIT 1");
		var count = 0;

		foreach (var (key, plans) in groups) {
			var first = plans[0];
			var shiire = shiireById.GetValueOrDefault(key.IdShiire);
			var vShiire = shiire != null ? new CodeNameView(shiire.Id, shiire.Code, shiire.Name) : new CodeNameView();

			var meisai = new List<Tran99Meisai>();
			var no = 1;
			foreach (var plan in plans.OrderBy(p => p.SourceLineNo)) {
				var src = plan.SourceMeisai;
				meisai.Add(new Tran99Meisai {
					No = no++,
					Kubun = src.Kubun,
					Id_Shohin = plan.Shohin.Id,
					Code_Shohin = plan.Shohin.Code,
					Mei_Shohin = plan.Shohin.Name,
					Id_Col = src.Id_Col,
					Code_Col = src.Code_Col,
					Mei_Col = src.Mei_Col,
					Id_Siz = src.Id_Siz,
					Code_Siz = src.Code_Siz,
					Mei_Siz = src.Mei_Siz,
					// 数量は正値で保持し、正負はヘッダCalcFlag(Kubun)で表現する(設計書§4.3)
					Su = src.Su,
					Tanka = (int)plan.UnitCost,
					Gedai = (int)plan.UnitCost,
					Jodai = src.Jodai,
					Kingaku = (long)src.Su * plan.UnitCost,
					Id_Tax = plan.Shohin.Id_Tax,
					Id_Shain = idShain,
				});
			}

			var shiireHeader = new Tran03Shiire {
				DenDay = first.SourceDay,
				KakeDay = first.SourceDay,
				Id_Soko = first.IdSoko,
				Id_Shain = idShain,
				VShain = vShain,
				Id_Shiire = key.IdShiire,
				VShiire = vShiire,
				IsPay = 1,
				Kubun = first.Sign > 0 ? (int)EnumShiire.Shiire : (int)EnumShiire.Henpin,
				IsStock = 0,
				GeneratedKind = (int)EnumGeneratedKind.ConsumptionPurchase,
				TaxCalcUnit = shiire?.TaxCalcUnit ?? 0,
				TaxRounding = shiire?.TaxRounding ?? 0,
				Jmeisai = meisai,
				SuTotal = meisai.Sum(m => m.Su),
				KingakuTotal = meisai.Sum(m => m.Kingaku),
				JodaiTotal = meisai.Sum(m => (long)m.Su * m.Jodai),
				GedaiTotal = meisai.Sum(m => (long)m.Su * m.Gedai),
				Memo = "消化仕入更新による自動生成",
			};
			var calcUnit = (EnumTaxCalcUnit)shiireHeader.TaxCalcUnit;
			var rounding = (EnumRounding)shiireHeader.TaxRounding;
			var totals = TaxCalculator.Apply(meisai, TaxRateResolver.CreateRateResolver(sysman, first.SourceDay), calcUnit, rounding);
			shiireHeader.TaxableAmount1 = totals.TaxableAmount1;
			shiireHeader.TaxableAmount2 = totals.TaxableAmount2;
			shiireHeader.TaxableAmount3 = totals.TaxableAmount3;
			shiireHeader.Tax1 = totals.Tax1;
			shiireHeader.Tax2 = totals.Tax2;
			shiireHeader.Tax3 = totals.Tax3;
			shiireHeader.Total = Math.Abs(shiireHeader.KingakuTotal) + totals.TaxTotal;

			_db.Insert(shiireHeader);
			count++;

			for (var i = 0; i < plans.Count; i++) {
				var plan = plans[i];
				_db.Insert(new TranConsumptionPurchaseLink {
					BatchId = batchId,
					SourceType = (int)plan.SourceType,
					SourceId = plan.SourceId,
					SourceLineNo = plan.SourceLineNo,
					SourceDay = plan.SourceDay,
					SourceVdu = plan.SourceVdu,
					GeneratedShiireId = shiireHeader.Id,
					GeneratedLineNo = meisai[i].No,
					Id_Shohin = plan.Shohin.Id,
					Id_Shiire = key.IdShiire,
					Vdc = vdate,
					Vdu = vdate,
				});
			}
		}
		return count;
	}

	/// <summary>影響する買掛月次を既存の<see cref="SummaryDb.CalcSummaryKaiKake"/>経路で再計算する（設計書§4.6）。</summary>
	private void RecalcAffectedKaiKake(ClosingMonthCalculator.KakeMonthPeriod period) {
		var fromYm = period.DayFrom[..6];
		var toYm = period.DayTo[..6];
		new SummaryDb(_db).CalcSummaryKaiKake(fromYm, toYm);
	}

	// ==================================================================
	// 5-3. 消化仕入(ProcessKind=1)の月次状態（設計書§2.5.6）
	// ==================================================================

	/// <summary>対象期間の消化仕入対象売上明細キー。<see cref="FetchConsumptionStatus"/>の突合に使う軽量行。</summary>
	private sealed class ConsumptionTargetKeyRow {
		public int SourceType { get; set; }
		public long SourceId { get; set; }
		public int SourceLineNo { get; set; }
		public long SourceVdu { get; set; }
	}

	/// <summary>
	/// 消化仕入(<c>ProcessKind=1</c>)の月次状態を設計書§2.5.6の算出方法1〜4のとおりに算出する。
	/// </summary>
	private CostMonthStatus FetchConsumptionStatus(string sumMonth) {
		var status = new CostMonthStatus { SumMonth = sumMonth, ProcessKind = EnumCostProcessKind.ConsumptionPurchase };
		var period = ResolvePeriod(sumMonth);

		if (!_db.IsExistTable(typeof(TranConsumptionPurchaseLink))) {
			status.Status = EnumCostProcessStatus.NotRun;
			return status;
		}
		var links = _db.Fetch<TranConsumptionPurchaseLink>(
			$"WHERE SourceDay BETWEEN @0 AND @1", period.DayFrom, period.DayTo);
		if (links.Count == 0) {
			status.Status = EnumCostProcessStatus.NotRun;
			return status;
		}

		var targets = FetchConsumptionTargetKeys(period)
			.ToDictionary(t => (t.SourceType, t.SourceId, t.SourceLineNo), t => t.SourceVdu);
		var linkKeys = links.Select(l => (l.SourceType, l.SourceId, l.SourceLineNo)).ToHashSet();

		// 追加: 対応行の無い売上明細がある。削除・更新: リンクに対応する売上明細が無いか、SourceVduが変わった
		var added = targets.Keys.Any(k => !linkKeys.Contains(k));
		var deletedOrChanged = links.Any(l =>
			!targets.TryGetValue((l.SourceType, l.SourceId, l.SourceLineNo), out var vdu) || vdu != l.SourceVdu);

		var latest = links.OrderByDescending(l => l.Vdc).ThenByDescending(l => l.Id).First();
		status.LastRunAt = links.Max(l => l.Vdc);
		status.BatchId = latest.BatchId;
		status.SourceCount = links.Count;
		status.Status = added || deletedOrChanged ? EnumCostProcessStatus.RerunRequired : EnumCostProcessStatus.Completed;
		return status;
	}

	/// <summary>
	/// 対象期間の消化仕入対象売上明細キー(設計書§4.3。ヘッダKubunは10・11・20・21、
	/// 明細商品はMasterShohin.PurchaseType=3)を、SourceVdu(=売上ヘッダのVdu。明細はJSON埋め込みで
	/// 独立したVduを持たないため)付きで返す。月次状態の突合専用の軽量抽出であり、
	/// <see cref="ProcessSalesTable"/>のようなエラー検査は行わない。
	/// </summary>
	private List<ConsumptionTargetKeyRow> FetchConsumptionTargetKeys(ClosingMonthCalculator.KakeMonthPeriod period) {
		var sql = $@"
SELECT {(int)EnumConsumptionSourceType.Uriage} AS SourceType, t.Id AS SourceId,
       CAST(json_extract(j.value, '$.No') AS INTEGER) AS SourceLineNo, t.Vdu AS SourceVdu
FROM {nameof(Tran00Uriage)} AS t CROSS JOIN json_each(t.Jmeisai) AS j
JOIN {nameof(MasterShohin)} AS ms ON ms.Id = CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)
WHERE t.DenDay BETWEEN @0 AND @1 AND t.Kubun IN (10, 11, 20, 21)
  AND ms.PurchaseType = {(int)EnumPurchaseType.Consumption}
  AND json_type(t.Jmeisai) = 'array'
UNION ALL
SELECT {(int)EnumConsumptionSourceType.Tenuri}, t.Id,
       CAST(json_extract(j.value, '$.No') AS INTEGER), t.Vdu
FROM {nameof(Tran01Tenuri)} AS t CROSS JOIN json_each(t.Jmeisai) AS j
JOIN {nameof(MasterShohin)} AS ms ON ms.Id = CAST(json_extract(j.value, '$.Id_Shohin') AS INTEGER)
WHERE t.DenDay BETWEEN @0 AND @1 AND t.Kubun IN (10, 11, 20, 21)
  AND ms.PurchaseType = {(int)EnumPurchaseType.Consumption}
  AND json_type(t.Jmeisai) = 'array'
";
		return _db.FetchDialect<ConsumptionTargetKeyRow>(sql, period.DayFrom, period.DayTo);
	}
}
