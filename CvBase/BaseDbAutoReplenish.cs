using NPoco;
using CvBase.Share;

namespace CvBase;

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("unq1", true, nameof(Id_Tenpo), nameof(Id_Shohin), nameof(Id_Col), nameof(Id_Siz))]
[KeyDml("nk1", false, nameof(Id_Soko))]
[Comment("マスタ：自動補充の店舗基準在庫")]
public sealed class MasterAutoReplenishStock : BaseDbClass {
	public long Id_Tenpo { get; set; }
	public long Id_Soko { get; set; }
	public long Id_Shohin { get; set; }
	public long Id_Col { get; set; }
	public long Id_Siz { get; set; }
	public int TargetSu { get; set; }
	public int Priority { get; set; }
	public int Enabled { get; set; }
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("unq1", true, nameof(Id_Soko), nameof(Id_Shohin), nameof(Id_Col), nameof(Id_Siz))]
[Comment("マスタ：自動補充の倉庫SKU除外")]
public sealed class MasterAutoReplenishExclude : BaseDbClass {
	public long Id_Soko { get; set; }
	public long Id_Shohin { get; set; }
	public long Id_Col { get; set; }
	public long Id_Siz { get; set; }
	public int Excluded { get; set; }
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("unq1", true, nameof(ExecutionKey))]
[KeyDml("unq2", true, nameof(ActiveKey))]
[KeyDml("nk1", false, nameof(Id_Soko))]
[Comment("トランザクション：自動補充実行単位")]
public sealed class TranAutoReplenishBatch : BaseDbClass {
	public long Id_Soko { get; set; }
	[ColumnSizeDml(8)] public string DenDay { get; set; } = string.Empty;
	[ColumnSizeDml(36)] public string ExecutionKey { get; set; } = string.Empty;
	[ColumnSizeDml(64)] public string ActiveKey { get; set; } = string.Empty;
	public int Status { get; set; }
	[ColumnSizeDml(64)] public string Fingerprint { get; set; } = string.Empty;
	public long Id_Shain { get; set; }
	[ColumnSizeDml(8)] public string KakuteiDay { get; set; } = string.Empty;
	// 計算内訳・表示値の保存時点スナップショット。業務数量はTranHojuが正本。
	[ColumnSizeDml(ColumnType.Json)] public string PreviewJson { get; set; } = "{}";
}

[KeyDml("nk2", false, nameof(Id_Batch))]
public sealed partial class TranHoju {
	public long Id_Batch { get; set; }
	public long Id_Tenpo { get; set; }
	public int DemandSu { get; set; }
	public int TransferSu { get; set; }
	public int CoveredSu { get; set; }
	public long GeneratedHachuId { get; set; }
	public long GeneratedHaibunId { get; set; }
}
