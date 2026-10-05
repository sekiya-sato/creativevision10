using System.Globalization;
using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

// L03 連携データ手動受信の取込・検査（仕様 4.1〜4.3・5.3）
public partial class LogisticsLinkDb {
	/// <summary>受信フォルダの未取込ファイル（*.csv / *.txt）</summary>
	public List<LogisticsReceiveFileInfo> QueryReceiveFiles() {
		var settings = RequireUsableSettings();
		var dir = new DirectoryInfo(GetFolder(settings, LogisticsSettings.ReceiveDir));
		return [.. dir.EnumerateFiles()
			.Where(f => f.Extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
			.OrderBy(f => f.Name, StringComparer.Ordinal)
			.Select(f => new LogisticsReceiveFileInfo(f.Name, LogisticsDataKind.DetectReceiveKind(f.Name), f.Length,
				f.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture)))];
	}

	/// <summary>
	/// 受信ファイルを取り込んで検査する（在庫・伝票は変えない）。ファイル1本＝バッチ1件。
	/// 同じ内容（SHA-256一致）を取り込み済みなら行を作らずに既取込として返す。
	/// 受信フォルダのファイルは取込後に recv_bak へ移す（種別判定不能・読込不能は受信フォルダに残す）。
	/// </summary>
	public LogisticsRunResult ImportReceiveFiles(LogisticsReceiveImportParam param, long idShain) {
		ArgumentNullException.ThrowIfNull(param);
		var settings = RequireUsableSettings();
		return RunLocked("連携データ受信 取込", param.ExecType, () => {
			var sources = new List<(string FileName, byte[] Content, string? Path)>();
			if (param.Uploads is { Length: > 0 }) {
				sources.AddRange(param.Uploads.Select(u => (Path.GetFileName(u.FileName), u.Content ?? [], (string?)null)));
			}
			else {
				var recv = GetFolder(settings, LogisticsSettings.ReceiveDir);
				var names = param.FileNames is { Length: > 0 }
					? param.FileNames.Select(Path.GetFileName).OfType<string>()
					: QueryReceiveFiles().Select(f => f.FileName);
				foreach (var name in names) {
					var path = Path.Combine(recv, name);
					if (!File.Exists(path)) {
						throw new ArgumentException($"受信フォルダにファイルがありません: {name}");
					}
					sources.Add((name, File.ReadAllBytes(path), path));
				}
			}
			if (sources.Count == 0) {
				return new LogisticsRunResult([], [], "取り込むファイルがありません。");
			}
			var kinds = new List<LogisticsKindResult>();
			var warnings = new List<string>();
			foreach (var (fileName, content, path) in sources) {
				var outcome = ImportOne(settings, fileName, content, idShain, param.ExecType);
				kinds.Add(new LogisticsKindResult(outcome.Kind, outcome.Rows, outcome.BatchId, fileName));
				if (outcome.Warning.Length > 0) {
					warnings.Add($"{fileName}: {outcome.Warning}");
				}
				if (path != null && outcome.MoveToBackup) {
					MoveToReceiveBackup(settings, path);
				}
			}
			var errorRows = kinds.Where(k => k.BatchId > 0).Sum(k => CountLines(k.BatchId, EnumLogisticsLineStatus.Error));
			return new LogisticsRunResult([.. kinds], [.. warnings],
				$"取込 {kinds.Count(k => k.BatchId > 0):N0}ファイル {kinds.Sum(k => k.Count):N0}行（エラー {errorRows:N0}行）");
		}, r => r.Kinds.Sum(k => k.Count));
	}

	/// <summary>受信バッチの未処理・エラー行を検査し直す（マスタ修正後など）</summary>
	public LogisticsRunResult RecheckBatch(LogisticsRecheckParam param, long idShain) {
		ArgumentNullException.ThrowIfNull(param);
		return RunLocked($"連携データ受信 再検査 {param.BatchId}", param.ExecType, () => {
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
			return new LogisticsRunResult([new LogisticsKindResult(batch.DataKind, batch.RowCount, batch.Id, batch.FileName)], [],
				$"再検査 エラー {batch.ErrorCount:N0}行");
		}, r => r.Kinds.Sum(k => k.Count));
	}

	private sealed record ImportOutcome(string Kind, int Rows, long BatchId, string Warning, bool MoveToBackup);

