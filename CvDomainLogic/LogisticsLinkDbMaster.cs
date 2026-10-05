using CvAsset;
using CvBase;
using CvBase.Share;

namespace CvDomainLogic;

// L01 マスタデータ作成（仕様 3.1・5.2）
public partial class LogisticsLinkDb {
	/// <summary>
	/// マスタファイル（PD 商品SKU / BSY 場所）を作成する。種別ごとに1ファイル、0件ならファイルを作らない。
	/// マスタは再作成できるため行明細（<see cref="TranLogisticsLine"/>）は残さない。
	/// </summary>
	public LogisticsRunResult CreateMasterFiles(LogisticsMasterParam param, long idShain) {
		ArgumentNullException.ThrowIfNull(param);
		var kinds = (param.Kinds ?? []).Where(LogisticsDataKind.MasterKinds.Contains).Distinct().ToArray();
		if (kinds.Length == 0) {
			throw new ArgumentException("出力するマスタの種別を選んでください。");
		}
		var settings = RequireUsableSettings();
		if (param.PreviewOnly) {
			return BuildMasterResult(kinds.Select(k => BuildMasterRows(settings, k, param)).ToList(), preview: true);
		}
		return RunLocked($"マスタデータ作成 {string.Join("/", kinds)}", param.ExecType, () => {
			var built = new List<MasterBuildResult>();
			foreach (var kind in kinds) {
				var rows = BuildMasterRows(settings, kind, param);
				if (rows.Rows.Count > 0) {
					var batch = InsertSendBatch(settings, kind, rows.Rows.Count, idShain, param.ExecType);
					batch.FileName = BuildFileName(settings, kind, batch.Id, DateTime.Now);
					var (text, _) = LogisticsFileFormat.BuildFile(kind, rows.Rows);
					if (!PlaceBatchFile(settings, batch, text)) {
						rows.Warnings.Add($"{LogisticsDataKind.DisplayName(kind)}: ファイルを配置できませんでした（連携エラーデータ照会から再出力してください）。");
					}
					rows = rows with { BatchId = batch.Id, FileName = batch.FileName };
				}
				built.Add(rows);
			}
			return BuildMasterResult(built, preview: false);
		}, r => r.Kinds.Sum(k => k.Count));
	}

	/// <summary>種別ごとの出力行と警告</summary>
	private sealed record MasterBuildResult(string Kind, List<IReadOnlyList<string?>> Rows, List<string> Warnings, long BatchId = 0, string FileName = "");

	private static LogisticsRunResult BuildMasterResult(List<MasterBuildResult> built, bool preview) {
		var kinds = built.Select(b => new LogisticsKindResult(b.Kind, b.Rows.Count, b.BatchId, b.FileName)).ToArray();
		var summary = string.Join("、", built.Select(b => $"{LogisticsDataKind.DisplayName(b.Kind)} {b.Rows.Count:N0}件"));
		return new LogisticsRunResult(kinds, [.. built.SelectMany(b => b.Warnings)], (preview ? "プレビュー: " : "作成: ") + summary);
	}

	/// <summary>差分の基準時刻。全件なら0、指定が無ければ前回成功した同種別バッチの作成時刻</summary>
	private long ResolveSince(string kind, LogisticsMasterParam param) =>
		param.IsFull ? 0 : param.SinceVdu > 0 ? param.SinceVdu : LastPlacedVdc(kind);

	private MasterBuildResult BuildMasterRows(LogisticsSettings settings, string kind, LogisticsMasterParam param) {
		var since = ResolveSince(kind, param);
		return kind switch {
			LogisticsDataKind.PD => BuildShohinRows(since),
			LogisticsDataKind.BSY => BuildPlaceRows(settings, since),
			_ => throw new ArgumentException($"未対応のマスタ種別です: {kind}"),
		};
	}

