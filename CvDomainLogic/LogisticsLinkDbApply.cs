using System.Data;
using CvAsset;
using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;

namespace CvDomainLogic;

// L03 反映（仕様 4.4）と L04 の行操作（仕様 4.5）
public partial class LogisticsLinkDb {
	/// <summary>
	/// 受信バッチを反映する。反映前に再検査し、未処理の行を伝票（まとめ単位）ごとに1トランザクションで反映する。
	/// 1つの伝票の失敗で他の伝票を巻き戻さない。失敗した伝票の行はエラーにする。
	/// </summary>
	public LogisticsRunResult ApplyReceiveBatch(LogisticsApplyParam param, long idShain) {
		ArgumentNullException.ThrowIfNull(param);
		return RunLocked($"連携データ受信 反映 {param.BatchId}", param.ExecType, () => {
			var batch = RequireReceiveBatch(param.BatchId);
			_db.BeginTransaction();
			try {
				CheckAndSave(batch);
				_db.CompleteTransaction();
			}
			catch {
				_db.AbortTransaction();
				throw;
			}
			var lines = _db.Fetch<TranLogisticsLine>("where Id_Batch = @0 and Status = @1 order by LineNo, Id",
				batch.Id, (int)EnumLogisticsLineStatus.Pending);
			var groups = batch.DataKind switch {
				LogisticsDataKind.ORDERFIX or LogisticsDataKind.LACK => GroupOrderFix(lines),
				LogisticsDataKind.STOCKFIX => [.. lines.GroupBy(l => (l.WorkDay, l.RefTable, l.RefId)).Select(g => g.ToList())],
				LogisticsDataKind.INVENTORY => [.. lines.GroupBy(l => (l.WorkDay, l.Id_Soko, TanaNoOf(l))).Select(g => g.ToList())],
				_ => throw new ArgumentException($"反映できない種別です: {batch.DataKind}"),
			};
			var orphan = lines.Except(groups.SelectMany(g => g)).ToList();
			if (orphan.Count > 0) {
				MarkApplyError(orphan, "配分がありません。");
			}
			_uriMonths.Clear();
			_kaiMonths.Clear();
			var slips = 0;
			var failed = orphan.Count;
			var seq = 0;
			foreach (var group in groups) {
				ReportProgress($"反映 {++seq}/{groups.Count}", seq);
				string? error;
				_db.BeginTransaction(IsolationLevel.Serializable);
				try {
					// 伝票トランザクションの中で行を確保する。排他が外れて並行に反映されても、確保できなかった側は何も書かない
					var claimed = _db.Execute(
						$"UPDATE {nameof(TranLogisticsLine)} SET Status = @0, Vdu = @1 WHERE Id IN ({string.Join(",", group.Select(l => l.Id))}) AND Status = @2",
						(int)EnumLogisticsLineStatus.Applied, Common.GetVdate(), (int)EnumLogisticsLineStatus.Pending);
					error = claimed != group.Count ? "他の処理で反映中または反映済みです。"
						: batch.DataKind switch {
							LogisticsDataKind.STOCKFIX => ApplyStockFix(group, idShain),
							LogisticsDataKind.INVENTORY => ApplyInventory(group, idShain),
							_ => ApplyOrderFix(group, idShain),
						};
					if (error == null) {
						var vdate = Common.GetVdate();
						foreach (var line in group) {
							line.Status = (int)EnumLogisticsLineStatus.Applied;
							line.Vdu = vdate;
							_db.Update(line);
						}
						_db.CompleteTransaction();
						slips++;
						continue;
					}
					_db.AbortTransaction();
				}
				catch (Exception ex) {
					_db.AbortTransaction();
					_logger.LogError(ex, "物流連携 反映失敗 Batch={BatchId} Line={LineId}", batch.Id, group[0].Id);
					error = $"反映できませんでした: {SafeMessage(ex)}";
				}
				failed += group.Count;
				MarkApplyError(group, error);
			}
			RecalcKakeSummaries();
			UpdateReceiveBatchSummary(batch);
			return new LogisticsRunResult([new LogisticsKindResult(batch.DataKind, lines.Count - failed, batch.Id, batch.FileName)],
				failed > 0 ? [$"{failed:N0}行を反映できませんでした（エラー E40）。原因を確認して再反映してください。"] : [],
				$"反映 {slips:N0}伝票 {lines.Count - failed:N0}行 / エラー {batch.ErrorCount:N0}行");
		}, r => r.Kinds.Sum(k => k.Count));
	}

