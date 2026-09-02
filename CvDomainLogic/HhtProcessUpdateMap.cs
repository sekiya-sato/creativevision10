using CvBase;
using CvBase.Share;
using System.Globalization;

namespace CvDomainLogic;

/// <summary>
/// HHTデータ更新のマスタ解決と伝票組み立て。
/// <para>
/// 仕様は `Doc/spec/archive/2026-08-24_HHTデータ更新詳細設計.md` の 5.4 - 5.6 を参照する。
/// </para>
/// </summary>
public partial class HhtProcess {

	#region マスタ解決

	/// <summary>
	/// 変換対象のバッチで必要なマスタを読み込む。
	/// <para>
	/// 件数の小さいマスタ(得意先・仕入先・社員)は全件、
	/// 商品(<see cref="DerivedShohinColSiz"/> 16万件)と顧客(<see cref="MasterEndCustomer"/> 155万件)は
	/// バッチ内に出現したキーだけを IN句で引く。全件ロードするとメモリと時間の無駄になるため。
	/// </para>
	/// </summary>
	private HhtMasterCache LoadMasterCache(List<TranVulcanHht> rows) {
		var cache = new HhtMasterCache {
			Tokui = _db.Fetch<MasterTokui>(),
			Shiire = _db.Fetch<MasterShiire>(),
			Shain = _db.Fetch<MasterShain>(),
			Sysman = _db.Fetch<MasterSysman>("where Id = 1").FirstOrDefault() ?? new MasterSysman(),
		};
		cache.TokuiByCode = BuildCodeIndex(cache.Tokui, x => x.Code);
		cache.ShiireByCode = BuildCodeIndex(cache.Shiire, x => x.Code);
		cache.ShainByCode = BuildCodeIndex(cache.Shain, x => x.Code);

		LoadSkuCache(rows, cache);
		LoadCustomerCache(rows, cache);
		return cache;
	}

	/// <summary>
	/// コードの前0を除いた値で索引を作る。
	/// <para>
	/// VULCAN側は前0埋め(店舗8桁・担当者6桁)だがマスタ側の桁数は不定のため、両辺の前0を除いて比較する。
	/// 前0を除くと複数一致し得る(実データで MasterTokui に "16" と "000016" が同名で併存)ので、
	/// 索引の値はリストにして <see cref="PickOne"/> で優先順位を付けて絞る。
	/// </para>
	/// </summary>
	private static Dictionary<string, List<T>> BuildCodeIndex<T>(List<T> items, Func<T, string> codeSelector) {
		var index = new Dictionary<string, List<T>>(StringComparer.Ordinal);
		foreach (var item in items) {
			var key = NormalizeCode(codeSelector(item));
			if (key.Length == 0) {
				continue;
			}
			if (!index.TryGetValue(key, out var list)) {
				list = [];
				index.Add(key, list);
			}
			list.Add(item);
		}
		return index;
	}

	/// <summary>コードの前0と空白を除く</summary>
	private static string NormalizeCode(string? code) =>
		(code ?? string.Empty).Trim().TrimStart('0');

	/// <summary>
	/// 商品(SKU)の索引を作る。上段JANのみと上段+下段の両方に対応する。
	/// <para>
	/// マスタ側の Jan1 にサイズCD("24"等)の誤登録が1,285行あるため、
	/// <see cref="JanMinLength"/> 未満の値は索引に入れない。入れると複数一致(E104)が誤発火する。
	/// </para>
	/// </summary>
	private void LoadSkuCache(List<TranVulcanHht> rows, HhtMasterCache cache) {
		var upperJans = rows
			.Select(x => (x.Jan1 ?? string.Empty).Trim())
			.Where(x => x.Length >= JanMinLength)
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (upperJans.Count == 0) {
			return;
		}

		var skus = new List<HhtSkuRow>();
		foreach (var chunk in upperJans.Chunk(InClauseChunkSize)) {
			var placeholders = string.Join(",", Enumerable.Range(0, chunk.Length).Select(i => "@" + i));
			var sql = $@"
select d.Id_Shohin as Id_Shohin, d.Code as Code_Shohin, m.Name as Mei_Shohin,
       d.Id_Col as Id_Col, d.Code_Col as Code_Col, d.Mei_Col as Mei_Col,
       d.Id_Siz as Id_Siz, d.Code_Siz as Code_Siz, d.Mei_Siz as Mei_Siz,
       d.Jan1 as Jan1, d.Jan2 as Jan2, d.Jan3 as Jan3,
       m.TankaJodai as TankaJodai, m.TankaGenka as TankaGenka
from {nameof(DerivedShohinColSiz)} d
inner join {nameof(MasterShohin)} m on m.Id = d.Id_Shohin
where d.Jan1 in ({placeholders}) or d.Jan2 in ({placeholders}) or d.Jan3 in ({placeholders})
";
			skus.AddRange(_db.Fetch<HhtSkuRow>(sql, [.. chunk.Cast<object>()]));
		}

		foreach (var sku in skus) {
			// 上段のみの照合: Jan1 / Jan2 / Jan3 のいずれかに一致すればよい
			foreach (var jan in new[] { sku.Jan1, sku.Jan2, sku.Jan3 }) {
				var key = (jan ?? string.Empty).Trim();
				if (key.Length < JanMinLength) {
					continue;
				}
				if (!cache.SkuByAnyJan.TryGetValue(key, out var list)) {
					list = [];
					cache.SkuByAnyJan.Add(key, list);
				}
				if (!list.Contains(sku)) {
					list.Add(sku);
				}
			}
			// 上段+下段の照合: 上段=Jan1 かつ 下段=Jan2 のAND条件
			var upper = (sku.Jan1 ?? string.Empty).Trim();
			var lower = (sku.Jan2 ?? string.Empty).Trim();
			if (upper.Length < JanMinLength || lower.Length == 0) {
				continue;
			}
			var pairKey = upper + "\t" + lower;
			if (!cache.SkuByJanPair.TryGetValue(pairKey, out var pairList)) {
				pairList = [];
				cache.SkuByJanPair.Add(pairKey, pairList);
			}
			if (!pairList.Contains(sku)) {
				pairList.Add(sku);
			}
		}
	}

