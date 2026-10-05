namespace CvBase;

public readonly record struct AutoReplenishSku(long Id_Shohin, long Id_Col, long Id_Siz);
public sealed record AutoReplenishStoreInput(long Id_Tenpo, string TenpoCode, int Priority, long TargetSu,
	long RealSu, long ReserveSu, long ArrivalSu, long TransitSu);
public sealed record AutoReplenishQuantity(long Id_Tenpo, int DemandSu, int TransferSu, int CoveredSu, int Su);

/// <summary>店舗需要と倉庫不足へ、実在庫・予定供給を一度ずつ割り当てる。</summary>
public static class AutoReplenishCalculator {
	public static IReadOnlyList<AutoReplenishQuantity> Calculate(long realSu, long reserveSu, long pendingSu,
		long transitSu, IEnumerable<AutoReplenishStoreInput> stores) {
		checked {
			var available = realSu - reserveSu;
			var transfer = Math.Max(0, available);
			var planned = Math.Max(0, pendingSu) + Math.Max(0, transitSu);
			var result = new List<AutoReplenishQuantity>();
			Add(0, Math.Max(0, -available), false);
			foreach (var store in stores.OrderBy(s => s.Priority).ThenBy(s => s.TenpoCode, StringComparer.Ordinal).ThenBy(s => s.Id_Tenpo)) {
				Add(store.Id_Tenpo, Math.Max(0, store.TargetSu - (store.RealSu - store.ReserveSu + store.ArrivalSu + Math.Max(0, store.TransitSu))), true);
			}
			return result;

			void Add(long store, long demand, bool canTransfer) {
				if (demand == 0) return;
				var allocated = canTransfer ? Math.Min(demand, transfer) : 0;
				transfer -= allocated;
				var covered = Math.Min(demand - allocated, planned);
				planned -= covered;
				result.Add(new(store, checked((int)demand), checked((int)allocated), checked((int)covered), checked((int)(demand - allocated - covered))));
			}
		}
	}
}
