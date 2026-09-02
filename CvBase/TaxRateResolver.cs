using CvBase.Share;
using System.Globalization;

namespace CvBase;

/// <summary>
/// 伝票日付時点の消費税率を解決する。
/// <para>
/// クライアント側の <c>AppGlobal.LogicGetTax</c> と同じ判定をサーバ側で行うための共通処理。
/// 仕様は `Doc/spec/archive/2026-08-25_明細別消費税計算_詳細設計.md` の 4.4 を参照する。
/// 元は <c>CvDomainLogic</c> にあったが、帳票VM（<c>CvWpfclient</c>、<c>CvDomainLogic</c> を参照しない）からも
/// 同じ判定を1箇所から使えるよう <c>CvBase</c> へ移した
/// （`Doc/spec/2026-09-01_消費税計算単位・端数処理_全体設計.md` D-05 のレビュー指摘）。
/// </para>
/// </summary>
public static class TaxRateResolver {

	/// <summary>
	/// 消費税区分の<b>定義自体が引けなかった</b>場合の既定値(%)。
	/// <para>
	/// 使ってよいのは「<see cref="MasterSysman.Jsub"/> に該当 <see cref="MasterSysTax.Id"/> の行が無い」
	/// という<b>マスタ未整備</b>のケースだけ。定義があって税率が 0% の枠（非課税・未使用枠）は
	/// <b>0% のまま扱う</b>こと。0%をここへ読み替えると、未使用枠の売上に課税してしまう。
	/// </para>
	/// </summary>
	public const int DefaultTaxRatePercent = 10;

	/// <summary>
	/// 伝票日付時点の消費税率(%)を返す。<c>Rate</c> は掛率に使うのでここでは触らない。
	/// <para>
	/// 定義された税率が 0% ならそのまま 0 を返す（非課税・未使用枠）。
	/// <see cref="DefaultTaxRatePercent"/> へ倒すのは税区分の定義そのものが引けないときだけ。
	/// </para>
	/// </summary>
	/// <param name="sysman">システム設定（<see cref="MasterSysman.Jsub"/> に税率定義を持つ）</param>
	/// <param name="taxId">消費税区分（<see cref="MasterSysTax.Id"/> 1-3）。0以下は非課税で 0 を返す</param>
	/// <param name="denDay">伝票日付(yyyyMMdd)</param>
	public static int ResolveTaxRatePercent(MasterSysman? sysman, long taxId, string? denDay) {
		// 非課税。MasterSysTax を引かずにここで確定させる
		// (Id=0 の定義は存在せず、既定値の DateFrom が空のまま CompareYmd に渡ると例外になる)
		if (taxId <= 0) {
			return 0;
		}
		var systax = sysman?.Jsub?.FirstOrDefault(x => x.Id == taxId);
		// 定義が無い＝マスタ未整備。0を返すと課税漏れになるため既定税率へ倒す
		return systax == null ? DefaultTaxRatePercent : ResolveRateOf(systax, denDay);
	}

	/// <summary>
	/// <paramref name="systax"/>1件ぶんの、適用日時点の税率(%)を返す。読み替えは一切しない。
	/// <c>DateFrom</c> による新税率への切替判定はここに1箇所だけ持つ。
	/// </summary>
	static int ResolveRateOf(MasterSysTax systax, string? denDay) {
		// DateFrom が未設定なら新税率への切替日が無いということなので現行税率を使う。
		// CvAsset.Common.CompareYmd は 8桁以外で例外を投げるため、渡す前に桁数を確認する
		return IsValidYmd(denDay) && IsValidYmd(systax.DateFrom)
			&& CvAsset.Common.CompareYmd(denDay!, systax.DateFrom) >= 0
			? systax.TaxNewRate
			: systax.TaxRate;
	}

	/// <summary>
	/// 明細の税額を計算する。常に正値を返し、返品等の符号はヘッダ Kubun の CalcFlag が決める。
	/// 端数処理は四捨五入(<see cref="EnumRounding.Round"/>)固定。取引先の端数処理を使う場合は
	/// <see cref="CalcMeisaiTax(int, int, EnumRounding)"/> を使うこと。
	/// </summary>
	/// <param name="kingaku">明細金額</param>
	/// <param name="taxRatePercent">適用消費税率(%)</param>
	public static int CalcMeisaiTax(int kingaku, int taxRatePercent) =>
		CalcMeisaiTax(kingaku, taxRatePercent, EnumRounding.Round);

	/// <summary>
	/// 明細の税額を計算する(端数処理指定版)。常に正値を返し、返品等の符号はヘッダ Kubun の CalcFlag が決める。
	/// </summary>
	/// <param name="kingaku">明細金額</param>
	/// <param name="taxRatePercent">適用消費税率(%)</param>
	/// <param name="rounding">端数処理</param>
	public static int CalcMeisaiTax(int kingaku, int taxRatePercent, EnumRounding rounding) =>
		(int)TranCalcBase.RoundTax(Math.Abs(kingaku), taxRatePercent, rounding);

	/// <summary>
	/// <see cref="MasterSysman"/> と伝票日付から、消費税区分→適用税率(%) の変換関数を作る。
	/// <see cref="TaxCalculator.Apply"/> の rateOf にそのまま渡せる。
	/// </summary>
	/// <param name="sysman">システム設定（税率定義を持つ）</param>
	/// <param name="denDay">伝票日付(yyyyMMdd)。税率の切替判定に使う</param>
	public static Func<long, int> CreateRateResolver(MasterSysman? sysman, string? denDay) =>
		taxId => ResolveTaxRatePercent(sysman, taxId, denDay);