	/// <summary>反映で作った出荷売上・仕入の掛月（反映後にまとめて1回だけ集計し直す）</summary>
	private readonly SortedSet<string> _uriMonths = [];
	private readonly SortedSet<string> _kaiMonths = [];

	/// <summary>売掛・買掛の集計を掛月の範囲でまとめて1回ずつ引き直す（HHT取込と同じ）。失敗しても月次再集計で直せる</summary>
	private void RecalcKakeSummaries() {
		if (_uriMonths.Count == 0 && _kaiMonths.Count == 0) {
			return;
		}
		var summaryDb = new SummaryDb(_db);
		_db.BeginTransaction();
		try {
			if (_uriMonths.Count > 0) {
				summaryDb.CalcSummaryUriKake(_uriMonths.Min!, _uriMonths.Max!);
			}
			if (_kaiMonths.Count > 0) {
				summaryDb.CalcSummaryKaiKake(_kaiMonths.Min!, _kaiMonths.Max!);
			}
			_db.CompleteTransaction();
		}
		catch (Exception ex) {
			_db.AbortTransaction();
			_logger.LogError(ex, "物流連携 売掛・買掛の集計に失敗しました。月次再集計で直してください。");
		}
	}

	/// <summary>反映できなかった行をエラー(E40)にする（伝票トランザクションの外で書く）</summary>
	private void MarkApplyError(List<TranLogisticsLine> group, string error) {
		var vdate = Common.GetVdate();
		foreach (var line in group) {
			line.Status = (int)EnumLogisticsLineStatus.Error;
			line.ErrorCode = "E40";
			line.ErrorMsg = Truncate1000(error);
			line.TargetTable = string.Empty;
			line.TargetId = 0;
			line.Vdu = vdate;
			_db.Update(line);
		}
	}

	/// <summary>
	/// バッチの件数と状態を更新する。未処理・エラーが残らなければ反映済み（除外だけでも反映済み）、
	/// 残っていて1行でも適用済みなら一部エラー、まだ1行も適用していなければ取込済み。
	/// </summary>
	private void UpdateReceiveBatchSummary(TranLogisticsBatch batch) {
		batch.OkCount = CountLines(batch.Id, EnumLogisticsLineStatus.Applied);
		batch.ErrorCount = CountLines(batch.Id, EnumLogisticsLineStatus.Error);
		var remaining = batch.ErrorCount + CountLines(batch.Id, EnumLogisticsLineStatus.Pending);
		batch.Status = remaining == 0 ? (int)EnumLogisticsReceiveStatus.Applied
			: batch.OkCount > 0 ? (int)EnumLogisticsReceiveStatus.PartialError
			: (int)EnumLogisticsReceiveStatus.Imported;
		batch.Vdu = Common.GetVdate();
		_db.Update(batch);
	}

	/// <summary>出荷確定のまとめ単位: 出荷日 × 配分確定の伝票キー（<see cref="HaibunHeaderKey"/>）</summary>
	private List<List<TranLogisticsLine>> GroupOrderFix(List<TranLogisticsLine> lines) {
		var ids = lines.Select(l => l.RefId).Distinct().ToList();
		var haibun = ids.Count == 0 ? [] : _db.Fetch<TranHaibun>($"where Id in ({string.Join(",", ids)})").ToDictionary(h => h.Id);
		return [.. lines.Where(l => haibun.ContainsKey(l.RefId))
			.GroupBy(l => (l.WorkDay, HaibunHeaderKey.From(haibun[l.RefId]))).Select(g => g.ToList())];
	}