	/// <summary>店舗売上・返品の顧客CD(DenNo)だけを引く。MasterEndCustomer は155万件あり全件ロードできない</summary>
	private void LoadCustomerCache(List<TranVulcanHht> rows, HhtMasterCache cache) {
		var codes = rows
			.Where(x => x.Type0 is TypeUriage or TypeHenpin)
			.Select(x => (x.DenNo ?? string.Empty).Trim())
			.Where(x => x.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.ToList();
		if (codes.Count == 0) {
			return;
		}
		foreach (var chunk in codes.Chunk(InClauseChunkSize)) {
			var placeholders = string.Join(",", Enumerable.Range(0, chunk.Length).Select(i => "@" + i));
			var found = _db.Fetch<MasterEndCustomer>($"where Code in ({placeholders})", [.. chunk.Cast<object>()]);
			foreach (var customer in found) {
				cache.CustomerByCode.TryAdd(customer.Code, customer);
			}
		}
	}

	/// <summary>
	/// 候補が複数あるときの優先順位。完全一致 → 在庫を持つ(IsZaiko=1) → 直営店(TenType=6) の順で絞る。
	/// 絞りきれない場合は null を返し、呼び出し側が E014 にする。
	/// </summary>
	private static MasterTokui? PickOne(List<MasterTokui> candidates, string rawCode) {
		if (candidates.Count == 1) {
			return candidates[0];
		}
		var exact = candidates.Where(x => string.Equals(x.Code, rawCode, StringComparison.Ordinal)).ToList();
		if (exact.Count == 1) {
			return exact[0];
		}
		var zaiko = candidates.Where(x => x.IsZaiko == 1).ToList();
		if (zaiko.Count == 1) {
			return zaiko[0];
		}
		var tenpo = zaiko.Count > 1 ? zaiko : candidates;
		var direct = tenpo.Where(x => x.TenType == (int)EnumTokui._6_Tenpo).ToList();
		return direct.Count == 1 ? direct[0] : null;
	}

	/// <summary>得意先マスタを解決する。<paramref name="tenTypes"/> で店種区分を絞る</summary>
	private static MasterTokui? ResolveTokui(HhtMasterCache cache, string rawCode, int[] tenTypes, out string? error) {
		error = null;
		var key = NormalizeCode(rawCode);
		if (key.Length == 0) {
			error = "コードが空です";
			return null;
		}
		if (!cache.TokuiByCode.TryGetValue(key, out var all)) {
			return null;
		}
		var candidates = all.Where(x => tenTypes.Contains(x.TenType)).ToList();
		if (candidates.Count == 0) {
			// コードは存在するが店種区分が区分の期待と違う
			error = $"店種区分が不一致 (TenType={string.Join("/", all.Select(x => x.TenType).Distinct())})";
			return null;
		}
		var picked = PickOne(candidates, (rawCode ?? string.Empty).Trim());
		if (picked == null) {
			error = $"複数のマスタに一致 ({candidates.Count}件)";
		}
		return picked;
	}

	/// <summary>HHT変換で使う標準の消費税区分(<see cref="MasterSysTax.Id"/>)</summary>
	private const long StandardTaxId = 1;

	#endregion

	#region 伝票の組み立て

	/// <summary>
	/// 伝票1枚を組み立てる。検証エラーは <paramref name="errors"/> へ積み、1件でもあれば伝票を作らない。
	/// </summary>
	private HhtSlip? BuildSlip(HhtSlipGroup group, HhtMasterCache cache, List<string> errors) {
		var head = group.Rows[0];

		if (group.Type0 is < TypeUriage or > TypeKyakusu) {
			errors.Add($"E001 区分が対象外です (区分={group.Type0})");
			return null;
		}
		if (!IsValidYmd(head.DenDay)) {
			errors.Add($"E002 日付が不正です ({head.DenDay})");
		}
		// 社販は CV10 の EnumUri00/EnumUri01 に対応する区分がないため一旦エラーにする。
		// ToDo: 区分に社販を追加したらここで Kubun へ割り当てる（決定 12-C）
		if (head.HanKubun == HanShahan && group.Type0 is TypeUriage or TypeHenpin or TypeOroshi or TypeOroshiHenpin) {
			errors.Add("E015 販売区分=2(社販)は未対応です");
		}

		var shain = ResolveShain(cache, head.Tanto, errors);
		var meisai = BuildMeisai(group, cache, errors, shain);
		if (errors.Count > 0 || meisai.Count == 0) {
			if (meisai.Count == 0 && errors.Count == 0) {
				errors.Add("E110 有効な明細がありません");
			}
			return null;
		}

		var slip = group.Type0 switch {
			TypeUriage or TypeHenpin => BuildTenuri(group, cache, meisai, shain, errors),
			TypeOroshi or TypeOroshiHenpin => BuildUriage(group, cache, meisai, shain, errors),
			TypeShiire or TypeShiireHenpin => BuildShiire(group, cache, meisai, shain, errors),
			TypeHachu => BuildHachu(group, cache, meisai, shain, errors),
			TypeTanaoroshi => BuildTana(group, cache, meisai, shain, errors),
			TypeNyuko => BuildIdoIn(group, cache, meisai, shain, errors),
			TypeShukko => BuildIdoOut(group, cache, meisai, shain, errors),
			TypeIdo => BuildIdo(group, cache, meisai, shain, errors),
			_ => null,
		};
		return errors.Count > 0 ? null : slip;
	}

	private static MasterShain? ResolveShain(HhtMasterCache cache, string tanto, List<string> errors) {
		var key = NormalizeCode(tanto);
		if (key.Length == 0) {
			return null;
		}
		if (!cache.ShainByCode.TryGetValue(key, out var list)) {
			errors.Add($"E011 担当者が未登録です ({tanto})");
			return null;
		}
		if (list.Count > 1) {
			var exact = list.Where(x => string.Equals(x.Code, tanto.Trim(), StringComparison.Ordinal)).ToList();
			if (exact.Count != 1) {
				errors.Add($"E014 担当者が複数のマスタに一致します ({tanto} {list.Count}件)");
				return null;
			}
			return exact[0];
		}
		return list[0];
	}

	/// <summary>明細を組み立てる。行単位のエラーは "行=n" を含めて積み、その行を特定できるようにする</summary>
	private List<Tran99Meisai> BuildMeisai(HhtSlipGroup group, HhtMasterCache cache, List<string> errors, MasterShain? shain) {
		var meisai = new List<Tran99Meisai>(group.Rows.Count);
		var sign = IsHenpin(group.Type0) ? -1 : 1;
		var kakeRitsu = TryParseKakeRitsu(group.Type0, group.Rows[0].KakeRitsu);
		var no = 0;

		foreach (var row in group.Rows) {
			var upper = (row.Jan1 ?? string.Empty).Trim();
			var lower = (row.Jan2 ?? string.Empty).Trim();
			if (upper.Length == 0) {
				errors.Add($"E100 JANコードが空です (行={row.LineNo})");
				continue;
			}
			if (upper.Length < JanMinLength) {
				errors.Add($"E105 JANコードの桁数が不足しています ({upper} 行={row.LineNo})");
				continue;
			}
			var sku = ResolveSku(cache, upper, lower, row.LineNo, errors);
			if (sku == null) {
				continue;
			}
			if (row.Su == 0) {
				errors.Add($"E110 数量が0です (行={row.LineNo})");
				continue;
			}

			var su = row.Su * sign;
			// 掛率が来る区分は 下代 = 上代 × 掛率。小数を保った掛率で計算し丸め誤差を作らない
			var gedai = kakeRitsu > 0
				? (int)Math.Round(sku.TankaJodai * kakeRitsu / 100m, MidpointRounding.AwayFromZero)
				: sku.TankaGenka;

			no++;
			meisai.Add(new Tran99Meisai {
				No = no,
				Kubun = row.HanKubun == 9 ? HanProper : row.HanKubun,
				Id_Shohin = sku.Id_Shohin,
				Code_Shohin = sku.Code_Shohin,
				Mei_Shohin = sku.Mei_Shohin,
				JanCode = lower.Length > 0 ? upper + "/" + lower : upper,
				Id_Col = sku.Id_Col,
				Code_Col = sku.Code_Col,
				Mei_Col = sku.Mei_Col,
				Id_Siz = sku.Id_Siz,
				Code_Siz = sku.Code_Siz,
				Mei_Siz = sku.Mei_Siz,
				Su = su,
				Tanka = row.Tanka,
				Kingaku = su * row.Tanka,
				Jodai = sku.TankaJodai,
				Gedai = gedai,
				Id_Shain = shain?.Id ?? 0,
				Code_Shain = shain?.Code ?? string.Empty,
				Mei_Shain = shain?.Name ?? string.Empty,
			});
		}
		return meisai;
	}

	/// <summary>
	/// JANから SKU を引く。下段が空なら Jan1/Jan2/Jan3 のいずれか、下段があれば 上段=Jan1 かつ 下段=Jan2（決定 12-H）
	/// </summary>
	private static HhtSkuRow? ResolveSku(HhtMasterCache cache, string upper, string lower, int lineNo, List<string> errors) {
		List<HhtSkuRow>? candidates;
		if (lower.Length > 0) {
			cache.SkuByJanPair.TryGetValue(upper + "\t" + lower, out candidates);
		}
		else {
			cache.SkuByAnyJan.TryGetValue(upper, out candidates);
		}
		if (candidates == null || candidates.Count == 0) {
			var jan = lower.Length > 0 ? $"{upper}/{lower}" : upper;
			errors.Add($"E103 商品が未登録です (JAN={jan} 行={lineNo})");
			return null;
		}
		if (candidates.Count > 1) {
			errors.Add($"E104 JANコードが複数商品に一致します (JAN={upper} {candidates.Count}件 行={lineNo})");
			return null;
		}
		return candidates[0];
	}

	private HhtSlip? BuildTenuri(HhtSlipGroup group, HhtMasterCache cache, List<Tran99Meisai> meisai, MasterShain? shain, List<string> errors) {
		var head = group.Rows[0];
		var tenpo = ResolveSoko(cache, head.Shop, errors, "店舗");
		if (tenpo == null) {
			return null;
		}
		var slip = new Tran01Tenuri {
			DenDay = head.DenDay,
			Id_Soko = tenpo.Id,
			VSoko = ToView(tenpo),
			Id_Tenpo = tenpo.Id,
			VTenpo = ToView(tenpo),
			Kubun = ResolveUriKubun(group.Type0, head.HanKubun),
			Code_Customer = (head.DenNo ?? string.Empty).Trim(),
			Memo = BuildMemo(group),
		};
		// 顧客CDはマスタに無くてもオフライン用の Code_Customer に残す（会計自体は成立しているため）
		if (cache.CustomerByCode.TryGetValue(slip.Code_Customer, out var customer)) {
			slip.Id_Customer = customer.Id;
			slip.VCustomer = new CodeNameView(customer.Id, customer.Code, customer.Name);
		}
		ApplyCommon(slip, meisai, shain);
		// Rate は掛率。店舗売上のHHTデータに掛率は来ないので0のままにする
		slip.Rate = 0;
		// 店舗売上はTaxCalcUnitを持たず常に伝票単位。端数処理は店舗(Id_Tenpo)のMasterTokuiから転記する
		slip.TaxRounding = ResolveTaxRounding(cache, tenpo);
		ApplyTaxOnly(cache, head.DenDay, slip.KingakuTotal, meisai, slip, EnumTaxCalcUnit.Slip, (EnumRounding)slip.TaxRounding);
		return new HhtSlip(nameof(Tran01Tenuri), slip);
	}

	private HhtSlip? BuildUriage(HhtSlipGroup group, HhtMasterCache cache, List<Tran99Meisai> meisai, MasterShain? shain, List<string> errors) {
		var head = group.Rows[0];
		var soko = ResolveSoko(cache, head.Shop, errors, "倉庫");
		var tokui = ResolveTokui(cache, head.ToriSaki, [(int)EnumTokui._1_Oroshi, (int)EnumTokui._3_UriShi], out var tokuiError);
		if (tokui == null) {
			errors.Add(tokuiError == null
				? $"E012 得意先が未登録です ({head.ToriSaki})"
				: $"E013 得意先{tokuiError} ({head.ToriSaki})");
		}
		if (soko == null || tokui == null) {
			return null;
		}
		var slip = new Tran00Uriage {
			DenDay = head.DenDay,
			KakeDay = head.DenDay,
			Id_Soko = soko.Id,
			VSoko = ToView(soko),
			Id_Tokui = tokui.Id,
			VTokui = ToView(tokui),
			// 掛計上する。IsPay=0 の伝票は売掛集計へ入らない(SummaryDb.KakeDenWhere)
			IsPay = (int)EnumYesNo.Yes,
			Kubun = ResolveUriKubun(group.Type0, head.HanKubun),
			ManualNo = (head.DenNo ?? string.Empty).Trim(),
			Memo = BuildMemo(group),
		};
		ApplyCommon(slip, meisai, shain);
		// Rate は掛率(パーセント整数。MasterTokui.RateProper と同単位)。消費税率には使わない
		slip.Rate = ToRatePercent(TryParseKakeRitsu(group.Type0, head.KakeRitsu));
		// 税計算単位・消費税端数処理は得意先マスタの伝票作成時点のスナップショット(Doc/spec/2026-09-01 2.2)
		slip.TaxCalcUnit = tokui.TaxCalcUnit;
		slip.TaxRounding = ResolveTaxRounding(cache, tokui);
		ApplyTaxOnly(cache, head.DenDay, slip.KingakuTotal, meisai, slip, (EnumTaxCalcUnit)slip.TaxCalcUnit, (EnumRounding)slip.TaxRounding);
		return new HhtSlip(nameof(Tran00Uriage), slip);
	}

	private HhtSlip? BuildShiire(HhtSlipGroup group, HhtMasterCache cache, List<Tran99Meisai> meisai, MasterShain? shain, List<string> errors) {
		var head = group.Rows[0];
		var soko = ResolveSoko(cache, head.Shop, errors, "倉庫");
		var shiire = ResolveShiire(cache, head.ToriSaki, errors);
		if (soko == null || shiire == null) {
			return null;
		}
		var slip = new Tran03Shiire {
			DenDay = head.DenDay,
			KakeDay = head.DenDay,
			Id_Soko = soko.Id,
			VSoko = ToView(soko),
			Id_Shiire = shiire.Id,
			VShiire = new CodeNameView(shiire.Id, shiire.Code, shiire.Name),
			IsPay = (int)EnumYesNo.Yes,
			Kubun = group.Type0 == TypeShiire ? (int)EnumShiire.Shiire : (int)EnumShiire.Henpin,
			ManualNo = (head.DenNo ?? string.Empty).Trim(),
			Memo = BuildMemo(group),
		};
		// 仕入の掛率欄には発注番号が入る。発注伝票のIdとして RelateNo1 へ持たせ、発注残の完了判定に使う
		var hachuNo = (head.KakeRitsu ?? string.Empty).Trim();
		if (hachuNo.Length > 0 && hachuNo.TrimStart('0').Length > 0) {
			if (!int.TryParse(hachuNo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var relateNo)) {
				errors.Add($"E120 発注番号が不正です ({hachuNo})");
				return null;
			}
			slip.RelateNo1 = relateNo;
		}
		ApplyCommon(slip, meisai, shain);
		// Rate は掛率。仕入の掛率欄には発注番号が入るため掛率は来ない
		slip.Rate = 0;
		// 税計算単位・消費税端数処理は仕入先マスタの伝票作成時点のスナップショット(Doc/spec/2026-09-01 2.2)
		slip.TaxCalcUnit = shiire.TaxCalcUnit;
		slip.TaxRounding = ResolveTaxRounding(cache, shiire);
		ApplyTaxOnly(cache, head.DenDay, slip.KingakuTotal, meisai, slip, (EnumTaxCalcUnit)slip.TaxCalcUnit, (EnumRounding)slip.TaxRounding);
		return new HhtSlip(nameof(Tran03Shiire), slip);
	}

	private HhtSlip? BuildHachu(HhtSlipGroup group, HhtMasterCache cache, List<Tran99Meisai> meisai, MasterShain? shain, List<string> errors) {
		var head = group.Rows[0];
		var soko = ResolveSoko(cache, head.Shop, errors, "倉庫");
		var shiire = ResolveShiire(cache, head.ToriSaki, errors);
		if (soko == null || shiire == null) {
			return null;
		}
		// 発注の掛率欄には納品日が入る
		var nouhinDay = (head.KakeRitsu ?? string.Empty).Trim();
		if (nouhinDay.Length > 0 && !IsValidYmd(nouhinDay)) {
			errors.Add($"E120 納品日が不正です ({nouhinDay})");
			return null;
		}
		var slip = new Tran13Hachu {
			DenDay = head.DenDay,
			Id_Soko = soko.Id,
			VSoko = ToView(soko),
			Id_Shiire = shiire.Id,
			VShiire = new CodeNameView(shiire.Id, shiire.Code, shiire.Name),
			Kubun = (int)EnumHachu.Hachu,
			NouhinDay = nouhinDay,
			Memo = BuildMemo(group),
		};
		ApplyCommon(slip, meisai, shain);
		// 発注はTaxCalcUnitを持たず常に伝票単位。端数処理は仕入先マスタから転記する
		slip.TaxRounding = ResolveTaxRounding(cache, shiire);
		ApplyTaxOnly(cache, head.DenDay, slip.KingakuTotal, meisai, slip, EnumTaxCalcUnit.Slip, (EnumRounding)slip.TaxRounding);
		return new HhtSlip(nameof(Tran13Hachu), slip);
	}

	private HhtSlip? BuildTana(HhtSlipGroup group, HhtMasterCache cache, List<Tran99Meisai> meisai, MasterShain? shain, List<string> errors) {
		var head = group.Rows[0];
		var soko = ResolveSoko(cache, head.Shop, errors, "倉庫");
		if (soko == null) {
			return null;
		}
		// 棚卸の伝票番号欄には棚番が入る（先頭8桁）
		var tanaNo = (head.DenNo ?? string.Empty).Trim();
		if (tanaNo.Length > 8) {
			tanaNo = tanaNo[..8];
		}
		if (tanaNo.Length > 0 && !tanaNo.All(char.IsAsciiDigit)) {
			errors.Add($"E130 棚番が不正です ({tanaNo})");
			return null;
		}
		var slip = new Tran60Tana {
			DenDay = head.DenDay,
			Id_Soko = soko.Id,
			VSoko = ToView(soko),
			TanaNo = tanaNo,
			Memo = BuildMemo(group),
		};
		ApplyCommon(slip, meisai, shain);
		return new HhtSlip(nameof(Tran60Tana), slip);
	}

	private HhtSlip? BuildIdoIn(HhtSlipGroup group, HhtMasterCache cache, List<Tran99Meisai> meisai, MasterShain? shain, List<string> errors) {
		var head = group.Rows[0];
		// 入庫(積送受)は受け側でスキャンするので Shop=移動先、取引先=移動元。
		// Tran11IdoIn は Id_Soko=出庫元 / Id_Ido=入庫先 の向きで在庫を動かす
		var ido = ResolveSoko(cache, head.Shop, errors, "移動先");
		var soko = ResolveSoko(cache, head.ToriSaki, errors, "移動元");
		if (ido == null || soko == null) {
			return null;
		}
		if (ido.Id == soko.Id) {
			errors.Add($"E020 移動元と移動先が同一です ({head.Shop})");
			return null;
		}
		var slip = new Tran11IdoIn {
			DenDay = head.DenDay,
			Id_Soko = soko.Id,
			VSoko = ToView(soko),
			Id_Ido = ido.Id,
			VIdo = ToView(ido),
			ManualNo = (head.DenNo ?? string.Empty).Trim(),
			Memo = BuildIdoMemo(group),
		};
		ApplyCommon(slip, meisai, shain);
		return new HhtSlip(nameof(Tran11IdoIn), slip);
	}

	private HhtSlip? BuildIdoOut(HhtSlipGroup group, HhtMasterCache cache, List<Tran99Meisai> meisai, MasterShain? shain, List<string> errors) {
		var head = group.Rows[0];
		var soko = ResolveSoko(cache, head.Shop, errors, "倉庫");
		var ido = ResolveSoko(cache, head.ToriSaki, errors, "移動先");
		if (soko == null || ido == null) {
			return null;
		}
		if (ido.Id == soko.Id) {
			errors.Add($"E020 移動元と移動先が同一です ({head.Shop})");
			return null;
		}
		var slip = new Tran10IdoOut {
			DenDay = head.DenDay,
			Id_Soko = soko.Id,
			VSoko = ToView(soko),
			Id_Ido = ido.Id,
			VIdo = ToView(ido),
			ManualNo = (head.DenNo ?? string.Empty).Trim(),
			Memo = BuildIdoMemo(group),
		};
		ApplyCommon(slip, meisai, shain);
		return new HhtSlip(nameof(Tran10IdoOut), slip);
	}

	private HhtSlip? BuildIdo(HhtSlipGroup group, HhtMasterCache cache, List<Tran99Meisai> meisai, MasterShain? shain, List<string> errors) {
		var head = group.Rows[0];
		var soko = ResolveSoko(cache, head.Shop, errors, "倉庫");
		var ido = ResolveSoko(cache, head.ToriSaki, errors, "移動先");
		if (soko == null || ido == null) {
			return null;
		}
		if (ido.Id == soko.Id) {
			errors.Add($"E020 移動元と移動先が同一です ({head.Shop})");
			return null;
		}
		var slip = new Tran05Ido {
			DenDay = head.DenDay,
			Id_Soko = soko.Id,
			VSoko = ToView(soko),
			Id_Ido = ido.Id,
			VIdo = ToView(ido),
			ManualNo = (head.DenNo ?? string.Empty).Trim(),
			Memo = BuildIdoMemo(group),
		};
		ApplyCommon(slip, meisai, shain);
		return new HhtSlip(nameof(Tran05Ido), slip);
	}

	#endregion

	#region 補助

	/// <summary>倉庫・店舗を解決する。倉庫(TenType=0)と直営店(TenType=6)のどちらでもよい</summary>
	private static MasterTokui? ResolveSoko(HhtMasterCache cache, string rawCode, List<string> errors, string label) {
		var tokui = ResolveTokui(cache, rawCode, [(int)EnumTokui._0_Soko, (int)EnumTokui._6_Tenpo], out var error);
		if (tokui == null) {
			errors.Add(error == null
				? $"E010 {label}が未登録です ({rawCode})"
				: error.StartsWith("複数", StringComparison.Ordinal)
					? $"E014 {label}が{error} ({rawCode})"
					: $"E013 {label}の{error} ({rawCode})");
		}
		return tokui;
	}

	private static MasterShiire? ResolveShiire(HhtMasterCache cache, string rawCode, List<string> errors) {
		var key = NormalizeCode(rawCode);
		if (key.Length == 0 || !cache.ShiireByCode.TryGetValue(key, out var list)) {
			errors.Add($"E012 仕入先が未登録です ({rawCode})");
			return null;
		}
		if (list.Count > 1) {
			var exact = list.Where(x => string.Equals(x.Code, rawCode.Trim(), StringComparison.Ordinal)).ToList();
			if (exact.Count != 1) {
				errors.Add($"E014 仕入先が複数のマスタに一致します ({rawCode} {list.Count}件)");
				return null;
			}
			return exact[0];
		}
		return list[0];
	}

	private static CodeNameView ToView(MasterTokui tokui) => new(tokui.Id, tokui.Code, tokui.Name);

	/// <summary>ヘッダの合計値と社員を明細から埋める</summary>
	private static void ApplyCommon(TranAllHeader slip, List<Tran99Meisai> meisai, MasterShain? shain) {
		slip.Jmeisai = meisai;
		slip.SuTotal = meisai.Sum(x => x.Su);
		slip.KingakuTotal = meisai.Sum(x => x.Kingaku);
		slip.JodaiTotal = meisai.Sum(x => x.Jodai * x.Su);
		slip.GedaiTotal = meisai.Sum(x => x.Gedai * x.Su);
		slip.Id_Shain = shain?.Id ?? 0;
		slip.VShain = shain == null ? new CodeNameView() : new CodeNameView(shain.Id, shain.Code, shain.Name);
	}

	/// <summary>
	/// 取引先の税計算単位・消費税端数処理を返す(Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md 3.7)。
	/// 取引先が引けない場合は自社既定の端数処理(<see cref="MasterSysman.TaxRounding"/>)を使う。
	/// <para>
	/// 呼び出し元は各Build*で取引先解決に失敗した時点で既にnullを弾いているため、
	/// このフォールバックは実質防御的なものである。
	/// </para>
	/// </summary>
	private static int ResolveTaxRounding(HhtMasterCache cache, MasterTorihiki? torihiki) =>
		torihiki?.TaxRounding ?? cache.Sysman.TaxRounding;

	/// <summary>
	/// 消費税と総合計を計算する。式は各InputViewModelの UpdateHeaderTotals と同じ
	/// (<see cref="TaxCalculator.Apply"/> で税区分ごとに1回だけ丸める)。
	/// <para>
	/// HHT変換は伝票ヘッダ単位で税率を1本決める（決定 F/G）ため、明細の税区分は標準(<see cref="StandardTaxId"/>)固定とする。
	/// 税率は <c>Rate</c> ではなく <see cref="TaxRateResolver"/> のローカル値を使う（<c>Rate</c> は掛率。決定 12-F / 12-G）。
	/// </para>
	/// </summary>
	private static void ApplyTaxOnly(
			HhtMasterCache cache, string denDay, long kingakuTotal, List<Tran99Meisai> meisai,
			ITranTax slip, EnumTaxCalcUnit calcUnit, EnumRounding rounding) {
		foreach (var m in meisai) {
			m.Id_Tax = StandardTaxId;
		}
		var rateOf = TaxRateResolver.CreateRateResolver(cache.Sysman, denDay);
		var totals = TaxCalculator.Apply(meisai, rateOf, calcUnit, rounding);
		slip.TaxableAmount1 = totals.TaxableAmount1;
		slip.TaxableAmount2 = totals.TaxableAmount2;
		slip.TaxableAmount3 = totals.TaxableAmount3;
		slip.Tax1 = totals.Tax1;
		slip.Tax2 = totals.Tax2;
		slip.Tax3 = totals.Tax3;
		slip.Total = Math.Abs(kingakuTotal) + totals.TaxTotal;
	}

	/// <summary>
	/// 売上・返品の区分。プロパー/セールで 10/11・20/21 に分ける。
	/// <see cref="EnumUri00"/>（本部売上）と <see cref="EnumUri01"/>（店舗売上）は同じ値なので共通で扱う。
	/// </summary>
	private static int ResolveUriKubun(int type0, int hanKubun) {
		var isHenpin = IsHenpin(type0);
		var isSale = hanKubun == HanSale;
		return (isHenpin, isSale) switch {
			(false, false) => (int)EnumUri00.Uriage,
			(false, true) => (int)EnumUri00.UriSale,
			(true, false) => (int)EnumUri00.Henpin,
			(true, true) => (int)EnumUri00.HenSale,
		};
	}

	private static bool IsHenpin(int type0) =>
		type0 is TypeHenpin or TypeShiireHenpin or TypeOroshiHenpin;

	/// <summary>
	/// 掛率が来る区分(入庫/出庫/卸売/卸返品/移動)の掛率を返す。来ない区分は 0。
	/// <para>
	/// VULCANは5桁前0埋めで 999.9 を格納する（小数点なしの整数表現）。10で割って実数に戻す。
	/// </para>
	/// </summary>
	private static decimal TryParseKakeRitsu(int type0, string? raw) {
		if (type0 is not (TypeNyuko or TypeShukko or TypeOroshi or TypeOroshiHenpin or TypeIdo)) {
			return 0;
		}
		var value = (raw ?? string.Empty).Trim();
		if (value.Length == 0 || !value.All(char.IsAsciiDigit)) {
			return 0;
		}
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits) ? digits / 10m : 0;
	}

