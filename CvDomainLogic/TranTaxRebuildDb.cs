using CvBase;
using CvBase.Share;
using Microsoft.Extensions.Logging;
using System.Text;

namespace CvDomainLogic;

/// <summary>
/// 伝票税額再更新の実行結果。
/// </summary>
/// <param name="TableName">対象テーブル名</param>
/// <param name="Scanned">走査した伝票数（期首日以降・明細ありの対象範囲）</param>
/// <param name="Updated">ヘッダ・明細を再計算して更新した伝票数</param>
/// <param name="HeaderTaxChanged">ヘッダ税額合計(Tax1+Tax2+Tax3)が旧値から変化した伝票数</param>
/// <param name="HeaderTaxDiff">ヘッダ税額合計の差額合計（新 - 旧）</param>
/// <param name="TaxableAmountFilled">課税対象額合計(TaxableAmount1+2+3)が0から非0へ新たに埋まった伝票数</param>
public sealed record TranTaxRebuildResult(
	string TableName, int Scanned, int Updated, int HeaderTaxChanged, long HeaderTaxDiff, int TaxableAmountFilled);

/// <summary>
/// 既存伝票を新しい消費税計算方式（Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md）へ揃える
/// 一括再計算処理。恒常運用では使わず、移行直後の一時的な管理者処理として使う。
/// <para>
/// 対象6伝票（<see cref="Tran00Uriage"/> / <see cref="Tran01Tenuri"/> / <see cref="Tran02Material"/> /
/// <see cref="Tran03Shiire"/> / <see cref="Tran12Jyuchu"/> / <see cref="Tran13Hachu"/>）の全件を走査し、
/// </para>
/// <list type="bullet">
/// <item>取引先マスタ（得意先・仕入先・店舗）の現在値から <c>TaxCalcUnit</c>/<c>TaxRounding</c> をヘッダへ再スナップショットする</item>
/// <item>明細の <c>Id_Tax</c> を商品/資材マスタの現在値から解決する</item>
/// <item><see cref="TaxCalculator.Apply"/> で <c>TaxableAmount1/2/3</c>・<c>Tax1/2/3</c>・明細 <c>Tax</c>・<c>Total</c> を確定する</item>
/// </list>
/// <para>
/// 計算はすべて現在のマスタ値と明細の生値（数量・単価・金額）から一意に決まるため、
/// 同じマスタ状態で複数回実行しても結果は変わらない（冪等）。
/// 期首日(<see cref="MasterSysman.FiscalStartDate"/>)より前の伝票は、期首残高として凍結し
/// 再計算しないという既存方針（<c>SummaryDb.GetFiscalStartDate</c> と同じ考え方）に合わせて対象外とする。
/// </para>
/// </summary>
public class TranTaxRebuildDb {
	readonly ExDatabase _db;
	readonly ILogger<TranTaxRebuildDb> _logger;

