using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

// L02 連携データ手動送信（仕様 3.2〜3.3・5.2）と L04 の送信バッチ操作
public partial class LogisticsLinkDb {
	/// <summary>
	/// 送信対象を照会する（DBを変えない）。
	/// </summary>
	public List<LogisticsSendCandidate> QuerySendCandidates(LogisticsSendQueryParam param) {
		ArgumentNullException.ThrowIfNull(param);
		var settings = RequireUsableSettings();
		var soko = ResolveSoko(settings, param.SokoIds);
		return BuildCandidates(param.Kind, param.ToDay, soko).Candidates;
	}

	/// <summary>
	/// 送信ファイルを作る。出荷指示は対象配分を <c>SendFlg=1</c> で確保して送信行を保存し（1トランザクション）、
	/// ファイル配置後に <c>SendFlg=2</c> にする。配置に失敗したらバッチを配置失敗にし、<c>SendFlg=1</c> のまま残す（L04 から再出力・取消）。
	/// </summary>
	public LogisticsRunResult CreateSendBatch(LogisticsSendParam param, long idShain) {
		ArgumentNullException.ThrowIfNull(param);
		if (!LogisticsDataKind.SendKinds.Contains(param.Kind)) {
			throw new ArgumentException($"送信できない種別です: {param.Kind}");
		}
		var settings = RequireUsableSettings();
		var soko = ResolveSoko(settings, param.SokoIds);
		return RunLocked($"連携データ送信 {param.Kind}", param.ExecType, () => {
			var warnings = new List<string>();
			var vdate = Common.GetVdate();
			TranLogisticsBatch? batch = null;
			string text;
			List<long> haibunIds = [];
			_db.BeginTransaction();
			try {
				var built = BuildCandidates(param.Kind, param.ToDay, soko);
				var picked = param.RefIds is { Length: > 0 }
					? built.Candidates.Where(c => param.RefIds.Contains(c.RefId)).ToList()
					: built.Candidates;
				if (picked.Count == 0) {
					_db.AbortTransaction();
					return new LogisticsRunResult([new LogisticsKindResult(param.Kind, 0, 0, string.Empty)], [], "送信する対象がありません。");
				}
				batch = InsertSendBatch(settings, param.Kind, picked.Count, idShain, param.ExecType);
				batch.FileName = BuildFileName(settings, param.Kind, batch.Id, DateTime.Now);
				var rows = picked.Select(built.ToRow).ToList();
				var (fileText, lines) = LogisticsFileFormat.BuildFile(param.Kind, rows);
				text = fileText;
				if (param.Kind == LogisticsDataKind.ORDER) {
					haibunIds = [.. picked.Select(c => c.RefId)];
					var reserved = _db.ExecuteDialect(
						$"UPDATE {nameof(TranHaibun)} SET SendFlg = 1, Vdu = @0 WHERE Id IN ({string.Join(",", haibunIds)}) AND {TranHaibun.EditableWhereSql}",
						vdate);
					if (reserved != haibunIds.Count) {
						throw new LogisticsUserException("対象の配分が他で更新されました。もう一度照会してください。");
					}
				}
				if (param.Kind != LogisticsDataKind.ZAIKO) {
					var lineRows = picked.Select((c, i) => new TranLogisticsLine {
						Id_Batch = batch.Id,
						LineNo = i + 1,
						RawText = lines[i],
						RefTable = c.RefTable,
						RefId = c.RefId,
						RefNo = c.RefNo,
						RefVdu = built.RefVdu.GetValueOrDefault((c.RefTable, c.RefId), vdate),
						Su = c.Su,
						WorkDay = c.Day,
						Vdc = vdate,
						Vdu = vdate,
					}).ToList();
					_db.InsertBulk(lineRows);
				}
				_db.Update(batch);
				_db.CompleteTransaction();
			}
			catch {
				_db.AbortTransaction();
				throw;
			}

			var placed = PlaceBatchFile(settings, batch, text);
			if (placed) {
				MarkHaibunSent(batch.Id, haibunIds);
			}
			else {
				warnings.Add("ファイルを配置できませんでした。連携エラーデータ照会から再出力または取消してください。");
			}
			return new LogisticsRunResult([new LogisticsKindResult(param.Kind, batch.RowCount, batch.Id, batch.FileName)], [.. warnings],
				$"{LogisticsDataKind.DisplayName(param.Kind)} {batch.RowCount:N0}件 {(placed ? "配置済み" : "配置失敗")}");
		}, r => r.Kinds.Sum(k => k.Count));
	}

