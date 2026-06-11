namespace CvPrints;

public interface IPrintService {
	Task<PrintResult> ExecutePrintAsync(PrintContext context);
}


public sealed record PrintContext {
	public string BasePath { get; set; } = string.Empty;
	public string FormPath { get; set; } = string.Empty;
	public string DataPath { get; set; } = string.Empty;
	/// <summary>
	/// スプール出力フォルダ
	/// </summary>
	public string OutputDir { get; set; } = string.Empty;
	/// <summary>
	/// スプールファイル
	/// </summary>
	public string OutputFileName { get; set; } = string.Empty;
}

public sealed record PrintResult {
	public bool IsSuccess { get; set; }
	public string Message { get; set; } = string.Empty;
}

public sealed record PrintProduct {
	public string Product { get; set; } = string.Empty;
	public bool Status { get; set; } = false;
}