	public TranTaxRebuildDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<TranTaxRebuildDb>();
	}

	/// <summary>
	/// 一度に読み込む伝票数。店舗売上は実運用で300万件規模になるため全件を持たない
	/// </summary>
	const int ChunkSize = 5000;

	/// <summary>
	/// 対象6伝票をすべて再更新する。呼び出し側でトランザクションを張ること。
	/// </summary>
	public List<TranTaxRebuildResult> RebuildAll() {
		var sysman = _db.Fetch<MasterSysman>("where Id = 1").FirstOrDefault() ?? new MasterSysman();
		var fiscalStart = GetFiscalStartDate();
		var shohinTaxMap = LoadShohinTaxIds();
		var materialTaxMap = LoadMaterialTaxIds();
		var tokuiMap = LoadTorihikiTaxSettings<MasterTokui>();
		var shiireMap = LoadTorihikiTaxSettings<MasterShiire>();
		_logger.LogInformation(
			"伝票税額再更新 開始 商品 {ShohinCount}件 資材 {MaterialCount}件 の税区分を読込 期首日 {FiscalStart} 以降が対象",
			shohinTaxMap.Count, materialTaxMap.Count, fiscalStart);

		return [
			RebuildGeneric<Tran00Uriage>(sysman, fiscalStart, shohinTaxMap,
				slip => ResolveTorihikiTax(tokuiMap, slip.Id_Tokui, sysman),
				(slip, rounding) => slip.TaxRounding = (int)rounding,
				(slip, calcUnit) => slip.TaxCalcUnit = (int)calcUnit),
			RebuildGeneric<Tran01Tenuri>(sysman, fiscalStart, shohinTaxMap,
				// 店舗売上はTaxCalcUnitを持たず常に伝票単位。端数処理は店舗(Id_Tenpo)のMasterTokuiから引く(3.7)
				slip => (EnumTaxCalcUnit.Slip, ResolveTorihikiTax(tokuiMap, slip.Id_Tenpo, sysman).Rounding),
				(slip, rounding) => slip.TaxRounding = (int)rounding),
			RebuildMaterial(sysman, fiscalStart, materialTaxMap, shiireMap),
			RebuildGeneric<Tran03Shiire>(sysman, fiscalStart, shohinTaxMap,
				slip => ResolveTorihikiTax(shiireMap, slip.Id_Shiire, sysman),
				(slip, rounding) => slip.TaxRounding = (int)rounding,
				(slip, calcUnit) => slip.TaxCalcUnit = (int)calcUnit),
			RebuildGeneric<Tran12Jyuchu>(sysman, fiscalStart, shohinTaxMap,
				// 受注はTaxCalcUnitを持たず常に伝票単位。端数処理は得意先のMasterTokuiから引く(3.7)
				slip => (EnumTaxCalcUnit.Slip, ResolveTorihikiTax(tokuiMap, slip.Id_Tokui, sysman).Rounding),
				(slip, rounding) => slip.TaxRounding = (int)rounding),
			RebuildGeneric<Tran13Hachu>(sysman, fiscalStart, shohinTaxMap,
				// 発注はTaxCalcUnitを持たず常に伝票単位。端数処理は仕入先のMasterShiireから引く(3.7)
				slip => (EnumTaxCalcUnit.Slip, ResolveTorihikiTax(shiireMap, slip.Id_Shiire, sysman).Rounding),
				(slip, rounding) => slip.TaxRounding = (int)rounding),
		];
	}

	/// <summary>
	/// 期首年月日(yyyyMMdd)を <see cref="MasterSysman"/> から取得する。未設定・未作成時は "19010101"。
	/// <c>SummaryDb.GetFiscalStartDate</c> と同じ考え方（期首残高として凍結し再計算しない範囲の境界）。
	/// </summary>
	string GetFiscalStartDate() {
		var tableExists = _db.FirstOrDefault<string>(
			"SELECT name FROM sqlite_master WHERE type='table' AND name='MasterSysman'");
		if (string.IsNullOrEmpty(tableExists)) {
			return "19010101";
		}
		var value = _db.FirstOrDefault<string>("SELECT FiscalStartDate FROM MasterSysman ORDER BY Id LIMIT 1");
		return string.IsNullOrWhiteSpace(value) ? "19010101" : value;
	}

	/// <summary>
	/// 商品Id → 消費税区分の対応を一括で読む。
	/// 明細1行ずつ引くと伝票数×明細数ぶんの往復になるため先にまとめて読む。
	/// </summary>
	public Dictionary<long, long> LoadShohinTaxIds() =>
		_db.Dictionary<long, long>($"SELECT Id, Id_Tax FROM {nameof(MasterShohin)}");

	/// <summary>生地・付属Id → 消費税区分の対応を一括で読む(<see cref="LoadShohinTaxIds"/>と同じ理由)</summary>
	public Dictionary<long, long> LoadMaterialTaxIds() =>
		_db.Dictionary<long, long>($"SELECT Id, Id_Tax FROM {nameof(MasterMaterial)}");

	/// <summary>
	/// 取引先Id → (税計算単位, 消費税端数処理) の対応を一括で辞書化する。
	/// 取引先ごとにマスタを引くと伝票件数ぶんの往復になるため、対象テーブルの走査前に1回だけ読む。
	/// </summary>
	Dictionary<long, (int TaxCalcUnit, int TaxRounding)> LoadTorihikiTaxSettings<T>() where T : MasterTorihiki, new() =>
		_db.Fetch<T>().ToDictionary(t => t.Id, t => (t.TaxCalcUnit, t.TaxRounding));

	/// <summary>
	/// 取引先Idから税計算単位・消費税端数処理を解決する(3.7)。取引先が引けない場合(削除済み等)は
	/// 自社既定の消費税端数処理(<see cref="MasterSysman.TaxRounding"/>)を使い、税計算単位は安全側の伝票単位とする。
	/// </summary>
	static (EnumTaxCalcUnit CalcUnit, EnumRounding Rounding) ResolveTorihikiTax(
		Dictionary<long, (int TaxCalcUnit, int TaxRounding)> map, long torihikiId, MasterSysman sysman) {
		if (map.TryGetValue(torihikiId, out var found)) {
			return ((EnumTaxCalcUnit)found.TaxCalcUnit, (EnumRounding)found.TaxRounding);
		}
		return (EnumTaxCalcUnit.Slip, (EnumRounding)sysman.TaxRounding);
	}

	/// <summary>
	/// <see cref="Tran99Meisai"/> を明細に持つ5伝票（Uriage/Tenuri/Shiire/Jyuchu/Hachu）共通の再更新処理。
	/// 伝票ごとに<paramref name="resolveTax"/>で税計算単位・端数処理を解決し、
	/// <see cref="TaxCalculator.Apply"/>でヘッダのTaxableAmount1/2/3・Tax1/2/3・Totalを確定する。
	/// </summary>
	/// <param name="setRounding">解決した端数処理をヘッダへ書き戻す</param>
	/// <param name="setCalcUnit">
	/// 解決した税計算単位をヘッダへ書き戻す。<see cref="Tran01Tenuri"/>/<see cref="Tran12Jyuchu"/>/
	/// <see cref="Tran13Hachu"/>はTaxCalcUnit列を持たず常に伝票単位のため null を渡す
	/// </param>
	TranTaxRebuildResult RebuildGeneric<TDen>(
		MasterSysman sysman, string fiscalStart, Dictionary<long, long> shohinTaxMap,
		Func<TDen, (EnumTaxCalcUnit CalcUnit, EnumRounding Rounding)> resolveTax,
		Action<TDen, EnumRounding> setRounding,
		Action<TDen, EnumTaxCalcUnit>? setCalcUnit = null)
		where TDen : TranAllHeader, ITranTax, new() {

		var tableName = typeof(TDen).Name;
		int scanned = 0, updated = 0, headerTaxChanged = 0, taxableAmountFilled = 0;
		long headerTaxDiff = 0;
		long lastId = 0;

		while (true) {
			// Id順に読み進めることで、対象範囲(期首日以降)を1回だけ走査する
			var slips = _db.Fetch<TDen>(
				$"WHERE Id > @0 AND DenDay >= @1 ORDER BY Id LIMIT {ChunkSize}", lastId, fiscalStart);
			if (slips.Count == 0) {
				break;
			}
			lastId = slips[^1].Id;
			scanned += slips.Count;

			foreach (var slip in slips) {
				var meisai = slip.Jmeisai;
				if (meisai == null || meisai.Count == 0) {
					continue;
				}

				var oldTax = slip.Tax1 + slip.Tax2 + slip.Tax3;
				var oldTaxable = slip.TaxableAmount1 + slip.TaxableAmount2 + slip.TaxableAmount3;

				var (calcUnit, rounding) = resolveTax(slip);
				setRounding(slip, rounding);
				setCalcUnit?.Invoke(slip, calcUnit);

				foreach (var m in meisai) {
					m.Id_Tax = m.Id_Shohin > 0 && shohinTaxMap.TryGetValue(m.Id_Shohin, out var found)
						? found : TaxCalculator.StandardTaxId;
				}
				var rateOf = TaxRateResolver.CreateRateResolver(sysman, slip.DenDay);
				var totals = TaxCalculator.Apply(meisai, rateOf, calcUnit, rounding);

				slip.TaxableAmount1 = totals.TaxableAmount1;
				slip.TaxableAmount2 = totals.TaxableAmount2;
				slip.TaxableAmount3 = totals.TaxableAmount3;
				slip.Tax1 = totals.Tax1;
				slip.Tax2 = totals.Tax2;
				slip.Tax3 = totals.Tax3;
				slip.Total = Math.Abs(slip.KingakuTotal) + totals.TaxTotal;
				slip.Jmeisai = meisai;

				var newTax = totals.TaxTotal;
				var newTaxable = totals.TaxableAmount1 + totals.TaxableAmount2 + totals.TaxableAmount3;
				if (newTax != oldTax) {
					headerTaxChanged++;
					headerTaxDiff += newTax - oldTax;
				}
				if (oldTaxable == 0 && newTaxable != 0) {
					taxableAmountFilled++;
				}

				_db.Update(slip);
				updated++;
			}
		}

		_logger.LogInformation(
			"伝票税額再更新 {Table} 走査:{Scanned} 更新:{Updated} ヘッダTax変化:{Changed} 差額:{Diff} 課税対象額新規:{Filled}",
			tableName, scanned, updated, headerTaxChanged, headerTaxDiff, taxableAmountFilled);
		return new TranTaxRebuildResult(tableName, scanned, updated, headerTaxChanged, headerTaxDiff, taxableAmountFilled);
	}

	/// <summary>
	/// <see cref="Tran02Material"/>専用の再更新処理。明細クラスが<see cref="Tran99MaterialMeisai"/>で異なる点、
	/// および<see cref="TranAllHeader"/>を継承しない点で<see cref="RebuildGeneric{TDen}"/>を使えないため分離する。
	/// <para>
	/// 区分99（その他/消費税調整）は明細に課税対象金額が無く、実額は<c>KingakuTotal</c>自体に
	/// 移行時（<see cref="ConvertDb.CnvTran02Material"/>）から入っている（Doc/spec 3.8 A-6）。
	/// この行は<see cref="TaxCalculator.Apply"/>の結果が自然に0になるため、KingakuTotalには触れず
	/// Total(=|KingakuTotal|+Tax計)だけ再計算すれば整合する。
	/// </para>
	/// </summary>
	TranTaxRebuildResult RebuildMaterial(
		MasterSysman sysman, string fiscalStart,
		Dictionary<long, long> materialTaxMap, Dictionary<long, (int TaxCalcUnit, int TaxRounding)> shiireMap) {

		var tableName = nameof(Tran02Material);
		int scanned = 0, updated = 0, headerTaxChanged = 0, taxableAmountFilled = 0;
		long headerTaxDiff = 0;
		long lastId = 0;

		while (true) {
			var slips = _db.Fetch<Tran02Material>(
				$"WHERE Id > @0 AND DenDay >= @1 ORDER BY Id LIMIT {ChunkSize}", lastId, fiscalStart);
			if (slips.Count == 0) {
				break;
			}
			lastId = slips[^1].Id;
			scanned += slips.Count;

			foreach (var slip in slips) {
				var meisai = slip.Jmeisai;
				if (meisai == null || meisai.Count == 0) {
					continue;
				}

				var oldTax = slip.Tax1 + slip.Tax2 + slip.Tax3;
				var oldTaxable = slip.TaxableAmount1 + slip.TaxableAmount2 + slip.TaxableAmount3;

				var (calcUnit, rounding) = ResolveTorihikiTax(shiireMap, slip.Id_Shiire, sysman);
				slip.TaxCalcUnit = (int)calcUnit;
				slip.TaxRounding = (int)rounding;

				foreach (var m in meisai) {
					m.Id_Tax = m.Id_Material > 0 && materialTaxMap.TryGetValue(m.Id_Material, out var found)
						? found : TaxCalculator.StandardTaxId;
				}
				var rateOf = TaxRateResolver.CreateRateResolver(sysman, slip.DenDay);
				var totals = TaxCalculator.Apply(meisai, rateOf, calcUnit, rounding);

				slip.TaxableAmount1 = totals.TaxableAmount1;
				slip.TaxableAmount2 = totals.TaxableAmount2;
				slip.TaxableAmount3 = totals.TaxableAmount3;
				slip.Tax1 = totals.Tax1;
				slip.Tax2 = totals.Tax2;
				slip.Tax3 = totals.Tax3;
				slip.Total = Math.Abs(slip.KingakuTotal) + totals.TaxTotal;
				slip.Jmeisai = meisai;

				var newTax = totals.TaxTotal;
				var newTaxable = totals.TaxableAmount1 + totals.TaxableAmount2 + totals.TaxableAmount3;
				if (newTax != oldTax) {
					headerTaxChanged++;
					headerTaxDiff += newTax - oldTax;
				}
				if (oldTaxable == 0 && newTaxable != 0) {
					taxableAmountFilled++;
				}

				_db.Update(slip);
				updated++;
			}
		}

		_logger.LogInformation(
			"伝票税額再更新 {Table} 走査:{Scanned} 更新:{Updated} ヘッダTax変化:{Changed} 差額:{Diff} 課税対象額新規:{Filled}",
			tableName, scanned, updated, headerTaxChanged, headerTaxDiff, taxableAmountFilled);
		return new TranTaxRebuildResult(tableName, scanned, updated, headerTaxChanged, headerTaxDiff, taxableAmountFilled);
	}

	/// <summary>
	/// [レガシー互換] 明細1伝票ぶんの消費税区分・適用税率・税額を伝票単位(<see cref="EnumTaxCalcUnit.Slip"/>、
	/// 四捨五入固定)で計算する<see cref="TaxCalculator.Apply"/>のラッパー。
	/// <para>
	/// 新しい<see cref="RebuildAll"/>本体はヘッダごとに税計算単位・端数処理をマスタから解決して
	/// <see cref="TaxCalculator.Apply"/>を直接呼ぶため、このメソッド自体はもう使わない。
	/// 固定シグネチャ（伝票単位・四捨五入固定）を前提にした既存の単体テスト向けに後方互換として残す。
	/// </para>
	/// </summary>
	/// <param name="meisai">対象の明細（内容を書き換える）</param>
	/// <param name="sysman">税率定義を持つシステム設定</param>
	/// <param name="taxIdByShohin">商品Id → 消費税区分。引けない商品は標準税率(1)にする</param>
	/// <param name="denDay">伝票日付(yyyyMMdd)。税率の切替判定に使う</param>
	/// <returns>ヘッダ Tax1/Tax2/Tax3 へそのまま代入できる、消費税区分ごとの税額合計</returns>
	public static (long Tax1, long Tax2, long Tax3) ApplyMeisaiTax(
		List<Tran99Meisai> meisai, MasterSysman sysman, Dictionary<long, long> taxIdByShohin, string denDay) {

		foreach (var m in meisai) {
			m.Id_Tax = m.Id_Shohin > 0 && taxIdByShohin.TryGetValue(m.Id_Shohin, out var found)
				? found : TaxCalculator.StandardTaxId;
		}
		var rateOf = TaxRateResolver.CreateRateResolver(sysman, denDay);
		var totals = TaxCalculator.Apply(meisai, rateOf, EnumTaxCalcUnit.Slip, EnumRounding.Round);
		return (totals.Tax1, totals.Tax2, totals.Tax3);
	}

	/// <summary>実行結果を利用者向けのテキストへ整形する。</summary>
	public static string BuildSummary(DateTime startTime, List<TranTaxRebuildResult> results) {
		var endTime = DateTime.Now;
		var sb = new StringBuilder();
		sb.AppendLine($"開始 {startTime:yyyy/MM/dd HH:mm:ss}  終了 {endTime:yyyy/MM/dd HH:mm:ss}  所要 {(endTime - startTime).TotalSeconds:N1}秒");
		sb.AppendLine();
		foreach (var r in results) {
			sb.AppendLine($"{r.TableName,-14} 走査 {r.Scanned,9:N0} 件 / 更新 {r.Updated,9:N0} 件"
				+ $" / ヘッダTax変化 {r.HeaderTaxChanged,7:N0} 件 差額 {r.HeaderTaxDiff,12:N0} 円"
				+ $" / 課税対象額新規 {r.TaxableAmountFilled,7:N0} 件");
		}
		sb.AppendLine();
		sb.AppendLine($"更新合計 {results.Sum(r => r.Updated):N0} 件"
			+ $"　ヘッダTax変化 {results.Sum(r => r.HeaderTaxChanged):N0} 件"
			+ $"　差額合計 {results.Sum(r => r.HeaderTaxDiff):N0} 円"
			+ $"　課税対象額新規 {results.Sum(r => r.TaxableAmountFilled):N0} 件");
		if (results.Any(r => r.HeaderTaxChanged > 0)) {
			sb.AppendLine();
			sb.AppendLine("※ ヘッダTaxが変化した伝票があります。請求計算・支払計算をやり直すか判断してください。");
		}
		return sb.ToString();
	}
}