	/// <summary>
	/// L04 送信バッチの再出力・取消。
	/// 再出力は保存済みの送信行から同じファイル名で配置し直す（その時点の配分・伝票は読み直さない）。
	/// 取消は配置失敗、または利用者が連携先の未受領を確認した配置済みバッチについて、配分の <c>SendFlg</c> を 0 へ戻す。
	/// </summary>
	public LogisticsRunResult ExecuteBatchAction(LogisticsBatchActionParam param, long idShain) {
		ArgumentNullException.ThrowIfNull(param);
		var settings = RequireUsableSettings();
		return RunLocked($"送信バッチ{(param.Action == EnumLogisticsBatchAction.Rewrite ? "再出力" : "取消")} {param.BatchId}", param.ExecType, () => {
			var batch = _db.Fetch<TranLogisticsBatch>("where Id = @0", param.BatchId).FirstOrDefault()
				?? throw new ArgumentException($"バッチ {param.BatchId} がありません。");
			if (batch.Direction != (int)EnumLogisticsDirection.Send || LogisticsDataKind.MasterKinds.Contains(batch.DataKind) || batch.DataKind == LogisticsDataKind.ZAIKO) {
				throw new ArgumentException("再出力・取消できるのは出荷指示・入荷予定の送信バッチだけです（マスタ・在庫は作り直してください）。");
			}
			var lines = _db.Fetch<TranLogisticsLine>("where Id_Batch = @0 order by LineNo", batch.Id);
			var haibunIds = lines.Where(l => l.RefTable == nameof(TranHaibun)).Select(l => l.RefId).ToList();
			switch (param.Action) {
				case EnumLogisticsBatchAction.Rewrite: {
					// 作成中(0)のまま残ったバッチ（DB確保後の異常終了など）も再出力・取消で回復できるようにする
					if (batch.Status is not ((int)EnumLogisticsSendStatus.PlaceFailed or (int)EnumLogisticsSendStatus.Creating)) {
						throw new ArgumentException("再出力できるのは配置失敗・作成中のバッチだけです。");
					}
					var text = string.Concat(new[] { LogisticsFileFormat.BuildLine(LogisticsFileFormat.Columns(batch.DataKind)) }
						.Concat(lines.Select(l => l.RawText)).Select(l => l + "\r\n"));
					var placed = PlaceBatchFile(settings, batch, text);
					if (placed) {
						MarkHaibunSent(batch.Id, haibunIds);
					}
					return new LogisticsRunResult([new LogisticsKindResult(batch.DataKind, lines.Count, batch.Id, batch.FileName)], [],
						placed ? "再出力しました。" : "ファイルを配置できませんでした。");
				}
				case EnumLogisticsBatchAction.Cancel: {
					if (batch.Status is not ((int)EnumLogisticsSendStatus.PlaceFailed or (int)EnumLogisticsSendStatus.Placed or (int)EnumLogisticsSendStatus.Creating)) {
						throw new ArgumentException("取消できるのは作成中・配置済み・配置失敗のバッチだけです。");
					}
					_db.BeginTransaction();
					try {
						var vdate = Common.GetVdate();
						var restored = haibunIds.Count == 0 ? 0 : _db.ExecuteDialect(
							$"UPDATE {nameof(TranHaibun)} SET SendFlg = 0, Vdu = @0 WHERE Id IN ({string.Join(",", haibunIds)}) "
							+ "AND SendFlg IN (1, 2) AND EndFlag = 0 AND ifnull(KakuteiDay,'') = ''", vdate);
						UpdateBatchStatus(batch, (int)EnumLogisticsSendStatus.Canceled, $"取消 社員{idShain} 配分戻し{restored}件");
						_db.CompleteTransaction();
						var warnings = restored < haibunIds.Count
							? new[] { $"確定・完了済みの配分 {haibunIds.Count - restored:N0}件は戻していません。" }
							: [];
						return new LogisticsRunResult([new LogisticsKindResult(batch.DataKind, lines.Count, batch.Id, batch.FileName)], warnings,
							"送信を取り消しました。連携先に届いているファイルは連携先で取り消してください。");
					}
					catch {
						_db.AbortTransaction();
						throw;
					}
				}
				default:
					throw new ArgumentException($"未対応の操作です: {param.Action}");
			}
		}, r => r.Kinds.Sum(k => k.Count));
	}

