namespace CvBase;

/// <summary>キャンペーン対象（店舗・商品）の保存要求。Msg064_PointCampaignTargetSave で使用する。</summary>
public sealed class PointCampaignTargetParameter {
	/// <summary>対象キャンペーンId</summary>
	public long Id_PointCampaign { get; set; }
	/// <summary>画面が表示しているキャンペーンのVdu。不一致は競合として拒否する</summary>
	public long Vdu { get; set; }
	/// <summary>対象店舗Id（店別・商品店別）</summary>
	public List<long> Ids_Tenpo { get; set; } = [];
	/// <summary>対象商品Id（商品全店・商品店別）</summary>
	public List<long> Ids_Shohin { get; set; } = [];
	/// <summary>true=重複確認のみでDBを変更しない</summary>
	public bool IsPreview { get; set; }
	/// <summary>確認で利用者に示した重複。更新時の再計算結果と一致しなければ拒否する</summary>
	public List<PointCampaignConflict> Confirmed { get; set; } = [];
}

/// <summary>対象保存の結果。</summary>
public sealed class PointCampaignTargetResult {
	/// <summary>期間が重なる他キャンペーンの対象。更新時は削除した内容</summary>
	public List<PointCampaignConflict> Conflicts { get; set; } = [];
	/// <summary>更新後のキャンペーンVdu（確認時は現在値）</summary>
	public long Vdu { get; set; }
}

/// <summary>他キャンペーンとの重複1件。TargetKind 1=店舗 2=商品で、削除対象は相手の該当行。</summary>
public sealed record PointCampaignConflict {
	public long Id_PointCampaign { get; init; }
	public string Code { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public string DayFrom { get; init; } = string.Empty;
	public string DayTo { get; init; } = string.Empty;
	public int TargetKind { get; init; }
	public long Id_Target { get; init; }
	public string TargetCode { get; init; } = string.Empty;
	public string TargetName { get; init; } = string.Empty;
}
