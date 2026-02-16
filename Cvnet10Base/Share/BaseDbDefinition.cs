using CommunityToolkit.Mvvm.ComponentModel;
using NPoco;
using Newtonsoft.Json;
using System.ComponentModel;

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
	[property: DefaultValue("")]
	string disp0 = string.Empty;
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
	[property: DefaultValue("")]
	string postalCode = string.Empty;
	/// <summary>
	/// 住所1 都道府県
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(60)]
	[property: DefaultValue("")]
	string address1 = string.Empty;
	/// <summary>
	/// 住所2 市区町村
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(60)]
	[property: DefaultValue("")]
	string address2 = string.Empty;
	/// <summary>
	/// 住所3 番地
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(60)]
	[property: DefaultValue("")]
	string address3 = string.Empty;
	/// <summary>
	/// 電話番号
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string tel = string.Empty;
	/// <summary>
	/// メールアドレス
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: DefaultValue("")]
	string mail = string.Empty;
}

/// <summary>
/// 汎用詳細クラス
/// </summary>
public partial class DetailDbClass : ObservableObject {
	/// <summary>
	/// 予備項目1
	/// </summary>
	[ObservableProperty]
	[property: DefaultValue("")]
	string yobi1 = string.Empty;
	/// <summary>
	/// 予備項目1
	/// </summary>
	[ObservableProperty]
	[property: DefaultValue("")]
	string yobi2 = string.Empty;
}
/// <summary>
/// 汎用カテゴリ名称マスター
/// </summary>
[NoCreate]
public sealed partial class MasterGeneralMeisho : ObservableObject {
	/// <summary>
	/// 名称区分
	/// </summary>
	[ObservableProperty]
	[property: DefaultValue("")]
	string kubun = string.Empty;
	[ObservableProperty]
	[property: DefaultValue("")]
	string kubunName = string.Empty;
	/// <summary>
	/// 名称マスタId, Code, Name
	/// </summary>
	[ObservableProperty]
	long id_Code;
	[ObservableProperty]
	[property: DefaultValue("")]
	string code = string.Empty;
	[ObservableProperty]
	[property: DefaultValue("")]
	string name = string.Empty;
	/* ToDo: JSON シリアライズにプロパティを含める含めないの制御は必要かどうか検討する IBaseSerializeMeisho の必要性
	[JsonIgnore]
	public bool Ser { get; set; } = false;
	public bool ShouldSerializeKubunName() => Ser;
	public bool ShouldSerializeName() => Ser;
	 */
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

	public string BaseDir { get; set; } = string.Empty;
}


