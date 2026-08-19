using CommunityToolkit.Mvvm.ComponentModel;
using CvBase;
using CvWpfclient.Helpers;
using System.Collections.ObjectModel;
using System.Linq;

namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 在庫強制調整入力 — 倉庫のSKUを一覧に並べ、行ごとに調整数（絶対値）を入力して
/// 在庫調整伝票(<see cref="Tran61Chosei"/>)を1伝票登録する。区分は強制調整(<see cref="EnumChosei.Kyosei"/>)。
/// <para>
/// 増減の方向は<b>調整理由</b>(<see cref="Tran61Chosei.Id_Riyu"/> = <see cref="MasterMeisho"/> の <c>CHR</c> 区分)で決まる。
/// 入力は0以上の絶対値で受け取り、理由の <see cref="ChoseiRiyu.CalcFlag(string)"/>（10〜19=＋/20〜29=−）を掛けて伝票へ積む。
/// 理由は1伝票に1つ（ヘッダ単位）。1回の登録は同じ方向のSKUだけを対象にする。
/// </para>
/// <para>
/// <b>在庫へ即時反映される。</b><see cref="Tran61Chosei"/> は <see cref="ITranSoko"/> 実装なので、
/// サーバの汎用CRUD副作用(<c>WriteEffectRunner</c>)が登録と同一トランザクションで
/// <see cref="SummaryRealStock"/> / <see cref="SummaryStock"/> を更新する（プラスで増、マイナスで減）。
/// 調整は集計テーブルへ直接書かず伝票として残すため、全件Rebuildでも消えない（棚卸確定処理と同じ設計）。
/// </para>
/// <para>仕様は `Doc/spec/2026-08-18_F2_在庫強制調整入力_詳細設計.md` を参照する。</para>
/// </summary>
public partial class StockForceInputViewModel : Helpers.BaseStockSheetInputViewModel<Tran61Chosei> {
	protected override string QueryTitle => "在庫強制調整入力";
	protected override string InputSuHeader => "調整数";

	/// <summary>調整理由の選択肢（<see cref="MasterMeisho"/> の <c>CHR</c> 区分）。初回検索時に読み込む。</summary>
	[ObservableProperty]
	public partial ObservableCollection<MasterMeisho> Reasons { get; set; } = [];

	/// <summary>選択中の調整理由。増減方向はこのコードの <see cref="ChoseiRiyu.CalcFlag(string)"/> で決まる。</summary>
	[ObservableProperty]
	public partial MasterMeisho? SelectedReason { get; set; }

	bool _reasonsLoaded;

	/// <summary>登録符号は選択理由から決める（10〜19=＋1 / 20〜29=−1）。未選択時は登録前検証で弾く。</summary>
	protected override int RegisterSign => SelectedReason is null ? 1 : ChoseiRiyu.CalcFlag(SelectedReason.Code);

	async Task EnsureReasonsAsync(CancellationToken ct) {
		if (_reasonsLoaded) return;
		var sql = $@"
SELECT Id, Vdc, Vdu, Kubun, Code, Name, Odr
FROM {nameof(MasterMeisho)}
WHERE Kubun = '{ChoseiRiyu.Kubun}'
ORDER BY Odr, Code";
		var list = await QuerySqlListAsync<MasterMeisho>(sql, [], ct);
		Reasons = [.. list];
		_reasonsLoaded = true;
	}

	protected override Task<bool> ValidateBeforeRegisterAsync(CancellationToken ct) {
		if (SelectedReason is null) {
			MessageEx.ShowWarningDialog("調整理由を選択してください。", owner: ActiveWindow);
			return Task.FromResult(false);
		}
		if (Rows.Any(r => r.InputSu < 0)) {
			MessageEx.ShowWarningDialog("調整数は0以上で入力してください（増減は調整理由で決まります）。", owner: ActiveWindow);
			return Task.FromResult(false);
		}
		return Task.FromResult(true);
	}

	protected override async Task<List<StockSheetRow>> LoadRowsAsync(CancellationToken ct) {
		await EnsureReasonsAsync(ct);
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
		Id_Riyu = SelectedReason?.Id ?? 0,
	};
}