	/// <summary>配置済みになった出荷指示の配分を送信済み(2)にし、送信行の RefVdu を合わせる</summary>
	private void MarkHaibunSent(long batchId, List<long> haibunIds) {
		if (haibunIds.Count == 0) {
			return;
		}
		var vdate = Common.GetVdate();
		_db.Execute($"UPDATE {nameof(TranHaibun)} SET SendFlg = 2, Vdu = @0 WHERE Id IN ({string.Join(",", haibunIds)}) AND SendFlg = 1", vdate);
		_db.Execute($"UPDATE {nameof(TranLogisticsLine)} SET RefVdu = @0, Vdu = @0 WHERE Id_Batch = @1", vdate, batchId);
	}

	/// <summary>倉庫の絞込（指定が無ければ対象倉庫すべて。対象倉庫外の指定は無視する）</summary>
	private List<MasterTokui> ResolveSoko(LogisticsSettings settings, long[]? sokoIds) {
		var targets = LoadTargetSoko(settings);
		return sokoIds is { Length: > 0 } ? [.. targets.Where(t => sokoIds.Contains(t.Id))] : targets;
	}

	/// <summary>照会結果と、ファイル行への変換・参照行の Vdu</summary>
	private sealed record CandidateSet(List<LogisticsSendCandidate> Candidates, Func<LogisticsSendCandidate, int, IReadOnlyList<string?>> ToRow,
		Dictionary<(string, long), long> RefVdu);

	private CandidateSet BuildCandidates(string kind, string toDay, List<MasterTokui> soko) {
		if (kind != LogisticsDataKind.ZAIKO && !AllocationRules.IsYmd(toDay)) {
			throw new ArgumentException("指定日を yyyyMMdd で指定してください。");
		}
		if (soko.Count == 0) {
			throw new ArgumentException("対象倉庫がありません。");
		}
		return kind switch {
			LogisticsDataKind.ORDER => BuildOrderCandidates(toDay, soko),
			LogisticsDataKind.STOCK => BuildStockCandidates(toDay, soko),
			LogisticsDataKind.ZAIKO => BuildZaikoCandidates(soko),
			_ => throw new ArgumentException($"送信できない種別です: {kind}"),
		};
	}

