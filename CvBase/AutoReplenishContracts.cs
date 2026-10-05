namespace CvBase;

public enum AutoReplenishOperation { Preview, Save, Load, Commit, Cancel, History, LoadSettings, SaveStockSetting, SaveExcludeSetting }
public enum AutoReplenishStatus { Draft, Confirmed, Cancelled }

public sealed record AutoReplenishParam(long Id_Soko, string DenDay, AutoReplenishOperation Operation,
	string ExecutionKey = "", string Fingerprint = "", long Id_Batch = 0, long ExpectedVdu = 0, long Id_Shain = 0) {
	public MasterAutoReplenishStock? StockSetting { get; init; }
	public MasterAutoReplenishExclude? ExcludeSetting { get; init; }
}

public sealed class AutoReplenishResult {
	public TranAutoReplenishBatch? Batch { get; set; }
	public List<AutoReplenishRow> Rows { get; set; } = [];
	public List<TranAutoReplenishBatch> Batches { get; set; } = [];
	public List<MasterAutoReplenishStock> StockSettings { get; set; } = [];
	public List<MasterAutoReplenishExclude> ExcludeSettings { get; set; } = [];
	public string Fingerprint { get; set; } = string.Empty;
	public List<string> Errors { get; set; } = [];
	public List<long> CreatedHachuIds { get; set; } = [];
	public List<long> CreatedHaibunIds { get; set; } = [];
}

public sealed class AutoReplenishRow {
	public TranHoju Row { get; set; } = new();
	public string ShopCode { get; set; } = string.Empty;
	public string ShopName { get; set; } = string.Empty;
	public string ProductCode { get; set; } = string.Empty;
	public string ProductName { get; set; } = string.Empty;
	public string ColorCode { get; set; } = string.Empty;
	public string SizeCode { get; set; } = string.Empty;
	public string ShiireName { get; set; } = string.Empty;
	public long RealSu { get; set; }
	public long ReserveQty { get; set; }
	public long TargetSu { get; set; }
	public long IncomingSu { get; set; }
	public long TransitSu { get; set; }
	public string Reason { get; set; } = string.Empty;
}
