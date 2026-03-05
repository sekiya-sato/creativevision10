namespace Cvnet10Base.Share;

/// <summary>
/// バージョン情報
/// </summary>
public sealed class VersionInfo {
	public string Product { get; set; } = string.Empty;
	/// <summary>
	/// バージョン文字列
	/// </summary>
	public string Version { get; set; } = string.Empty;
	/// <summary>
	/// ビルド日
	/// </summary>
	public DateTime BuildDate { get; set; } = DateTime.MinValue;
	/// <summary>
	/// サーバー起動時間
	/// </summary>
	public DateTime StartTime { get; set; } = DateTime.MinValue;
	/// <summary>
	/// ベースフォルダ
	/// </summary>
	public string BaseDir { get; set; } = string.Empty;
}