	/// <summary>
	/// 出荷確定・欠品の反映。既存の配分確定（<see cref="ShippingDb.Commit"/>）だけを使う（決定 D8: 1回確定・残り欠品で完了）。
	/// </summary>
	/// <returns>失敗理由（成功は null）</returns>
	private string? ApplyOrderFix(List<TranLogisticsLine> group, long idShain) {
		var current = _db.Fetch<TranHaibun>($"where Id in ({string.Join(",", group.Select(l => l.RefId))})").ToDictionary(h => h.Id);
		var shippingDb = new ShippingDb(_db);
		var (created, _, _) = shippingDb.Commit(
			[.. group.Select(l => (l.RefId, current.TryGetValue(l.RefId, out var h) ? h.Vdu : -1, l.Su))],
			group[0].WorkDay, idShain, out var outcome, out var shortages);
		switch (outcome) {
			case CommitOutcome.Conflict:
				return "配分が他で更新・確定されています。";
			case CommitOutcome.InvalidKubun:
				return "確定できない配分区分が含まれています。";
			case CommitOutcome.NotArrived:
				return "入荷済み数を超えて確定しようとしています（仕入の入荷確定を先に反映してください）。";
			case CommitOutcome.Shortage:
				return $"有効在庫が不足しています（{shortages.Count:N0}SKU）。入荷確定の未反映などCVの在庫を確認してください。";
		}
		var tenpo = LoadTokuiByIds(current.Values.Select(h => h.Id_Tenpo).Take(1));
		var targetTable = ShippingDb.IsShukka(tenpo.Values.FirstOrDefault()?.TenType ?? 0) ? nameof(Tran00Uriage) : nameof(Tran10IdoOut);
		foreach (var line in group) {
			line.TargetTable = created.Count > 0 ? targetTable : string.Empty;
			line.TargetId = created.Count > 0 ? created[0] : 0;
		}
		if (created.Count > 0 && targetTable == nameof(Tran00Uriage)) {
			// 出荷売上は売掛集計へ入るため、反映後にまとめて引き直す
			var kakeDay = _db.Fetch<Tran00Uriage>("where Id = @0", created[0]).FirstOrDefault()?.KakeDay ?? group[0].WorkDay;
			_uriMonths.Add(ClosingMonthCalculator.CalculateKakeMonth(kakeDay, new SummaryDb(_db).GetOwnClosingDay()));
		}
		return null;
	}

