using CvBase;
using Microsoft.Extensions.Logging;
using System.Text;

namespace CvDomainLogic;

/// <summary>
/// 伝票税額再更新の実行結果。
/// </summary>
/// <param name="TableName">対象テーブル名</param>
/// <param name="Scanned">走査した伝票数</param>
/// <param name="Updated">明細税額を投入した伝票数</param>
/// <param name="HeaderTaxChanged">ヘッダ Tax が変化した伝票数</param>
/// <param name="HeaderTaxDiff">ヘッダ Tax の差額合計（新 - 旧）</param>
public sealed record TranTaxRebuildResult(
	string TableName, int Scanned, int Updated, int HeaderTaxChanged, long HeaderTaxDiff);

/// <summary>
/// 明細別消費税へ移行するための一時的な管理者処理。
/// <para>
/// 明細 <c>Tax</c> の合計が0の伝票（＝まだ明細別税額が入っていない伝票）だけを対象に、
/// 明細の <c>Id_Tax</c> / <c>TaxRate</c> / <c>Tax</c> を投入し、ヘッダ <c>Tax</c> / <c>Total</c> を再計算する。
/// 既に税額が入った伝票は対象外なので、何度実行しても結果は変わらない（冪等）。
/// </para>
/// <para>
/// 仕様は `Doc/spec/2026-08-25_明細別消費税計算_詳細設計.md` の 4.5 を参照する。
/// 恒常運用では使わず、移行・既存データの救済にのみ使う。
/// </para>
/// </summary>
public class TranTaxRebuildDb {
	readonly ExDatabase _db;
	readonly ILogger<TranTaxRebuildDb> _logger;

	public TranTaxRebuildDb(ExDatabase db) {
		_db = db;
		_logger = new NLogExtender<TranTaxRebuildDb>();
	}

	/// <summary>商品マスタが引けない明細に使う既定の消費税区分</summary>
	const long StandardTaxId = 1;

	/// <summary>
	/// 一度に読み込む伝票数。店舗売上は実運用で300万件規模になるため全件を持たない
	/// </summary>
	const int ChunkSize = 5000;

	/// <summary>
	/// 対象5伝票をすべて再更新する。呼び出し側でトランザクションを張ること。
	/// </summary>
	public List<TranTaxRebuildResult> RebuildAll() {
		var sysman = _db.Fetch<MasterSysman>("where Id = 1").FirstOrDefault() ?? new MasterSysman();
		var taxIdByShohin = LoadShohinTaxIds();
		_logger.LogInformation("伝票税額再更新 開始 商品マスタ {Count} 件の税区分を読込", taxIdByShohin.Count);
		return [
			Rebuild<Tran00Uriage>(sysman, taxIdByShohin),
			Rebuild<Tran01Tenuri>(sysman, taxIdByShohin),
			Rebuild<Tran03Shiire>(sysman, taxIdByShohin),
			Rebuild<Tran12Jyuchu>(sysman, taxIdByShohin),
			Rebuild<Tran13Hachu>(sysman, taxIdByShohin),
		];
	}

	/// <summary>
	/// 商品Id → 消費税区分の対応を一括で読む。
	/// 明細1行ずつ引くと伝票数×明細数ぶんの往復になるため先にまとめて読む。
	/// </summary>
	public Dictionary<long, long> LoadShohinTaxIds() =>
		_db.Dictionary<long, long>($"SELECT Id, Id_Tax FROM {nameof(MasterShohin)}");

