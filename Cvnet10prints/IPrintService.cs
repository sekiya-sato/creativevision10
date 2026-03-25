namespace Cvnet10Prints;

public interface IPrintService {
	Task<PrintResult> ExecutePrintAsync(PrintContext context);
}


public class PrintContext {
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

public class PrintResult {
	public bool IsSuccess { get; }
	public string Message { get; set; } = string.Empty;
	public PrintResult(bool isSuccess, string message) {
		IsSuccess = isSuccess;
		Message = message;
	}
}

public class PrintProduct {
	public string Product { get; set; } = string.Empty;
	public bool Status { get; set; } = false;
}