	/// <summary>
	/// 入荷確定の反映。発注→仕入（<c>RelateNo1</c>=発注Id）、移動出庫→移動受（<c>RelateNo1</c>=移動出庫Id）。
	/// 伝票の組立・在庫反映・後処理は HHT 取込と同じ考え方（発注完了判定・入荷割当・買掛集計）。
	/// </summary>
	private string? ApplyStockFix(List<TranLogisticsLine> group, long idShain) {
		var head = group[0];
		var summaryDb = new SummaryDb(_db);
		if (head.RefTable == nameof(Tran13Hachu)) {
			var hachu = _db.Fetch<Tran13Hachu>("where Id = @0", head.RefId).FirstOrDefault();
			if (hachu == null || hachu.EndFlag != 0) {
				return "発注が無いか完了済みです。";
			}
			var shiire = _db.Fetch<MasterShiire>("where Id = @0", hachu.Id_Shiire).FirstOrDefault();
			var meisai = BuildMeisai(group, LogisticsDataKind.STOCKFIX, hachu.Jmeisai ?? [], useShiireCost: true);
			// V* は伝票作成時点の現行マスタ名（HHT取込と同じ）
			var slip = new Tran03Shiire {
				DenDay = head.WorkDay,
				KakeDay = head.WorkDay,
				Id_Soko = hachu.Id_Soko,
				VSoko = SokoView(hachu.Id_Soko, hachu.VSoko),
				Id_Shiire = hachu.Id_Shiire,
				VShiire = shiire == null ? hachu.VShiire : new CodeNameView(shiire.Id, shiire.Code, shiire.Name),
				IsPay = (int)EnumYesNo.Yes,
				Kubun = (int)EnumShiire.Shiire,
				RelateNo1 = (int)hachu.Id,
				Memo = SlipMemo(group),
			};
			FillHeader(slip, meisai, idShain);
			var sysman = _db.Fetch<MasterSysman>("where Id = 1").FirstOrDefault() ?? new MasterSysman();
			slip.TaxCalcUnit = shiire?.TaxCalcUnit ?? 0;
			slip.TaxRounding = shiire?.TaxRounding ?? sysman.TaxRounding;
			ApplyTax(slip, meisai, head.WorkDay, (EnumTaxCalcUnit)slip.TaxCalcUnit, (EnumRounding)slip.TaxRounding);
			_db.Insert(slip);
			summaryDb.CalcTran2SummaryStock(nameof(Tran03Shiire), nameof(ITranSoko.Id_Soko), slip.Id, invertFlag: false);
			new CompletionDb(_db).CalcHachuEndFlag([hachu.Id]);
			new ArrivalDb(_db).Recalc([hachu.Id]);
			_kaiMonths.Add(ClosingMonthCalculator.CalculateKakeMonth(slip.KakeDay, summaryDb.GetOwnClosingDay()));
			SetTarget(group, nameof(Tran03Shiire), slip.Id);
			return null;
		}
		var ido = _db.Fetch<Tran10IdoOut>("where Id = @0", head.RefId).FirstOrDefault();
		if (ido == null) {
			return "移動出庫がありません。";
		}
		if (_db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {nameof(Tran11IdoIn)} WHERE RelateNo1 = @0", ido.Id) > 0) {
			return "移動は受入済みです。";
		}
		var idoMeisai = BuildMeisai(group, LogisticsDataKind.STOCKFIX, ido.Jmeisai ?? [], useShiireCost: false);
		var idoIn = new Tran11IdoIn {
			DenDay = head.WorkDay,
			Id_Soko = ido.Id_Soko,
			VSoko = SokoView(ido.Id_Soko, ido.VSoko),
			Id_Ido = ido.Id_Ido,
			VIdo = SokoView(ido.Id_Ido, ido.VIdo),
			RelateNo1 = ido.Id,
			Memo = SlipMemo(group),
		};
		FillHeader(idoIn, idoMeisai, idShain);
		_db.Insert(idoIn);
		summaryDb.CalcTran2SummaryStock(nameof(Tran11IdoIn), nameof(ITranSoko.Id_Soko), idoIn.Id, invertFlag: false);
		summaryDb.CalcTran2SummaryStock(nameof(Tran11IdoIn), nameof(ITranIdo.Id_Ido), idoIn.Id, invertFlag: false);
		SetTarget(group, nameof(Tran11IdoIn), idoIn.Id);
		return null;
	}

	/// <summary>棚卸の反映。棚卸データ（<see cref="Tran60Tana"/>）を作るだけで、棚卸確定・在庫調整は既存の棚卸確定画面で行う</summary>
	private string? ApplyInventory(List<TranLogisticsLine> group, long idShain) {
		var head = group[0];
		var soko = LoadTokuiByIds([head.Id_Soko]).GetValueOrDefault(head.Id_Soko);
		var meisai = BuildMeisai(group, LogisticsDataKind.INVENTORY, [], useShiireCost: false);
		var slip = new Tran60Tana {
			DenDay = head.WorkDay,
			Id_Soko = head.Id_Soko,
			VSoko = soko == null ? new CodeNameView() : new CodeNameView(soko.Id, soko.Code, soko.Name),
			TanaNo = TanaNoOf(head),
			Memo = SlipMemo(group),
		};
		FillHeader(slip, meisai, idShain);
		_db.Insert(slip);
		SetTarget(group, nameof(Tran60Tana), slip.Id);
		return null;
	}

	/// <summary>棚卸行の棚番（原文から読み、伝票の桁数8で切る）</summary>
	private static string TanaNoOf(TranLogisticsLine line) => TanaNo(FieldOf(line, LogisticsDataKind.INVENTORY, "棚番"));

	/// <summary>受信行の原文から項目を読む（無ければ空）</summary>
	private static string FieldOf(TranLogisticsLine line, string kind, string header) {
		var fields = LogisticsFileFormat.Parse(kind, line.RawText).FirstOrDefault()?.Fields;
		var index = LogisticsFileFormat.ColumnIndex(kind, header);
		return fields != null && fields.Count > index ? fields[index] : string.Empty;
	}