	/// <summary>
	/// 出荷指示（配分）。未送信・未完了・未確定・確定可能区分（取置を除く）・対象倉庫・納品日（空なら指示日）≦指定日。
	/// 仕入配分(区分0)は入荷済み数が正の行だけで、指示数は入荷済み数（確定時に残りは欠品として完了する。J-03）。
	/// 区分は出荷先の店種で 20=出荷売上（卸先・売仕店）／10=移動。
	/// </summary>
	private CandidateSet BuildOrderCandidates(string toDay, List<MasterTokui> soko) {
		var sokoById = soko.ToDictionary(t => t.Id);
		var kubuns = string.Join(",", new[] { EnumHaibun.Hatsukai, EnumHaibun.Zaiko, EnumHaibun.Juchu }.Select(k => (int)k));
		var rows = _db.FetchDialect<TranHaibun>(
			$"where {TranHaibun.EditableWhereSql} AND Kubun IN ({kubuns}) AND Id_Soko IN ({string.Join(",", sokoById.Keys)}) "
			+ "AND (CASE WHEN ifnull(NouhinDay,'') = '' THEN DenDay ELSE NouhinDay END) <= @0", toDay)
			.Where(h => h.Kubun != (int)EnumHaibun.Hatsukai || h.ArrivedSu > 0)
			.OrderBy(h => h.DenDay).ThenBy(h => h.NouhinDay).ThenBy(h => h.Id_Soko).ThenBy(h => h.Id_Tenpo)
			.ThenBy(h => h.Kubun).ThenBy(h => h.RelateNo1).ThenBy(h => h.Id).ToList();
		var tenpo = LoadTokuiByIds(rows.Select(h => h.Id_Tenpo));
		var sku = LoadSkuCodes(rows.Select(h => h.Id_Shohin));
		var byId = rows.ToDictionary(h => h.Id);
		var candidates = rows.Select(h => {
			var t = tenpo.GetValueOrDefault(h.Id_Tenpo);
			var s = sku.GetValueOrDefault((h.Id_Shohin, h.Id_Col, h.Id_Siz));
			var su = h.Kubun == (int)EnumHaibun.Hatsukai ? Math.Min(h.ArrivedSu, h.Su) : h.Su;
			return new LogisticsSendCandidate(nameof(TranHaibun), h.Id, 0, ShippingDb.IsShukka(t?.TenType ?? 0) ? "20" : "10",
				string.IsNullOrEmpty(h.NouhinDay) ? h.DenDay : h.NouhinDay, sokoById[h.Id_Soko].Code, sokoById[h.Id_Soko].Name,
				t?.Code ?? string.Empty, t?.Name ?? string.Empty, s.Shohin, s.Col, s.Siz, string.IsNullOrEmpty(h.JanCode) ? s.Jan : h.JanCode,
				su, 0, h.Memo);
		}).ToList();
		// 指示伝票番号は配分確定の伝票の括り（HaibunHeaderKey）ごとのファイル内連番
		var headerNo = new Dictionary<HaibunHeaderKey, int>();
		IReadOnlyList<string?> ToRow(LogisticsSendCandidate c, int _) {
			var h = byId[c.RefId];
			var key = HaibunHeaderKey.From(h);
			if (!headerNo.TryGetValue(key, out var no)) {
				headerNo[key] = no = headerNo.Count + 1;
			}
			return [LogisticsDataKind.ORDER, c.Kubun, no.ToString(), h.Id.ToString(), h.Kubun.ToString(), h.DenDay, h.NouhinDay, c.SokoCode, c.PartnerCode,
				c.ShohinCode, c.ColCode, c.SizCode, c.Jan, c.Su.ToString(), h.Tanka.ToString(), h.Jodai.ToString(), h.Gedai.ToString(), h.RelateNo1.ToString(), h.Memo];
		}
		return new CandidateSet(candidates, ToRow, rows.ToDictionary(h => (nameof(TranHaibun), h.Id), h => h.Vdu));
	}