	private ImportOutcome ImportOne(LogisticsSettings settings, string fileName, byte[] content, long idShain, int execType) {
		var hash = ComputeHash(content);
		var kind = LogisticsDataKind.DetectReceiveKind(fileName);
		var existing = _db.Fetch<TranLogisticsBatch>(
			"where Direction = @0 and FileHash = @1 and Status not in (@2, @3)",
			(int)EnumLogisticsDirection.Receive, hash, (int)EnumLogisticsReceiveStatus.ImportFailed, (int)EnumLogisticsReceiveStatus.Canceled).FirstOrDefault();
		if (existing != null) {
			return new ImportOutcome(kind, 0, 0, $"取込済みのファイルと同じ内容です（バッチ{existing.Id}）。取り込みませんでした。", true);
		}
		var vdate = Common.GetVdate();
		var batch = new TranLogisticsBatch {
			LinkCode = settings.LinkCode,
			Direction = (int)EnumLogisticsDirection.Receive,
			DataKind = kind,
			FileName = fileName,
			FileHash = hash,
			Status = (int)EnumLogisticsReceiveStatus.Imported,
			ExecType = execType,
			Id_Shain = idShain,
			Vdc = vdate,
			Vdu = vdate,
		};
		List<LogisticsCsvLine> parsed;
		try {
			if (kind.Length == 0) {
				throw new InvalidDataException("ファイル名から種別（ORDERFIX/LACK/STOCKFIX/INVENTORY）を判定できません。");
			}
			var text = GetEncoding(settings).GetString(content);
			parsed = LogisticsFileFormat.Parse(kind, text);
		}
		catch (InvalidDataException ex) {
			batch.Status = (int)EnumLogisticsReceiveStatus.ImportFailed;
			batch.Memo = ex.Message;
			_db.Insert(batch);
			return new ImportOutcome(kind, 0, batch.Id, ex.Message, false);
		}
		_db.BeginTransaction();
		try {
			batch.RowCount = parsed.Count;
			_db.Insert(batch);
			_db.InsertBulk(parsed.Select(p => new TranLogisticsLine {
				Id_Batch = batch.Id,
				LineNo = p.LineNo,
				RawText = p.RawText.Length > 2000 ? p.RawText[..2000] : p.RawText,
				Vdc = vdate,
				Vdu = vdate,
			}).ToList());
			CheckAndSave(batch);
			_db.CompleteTransaction();
		}
		catch {
			_db.AbortTransaction();
			throw;
		}
		return new ImportOutcome(kind, parsed.Count, batch.Id, string.Empty, true);
	}