	/// <summary>掛率をパーセント整数へ丸める。単位は <see cref="MasterTorihiki.RateProper"/> と同じ（等倍）</summary>
	private static int ToRatePercent(decimal kakeRitsu) =>
		(int)Math.Round(kakeRitsu, MidpointRounding.AwayFromZero);

	private static string BuildMemo(HhtSlipGroup group) {
		var head = group.Rows[0];
		return $"HHT {head.BackupFileName} #{head.HhtNo}-{head.Serial}";
	}

	/// <summary>
	/// 移動系のメモ。買取(0)/委託(1)と掛率は移動伝票に対応列がないためメモへ残す（決定 12-D / 12-F）
	/// </summary>
	private static string BuildIdoMemo(HhtSlipGroup group) {
		var head = group.Rows[0];
		var memo = BuildMemo(group);
		if (head.HanKubun == HanSale) {
			memo += " 委託";
		}
		else if (head.HanKubun == HanProper) {
			memo += " 買取";
		}
		var kakeRitsu = TryParseKakeRitsu(group.Type0, head.KakeRitsu);
		if (kakeRitsu > 0) {
			memo += $" 掛率={kakeRitsu:0.#}";
		}
		return memo;
	}

	private static bool IsValidYmd(string? ymd) =>
		!string.IsNullOrWhiteSpace(ymd)
		&& ymd.Length == 8
		&& DateTime.TryParseExact(ymd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

	#endregion

	/// <summary>1回の更新処理で使うマスタのキャッシュ</summary>
	private sealed class HhtMasterCache {
		public List<MasterTokui> Tokui { get; set; } = [];
		public List<MasterShiire> Shiire { get; set; } = [];
		public List<MasterShain> Shain { get; set; } = [];
		public MasterSysman Sysman { get; set; } = new();
		public Dictionary<string, List<MasterTokui>> TokuiByCode { get; set; } = new(StringComparer.Ordinal);
		public Dictionary<string, List<MasterShiire>> ShiireByCode { get; set; } = new(StringComparer.Ordinal);
		public Dictionary<string, List<MasterShain>> ShainByCode { get; set; } = new(StringComparer.Ordinal);
		public Dictionary<string, MasterEndCustomer> CustomerByCode { get; } = new(StringComparer.Ordinal);
		/// <summary>上段のみの照合用。Jan1/Jan2/Jan3 のどれでも引ける</summary>
		public Dictionary<string, List<HhtSkuRow>> SkuByAnyJan { get; } = new(StringComparer.Ordinal);
		/// <summary>上段+下段の照合用。キーは "Jan1\tJan2"</summary>
		public Dictionary<string, List<HhtSkuRow>> SkuByJanPair { get; } = new(StringComparer.Ordinal);
	}

	/// <summary>JAN照合で引く商品(SKU)の1行。NPocoのマッピング対象なので可変プロパティで持つ</summary>
	private sealed class HhtSkuRow {
		public long Id_Shohin { get; set; }
		public string Code_Shohin { get; set; } = string.Empty;
		public string Mei_Shohin { get; set; } = string.Empty;
		public long Id_Col { get; set; }
		public string Code_Col { get; set; } = string.Empty;
		public string Mei_Col { get; set; } = string.Empty;
		public long Id_Siz { get; set; }
		public string Code_Siz { get; set; } = string.Empty;
		public string Mei_Siz { get; set; } = string.Empty;
		public string Jan1 { get; set; } = string.Empty;
		public string Jan2 { get; set; } = string.Empty;
		public string Jan3 { get; set; } = string.Empty;
		public int TankaJodai { get; set; }
		public int TankaGenka { get; set; }
	}
}
