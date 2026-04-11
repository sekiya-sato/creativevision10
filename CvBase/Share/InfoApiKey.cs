namespace CvBase.Share;

/// <summary>
/// クライアントアプリケーションで使用するAPIキーや設定情報のルートオブジェクト
/// </summary>
public sealed class InfoApiKey {
	public ApplicationSettings Application { get; set; } = new();

	public JapanPostBizSettings JapanPostBiz { get; set; } = new();
}


/// <summary>
/// アプリケーション基本設定
/// </summary>
public sealed class ApplicationSettings {
	public string OpenWeatherApiKey { get; set; } = string.Empty;

}

/// <summary>
/// 日本郵便Biz API設定
/// </summary>
public sealed class JapanPostBizSettings {
	public string ClientId { get; set; } = string.Empty;

	public string SecretKey { get; set; } = string.Empty;
}