	/// <summary>8桁yyyyMMddとして妥当か</summary>
	public static bool IsValidYmd(string? ymd) =>
		!string.IsNullOrWhiteSpace(ymd)
		&& ymd.Length == 8
		&& DateTime.TryParseExact(ymd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

	/// <summary>
	/// 同一適用日において同一税率になっている <see cref="MasterSysTax"/> の組（Doc/spec
	/// `2026-09-01_消費税計算単位・端数処理_全体設計.md` 3.6）。
	/// </summary>
	/// <param name="IdTaxA">消費税区分Id（小さい方）</param>
	/// <param name="IdTaxB">消費税区分Id（大きい方）</param>
	/// <param name="RatePercent">重複している税率(%)</param>
	/// <param name="EffectiveDates">
	/// 重複が発生する適用日(yyyyMMdd)の一覧。同じ組が複数区間で重複しても1件にまとめ、ここへ列挙する。
	/// nullは「現行（最初の切替前）」を表す
	/// </param>
	public sealed record TaxRateDuplicate(long IdTaxA, long IdTaxB, int RatePercent, IReadOnlyList<string?> EffectiveDates);

	/// <summary>
	/// <paramref name="taxes"/>（<see cref="MasterSysman.Jsub"/>）の中から、同一適用日において
	/// 同一税率になる <c>Id_Tax</c> の組を探す。<c>MasterSysKanriMenteViewModel</c>（システム管理マスタ保守画面）の
	/// 保存時バリデーション（3.6の決定「同一適用日において同一税率となるId_Taxを複数定義してはならない」）
	/// から使う純粋な判定ロジック。
	/// <para>
	/// 適用日ごとの税率は <see cref="ResolveTaxRatePercent"/> と同じ規則（<c>DateFrom</c>が未設定・不正なら
	/// 現行税率を使う）で解決する。税率が変わりうる日（各 <c>Id_Tax</c> の有効な<c>DateFrom</c>）と、
	/// どの切替も起きていない状態（現行税率のみ）の両方を確認日として突合するため、
	/// 期間内のどの時点でも重複を見逃さない。
	/// </para>
	/// <para>
	/// 税率0（非課税相当・未使用枠）どうしの重複は対象外にする。未使用の枠が複数あるのは正常な運用であり、
	/// 制度が求める「税率ごとに1回の端数処理」に反しないため。
	/// </para>
	/// </summary>
	public static List<TaxRateDuplicate> FindDuplicateTaxRates(IReadOnlyList<MasterSysTax> taxes) {
		if (taxes.Count < 2) {
			return [];
		}

		// null: どの Id_Tax もまだ新税率へ切り替わっていない状態（現行税率のみ）を確認する。
		// それ以外は、いずれかの Id_Tax の新税率が効き始める日を確認日として使う。
		string?[] checkDates = [null, .. taxes.Select(t => t.DateFrom).Where(IsValidYmd).Distinct()];

		// (IdA, IdB, Rate) の組ごとに、重複が起きた確認日を集める。同じ組が複数区間で重複しても1件にまとめるため
		var periodsByPair = new Dictionary<(long IdA, long IdB, int Rate), List<string?>>();
		foreach (var checkDate in checkDates) {
			var rates = taxes.Select(t => (t.Id, Rate: ResolveRateOf(t, checkDate))).ToList();
			for (var i = 0; i < rates.Count; i++) {
				for (var j = i + 1; j < rates.Count; j++) {
					if (rates[i].Rate <= 0 || rates[i].Rate != rates[j].Rate) {
						continue;
					}
					var key = (Math.Min(rates[i].Id, rates[j].Id), Math.Max(rates[i].Id, rates[j].Id), rates[i].Rate);
					if (!periodsByPair.TryGetValue(key, out var periods)) {
						periodsByPair[key] = periods = [];
					}
					periods.Add(checkDate);
				}
			}
		}

		return [.. periodsByPair
			.Select(kv => new TaxRateDuplicate(kv.Key.IdA, kv.Key.IdB, kv.Key.Rate, kv.Value))
			.OrderBy(d => d.IdTaxA).ThenBy(d => d.IdTaxB)];
	}

	/// <summary>
	/// <see cref="FindDuplicateTaxRates"/> の結果を保守画面の確認ダイアログ向けの日本語メッセージへ整形する。
	/// 重複が無ければ null。
	/// </summary>
	public static string? BuildDuplicateTaxRateWarning(IReadOnlyList<MasterSysTax> taxes) {
		var duplicates = FindDuplicateTaxRates(taxes);
		if (duplicates.Count == 0) {
			return null;
		}

		var lines = duplicates.Select(d => {
			var periods = string.Join("、", d.EffectiveDates.Select(FormatEffectivePeriodLabel));
			return $"消費税区分{d.IdTaxA}と消費税区分{d.IdTaxB}が{periods}に同じ税率({d.RatePercent}%)になっています";
		});

		return "税率が重複している組み合わせがあります。" + Environment.NewLine
			+ string.Join(Environment.NewLine, lines) + Environment.NewLine
			+ "このまま保存しますか？";
	}

	/// <summary>重複警告の適用日1件を表示用ラベルへ整形する。nullは「現行」。</summary>
	static string FormatEffectivePeriodLabel(string? effectiveDate) {
		if (effectiveDate == null) {
			return "現行";
		}
		return IsValidYmd(effectiveDate) && DateTime.TryParseExact(
			effectiveDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
			? date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) + "以降"
			: effectiveDate;
	}
}
