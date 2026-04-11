namespace CvBase.Share;

/// <summary>
/// クライアントアプリケーションで使用するAPIキーや設定情報のルートオブジェクト
/// </summary>
public sealed class InfoApiKey {

	public string? DecriptKey { get; set; }
	public ApplicationSettings Application { get; set; } = new();

	public JapanPostBizSettings JapanPostBiz { get; set; } = new();

	public void Decrypt(Func<string, string, string> Action) {
		if (string.IsNullOrWhiteSpace(DecriptKey))
			return;
		// DecriptKeyを使って暗号化されたAPIキーを復号化する処理を実装する
		if (!string.IsNullOrWhiteSpace(Application.OpenWeatherApiKey))
			Application.OpenWeatherApiKey = Action(Application.OpenWeatherApiKey, DecriptKey);
		if (!string.IsNullOrWhiteSpace(JapanPostBiz.ClientId))
			JapanPostBiz.ClientId = Action(JapanPostBiz.ClientId, DecriptKey);
		if (!string.IsNullOrWhiteSpace(JapanPostBiz.SecretKey))
			JapanPostBiz.SecretKey = Action(JapanPostBiz.SecretKey, DecriptKey);
		DecriptKey = string.Empty; // 復号化後はキーをクリアしてメモリ上に残さないようにする
	}
	public void Encrypt(string key, Func<string, string, string> Action) {
		if (string.IsNullOrWhiteSpace(key))
			return;
		if (!string.IsNullOrWhiteSpace(DecriptKey))
			return;
		DecriptKey = key;
		// DecriptKeyを使ってAPIキーを暗号化する処理を実装する
		if (!string.IsNullOrWhiteSpace(Application.OpenWeatherApiKey))
			Application.OpenWeatherApiKey = Action(Application.OpenWeatherApiKey, DecriptKey);
		if (!string.IsNullOrWhiteSpace(JapanPostBiz.ClientId))
			JapanPostBiz.ClientId = Action(JapanPostBiz.ClientId, DecriptKey);
		if (!string.IsNullOrWhiteSpace(JapanPostBiz.SecretKey))
			JapanPostBiz.SecretKey = Action(JapanPostBiz.SecretKey, DecriptKey);
	}
}
/// <summary>
/// アプリケーション基本設定
/// </summary>
public sealed class ApplicationSettings {
	public string? OpenWeatherApiKey { get; set; }

}

/// <summary>
/// 日本郵便Biz API設定
/// </summary>
public sealed class JapanPostBizSettings {
	public string? ClientId { get; set; }

	public string? SecretKey { get; set; }
}