	/// <summary>
	/// 入荷予定。発注（区分10〜19・未完了・納品倉庫が対象倉庫）は発注残（明細数−紐付く仕入数）、
	/// 移動出庫（移動先が対象倉庫・移動受なし）は明細数。計上日≦指定日で、取消していない送信バッチに無いもの。
	/// </summary>
	private CandidateSet BuildStockCandidates(string toDay, List<MasterTokui> soko) {
		var sokoById = soko.ToDictionary(t => t.Id);
		var sokoIds = string.Join(",", sokoById.Keys);
		var sent = SentRefIds();
		var candidates = new List<LogisticsSendCandidate>();
		var refVdu = new Dictionary<(string, long), long>();
		var detail = new Dictionary<(string, long, int), Tran99Meisai>();

		var hachus = _db.Fetch<Tran13Hachu>(
			$"where EndFlag = 0 AND Kubun BETWEEN 10 AND 19 AND Id_Soko IN ({sokoIds}) AND DenDay <= @0 order by DenDay, Id", toDay)
			.Where(x => !sent.Contains((nameof(Tran13Hachu), x.Id))).ToList();
		var received = LoadShiireReceived(hachus.Select(x => x.Id));
		foreach (var x in hachus) {
			refVdu[(nameof(Tran13Hachu), x.Id)] = x.Vdu;
			foreach (var m in x.Jmeisai ?? []) {
				var rest = m.Su - received.GetValueOrDefault((x.Id, m.Id_Shohin, m.Id_Col, m.Id_Siz));
				if (rest <= 0) {
					continue;
				}
				// 同じSKUが複数明細にあるときは、仕入済み数を先の明細から消し込む
				received[(x.Id, m.Id_Shohin, m.Id_Col, m.Id_Siz)] = Math.Max(0, received.GetValueOrDefault((x.Id, m.Id_Shohin, m.Id_Col, m.Id_Siz)) - m.Su);
				detail[(nameof(Tran13Hachu), x.Id, m.No)] = m;
				candidates.Add(new LogisticsSendCandidate(nameof(Tran13Hachu), x.Id, m.No, "20",
					string.IsNullOrEmpty(x.NouhinDay) ? x.DenDay : x.NouhinDay, sokoById[x.Id_Soko].Code, sokoById[x.Id_Soko].Name,
					x.VShiire?.Cd ?? string.Empty, x.VShiire?.Mei ?? string.Empty, m.Code_Shohin, m.Code_Col, m.Code_Siz, m.JanCode, rest, 0, x.Memo));
			}
		}

		var idos = _db.Fetch<Tran10IdoOut>(
			$"where Id_Ido IN ({sokoIds}) AND DenDay <= @0 AND NOT EXISTS (SELECT 1 FROM {nameof(Tran11IdoIn)} i WHERE i.RelateNo1 = {nameof(Tran10IdoOut)}.Id) order by DenDay, Id", toDay)
			.Where(x => !sent.Contains((nameof(Tran10IdoOut), x.Id))).ToList();
		foreach (var x in idos) {
			refVdu[(nameof(Tran10IdoOut), x.Id)] = x.Vdu;
			foreach (var m in x.Jmeisai ?? []) {
				if (m.Su <= 0) {
					continue;
				}
				detail[(nameof(Tran10IdoOut), x.Id, m.No)] = m;
				candidates.Add(new LogisticsSendCandidate(nameof(Tran10IdoOut), x.Id, m.No, "10", x.DenDay,
					sokoById[x.Id_Ido].Code, sokoById[x.Id_Ido].Name, x.VSoko?.Cd ?? string.Empty, x.VSoko?.Mei ?? string.Empty,
					m.Code_Shohin, m.Code_Col, m.Code_Siz, m.JanCode, m.Su, 0, x.Memo));
			}
		}
		var denDayById = hachus.Select(x => (nameof(Tran13Hachu), x.Id, x.DenDay)).Concat(idos.Select(x => (nameof(Tran10IdoOut), x.Id, x.DenDay)))
			.ToDictionary(x => (x.Item1, x.Id), x => x.DenDay);
		IReadOnlyList<string?> ToRow(LogisticsSendCandidate c, int _) {
			var m = detail[(c.RefTable, c.RefId, c.RefNo)];
			return [LogisticsDataKind.STOCK, c.Kubun, c.RefId.ToString(), c.RefNo.ToString(), denDayById[(c.RefTable, c.RefId)], c.Day,
				c.SokoCode, c.PartnerCode, c.ShohinCode, c.ColCode, c.SizCode, c.Jan, c.Su.ToString(), m.Tanka.ToString(), m.Jodai.ToString(), c.Memo];
		}
		return new CandidateSet(candidates, ToRow, refVdu);
	}

	/// <summary>在庫。対象倉庫の有効在庫（実在庫−引当）と積送中（全期間合計）。どちらかが0でないSKU</summary>
	private CandidateSet BuildZaikoCandidates(List<MasterTokui> soko) {
		var sokoById = soko.ToDictionary(t => t.Id);
		var sokoIds = string.Join(",", sokoById.Keys);
		var stock = _db.Fetch<SummaryRealStock>($"where Id_Soko IN ({sokoIds})")
			.ToDictionary(x => (x.Id_Soko, x.Id_Shohin, x.Id_Col, x.Id_Siz), x => x.Su - x.ReserveQty);
		var transit = _db.Fetch<SummaryStock>($"where Id_Soko IN ({sokoIds})")
			.GroupBy(x => (x.Id_Soko, x.Id_Shohin, x.Id_Col, x.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.TransitQty));
		var keys = stock.Keys.Union(transit.Keys)
			.Where(k => stock.GetValueOrDefault(k) != 0 || transit.GetValueOrDefault(k) != 0).ToList();
		var sku = LoadSkuCodes(keys.Select(k => k.Id_Shohin));
		var candidates = keys.Select(k => {
			var s = sku.GetValueOrDefault((k.Id_Shohin, k.Id_Col, k.Id_Siz));
			return new LogisticsSendCandidate(nameof(SummaryRealStock), 0, 0, string.Empty, string.Empty, sokoById[k.Id_Soko].Code, sokoById[k.Id_Soko].Name,
				string.Empty, string.Empty, s.Shohin, s.Col, s.Siz, s.Jan, stock.GetValueOrDefault(k), transit.GetValueOrDefault(k), string.Empty);
		}).OrderBy(c => c.SokoCode).ThenBy(c => c.ShohinCode).ThenBy(c => c.ColCode).ThenBy(c => c.SizCode).ToList();
		var at = DateTime.Now.ToString("yyyyMMddHHmmss");
		IReadOnlyList<string?> ToRow(LogisticsSendCandidate c, int _) =>
			[LogisticsDataKind.ZAIKO, at, c.SokoCode, c.ShohinCode, c.ColCode, c.SizCode, c.Jan, c.Su.ToString(), c.Su2.ToString()];
		return new CandidateSet(candidates, ToRow, []);
	}

