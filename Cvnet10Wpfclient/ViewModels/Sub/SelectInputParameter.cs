using CommunityToolkit.Mvvm.ComponentModel;

namespace Cvnet10Wpfclient.ViewModels.Sub;

public partial class SelectInputParameter : ObservableObject {
	[ObservableProperty]
	long? fromId;
	[ObservableProperty]
	long? toId;
	[ObservableProperty]
	string? fromDate;
	[ObservableProperty]
	string? toDate;

	/// <summary>店舗CD（テーブルにより得意先/移動先等に読み替え）</summary>
	[ObservableProperty]
	string? fromToriCd;
	[ObservableProperty]
	string? toToriCd;
	/// <summary>店舗名（from側）</summary>
	[ObservableProperty]
	string? fromToriName;
	/// <summary>店舗名（to側）</summary>
	[ObservableProperty]
	string? toToriName;
	/// <summary>店舗CD行のラベル（テーブルにより「店舗CD」「得意先」「移動先」等に変更）</summary>
	[ObservableProperty]
	string toriLabel = "店舗CD";
	/// <summary>店舗CD行を表示するか</summary>
	[ObservableProperty]
	bool isToriVisible = true;
	/// <summary>店舗CDの検索Where句（MasterTokui TenType条件等）</summary>
	public string? ToriSearchWhere { get; set; }

	/// <summary>倉庫CD</summary>
	[ObservableProperty]
	string? fromSokoCd;
	[ObservableProperty]
	string? toSokoCd;
	/// <summary>倉庫名（from側）</summary>
	[ObservableProperty]
	string? fromSokoName;
	/// <summary>倉庫名（to側）</summary>
	[ObservableProperty]
	string? toSokoName;

	/// <summary>商品CD（部分一致検索用）</summary>
	[ObservableProperty]
	string? shohinCdLike;

	[ObservableProperty]
	int? maxCount;

	[ObservableProperty]
	string? displayName;
}
