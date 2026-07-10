using CommunityToolkit.Mvvm.ComponentModel;

namespace CvWpfclient.ViewModels.Sub;

public partial class SelectInputParameter : ObservableObject {
	[ObservableProperty]
	public partial long? FromId { get; set; }
	[ObservableProperty]
	public partial long? ToId { get; set; }
	[ObservableProperty]
	public partial string? FromDate { get; set; }
	[ObservableProperty]
	public partial string? ToDate { get; set; }

	/// <summary>店舗CD（テーブルにより得意先/移動先等に読み替え）</summary>
	[ObservableProperty]
	public partial string? FromToriCd { get; set; }
	[ObservableProperty]
	public partial string? ToToriCd { get; set; }
	/// <summary>店舗Idの複数選択</summary>
	[ObservableProperty]
	public partial List<long> ToriIds { get; set; } = [];
	/// <summary>店舗Id複数選択の表示文字列</summary>
	[ObservableProperty]
	public partial string ToriIdsText { get; set; } = "未選択";
	/// <summary>店舗名（from側）</summary>
	[ObservableProperty]
	public partial string? FromToriName { get; set; }
	/// <summary>店舗名（to側）</summary>
	[ObservableProperty]
	public partial string? ToToriName { get; set; }
	/// <summary>店舗CD行のラベル（テーブルにより「店舗CD」「得意先」「移動先」等に変更）</summary>
	[ObservableProperty]
	public partial string ToriLabel { get; set; } = "店舗CD";
	/// <summary>店舗CD行を表示するか</summary>
	[ObservableProperty]
	public partial bool IsToriVisible { get; set; } = true;
	/// <summary>店舗CDの検索Where句（MasterTokui TenType条件等）</summary>
	public string? ToriSearchWhere { get; set; }

	/// <summary>倉庫CD</summary>
	[ObservableProperty]
	public partial string? FromSokoCd { get; set; }
	[ObservableProperty]
	public partial string? ToSokoCd { get; set; }
	/// <summary>倉庫Idの複数選択</summary>
	[ObservableProperty]
	public partial List<long> SokoIds { get; set; } = [];
	/// <summary>倉庫Id複数選択の表示文字列</summary>
	[ObservableProperty]
	public partial string SokoIdsText { get; set; } = "未選択";
	/// <summary>倉庫名（from側）</summary>
	[ObservableProperty]
	public partial string? FromSokoName { get; set; }
	/// <summary>倉庫名（to側）</summary>
	[ObservableProperty]
	public partial string? ToSokoName { get; set; }

	/// <summary>商品Idの複数選択</summary>
	[ObservableProperty]
	public partial List<long> ShohinIds { get; set; } = [];
	/// <summary>商品Id複数選択の表示文字列</summary>
	[ObservableProperty]
	public partial string ShohinIdsText { get; set; } = "未選択";

	/// <summary>入力バーコード</summary>
	[ObservableProperty]
	public partial string? InputBarcode { get; set; }

	/// <summary>商品名（部分一致検索用）</summary>
	[ObservableProperty]
	public partial string? ShohinNameLike { get; set; }

	[ObservableProperty]
	public partial int? MaxCount { get; set; }

	[ObservableProperty]
	public partial string? DisplayName { get; set; }
}
