using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;

/// <summary>ポイント制度の版ごとの基本付与条件。使用済み版は変更せず新版を追加する。</summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Code), nameof(Version))]
[KeyDml("nk1", false, nameof(Code), nameof(DayFrom), nameof(DayTo))]
[Comment("ポイント制度の版ごとの基本付与条件")]
public sealed partial class MasterPointBase : BaseDbClass {
	/// <summary>制度コード</summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[Comment("制度コード")]
	public partial string Code { get; set; } = string.Empty;
	/// <summary>制度名称</summary>
	[ObservableProperty]
	[ColumnSizeDml(80)]
	[Comment("制度名称")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>版番号（1以上）</summary>
	[ObservableProperty]
	[Comment("版番号（1以上）")]
	public partial int Version { get; set; } = 1;
	/// <summary>適用開始日 yyyyMMdd</summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("適用開始日 yyyyMMdd")]
	public partial string DayFrom { get; set; } = string.Empty;
	/// <summary>適用終了日 yyyyMMdd</summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("適用終了日 yyyyMMdd")]
	public partial string DayTo { get; set; } = string.Empty;
	/// <summary>有効 0=しない 1=する</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsEnabled))]
	[ForeignKey(nameof(EnumYesNo))]
	[Comment("有効 0=しない 1=する")]
	public partial int IsEnabled { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsEnabled {
		get => (EnumYesNo)IsEnabled;
		set => IsEnabled = (int)value;
	}
	/// <summary>ポイント付与単価（正の金額）</summary>
	[ObservableProperty]
	[Comment("ポイント付与単価（正の金額）")]
	public partial long PointUnitPrice { get; set; } = 100;
	/// <summary>プロパーの付与ポイント数（0以上）</summary>
	[ObservableProperty]
	[Comment("プロパーの付与ポイント数（0以上）")]
	public partial long PointAmountProper { get; set; } = 1;
	/// <summary>セールの付与ポイント数（0以上）</summary>
	[ObservableProperty]
	[Comment("セールの付与ポイント数（0以上）")]
	public partial long PointAmountSale { get; set; } = 1;
	/// <summary>付与対象金額の税基準 0=税抜 1=税込</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnTaxBasis))]
	[ForeignKey(nameof(EnumPointTaxBasis))]
	[Comment("付与対象金額の税基準 0=税抜 1=税込")]
	public partial int TaxBasis { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumPointTaxBasis EnTaxBasis {
		get => (EnumPointTaxBasis)TaxBasis;
		set => TaxBasis = (int)value;
	}
	/// <summary>ポイント計算単位 0=伝票 1=明細</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnCalcUnit))]
	[ForeignKey(nameof(EnumPointCalcUnit))]
	[Comment("ポイント計算単位 0=伝票 1=明細")]
	public partial int CalcUnit { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumPointCalcUnit EnCalcUnit {
		get => (EnumPointCalcUnit)CalcUnit;
		set => CalcUnit = (int)value;
	}
	/// <summary>端数処理 0=四捨五入 1=切上 2=切捨</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnRounding))]
	[ForeignKey(nameof(EnumRounding))]
	[Comment("端数処理 0=四捨五入 1=切上 2=切捨")]
	public partial int Rounding { get; set; } = (int)EnumRounding.Floor;
	[Ignore]
	[JsonIgnore]
	public EnumRounding EnRounding {
		get => (EnumRounding)Rounding;
		set => Rounding = (int)value;
	}
	/// <summary>利用ポイント相当額を付与対象額から控除 0=しない 1=する</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnDeductPointUse))]
	[ForeignKey(nameof(EnumYesNo))]
	[Comment("利用ポイント相当額を付与対象額から控除 0=しない 1=する")]
	public partial int DeductPointUse { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnDeductPointUse {
		get => (EnumYesNo)DeductPointUse;
		set => DeductPointUse = (int)value;
	}
}

/// <summary>ベース条件の版に属するランク別付与条件。税基準・計算単位・丸めは親を継承する。</summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_PointBase), nameof(Kubun))]
[Comment("ランク別ポイント付与条件")]
public sealed partial class MasterPointRank : BaseDbClass {
	/// <summary>ベース条件の版Id。移行した旧ランクの0は親の設定待ち</summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterPointBase))]
	[Comment("ベース条件の版Id。移行した旧ランクの0は親の設定待ち")]
	public partial long Id_PointBase { get; set; } = 0;
	/// <summary>ランクコード（マスタで定義するためenumではない）</summary>
	[ObservableProperty]
	[Comment("ランクコード（マスタで定義するためenumではない）")]
	public partial int Kubun { get; set; } = 0;
	/// <summary>ランク名称</summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	[Comment("ランク名称")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>ランク別ポイント付与単価（正の金額）</summary>
	[ObservableProperty]
	[Comment("ランク別ポイント付与単価（正の金額）")]
	public partial long PointUnitPrice { get; set; } = 100;
	/// <summary>プロパーの付与ポイント数（0以上）</summary>
	[ObservableProperty]
	[Comment("プロパーの付与ポイント数（0以上）")]
	public partial long PointAmountProper { get; set; } = 1;
	/// <summary>セールの付与ポイント数（0以上）</summary>
	[ObservableProperty]
	[Comment("セールの付与ポイント数（0以上）")]
	public partial long PointAmountSale { get; set; } = 1;
}

/// <summary>基本付与とは別イベントで記録する追加ポイント条件。</summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Code), nameof(Version))]
[KeyDml("nk1", false, nameof(Id_PointBase), nameof(DayFrom), nameof(DayTo))]
[Comment("ボーナスポイント付与条件")]
public sealed partial class MasterPointBonus : BaseDbClass {
	/// <summary>ボーナスコード。版をまたぐ回数制限の識別子</summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[Comment("ボーナスコード。版をまたぐ回数制限の識別子")]
	public partial string Code { get; set; } = string.Empty;
	/// <summary>ボーナス名称</summary>
	[ObservableProperty]
	[ColumnSizeDml(80)]
	[Comment("ボーナス名称")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>版番号（1以上）</summary>
	[ObservableProperty]
	[Comment("版番号（1以上）")]
	public partial int Version { get; set; } = 1;
	/// <summary>対象ベース条件の版Id</summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterPointBase))]
	[Comment("対象ベース条件の版Id")]
	public partial long Id_PointBase { get; set; } = 0;
	/// <summary>適用開始日 yyyyMMdd</summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("適用開始日 yyyyMMdd")]
	public partial string DayFrom { get; set; } = string.Empty;
	/// <summary>適用終了日 yyyyMMdd</summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("適用終了日 yyyyMMdd")]
	public partial string DayTo { get; set; } = string.Empty;
	/// <summary>有効 0=しない 1=する</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsEnabled))]
	[ForeignKey(nameof(EnumYesNo))]
	[Comment("有効 0=しない 1=する")]
	public partial int IsEnabled { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsEnabled {
		get => (EnumYesNo)IsEnabled;
		set => IsEnabled = (int)value;
	}
	/// <summary>付与契機 0=期間内購入 1=誕生月購入 2=初回購入</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnTriggerType))]
	[ForeignKey(nameof(EnumPointBonusTrigger))]
	[Comment("付与契機 0=期間内購入 1=誕生月購入 2=初回購入")]
	public partial int TriggerType { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumPointBonusTrigger EnTriggerType {
		get => (EnumPointBonusTrigger)TriggerType;
		set => TriggerType = (int)value;
	}
	/// <summary>追加付与ポイント（正の数）</summary>
	[ObservableProperty]
	[Comment("追加付与ポイント（正の数）")]
	public partial long PointAmount { get; set; } = 0;
	/// <summary>最低購入金額（親の税基準で判定、0以上）</summary>
	[ObservableProperty]
	[Comment("最低購入金額（親の税基準で判定、0以上）")]
	public partial long MinimumKingaku { get; set; } = 0;
	/// <summary>全ランク対象 0=しない 1=する</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsAllRanks))]
	[ForeignKey(nameof(EnumYesNo))]
	[Comment("全ランク対象 0=しない 1=する")]
	public partial int IsAllRanks { get; set; } = (int)EnumYesNo.Yes;
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsAllRanks {
		get => (EnumYesNo)IsAllRanks;
		set => IsAllRanks = (int)value;
	}
	/// <summary>対象ランクコード。全ランク対象の場合は0</summary>
	[ObservableProperty]
	[Comment("対象ランクコード。全ランク対象の場合は0")]
	public partial int RankKubun { get; set; } = 0;
	/// <summary>回数制限期間 0=伝票 1=適用期間 2=会計年度 3=生涯</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnLimitPeriodType))]
	[ForeignKey(nameof(EnumPointLimitPeriod))]
	[Comment("回数制限期間 0=伝票 1=適用期間 2=会計年度 3=生涯")]
	public partial int LimitPeriodType { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumPointLimitPeriod EnLimitPeriodType {
		get => (EnumPointLimitPeriod)LimitPeriodType;
		set => LimitPeriodType = (int)value;
	}
	/// <summary>制限期間内の付与回数上限（1以上）</summary>
	[ObservableProperty]
	[Comment("制限期間内の付与回数上限（1以上）")]
	public partial int LimitCount { get; set; } = 1;
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(EventKey))]
[KeyDml("nk1", false, nameof(Id_Customer), nameof(DenDay), nameof(Id))]
[KeyDml("nk2", false, nameof(Id_Tenuri))]
[KeyDml("nk3", false, nameof(Id_Tenpo), nameof(DenDay))]
[KeyDml("nk4", false, nameof(Id_OriginalEvent))]
[KeyDml("nk5", false, nameof(Id_PointBase))]
[KeyDml("nk6", false, nameof(Id_PointRank))]
[KeyDml("nk7", false, nameof(Id_PointBonus))]
[Comment("ポイント取引台帳。確定履歴は上書きせず、取消・訂正は新しいイベントで記録する")]
public sealed partial class TranPointEvent : BaseDbClass {
	/// <summary>伝票・操作ごとの一意キー。同じ操作の再送では同じキーを使用する。</summary>
	[ObservableProperty]
	[ColumnSizeDml(160)]
	[Comment("伝票・操作ごとの一意キー。同じ操作の再送では同じキーを使用する")]
	public partial string EventKey { get; set; } = string.Empty;
	/// <summary>
	/// ポイント計上日 yyyyMMdd
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("ポイント計上日 yyyyMMdd")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 顧客Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterEndCustomer))]
	[Comment("顧客Id")]
	public partial long Id_Customer { get; set; }
	/// <summary>発生店舗Id。移行・調整など店舗に紐づかない場合は0。</summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 6)]
	[Comment("発生店舗Id。店舗に紐づかない場合は0")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>対象店舗売上伝票Id。売上に関係しない場合は0。</summary>
	[ObservableProperty]
	[ForeignKey(nameof(Tran01Tenuri))]
	[Comment("対象店舗売上伝票Id。売上に関係しない場合は0")]
	public partial long Id_Tenuri { get; set; }
	/// <summary>イベント種別 0=未指定 1=付与 2=利用 3=取消 4=調整 5=失効 6=移行残高</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnEventType))]
	[ForeignKey(nameof(EnumPointEventType))]
	[Comment("イベント種別 0=未指定 1=付与 2=利用 3=取消 4=調整 5=失効 6=移行残高")]
	public partial int EventType { get; set; } = 0;
	[Ignore]
	[JsonIgnore]
	public EnumPointEventType EnEventType {
		get => (EnumPointEventType)EventType;
		set => EventType = (int)value;
	}
	/// <summary>増減ポイント。付与は正、利用・失効は負、取消は元履歴の逆符号。</summary>
	[ObservableProperty]
	[Comment("増減ポイント。付与は正、利用・失効は負、取消は元履歴の逆符号")]
	public partial long PointDelta { get; set; }
	/// <summary>取消・訂正対象の履歴Id。通常は0。</summary>
	[ObservableProperty]
	[ForeignKey(nameof(TranPointEvent))]
	[Comment("取消・訂正対象の履歴Id。通常は0")]
	public partial long Id_OriginalEvent { get; set; }
	/// <summary>操作者Id。自動処理は0。</summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShain))]
	[Comment("操作者Id。自動処理は0")]
	public partial long Id_Shain { get; set; }
	/// <summary>適用したベース条件の版Id。条件に関係しない場合は0</summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterPointBase))]
	[Comment("適用したベース条件の版Id。条件に関係しない場合は0")]
	public partial long Id_PointBase { get; set; } = 0;
	/// <summary>適用したランク条件Id。該当しない場合は0</summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterPointRank))]
	[Comment("適用したランク条件Id。該当しない場合は0")]
	public partial long Id_PointRank { get; set; } = 0;
	/// <summary>適用したボーナス条件の版Id。該当しない場合は0</summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterPointBonus))]
	[Comment("適用したボーナス条件の版Id。該当しない場合は0")]
	public partial long Id_PointBonus { get; set; } = 0;
	/// <summary>計算対象だった店舗売上のVdu（UTC.Ticks）。売上に関係しない場合は0。</summary>
	[ObservableProperty]
	[Comment("計算対象だった店舗売上のVdu（UTC.Ticks）。売上に関係しない場合は0")]
	public partial long SourceVdu { get; set; }
	/// <summary>適用時のルール・版・ランク・対象金額・計算結果を保持するJSON文字列。保存時にJSONの妥当性を検査する。</summary>
	[ObservableProperty]
	[ColumnSizeDml(4000)]
	[Comment("適用時の計算条件・結果のJSON文字列。保存時にJSONの妥当性を検査する")]
	public partial string Jcalc { get; set; } = "{}";
	/// <summary>
	/// 摘要
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[Comment("摘要")]
	public partial string Memo { get; set; } = string.Empty;
}

/// <summary>ポイント取引台帳のイベント種別。</summary>
public enum EnumPointEventType : int {
	[Comment("未指定")]
	Unspecified = 0,
	[Comment("付与")]
	Grant = 1,
	[Comment("利用")]
	Use = 2,
	[Comment("取消")]
	Cancel = 3,
	[Comment("調整")]
	Adjustment = 4,
	[Comment("失効")]
	Expire = 5,
	[Comment("移行残高")]
	OpeningBalance = 6
}

[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_Customer))]
[Comment("ポイント残高テーブル")]
public sealed partial class SummaryPoint : BaseDbClass {
	/// <summary>
	/// 顧客Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterEndCustomer))]
	[Comment("顧客Id")]
	public partial int Id_Customer { get; set; }
	/// <summary>
	/// 合計ポイント
	/// </summary>
	[ObservableProperty]
	[Comment("合計ポイント")]
	public partial int Point { get; set; }
	/// <summary>
	/// 累計購買回数
	/// </summary>
	[ObservableProperty]
	[Comment("累計購買回数")]
	public partial int SalesCount { get; set; }
	/// <summary>
	/// 累計購買金額
	/// </summary>
	[ObservableProperty]
	[Comment("累計購買金額")]
	public partial int SalesKingaku { get; set; }
}