	/// <summary>
	/// 明細1伝票ぶんの消費税区分・適用税率・税額を設定し、明細税額の合計を返す。
	/// <para>
	/// 商品ごとに税区分が異なれば明細ごとに税率が変わる（軽減税率の混在）。
	/// 税額は常に正値で、返品等の符号はヘッダ <c>Kubun</c> の CalcFlag が集計側で担う。
	/// </para>
	/// </summary>
	/// <param name="meisai">対象の明細（内容を書き換える）</param>
	/// <param name="sysman">税率定義を持つシステム設定</param>
	/// <param name="taxIdByShohin">商品Id → 消費税区分。引けない商品は標準税率(1)にする</param>
	/// <param name="denDay">伝票日付(yyyyMMdd)。税率の切替判定に使う</param>
	public static int ApplyMeisaiTax(
		List<Tran99Meisai> meisai, MasterSysman sysman, Dictionary<long, long> taxIdByShohin, string denDay) {

		foreach (var m in meisai) {
			var taxId = m.Id_Shohin > 0 && taxIdByShohin.TryGetValue(m.Id_Shohin, out var found)
				? found
				: StandardTaxId;
			var rate = TaxRateResolver.ResolveTaxRatePercent(sysman, taxId, denDay);
			m.Id_Tax = taxId;
			m.TaxRate = rate;
			m.Tax = (int)TranCalcBase.RoundTax(m.Kingaku, rate, CvBase.Share.EnumRounding.Round);
		}
		return meisai.Sum(m => m.Tax);
	}

	/// <summary>1テーブルぶんの再更新。Idの昇順にチャンクで読み進める。</summary>
	TranTaxRebuildResult Rebuild<TDen>(MasterSysman sysman, Dictionary<long, long> taxIdByShohin)
		where TDen : TranAllHeader, ITranTax, new() {

		var tableName = typeof(TDen).Name;
		int scanned = 0, updated = 0, headerTaxChanged = 0;
		long headerTaxDiff = 0;
		long lastId = 0;

		while (true) {
			// Id順に読み進めることで、更新済みの伝票を読み直さずに全件を1回だけ走査する
			var slips = _db.Fetch<TDen>(
				$"WHERE Id > @0 ORDER BY Id LIMIT {ChunkSize}", lastId);
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
				// 既に明細別税額が入っている伝票は触らない（再実行しても結果が変わらないようにする）
				if (meisai.Sum(m => m.Tax) != 0) {
					continue;
				}

				var newTax = ApplyMeisaiTax(meisai, sysman, taxIdByShohin, slip.DenDay);
				if (newTax != slip.Tax) {
					headerTaxChanged++;
					headerTaxDiff += newTax - slip.Tax;
				}
				slip.Jmeisai = meisai;
				slip.Tax = newTax;
				slip.Total = (int)(Math.Abs(slip.KingakuTotal) + newTax);
				_db.Update(slip);
				updated++;
			}
		}

		_logger.LogInformation(
			"伝票税額再更新 {Table} 走査:{Scanned} 更新:{Updated} ヘッダTax変化:{Changed} 差額:{Diff}",
			tableName, scanned, updated, headerTaxChanged, headerTaxDiff);
		return new TranTaxRebuildResult(tableName, scanned, updated, headerTaxChanged, headerTaxDiff);
	}

	/// <summary>実行結果を利用者向けのテキストへ整形する。</summary>
	public static string BuildSummary(DateTime startTime, List<TranTaxRebuildResult> results) {
		var endTime = DateTime.Now;
		var sb = new StringBuilder();
		sb.AppendLine($"開始 {startTime:yyyy/MM/dd HH:mm:ss}  終了 {endTime:yyyy/MM/dd HH:mm:ss}  所要 {(endTime - startTime).TotalSeconds:N1}秒");
		sb.AppendLine();
		foreach (var r in results) {
			sb.AppendLine($"{r.TableName,-14} 走査 {r.Scanned,9:N0} 件 / 更新 {r.Updated,9:N0} 件"
				+ $" / ヘッダTax変化 {r.HeaderTaxChanged,7:N0} 件 差額 {r.HeaderTaxDiff,12:N0} 円");
		}
		sb.AppendLine();
		sb.AppendLine($"更新合計 {results.Sum(r => r.Updated):N0} 件"
			+ $"　ヘッダTax変化 {results.Sum(r => r.HeaderTaxChanged):N0} 件"
			+ $"　差額合計 {results.Sum(r => r.HeaderTaxDiff):N0} 円");
		if (results.Any(r => r.HeaderTaxChanged > 0)) {
			sb.AppendLine();
			sb.AppendLine("※ ヘッダTaxが変化した伝票があります。請求計算・支払計算をやり直すか判断してください。");
		}
		return sb.ToString();
	}
}