	/// <summary>取消していない送信バッチに載っている元伝票（入荷予定の二重送信防止）</summary>
	private HashSet<(string, long)> SentRefIds() =>
		_db.Fetch<TranLogisticsLine>(
			$"where RefTable IN (@0, @1) AND Id_Batch IN (SELECT Id FROM {nameof(TranLogisticsBatch)} WHERE Direction = @2 AND Status <> @3)",
			nameof(Tran13Hachu), nameof(Tran10IdoOut), (int)EnumLogisticsDirection.Send, (int)EnumLogisticsSendStatus.Canceled)
			.Select(l => (l.RefTable, l.RefId)).ToHashSet();

	/// <summary>発注に紐付く仕入数（CalcFlag を掛けた符号付き）を発注×SKUで集計する</summary>
	internal Dictionary<(long, long, long, long), int> LoadShiireReceived(IEnumerable<long> hachuIds) {
		var ids = hachuIds.Distinct().ToList();
		var result = new Dictionary<(long, long, long, long), int>();
		if (ids.Count == 0) {
			return result;
		}
		foreach (var s in _db.Fetch<Tran03Shiire>($"where RelateNo1 IN ({string.Join(",", ids)})")) {
			foreach (var m in s.Jmeisai ?? []) {
				var key = ((long)s.RelateNo1, m.Id_Shohin, m.Id_Col, m.Id_Siz);
				result[key] = result.GetValueOrDefault(key) + m.Su * s.CalcFlag;
			}
		}
		return result;
	}

	/// <summary>得意先を Id で引く</summary>
	internal Dictionary<long, MasterTokui> LoadTokuiByIds(IEnumerable<long> ids) {
		var list = ids.Where(x => x > 0).Distinct().ToList();
		return list.Count == 0 ? [] : _db.Fetch<MasterTokui>($"where Id IN ({string.Join(",", list)})").ToDictionary(t => t.Id);
	}

	/// <summary>SKUのコード（商品・色・サイズ・JAN1）。キーは (商品Id, 色Id, サイズId)</summary>
	internal Dictionary<(long, long, long), (string Shohin, string Col, string Siz, string Jan)> LoadSkuCodes(IEnumerable<long> shohinIds) {
		var list = shohinIds.Where(x => x > 0).Distinct().ToList();
		var result = new Dictionary<(long, long, long), (string, string, string, string)>();
		if (list.Count == 0) {
			return result;
		}
		// 正典は SKU 展開済みの派生マスタ（受信の SKU 解決と同じ）。派生に無いものだけ商品マスタの色・サイズから補う
		foreach (var chunk in list.Chunk(500)) {
			foreach (var d in _db.Fetch<DerivedShohinColSiz>($"where Id_Shohin IN ({string.Join(",", chunk)})")) {
				result.TryAdd((d.Id_Shohin, d.Id_Col, d.Id_Siz), (d.Code, d.Code_Col, d.Code_Siz, d.Jan1));
			}
			foreach (var s in _db.Fetch<MasterShohin>($"where Id IN ({string.Join(",", chunk)})")) {
				foreach (var k in s.Jcolsiz ?? []) {
					result.TryAdd((s.Id, k.Id_Col, k.Id_Siz), (s.Code, k.Code_Col, k.Code_Siz, k.Jan1));
				}
			}
		}
		return result;
	}