	/// <summary>倉庫の V*（現行マスタ名。マスタに無ければ元伝票の値）</summary>
	private CodeNameView SokoView(long id, CodeNameView fallback) {
		var t = LoadTokuiByIds([id]).GetValueOrDefault(id);
		return t == null ? fallback : new CodeNameView(t.Id, t.Code, t.Name);
	}

	/// <summary>
	/// 明細を作る。単価類は元伝票の同じSKUの明細から、無ければ商品マスタ（仕入は仕入単価→原価、移動・棚卸は原価）から取る。
	/// 受信ファイルのメモは明細メモへ入れる。
	/// </summary>
	private List<Tran99Meisai> BuildMeisai(List<TranLogisticsLine> group, string kind, List<Tran99Meisai> source, bool useShiireCost) {
		var shohinIds = group.Select(l => l.Id_Shohin).Distinct().ToList();
		var shohin = _db.Fetch<MasterShohin>($"where Id in ({string.Join(",", shohinIds)})").ToDictionary(s => s.Id);
		var taxIds = new TranTaxRebuildDb(_db).LoadShohinTaxIds();
		var result = new List<Tran99Meisai>();
		foreach (var line in group) {
			var src = source.FirstOrDefault(m => (m.Id_Shohin, m.Id_Col, m.Id_Siz) == (line.Id_Shohin, line.Id_Col, line.Id_Siz));
			var s = shohin.GetValueOrDefault(line.Id_Shohin);
			var sku = s?.Jcolsiz?.FirstOrDefault(k => k.Id_Col == line.Id_Col && k.Id_Siz == line.Id_Siz);
			var tanka = src?.Tanka ?? (useShiireCost && s?.TankaShiire > 0 ? s.TankaShiire : s?.TankaGenka ?? 0);
			result.Add(new Tran99Meisai {
				No = result.Count + 1,
				Id_Shohin = line.Id_Shohin,
				Code_Shohin = s?.Code ?? string.Empty,
				Mei_Shohin = s?.Name ?? string.Empty,
				JanCode = sku?.Jan1 ?? string.Empty,
				Id_Col = line.Id_Col,
				Code_Col = sku?.Code_Col ?? string.Empty,
				Mei_Col = sku?.Mei_Col ?? string.Empty,
				Id_Siz = line.Id_Siz,
				Code_Siz = sku?.Code_Siz ?? string.Empty,
				Mei_Siz = sku?.Mei_Siz ?? string.Empty,
				Su = line.Su,
				Tanka = tanka,
				Kingaku = (long)line.Su * tanka,
				Jodai = src?.Jodai ?? s?.TankaJodai ?? 0,
				Gedai = src?.Gedai ?? s?.TankaGenka ?? 0,
				Id_Tax = taxIds.TryGetValue(line.Id_Shohin, out var tax) && tax > 0 ? tax : TaxCalculator.StandardTaxId,
				Memo = FieldOf(line, kind, "メモ"),
			});
		}
		return result;
	}

	/// <summary>ヘッダの合計と社員を明細から埋める（HHT取込と同じ）</summary>
	private void FillHeader(TranAllHeader slip, List<Tran99Meisai> meisai, long idShain) {
		slip.Jmeisai = meisai;
		slip.SuTotal = meisai.Sum(x => x.Su);
		slip.KingakuTotal = meisai.Sum(x => x.Kingaku);
		slip.JodaiTotal = meisai.Sum(x => (long)x.Su * x.Jodai);
		slip.GedaiTotal = meisai.Sum(x => (long)x.Su * x.Gedai);
		slip.Id_Shain = idShain;
		var shain = idShain > 0 ? _db.Fetch<MasterShain>("where Id = @0", idShain).FirstOrDefault() : null;
		slip.VShain = shain == null ? new CodeNameView() : new CodeNameView(shain.Id, shain.Code, shain.Name);
		var vdate = Common.GetVdate();
		slip.Vdc = vdate;
		slip.Vdu = vdate;
	}

