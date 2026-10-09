using Newtonsoft.Json;

namespace CvBase;

/// <summary>旧CV確定ポイント履歴の元値。TranPointEvent.Jcalcに保存する。</summary>
public sealed class LegacyPointHistory {
	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public string SourceSystem { get; set; } = string.Empty;

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public long SeqNo { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public decimal CreatedAt { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public decimal UpdatedAt { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public string CustomerCode { get; set; } = string.Empty;

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public string Rank { get; set; } = string.Empty;

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public string Day { get; set; } = string.Empty;

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public string ShopCode { get; set; } = string.Empty;

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public long RegisterNo { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public long ReceiptNo { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public string EmployeeCode { get; set; } = string.Empty;

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public int TransactionType { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public int OriginType { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public long GrantPoints { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public long UsePoints { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public long ExpirePoints { get; set; }

	[JsonProperty(DefaultValueHandling = DefaultValueHandling.Include)]
	public string Memo { get; set; } = string.Empty;
}
