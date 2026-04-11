namespace CvWpfclient.Models;

public sealed class JapanPostBizOptions {
	public string BaseUrl { get; set; } = "https://api.da.pf.japanpost.jp";
	public string TokenPath { get; set; } = "/api/v2/j/token";
	public string SearchCodePath { get; set; } = "/api/v2/searchcode";
	public string ClientId { get; set; } = string.Empty;
	public string SecretKey { get; set; } = string.Empty;
	public string EcUid { get; set; } = string.Empty;
	public string UserAgent { get; set; } = "CvWpfclient/1.0";
	public int TimeoutSeconds { get; set; } = 10;
	public int DefaultLimit { get; set; } = 1000;
	public int DefaultChoikiType { get; set; } = 1;
	public int DefaultSearchType { get; set; } = 2;
	public int TokenRefreshMarginSeconds { get; set; } = 60;
}
