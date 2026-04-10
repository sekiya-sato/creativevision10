namespace CvBase.Share;

/// <summary>
/// サーババージョン情報
/// </summary>
public sealed class InfoServer {
	public string Product { get; set; } = "CvServer";
	/// <summary>
	/// バージョン文字列
	/// </summary>
	public string Version { get; set; } = "0.0.0";
	/// <summary>
	/// ビルド日
	/// </summary>
	public DateTime BuildDate { get; set; } = DateTime.MinValue;
	/// <summary>
	/// ビルドコンフィグ
	/// </summary>
	public string BuildConfig { get; set; } = string.Empty;
	/// <summary>
	/// サーバー起動時間
	/// </summary>
	public DateTime StartTime { get; set; } = DateTime.MinValue;
	/// <summary>
	/// サーバURL (例 https://localhost:8888/)
	/// </summary>
	public string Url { get; set; } = string.Empty;
	/// <summary>
	/// ベースフォルダ
	/// </summary>
	public string BaseDir { get; set; } = string.Empty;
	/// <summary>
	/// コンピュータ名
	/// </summary>
	public string MachineName { get; set; } = string.Empty;
	/// <summary>
	/// ユーザ名
	/// </summary>
	public string UserName { get; set; } = string.Empty;
	/// <summary>
	/// OSバージョン
	/// </summary>
	public string OsVersion { get; set; } = string.Empty;
	/// <summary>
	/// .NETバージョン
	/// </summary>
	public string DotNetVersion { get; set; } = string.Empty;

}