	/// <summary>消費税と総合計（各入力画面・HHT取込と同じく <see cref="TaxCalculator.Apply"/> で税区分ごとに丸める）</summary>
	private void ApplyTax(ITranTax slip, List<Tran99Meisai> meisai, string denDay, EnumTaxCalcUnit calcUnit, EnumRounding rounding) {
		var sysman = _db.Fetch<MasterSysman>("where Id = 1").FirstOrDefault() ?? new MasterSysman();
		var totals = TaxCalculator.Apply(meisai, TaxRateResolver.CreateRateResolver(sysman, denDay), calcUnit, rounding);
		slip.TaxableAmount1 = totals.TaxableAmount1;
		slip.TaxableAmount2 = totals.TaxableAmount2;
		slip.TaxableAmount3 = totals.TaxableAmount3;
		slip.Tax1 = totals.Tax1;
		slip.Tax2 = totals.Tax2;
		slip.Tax3 = totals.Tax3;
		slip.Total = Math.Abs(meisai.Sum(x => x.Kingaku)) + totals.TaxTotal;
	}

	private static string SlipMemo(List<TranLogisticsLine> group) => $"WMS受信 {group[0].Id_Batch}";

	private static void SetTarget(List<TranLogisticsLine> group, string table, long id) {
		foreach (var line in group) {
			line.TargetTable = table;
			line.TargetId = id;
		}
	}

	/// <summary>
	/// L04 受信行の除外・訂正版追加。原文は変更せず、訂正版は元行と同じ行番号・<c>Id_LineOrg</c> 付きで追加して検査する。
	/// </summary>
	public LogisticsRunResult ExecuteLineAction(LogisticsLineActionParam param, long idShain) {
		ArgumentNullException.ThrowIfNull(param);
		return RunLocked($"連携データ受信 行{(param.Action == EnumLogisticsLineAction.Exclude ? "除外" : "訂正")} {param.LineId}", 1, () => {
			var line = _db.Fetch<TranLogisticsLine>("where Id = @0", param.LineId).FirstOrDefault()
				?? throw new ArgumentException($"行 {param.LineId} がありません。");
			var batch = RequireReceiveBatch(line.Id_Batch);
			if (line.Status is not ((int)EnumLogisticsLineStatus.Pending or (int)EnumLogisticsLineStatus.Error)) {
				throw new ArgumentException("除外・訂正できるのは未処理・エラーの行だけです。");
			}
			var reason = (param.Reason ?? string.Empty).Trim();
			var vdate = Common.GetVdate();
			_db.BeginTransaction();
			try {
				switch (param.Action) {
					case EnumLogisticsLineAction.Exclude:
						line.Status = (int)EnumLogisticsLineStatus.Excluded;
						line.ErrorMsg = Truncate1000($"除外 社員{idShain} {reason}");
						line.Vdu = vdate;
						_db.Update(line);
						break;
					case EnumLogisticsLineAction.Correct: {
						var columns = LogisticsFileFormat.Columns(batch.DataKind);
						if (param.Fields is null || param.Fields.Length != columns.Count) {
							throw new ArgumentException($"訂正版は{columns.Count}項目を指定してください。");
						}
						line.Status = (int)EnumLogisticsLineStatus.Corrected;
						line.ErrorMsg = Truncate1000($"訂正済み 社員{idShain} {reason}");
						line.Vdu = vdate;
						_db.Update(line);
						_db.Insert(new TranLogisticsLine {
							Id_Batch = batch.Id,
							LineNo = line.LineNo,
							RawText = LogisticsFileFormat.BuildLine(param.Fields),
							Id_LineOrg = line.Id,
							Vdc = vdate,
							Vdu = vdate,
						});
						break;
					}
					default:
						throw new ArgumentException($"未対応の操作です: {param.Action}");
				}
				CheckAndSave(batch);
				UpdateReceiveBatchSummary(batch);
				_db.CompleteTransaction();
			}
			catch {
				_db.AbortTransaction();
				throw;
			}
			return new LogisticsRunResult([new LogisticsKindResult(batch.DataKind, 1, batch.Id, batch.FileName)], [],
				param.Action == EnumLogisticsLineAction.Exclude ? "除外しました。" : "訂正版を追加して検査しました。");
		}, r => r.Kinds.Sum(k => k.Count));
	}

	private static string Truncate1000(string s) => s.Length > 1000 ? s[..1000] : s;
}
