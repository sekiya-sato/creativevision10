using CvBase;
using CvWpfclient.Helpers;
using System.Linq;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 在庫強制調整入力 — 倉庫のSKUを一覧に並べ、行ごとに調整数（符号付き）を入力して
/// 在庫調整伝票(<see cref="Tran61Chosei"/>)を1伝票登録する。区分は強制調整(<see cref="EnumChosei.Kyosei"/>)。
/// <para>
/// <b>在庫へ即時反映される。</b><see cref="Tran61Chosei"/> は <see cref="ITranSoko"/> 実装なので、
/// サーバの汎用CRUD副作用(<c>WriteEffectRunner</c>)が登録と同一トランザクションで
/// <see cref="SummaryRealStock"/> / <see cref="SummaryStock"/> を更新する（プラスで増、マイナスで減）。
/// 調整は集計テーブルへ直接書かず伝票として残すため、全件Rebuildでも消えない（棚卸確定処理と同じ設計）。
/// </para>
/// <para>
/// 取消（既存調整伝票の削除）・在庫強制調整実績照会（帳票）・調整理由マスタ(<see cref="Tran61Chosei.Id_Riyu"/>)は
/// follow-up 課題。理由は当面メモで残す。仕様は `Doc/spec/2026-08-18_F2_在庫強制調整入力_詳細設計.md` を参照する。
/// </para>
/// </summary>
public partial class StockForceInputViewModel : Helpers.BaseStockSheetInputViewModel<Tran61Chosei> {
	protected override string QueryTitle => "在庫強制調整入力";
	protected override string InputSuHeader => "調整数";

	protected override async Task<List<StockSheetRow>> LoadRowsAsync(CancellationToken ct) {
		// 棚卸入力(一覧)と同じ組み立て。対象倉庫の在庫SKU（+品番範囲指定時は在庫0のSKUも）を並べる
		var stock = await LoadStockAsync(ct);
		var stockMap = stock
			.GroupBy(s => (s.Id_Shohin, s.Id_Col, s.Id_Siz))
			.ToDictionary(g => g.Key, g => g.Sum(x => x.Su));

		// 品番範囲を指定したときだけ、在庫0のSKUも並べる（在庫レコードが無いSKUを増やす調整用）
		var hasShohinRange = !string.IsNullOrWhiteSpace(ShohinCodeFrom) || !string.IsNullOrWhiteSpace(ShohinCodeTo);
		var skuList = hasShohinRange ? await LoadSkuAsync(ct) : [];

		var keys = new HashSet<(long Shohin, long Col, long Siz)>(stockMap.Keys);
		foreach (var sku in skuList) keys.Add((sku.Id_Shohin, sku.Id_Col, sku.Id_Siz));

		var shohinMap = await LoadShohinMapAsync(keys.Select(k => k.Shohin), ct);
		var skuMap = await LoadSkuMapAsync(keys.Select(k => k.Shohin), ct);

		List<StockSheetRow> rows = [];
		foreach (var key in keys) {
			shohinMap.TryGetValue(key.Shohin, out var shohin);
			skuMap.TryGetValue(key, out var sku);
			rows.Add(CreateRow(
				key.Shohin, key.Col, key.Siz,
				stockMap.TryGetValue(key, out var su) ? su : 0,
				shohin, sku));
		}
		return rows;
	}

	protected override Tran61Chosei BuildDenpyo(List<Tran99Meisai> meisai) => new() {
		EnKubun = EnumChosei.Kyosei,
	};
}
