using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;

/// <summary>
/// 基底データクラス(Id,Vdc,Vdu) Id_??=joinキー,Fg_=0/1フラグ,En_=enum値,Disp0=表示用
/// </summary>
public partial class BaseDbClass : ObservableObject {
	/// <summary>
	/// ユニークキー
	/// </summary>
	[ObservableProperty]
	[Comment("ユニークキー")]
	public partial long Id { get; set; }
	/// <summary>
	/// 作成日UTC.Ticks
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(VdateC))]
	[Comment("V作成日UTC.Ticks")]
	public partial long Vdc { get; set; } = DateTime.Now.ToUniversalTime().Ticks;
	/// <summary>
	/// 修正日UTC.Ticks
	/// </summary>
	[ObservableProperty]
	[Comment("V修正日UTC.Ticks")]
	[NotifyPropertyChangedFor(nameof(VdateU))]
	public partial long Vdu { get; set; } = DateTime.Now.ToUniversalTime().Ticks;
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
	[ResultColumn]
	public partial string Disp0 { get; set; } = string.Empty;
}
/// <summary>
/// 住所を持つ共通基底クラス
/// </summary>
public partial class BaseDbHasAddress : BaseDbClass {
	/// <summary>
	/// 郵便番号
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[Comment("郵便番号")]
	public partial string PostalCode { get; set; } = string.Empty;
	/// <summary>
	/// 住所1 都道府県
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(60)]
	[Comment("住所1 都道府県")]
	public partial string Address1 { get; set; } = string.Empty;
	/// <summary>
	/// 住所2 市区町村
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(60)]
	[Comment("住所2 市区町村")]
	public partial string Address2 { get; set; } = string.Empty;
	/// <summary>
	/// 住所3 番地
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(60)]
	[Comment("住所3 番地")]
	public partial string Address3 { get; set; } = string.Empty;
	/// <summary>
	/// 電話番号
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[Comment("電話番号")]
	public partial string Tel { get; set; } = string.Empty;
	/// <summary>
	/// メールアドレス
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[Comment("メールアドレス")]
	public partial string Mail { get; set; } = string.Empty;
}

/// <summary>
/// 汎用詳細クラス
/// </summary>
[SubTableDefine]
public partial class BaseDetailClass : ObservableObject {
	/// <summary>
	/// 予備項目1
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[Comment("予備項目1")]
	public partial string Yobi1 { get; set; } = string.Empty;
	/// <summary>
	/// 予備項目2
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(120)]
	[Comment("予備項目2")]
	public partial string Yobi2 { get; set; } = string.Empty;
}
/// <summary>
/// Id、コード、名称のみの短い名称データ
/// <para>
/// この型の列（V*列）は参照先マスタの複製であり、意味論はテーブル種別で異なる。
/// <b>Tran系＝伝票作成時点の名称（伝播しない監査値）／Master系＝常に現行名称（CvDomainLogic/MasterCascadeDb が同期）</b>。
/// Master系に新しくV*列を追加した場合は MasterCascadeDb.VRules への登録も必須
/// （未登録は MasterCascadeDbTests の VRules_CoverAllMasterVColumns が検出する）。
/// </para>
/// </summary>
[Comment("V*列:参照先マスタの複製")]
[NoCreate]
public partial class CodeNameView : ObservableObject {
	/// <summary>
	/// 対象テーブルのId
	/// </summary>
	[ObservableProperty]
	[Comment("対象テーブルのId")]
	public partial long Sid { get; set; }
	/// <summary>
	/// 対象テーブルのCode
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[Comment("対象テーブルのCode")]
	public partial string Cd { get; set; } = string.Empty;
	/// <summary>
	/// 対象テーブルのName
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[Comment("対象テーブルのName")]
	public partial string Mei { get; set; } = string.Empty;

	public CodeNameView() : base() {
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
[SubTableDefine]
public sealed partial class MasterGeneralMeisho : CodeNameView {
	/// <summary>
	/// 名称区分
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(10)]
	[Comment("名称区分")]
	public partial string Kb { get; set; } = string.Empty;
	/// <summary>
	/// 区分名(PascalCase規約外)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	[Comment("区分名(PascalCase規約外)")]
	public partial string Kbname { get; set; } = string.Empty;
	/// <summary>
	/// 選択元のマスターリスト
	/// </summary>
	public List<MasterMeisho> BaseList { get; private set; } = [];
	// Kb が変更されたら自動的に呼ばれる (XAML側のトリガー不要)
	partial void OnKbChanged(string value) {
		if (BaseList == null || BaseList.Count == 0) return;
		var item = BaseList.FirstOrDefault(x => x.Code == value);
		if (item == null) return;
		Kbname = item.Name ?? string.Empty;
	}
	public MasterGeneralMeisho() : base() {
	}
	public MasterGeneralMeisho(List<MasterMeisho> baseList) {
		BaseList = baseList;
	}
	public void SetBaseList(List<MasterMeisho> baseList) {
		BaseList = baseList;
	}
}