	/// <summary>PD 商品SKU。商品マスタの SKU（Jcolsiz）単位。削除FLGは該当項目が無いため常に0</summary>
	private MasterBuildResult BuildShohinRows(long since) {
		var shohins = _db.Fetch<MasterShohin>("where Vdu >= @0 order by Code", since);
		var rows = new List<IReadOnlyList<string?>>();
		var warnings = new List<string>();
		var noSku = new List<string>();
		var janOwners = new Dictionary<string, HashSet<string>>();
		var noJan = 0;
		foreach (var s in shohins) {
			var skus = s.Jcolsiz ?? [];
			if (skus.Count == 0) {
				noSku.Add(s.Code);
				continue;
			}
			var material = string.Join(" ", (s.Jgrade ?? []).OrderBy(g => g.No)
				.Where(g => !string.IsNullOrWhiteSpace(g.Hinshitu))
				.Select(g => g.Percent > 0 ? $"{g.Hinshitu}{g.Percent}%" : g.Hinshitu));
			if (material.Length == 0) {
				material = s.VMaterial?.Mei ?? string.Empty;
			}
			foreach (var k in skus) {
				var skuKey = $"{s.Code}/{k.Code_Col}/{k.Code_Siz}";
				var jans = new[] { k.Jan1, k.Jan2, k.Jan3 }.Where(j => !string.IsNullOrWhiteSpace(j)).Select(j => j.Trim()).ToList();
				if (jans.Count == 0) {
					noJan++;
				}
				foreach (var jan in jans) {
					if (!janOwners.TryGetValue(jan, out var owners)) {
						janOwners[jan] = owners = [];
					}
					owners.Add(skuKey);
				}
				rows.Add([
					LogisticsDataKind.PD, s.Code, s.Name, s.Ryaku, s.Kana, k.Code_Col, k.Mei_Col, k.Code_Siz, k.Mei_Siz, k.Jan1, k.Jan2, k.Jan3,
					s.VBrand?.Cd, s.VSeason?.Cd, s.VItem?.Cd, s.VCountry?.Cd, material,
					s.TankaJodai.ToString(), s.TankaGenka.ToString(), s.IsZaiko.ToString(), s.Id_Tax.ToString(), s.DayTento, "0",
				]);
			}
		}
		var duplicated = janOwners.Where(kv => kv.Value.Count > 1).ToList();
		if (duplicated.Count > 0) {
			warnings.Add($"商品SKUマスタ: 複数のSKUに同じJANがあります {duplicated.Count:N0}件（連携先でSKUを特定できません）: "
				+ string.Join("、", duplicated.Take(5).Select(kv => $"{kv.Key}={string.Join("|", kv.Value)}")));
		}
		if (noJan > 0) {
			warnings.Add($"商品SKUマスタ: JANの無いSKUが {noJan:N0}件あります。");
		}
		if (noSku.Count > 0) {
			warnings.Add($"商品SKUマスタ: 色・サイズの無い商品は出力しません {noSku.Count:N0}件: {string.Join("、", noSku.Take(5))}");
		}
		return new MasterBuildResult(LogisticsDataKind.PD, rows, warnings);
	}

	/// <summary>
	/// BSY 場所。得意先（倉庫・店舗・卸先等）と仕入先。場所区分は旧CVの業種区分に合わせる
	/// （10 仕入先 / 20 対象倉庫 / 30 その他倉庫 / 40 売仕店・直営店 / 50 卸先）。FAX・削除FLGは該当項目が無いため空・0。
	/// </summary>
	private MasterBuildResult BuildPlaceRows(LogisticsSettings settings, long since) {
		var targets = settings.TargetSokoCodes.ToHashSet();
		var rows = new List<IReadOnlyList<string?>>();
		foreach (var t in _db.Fetch<MasterTokui>("where Vdu >= @0 order by Code", since)) {
			var placeKubun = t.TenType switch {
				0 => targets.Contains(t.Code) ? "20" : "30",
				1 => "50",
				_ => "40",
			};
			rows.Add(PlaceRow(placeKubun, t));
		}
		foreach (var s in _db.Fetch<MasterShiire>("where Vdu >= @0 order by Code", since)) {
			rows.Add(PlaceRow("10", s));
		}
		return new MasterBuildResult(LogisticsDataKind.BSY, rows, []);
	}

	private static IReadOnlyList<string?> PlaceRow(string placeKubun, MasterTorihiki t) => [
		LogisticsDataKind.BSY, placeKubun, t.Code, t.Name, t.Kana, t.Ryaku,
		(t.PostalCode ?? string.Empty).Replace("-", string.Empty), t.Address1, t.Address2, t.Address3,
		(t.Tel ?? string.Empty).Replace("-", string.Empty), string.Empty, "0",
	];
}
