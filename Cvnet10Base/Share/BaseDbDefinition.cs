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
	[NotifyPropertyChangedFor(nameof(VdateC))]
	[property: Comment("V作成日UTC.Ticks")]
	long vdc = DateTime.Now.ToUniversalTime().Ticks;
	/// <summary>
	/// 修正日UTC.Ticks
	/// </summary>
	[ObservableProperty]
	[property: Comment("V修正日UTC.Ticks")]
	[NotifyPropertyChangedFor(nameof(VdateU))]
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
[NoCreate]
public partial class BaseDetailClass : ObservableObject {
	/// <summary>
	/// 予備項目1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: DefaultValue("")]
	string yobi1 = string.Empty;
	/// <summary>
	/// 予備項目1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: DefaultValue("")]
	string yobi2 = string.Empty;
}
/// <summary>
/// Id、コード、名称のみの短い名称データ
/// </summary>
[NoCreate]
public partial class CodeNameView : ObservableObject {
	/// <summary>
	/// 対象テーブルのId
	/// </summary>
	[ObservableProperty]
	long sid;
	/// <summary>
	/// 対象テーブルのCode
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: DefaultValue("")]
	string cd = string.Empty;
	/// <summary>
	/// 対象テーブルのName
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: DefaultValue("")]
	string mei = string.Empty;

	public CodeNameView() :base() {
	}
	public CodeNameView(MasterMeisho meisho) {
		Sid = meisho.Id;
		Cd = meisho.Code;
		Mei = meisho.Name;
	}
	public CodeNameView(long id, string code, string name) {
		Sid = id;
		Cd = code;
		Mei = name;
	}
}
/// <summary>
/// 汎用カテゴリ名称マスター
/// </summary>
[NoCreate]
public sealed partial class MasterGeneralMeisho : CodeNameView {
	/// <summary>
	/// 名称区分
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(10)]
	[property: DefaultValue("")]
	string kb = string.Empty;
	/// <summary>
	/// 区分名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(40)]
	[property: DefaultValue("")]
	string kbname = string.Empty;
}

