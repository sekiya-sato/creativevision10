namespace CvWpfclient.Models;

public sealed class ClientSettingsDocument {
	public ClientConnectionString ConnectionStrings { get; set; } = new();
	public ClientApplication Application { get; set; } = new();
}

public sealed class ClientConnectionString {
	public string Url { get; set; } = "https://localhost:5012";
}
public sealed class ClientApplication {
	public string WeatherRegion { get; set; } = string.Empty;
	public string JmaWeatherAreaCode { get; set; } = string.Empty;
	public string FitPosition { get; set; } = string.Empty;
	public string Theme { get; set; } = string.Empty;
	public string MainTheme { get; set; } = string.Empty;

	public int Limit { get; set; } = 0;
	public string LoginId { get; set; } = string.Empty;
	/// <summary>
	/// Product: リリース時には暗号化するか、保存しないようにする
	/// </summary>
	public string LoginPass { get; set; } = string.Empty;
	/// <summary>
	/// Product: リリース時には暗号化するか、保存しないようにする
	/// </summary>
	public string LoginJwt { get; set; } = string.Empty;
}
