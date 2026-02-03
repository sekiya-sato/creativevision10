
using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;
using System.Text.Json.Serialization;

namespace Cvnet10Base;

/// <summary>
/// 基底データクラス(Id,Vdc,Vdu) Id_??=joinキー,Fg_=0/1フラグ,En_=enum値,Disp0=表示用
/// </summary>
public partial class BaseDbClass : ObservableObject {
	/// <summary>
	/// ユニークキー
	/// </summary>
	[ObservableProperty]
	[property: Comment("ユニークキー")]
	long id;
	/// <summary>
	/// 作成日UTC.Ticks
	/// </summary>
	[ObservableProperty]
	[property: Comment("V作成日UTC.Ticks")]
	long vdc = DateTime.Now.ToUniversalTime().Ticks;
	/// <summary>
	/// 修正日UTC.Ticks
	/// </summary>
	[ObservableProperty]
	[property: Comment("V修正日UTC.Ticks")]
	long vdu = DateTime.Now.ToUniversalTime().Ticks;
	/// <summary>
	/// 作成日(参照のみ)書式 yyyy/MM/dd HH:mm:ss.ffff DateTime(Vdc).ToLocalTime)
	/// </summary>
	[Ignore]
	[JsonIgnore]
	public DateTime VdateC {
		get => new DateTime(Vdc).ToLocalTime();
	}
	/// <summary>
	/// 修正日(参照のみ)書式 yyyy/MM/dd HH:mm:ss.ffff DateTime(Vdu).ToLocalTime
	/// </summary>
	[Ignore]
	[JsonIgnore]
	public DateTime VdateU {
		get => new DateTime(Vdu).ToLocalTime();
	}
	/// <summary>
	/// 表示専用項目
	/// </summary>
	[ObservableProperty]
	[property: ResultColumn]
	string? disp0;
}
/// <summary>
/// 住所を持つ共通基底クラス
/// </summary>
public partial class BaseDbHasAddress : BaseDbClass {
	/// <summary>
	/// 郵便番号
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(30)]
	string? postalCode;
	/// <summary>
	/// 住所1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(60)]
	string? address1;
	/// <summary>
	/// 住所2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(60)]
	string? address2;
	/// <summary>
	/// 住所3
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(60)]
	string? address3;
	/// <summary>
	/// 電話番号
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	string? tel;
	/// <summary>
	/// メールアドレス
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	string? mail;
}

/// <summary>
/// 汎用詳細クラス
/// </summary>
public partial class DetailDbClass : ObservableObject {
	/// <summary>
	/// 予備項目1
	/// </summary>
	[ObservableProperty]
	string? yobi1;
	/// <summary>
	/// 予備項目1
	/// </summary>
	[ObservableProperty]
	string? yobi2;
}

/// <summary>
/// バージョン情報
/// </summary>
public sealed class VersionInfo {
	public string Product { get; set; }= string.Empty;
	/// <summary>
	/// バージョン文字列
	/// </summary>
	public string Version { get; set; } = string.Empty;
	/// <summary>
	/// ビルド日
	/// </summary>
	public DateTime BuildDate { get; set; }
	/// <summary>
	/// サーバー起動時間
	/// </summary>
	public DateTime StartTime { get; set; }
}