	private static void MoveToReceiveBackup(LogisticsSettings settings, string path) {
		var backupDir = GetFolder(settings, LogisticsSettings.ReceiveBackupDir);
		var target = Path.Combine(backupDir, Path.GetFileName(path));
		if (File.Exists(target)) {
			target = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(path)}_{DateTime.Now:yyyyMMddHHmmssfff}{Path.GetExtension(path)}");
		}
		File.Move(path, target);
	}

	private int CountLines(long batchId, EnumLogisticsLineStatus status) =>
		_db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {nameof(TranLogisticsLine)} WHERE Id_Batch = @0 AND Status = @1", batchId, (int)status);

	/// <summary>棚番（伝票の TanaNo は8桁まで。まとめ・重複判定も切り詰めた値で行う）</summary>
	internal static string TanaNo(string? raw) {
		var t = (raw ?? string.Empty).Trim();
		return t.Length > 8 ? t[..8] : t;
	}

	internal TranLogisticsBatch RequireReceiveBatch(long batchId) {
		var batch = _db.Fetch<TranLogisticsBatch>("where Id = @0", batchId).FirstOrDefault()
			?? throw new ArgumentException($"バッチ {batchId} がありません。");
		if (batch.Direction != (int)EnumLogisticsDirection.Receive || !LogisticsDataKind.IsReceive(batch.DataKind)
			|| batch.Status is (int)EnumLogisticsReceiveStatus.ImportFailed or (int)EnumLogisticsReceiveStatus.Canceled) {
			throw new ArgumentException("取込済みの受信バッチではありません。");
		}
		return batch;
	}

	/// <summary>バッチの未処理・エラー行を検査して保存し、バッチのエラー件数を更新する（呼び出し側のトランザクション内）</summary>
	internal void CheckAndSave(TranLogisticsBatch batch) {
		var lines = _db.Fetch<TranLogisticsLine>("where Id_Batch = @0 and Status in (@1, @2) order by LineNo, Id",
			batch.Id, (int)EnumLogisticsLineStatus.Pending, (int)EnumLogisticsLineStatus.Error);
		new ReceiveChecker(this, batch, lines).Run();
		var vdate = Common.GetVdate();
		foreach (var line in lines) {
			line.Vdu = vdate;
			_db.Update(line);
		}
		batch.ErrorCount = CountLines(batch.Id, EnumLogisticsLineStatus.Error);
		batch.Vdu = vdate;
		_db.Update(batch);
	}

	/// <summary>受信行の検査（仕様 4.3）。行の解決キー・数量・作業日・エラーを設定する（保存はしない）</summary>
	private sealed class ReceiveChecker(LogisticsLinkDb owner, TranLogisticsBatch batch, List<TranLogisticsLine> lines) {
		private readonly ExDatabase _db = owner._db;
		private readonly string _kind = batch.DataKind;
		private readonly Dictionary<long, List<string>> _fields = [];
		private Dictionary<string, MasterTokui> _targetSoko = [];
		private Dictionary<(string, string, string), DerivedShohinColSiz> _skuByCode = [];
		private Dictionary<string, List<DerivedShohinColSiz>> _skuByJan = [];

		public void Run() {
			var settings = owner.LoadSettings();
			_targetSoko = owner.LoadTargetSoko(settings).ToDictionary(t => t.Code);
			foreach (var line in lines) {
				line.Status = (int)EnumLogisticsLineStatus.Pending;
				line.ErrorCode = string.Empty;
				line.ErrorMsg = string.Empty;
				var parsed = LogisticsFileFormat.Parse(_kind, line.RawText).FirstOrDefault();
				var columns = LogisticsFileFormat.Columns(_kind).Count;
				if (parsed == null || parsed.Fields.Count != columns) {
					SetError(line, "E01", $"列数が{columns}ではありません（{parsed?.Fields.Count ?? 0}）。");
					continue;
				}
				_fields[line.Id] = [.. parsed.Fields];
			}
			LoadSku();
			switch (_kind) {
				case LogisticsDataKind.ORDERFIX:
				case LogisticsDataKind.LACK:
					CheckOrderFix();
					break;
				case LogisticsDataKind.STOCKFIX:
					CheckStockFix();
					break;
				case LogisticsDataKind.INVENTORY:
					CheckInventory();
					break;
				default:
					foreach (var line in lines) {
						SetError(line, "E01", $"受信できない種別です: {_kind}");
					}
					break;
			}
		}

		private IEnumerable<TranLogisticsLine> Active => lines.Where(l => l.Status == (int)EnumLogisticsLineStatus.Pending);

		private string F(TranLogisticsLine line, string header) => _fields[line.Id][LogisticsFileFormat.ColumnIndex(_kind, header)];

		private static void SetError(TranLogisticsLine line, string code, string message) {
			line.Status = (int)EnumLogisticsLineStatus.Error;
			line.ErrorCode = code;
			line.ErrorMsg = message.Length > 1000 ? message[..1000] : message;
		}

		private static void SetWarning(TranLogisticsLine line, string code, string message) {
			if (line.Status == (int)EnumLogisticsLineStatus.Pending && line.ErrorCode.Length == 0) {
				line.ErrorCode = code;
				line.ErrorMsg = message;
			}
		}

		/// <summary>1行でもエラーのある伝票（まとめ単位）は全行エラーにする（旧CVと同じ）</summary>
		private void PropagateGroupErrors(Func<TranLogisticsLine, string?> groupKey) {
			var keys = lines.Where(l => _fields.ContainsKey(l.Id)).Select(l => (Line: l, Key: groupKey(l))).Where(x => x.Key != null).ToList();
			var errorKeys = keys.Where(x => x.Line.Status == (int)EnumLogisticsLineStatus.Error).Select(x => x.Key).ToHashSet();
			foreach (var (line, _) in keys.Where(x => errorKeys.Contains(x.Key) && x.Line.Status != (int)EnumLogisticsLineStatus.Error)) {
				SetError(line, "E19", "同じ伝票にエラー行があるため反映しません。");
			}
		}

		private void LoadSku() {
			var codes = Active.Where(l => _fields.ContainsKey(l.Id)).Select(l => F(l, "商品CD")).Where(c => c.Length > 0).Distinct().ToList();
			var jans = Active.Where(l => _fields.ContainsKey(l.Id)).Select(l => F(l, "JAN")).Where(c => c.Length > 0).Distinct().ToList();
			var rows = new List<DerivedShohinColSiz>();
			foreach (var chunk in codes.Chunk(500)) {
				rows.AddRange(_db.Fetch<DerivedShohinColSiz>($"where Code in ({string.Join(",", chunk.Select((_, i) => "@" + i))})", [.. chunk]));
			}
			foreach (var chunk in jans.Chunk(300)) {
				var p = string.Join(",", chunk.Select((_, i) => "@" + i));
				rows.AddRange(_db.Fetch<DerivedShohinColSiz>($"where Jan1 in ({p}) or Jan2 in ({p}) or Jan3 in ({p})", [.. chunk]));
			}
			rows = [.. rows.DistinctBy(r => r.Id)];
			_skuByCode = rows.GroupBy(r => (r.Code, r.Code_Col, r.Code_Siz)).ToDictionary(g => g.Key, g => g.First());
			_skuByJan = rows.SelectMany(r => new[] { r.Jan1, r.Jan2, r.Jan3 }.Where(j => !string.IsNullOrWhiteSpace(j)).Select(j => (Jan: j.Trim(), Row: r)))
				.GroupBy(x => x.Jan).ToDictionary(g => g.Key, g => g.Select(x => x.Row).DistinctBy(r => r.Id).ToList());
		}

		/// <summary>SKUを商品CD・色CD・サイズCD（優先）またはJANで解決する。どれも空なら null を返し、エラーは呼び出し側が判断する</summary>
		private DerivedShohinColSiz? ResolveSku(TranLogisticsLine line, out bool given) {
			var code = F(line, "商品CD");
			var jan = F(line, "JAN");
			given = code.Length > 0 || jan.Length > 0;
			if (code.Length > 0) {
				if (_skuByCode.TryGetValue((code, F(line, "色CD"), F(line, "サイズCD")), out var byCode)) {
					if (jan.Length > 0 && !new[] { byCode.Jan1, byCode.Jan2, byCode.Jan3 }.Any(j => j?.Trim() == jan)) {
						SetWarning(line, "W02", $"JAN {jan} が商品・色・サイズと一致しません（商品・色・サイズを優先しました）。");
					}
					return byCode;
				}
				SetError(line, "E02", $"商品・色・サイズ {code}/{F(line, "色CD")}/{F(line, "サイズCD")} が商品マスタにありません。");
				return null;
			}
			if (jan.Length > 0) {
				if (_skuByJan.TryGetValue(jan, out var byJan) && byJan.Count == 1) {
					return byJan[0];
				}
				SetError(line, "E02", byJan is { Count: > 1 } ? $"JAN {jan} が複数のSKUにあります。" : $"JAN {jan} が商品マスタにありません。");
				return null;
			}
			return null;
		}

		private static bool TryInt(string s, out int value) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

		/// <summary>出荷確定・欠品</summary>
		private void CheckOrderFix() {
			var isLack = _kind == LogisticsDataKind.LACK;
			var ids = Active.Select(l => long.TryParse(F(l, "指示ID"), out var id) ? id : 0).Where(id => id > 0).Distinct().ToList();
			var haibun = ids.Count == 0 ? [] : _db.Fetch<TranHaibun>($"where Id in ({string.Join(",", ids)})").ToDictionary(h => h.Id);
			var sentSu = LoadSentSu(nameof(TranHaibun), ids);
			var sokoCode = owner.LoadTokuiByIds(haibun.Values.Select(h => h.Id_Soko)).ToDictionary(kv => kv.Key, kv => kv.Value.Code);
			var pendingElsewhere = LoadPendingRefIds(nameof(TranHaibun), ids);
			foreach (var line in Active.ToList()) {
				if (!long.TryParse(F(line, "指示ID"), out var id) || id <= 0) {
					SetError(line, "E01", "指示IDがありません。");
					continue;
				}
				if (!AllocationRules.IsYmd(F(line, "出荷日"))) {
					SetError(line, "E01", "出荷日を yyyyMMdd で指定してください。");
					continue;
				}
				if (!haibun.TryGetValue(id, out var h)) {
					SetError(line, "E10", $"指示ID {id} の配分がありません。");
					continue;
				}
				line.RefTable = nameof(TranHaibun);
				line.RefId = id;
				line.Id_Soko = h.Id_Soko;
				line.Id_Shohin = h.Id_Shohin;
				line.Id_Col = h.Id_Col;
				line.Id_Siz = h.Id_Siz;
				line.WorkDay = F(line, "出荷日");
				var sku = ResolveSku(line, out var given);
				if (line.Status == (int)EnumLogisticsLineStatus.Error) {
					continue;
				}
				if (given && sku != null && (sku.Id_Shohin, sku.Id_Col, sku.Id_Siz) != (h.Id_Shohin, h.Id_Col, h.Id_Siz)) {
					SetError(line, "E10", "商品・色・サイズが指示と違います。");
					continue;
				}
				var soko = F(line, "倉庫CD");
				if (soko.Length > 0 && soko != sokoCode.GetValueOrDefault(h.Id_Soko)) {
					SetError(line, "E03", $"倉庫CD {soko} が指示の倉庫と違います。");
					continue;
				}
				if (h.EndFlag != 0 || !string.IsNullOrEmpty(h.KakuteiDay)) {
					SetError(line, "E11", "配分は確定・完了済みです（重複受信の可能性）。");
					continue;
				}
				if (!AllocationRules.IsCommittableKubun(h.Kubun)) {
					SetError(line, "E11", "確定できない配分区分です（取置など）。");
					continue;
				}
				if (h.SendFlg == 0) {
					SetError(line, "E11", "送信していない配分です。");
					continue;
				}
				var shiji = sentSu.TryGetValue(id, out var s) ? s : h.Kubun == (int)EnumHaibun.Hatsukai ? Math.Min(h.ArrivedSu, h.Su) : h.Su;
				var kakuteiText = F(line, "確定数");
				var ketsuText = F(line, "欠品数");
				int kakutei = 0, ketsu;
				if (!isLack || kakuteiText.Length > 0) {
					if (!TryInt(kakuteiText, out kakutei)) {
						SetError(line, "E01", "確定数が数値ではありません。");
						continue;
					}
				}
				if (ketsuText.Length == 0) {
					ketsu = shiji - kakutei;
				}
				else if (!TryInt(ketsuText, out ketsu)) {
					SetError(line, "E01", "欠品数が数値ではありません。");
					continue;
				}
				line.Su = kakutei;
				line.Su2 = ketsu;
				if (kakutei < 0 || kakutei > shiji || ketsu < 0) {
					SetError(line, "E12", $"確定数 {kakutei}・欠品数 {ketsu} が指示数 {shiji} の範囲外です。");
					continue;
				}
				if (pendingElsewhere.Contains(id)) {
					SetError(line, "E13", "同じ指示の未反映の受信が他のバッチにあります。");
					continue;
				}
				if (kakutei + ketsu != shiji) {
					SetWarning(line, "W12", $"確定数 {kakutei}＋欠品数 {ketsu} が指示数 {shiji} と一致しません（残りは欠品として完了します）。");
				}
			}
			foreach (var dup in Active.GroupBy(l => l.RefId).Where(g => g.Key > 0 && g.Count() > 1).SelectMany(g => g).ToList()) {
				SetError(dup, "E13", "同じ指示IDがファイル内に複数あります。");
			}
			PropagateGroupErrors(l => l.RefId > 0 && haibun.TryGetValue(l.RefId, out var h)
				? $"{l.WorkDay}|{HaibunHeaderKey.From(h)}" : null);
		}

		/// <summary>入荷確定</summary>
		private void CheckStockFix() {
			var hachuIds = new List<long>();
			var idoIds = new List<long>();
			foreach (var line in Active) {
				if (long.TryParse(F(line, "元伝票ID"), out var id) && id > 0) {
					(F(line, "区分") == "20" ? hachuIds : idoIds).Add(id);
				}
			}
			var hachu = hachuIds.Count == 0 ? [] : _db.Fetch<Tran13Hachu>($"where Id in ({string.Join(",", hachuIds.Distinct())})").ToDictionary(x => x.Id);
			var ido = idoIds.Count == 0 ? [] : _db.Fetch<Tran10IdoOut>($"where Id in ({string.Join(",", idoIds.Distinct())})").ToDictionary(x => x.Id);
			var received = idoIds.Count == 0 ? [] : _db.Fetch<Tran11IdoIn>($"where RelateNo1 in ({string.Join(",", idoIds.Distinct())})")
				.Select(x => x.RelateNo1).ToHashSet();
			var sent = SentRefKeys();
			var pendingIdo = LoadPendingRefIds(nameof(Tran10IdoOut), idoIds);
			var pendingHachu = LoadPendingRefIds(nameof(Tran13Hachu), hachuIds);
			var appliedHachu = LoadAppliedLines(nameof(Tran13Hachu), hachuIds)
				.Select(l => (l.RefId, l.WorkDay, l.Id_Shohin, l.Id_Col, l.Id_Siz, l.Su)).ToHashSet();
			var shiireReceived = owner.LoadShiireReceived(hachuIds);
			var hachuRest = new Dictionary<(long, long, long, long), int>();
			foreach (var line in Active.ToList()) {
				var kubun = F(line, "区分");
				if (kubun is not ("10" or "20")) {
					SetError(line, "E01", "区分は 10（移動）か 20（仕入）です。");
					continue;
				}
				if (!long.TryParse(F(line, "元伝票ID"), out var id) || id <= 0) {
					SetError(line, "E01", "元伝票IDがありません。");
					continue;
				}
				if (!AllocationRules.IsYmd(F(line, "入荷日"))) {
					SetError(line, "E01", "入荷日を yyyyMMdd で指定してください。");
					continue;
				}
				line.WorkDay = F(line, "入荷日");
				line.RefTable = kubun == "20" ? nameof(Tran13Hachu) : nameof(Tran10IdoOut);
				line.RefId = id;
				line.RefNo = int.TryParse(F(line, "元伝票行No"), out var no) ? no : 0;
				List<Tran99Meisai> meisai;
				long expectedSoko;
				if (kubun == "20") {
					if (!hachu.TryGetValue(id, out var x) || x.Kubun is < 10 or > 19) {
						SetError(line, "E20", $"発注 {id} がありません。");
						continue;
					}
					if (x.EndFlag != 0) {
						SetError(line, "E21", "発注は完了済みです。");
						continue;
					}
					if (pendingHachu.Contains(id)) {
						SetError(line, "E13", "同じ発注の未反映の受信が他のバッチにあります。先に反映または除外してください。");
						continue;
					}
					meisai = x.Jmeisai ?? [];
					expectedSoko = x.Id_Soko;
				}
				else {
					if (!ido.TryGetValue(id, out var x)) {
						SetError(line, "E20", $"移動出庫 {id} がありません。");
						continue;
					}
					if (received.Contains(id)) {
						SetError(line, "E21", "移動は受入済みです（重複受信の可能性）。");
						continue;
					}
					if (pendingIdo.Contains(id)) {
						SetError(line, "E13", "同じ移動の未反映の受信が他のバッチにあります。");
						continue;
					}
					meisai = x.Jmeisai ?? [];
					expectedSoko = x.Id_Ido;
				}
				if (!sent.Contains((line.RefTable, id))) {
					SetError(line, "E20", "入荷予定として送信していない伝票です。");
					continue;
				}
				if (!_targetSoko.TryGetValue(F(line, "入荷倉庫CD"), out var soko) || soko.Id != expectedSoko) {
					SetError(line, "E03", $"入荷倉庫CD {F(line, "入荷倉庫CD")} が元伝票の入荷倉庫と違います。");
					continue;
				}
				line.Id_Soko = soko.Id;
				var sku = ResolveSku(line, out var given);
				if (line.Status == (int)EnumLogisticsLineStatus.Error) {
					continue;
				}
				if (!given || sku == null) {
					SetError(line, "E02", "商品・色・サイズまたはJANを指定してください。");
					continue;
				}
				(line.Id_Shohin, line.Id_Col, line.Id_Siz) = (sku.Id_Shohin, sku.Id_Col, sku.Id_Siz);
				if (!TryInt(F(line, "入荷数"), out var su) || su <= 0) {
					SetError(line, "E22", "入荷数は1以上の数値です。");
					continue;
				}
				line.Su = su;
				if (kubun == "20") {
					// 分割入荷は受けるが、同じ発注・入荷日・SKU・数量が反映済みなら重複受信とみなす（内容の違う再送で二重計上しない）
					if (appliedHachu.Contains((id, line.WorkDay, sku.Id_Shohin, sku.Id_Col, sku.Id_Siz, su))) {
						SetError(line, "E14", "同じ発注・入荷日・SKU・数量の入荷が反映済みです（重複受信の可能性）。分割入荷なら除外せず連携先に確認してください。");
						continue;
					}
					var key = (id, sku.Id_Shohin, sku.Id_Col, sku.Id_Siz);
					if (!hachuRest.TryGetValue(key, out var rest)) {
						rest = meisai.Where(m => (m.Id_Shohin, m.Id_Col, m.Id_Siz) == (sku.Id_Shohin, sku.Id_Col, sku.Id_Siz)).Sum(m => m.Su)
							- shiireReceived.GetValueOrDefault(key);
					}
					hachuRest[key] = rest - su;
					var inSource = meisai.Any(m => (m.Id_Shohin, m.Id_Col, m.Id_Siz) == (sku.Id_Shohin, sku.Id_Col, sku.Id_Siz));
					if (inSource && su > rest) {
						SetWarning(line, "W21", $"発注残 {Math.Max(rest, 0)} を超える入荷です（反映は行います）。");
					}
				}
				if (!meisai.Any(m => (m.Id_Shohin, m.Id_Col, m.Id_Siz) == (sku.Id_Shohin, sku.Id_Col, sku.Id_Siz))) {
					SetWarning(line, "W20", "元伝票に無いSKUです。");
				}
			}
			// 同じ移動出庫の入荷日違いは1回の移動受にできないため検査で止める
			foreach (var split in Active.Where(l => l.RefTable == nameof(Tran10IdoOut)).GroupBy(l => l.RefId)
				.Where(g => g.Select(l => l.WorkDay).Distinct().Count() > 1).SelectMany(g => g).ToList()) {
				SetError(split, "E13", "同じ移動の入荷日が複数あります（移動受は1回だけです）。");
			}
			PropagateGroupErrors(l => l.RefId > 0 ? $"{l.WorkDay}|{l.RefTable}|{l.RefId}" : null);
		}

		/// <summary>棚卸</summary>
		private void CheckInventory() {
			foreach (var line in Active.ToList()) {
				if (!AllocationRules.IsYmd(F(line, "棚卸日"))) {
					SetError(line, "E01", "棚卸日を yyyyMMdd で指定してください。");
					continue;
				}
				line.WorkDay = F(line, "棚卸日");
				if (!_targetSoko.TryGetValue(F(line, "倉庫CD"), out var soko)) {
					SetError(line, "E03", $"倉庫CD {F(line, "倉庫CD")} は対象倉庫ではありません。");
					continue;
				}
				line.Id_Soko = soko.Id;
				var sku = ResolveSku(line, out var given);
				if (line.Status == (int)EnumLogisticsLineStatus.Error) {
					continue;
				}
				if (!given || sku == null) {
					SetError(line, "E02", "商品・色・サイズまたはJANを指定してください。");
					continue;
				}
				(line.Id_Shohin, line.Id_Col, line.Id_Siz) = (sku.Id_Shohin, sku.Id_Col, sku.Id_Siz);
				if (!TryInt(F(line, "数量"), out var su) || su < 0) {
					SetError(line, "E01", "数量は0以上の数値です。");
					continue;
				}
				line.Su = su;
			}
			foreach (var dup in Active.GroupBy(l => (l.Id_Soko, l.WorkDay, TanaNo(F(l, "棚番")), l.Id_Shohin, l.Id_Col, l.Id_Siz))
				.Where(g => g.Count() > 1).SelectMany(g => g).ToList()) {
				SetError(dup, "E30", "同じ倉庫・棚卸日・棚番・SKUがファイル内に複数あります。");
			}
			// 同じ倉庫・棚卸日・棚番の棚卸データが既にある（前回の受信・HHT・手入力）か、他のバッチで未反映なら重複とみなす
			var keys = Active.Select(l => (l.Id_Soko, l.WorkDay, Tana: TanaNo(F(l, "棚番")))).Distinct().ToList();
			if (keys.Count > 0) {
				var existing = _db.Fetch<Tran60Tana>(
					$"where Id_Soko in ({string.Join(",", keys.Select(k => k.Id_Soko).Distinct())}) and DenDay in ({string.Join(",", keys.Select((_, i) => "@" + i))})",
					[.. keys.Select(k => (object)k.WorkDay)])
					.Select(t => (t.Id_Soko, t.DenDay, t.TanaNo)).ToHashSet();
				var pending = _db.Fetch<TranLogisticsLine>(
					$"where Status = @0 AND Id_Batch <> @1 AND Id_Soko in ({string.Join(",", keys.Select(k => k.Id_Soko).Distinct())}) "
					+ $"AND Id_Batch IN (SELECT Id FROM {nameof(TranLogisticsBatch)} WHERE Direction = @2 AND DataKind = @3 AND Status NOT IN (@4, @5))",
					(int)EnumLogisticsLineStatus.Pending, batch.Id, (int)EnumLogisticsDirection.Receive, LogisticsDataKind.INVENTORY,
					(int)EnumLogisticsReceiveStatus.ImportFailed, (int)EnumLogisticsReceiveStatus.Canceled)
					.Select(l => (l.Id_Soko, l.WorkDay, TanaNo(LogisticsFileFormat.Parse(LogisticsDataKind.INVENTORY, l.RawText).FirstOrDefault()?.Fields
						.ElementAtOrDefault(LogisticsFileFormat.ColumnIndex(LogisticsDataKind.INVENTORY, "棚番")) ?? string.Empty))).ToHashSet();
				foreach (var line in Active.ToList()) {
					var key = (line.Id_Soko, line.WorkDay, TanaNo(F(line, "棚番")));
					if (existing.Contains(key)) {
						SetError(line, "E31", "同じ倉庫・棚卸日・棚番の棚卸データが既にあります（重複受信の可能性）。");
					}
					else if (pending.Contains(key)) {
						SetError(line, "E31", "同じ倉庫・棚卸日・棚番の未反映の受信が他のバッチにあります。");
					}
				}
			}
			PropagateGroupErrors(l => l.Id_Soko > 0 ? $"{l.WorkDay}|{l.Id_Soko}|{TanaNo(F(l, "棚番"))}" : null);
		}

		/// <summary>反映済みの受信行（同じ参照）</summary>
		private List<TranLogisticsLine> LoadAppliedLines(string refTable, List<long> ids) =>
			ids.Count == 0 ? [] : _db.Fetch<TranLogisticsLine>(
				$"where RefTable = @0 AND RefId IN ({string.Join(",", ids.Distinct())}) AND Status = @1 "
				+ $"AND Id_Batch IN (SELECT Id FROM {nameof(TranLogisticsBatch)} WHERE Direction = @2)",
				refTable, (int)EnumLogisticsLineStatus.Applied, (int)EnumLogisticsDirection.Receive);

		/// <summary>取消していない送信バッチで送った数（参照Idごとの最新）</summary>
		private Dictionary<long, int> LoadSentSu(string refTable, List<long> ids) {
			if (ids.Count == 0) {
				return [];
			}
			return _db.Fetch<TranLogisticsLine>(
				$"where RefTable = @0 AND RefId IN ({string.Join(",", ids)}) AND Id_Batch IN (SELECT Id FROM {nameof(TranLogisticsBatch)} WHERE Direction = @1 AND Status <> @2) order by Id",
				refTable, (int)EnumLogisticsDirection.Send, (int)EnumLogisticsSendStatus.Canceled)
				.GroupBy(l => l.RefId).ToDictionary(g => g.Key, g => g.Last().Su);
		}

		/// <summary>取消していない送信バッチに載っている元伝票</summary>
		private HashSet<(string, long)> SentRefKeys() =>
			_db.Fetch<TranLogisticsLine>(
				$"where RefTable IN (@0, @1) AND Id_Batch IN (SELECT Id FROM {nameof(TranLogisticsBatch)} WHERE Direction = @2 AND Status <> @3)",
				nameof(Tran13Hachu), nameof(Tran10IdoOut), (int)EnumLogisticsDirection.Send, (int)EnumLogisticsSendStatus.Canceled)
				.Select(l => (l.RefTable, l.RefId)).ToHashSet();

		/// <summary>このバッチ以外の受信バッチで未反映（未処理）の同じ参照</summary>
		private HashSet<long> LoadPendingRefIds(string refTable, List<long> ids) {
			if (ids.Count == 0) {
				return [];
			}
			return _db.Fetch<TranLogisticsLine>(
				$"where RefTable = @0 AND RefId IN ({string.Join(",", ids.Distinct())}) AND Status = @1 AND Id_Batch <> @2 "
				+ $"AND Id_Batch IN (SELECT Id FROM {nameof(TranLogisticsBatch)} WHERE Direction = @3 AND Status NOT IN (@4, @5))",
				refTable, (int)EnumLogisticsLineStatus.Pending, batch.Id, (int)EnumLogisticsDirection.Receive,
				(int)EnumLogisticsReceiveStatus.ImportFailed, (int)EnumLogisticsReceiveStatus.Canceled)
				.Select(l => l.RefId).ToHashSet();
		}
	}
}