	/// <summary>L04 バッチ一覧。送信の出荷指示・入荷予定は送信後に元データが変わった行数を数える（J-05）</summary>
	public List<LogisticsBatchRow> QueryBatches(LogisticsBatchQueryParam param) {
		ArgumentNullException.ThrowIfNull(param);
		var where = new List<string> { "1 = 1" };
		var args = new List<object>();
		if (param.Direction > 0) {
			where.Add($"Direction = @{args.Count}");
			args.Add(param.Direction);
		}
		if (!string.IsNullOrEmpty(param.Kind)) {
			where.Add($"DataKind = @{args.Count}");
			args.Add(param.Kind);
		}
		if (AllocationRules.IsYmd(param.FromDay)) {
			where.Add($"Vdc >= @{args.Count}");
			args.Add(LocalDayToTicks(param.FromDay));
		}
		if (AllocationRules.IsYmd(param.ToDay)) {
			where.Add($"Vdc < @{args.Count}");
			args.Add(LocalDayToTicks(param.ToDay) + TimeSpan.TicksPerDay);
		}
		var batches = _db.Fetch<TranLogisticsBatch>($"where {string.Join(" AND ", where)} order by Id desc", [.. args]);
		var rows = batches.Select(b => new LogisticsBatchRow(b, CountChangedAfterSend(b))).ToList();
		if (param.ProblemOnly) {
			rows = [.. rows.Where(r => IsProblem(r))];
		}
		return rows;
	}

	private static bool IsProblem(LogisticsBatchRow r) => r.Batch.Direction == (int)EnumLogisticsDirection.Send
		? r.Batch.Status is (int)EnumLogisticsSendStatus.Creating or (int)EnumLogisticsSendStatus.PlaceFailed || r.ChangedAfterSend > 0
		: r.Batch.Status is (int)EnumLogisticsReceiveStatus.Imported or (int)EnumLogisticsReceiveStatus.PartialError or (int)EnumLogisticsReceiveStatus.ImportFailed;

	/// <summary>送信後に元伝票の Vdu が変わった・削除された件数（出荷指示は配分の送信フラグ更新で Vdu が変わるため数えない）</summary>
	private int CountChangedAfterSend(TranLogisticsBatch batch) {
		if (batch.Direction != (int)EnumLogisticsDirection.Send || batch.DataKind != LogisticsDataKind.STOCK
			|| batch.Status == (int)EnumLogisticsSendStatus.Canceled) {
			return 0;
		}
		var refs = _db.Fetch<TranLogisticsLine>("where Id_Batch = @0", batch.Id)
			.GroupBy(l => (l.RefTable, l.RefId)).Select(g => g.First()).ToList();
		var changed = 0;
		foreach (var table in refs.GroupBy(r => r.RefTable)) {
			var ids = string.Join(",", table.Select(r => r.RefId));
			var current = _db.Fetch<IdVdu>($"SELECT Id, Vdu FROM {table.Key} WHERE Id IN ({ids})").ToDictionary(x => x.Id, x => x.Vdu);
			// 入荷が済んだ元伝票（発注の完了・移動の受入）は、入荷確定の反映で Vdu が変わるのが正常なので数えない
			var finished = table.Key == nameof(Tran13Hachu)
				? _db.Fetch<IdVdu>($"SELECT Id, Vdu FROM {nameof(Tran13Hachu)} WHERE Id IN ({ids}) AND EndFlag <> 0").Select(x => x.Id).ToHashSet()
				: _db.Fetch<IdVdu>($"SELECT RelateNo1 AS Id, Vdu FROM {nameof(Tran11IdoIn)} WHERE RelateNo1 IN ({ids})").Select(x => x.Id).ToHashSet();
			changed += table.Count(r => !finished.Contains(r.RefId) && (!current.TryGetValue(r.RefId, out var vdu) || vdu != r.RefVdu));
		}
		return changed;
	}

	/// <summary>Id と Vdu だけを読む受け皿</summary>
	private sealed class IdVdu {
		public long Id { get; set; }
		public long Vdu { get; set; }
	}

	/// <summary>L04 行一覧</summary>
	public List<TranLogisticsLine> QueryLines(LogisticsLineQueryParam param) =>
		_db.Fetch<TranLogisticsLine>("where Id_Batch = @0 order by LineNo, Id", param.BatchId);

	/// <summary>yyyyMMdd（ローカル日付の0時）を UTC Ticks にする</summary>
	internal static long LocalDayToTicks(string ymd) =>
		DateTime.ParseExact(ymd, "yyyyMMdd", null).ToUniversalTime().Ticks;
}
