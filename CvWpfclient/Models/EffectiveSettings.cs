using Microsoft.Extensions.Configuration;

namespace CvWpfclient.Models;

public sealed class EffectiveSettings(IConfiguration configuration) {
	public string WeatherRegion => configuration["Application:WeatherRegion"] ?? "Tokyo";

	public string OpenWeatherApiKey => FirstNonEmpty(
		global::CvWpfclient.AppGlobal.InfoApiKey.Application.OpenWeatherApiKey,
		configuration["Application:OpenWeatherApiKey"]);

	public JapanPostBizOptions GetJapanPostBizOptions() {
		return new JapanPostBizOptions {
			BaseUrl = configuration.GetSection("JapanPostBiz")["BaseUrl"] ?? "https://api.da.pf.japanpost.jp",
			TokenPath = configuration.GetSection("JapanPostBiz")["TokenPath"] ?? "/api/v2/j/token",
			SearchCodePath = configuration.GetSection("JapanPostBiz")["SearchCodePath"] ?? "/api/v2/searchcode",
			ClientId = FirstNonEmpty(global::CvWpfclient.AppGlobal.InfoApiKey.JapanPostBiz.ClientId, configuration.GetSection("JapanPostBiz")["ClientId"]),
			SecretKey = FirstNonEmpty(global::CvWpfclient.AppGlobal.InfoApiKey.JapanPostBiz.SecretKey, configuration.GetSection("JapanPostBiz")["SecretKey"]),
			EcUid = configuration.GetSection("JapanPostBiz")["EcUid"] ?? string.Empty,
			UserAgent = configuration.GetSection("JapanPostBiz")["UserAgent"] ?? "CvWpfclient/1.0",
			TimeoutSeconds = configuration.GetValue<int?>("JapanPostBiz:TimeoutSeconds") ?? 10,
			DefaultLimit = configuration.GetValue<int?>("JapanPostBiz:DefaultLimit") ?? 1000,
			DefaultChoikiType = configuration.GetValue<int?>("JapanPostBiz:DefaultChoikiType") ?? 1,
			DefaultSearchType = configuration.GetValue<int?>("JapanPostBiz:DefaultSearchType") ?? 2,
			TokenRefreshMarginSeconds = configuration.GetValue<int?>("JapanPostBiz:TokenRefreshMarginSeconds") ?? 60,
		};
	}

	private static string FirstNonEmpty(string? preferred, string? fallback) {
		return !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback ?? string.Empty;
	}
}
