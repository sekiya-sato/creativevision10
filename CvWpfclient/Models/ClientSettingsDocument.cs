namespace CvWpfclient.Models;

public sealed class ClientSettingsDocument {
	public ClientConnectionString ConnectionStrings { get; set; } = new();
	public ClientParameters Parameters { get; set; } = new();
	public ClientApplication Application { get; set; } = new();
}

public sealed class ClientConnectionString {
	public string Url { get; set; } = "https://localhost:5012";
}

public sealed class ClientParameters {
	public string LoginId { get; set; } = string.Empty;
	/// <summary>
	/// ToDo: リリース時には暗号化するか、保存しないようにする
	/// </summary>
	public string LoginPass { get; set; } = string.Empty;
	/// <summary>
	/// ToDo: リリース時には暗号化するか、保存しないようにする
	/// </summary>
	public string LoginJwt { get; set; } = string.Empty;

}
public sealed class ClientApplication {
	public string WeatherRegion { get; set; } = string.Empty;
	public string FitPosition { get; set; } = string.Empty;
	public string Theme { get; set; } = string.Empty;
	public string MainTheme { get; set; } = string.Empty;
}
