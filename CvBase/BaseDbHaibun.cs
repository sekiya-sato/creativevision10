using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;

// 配分トランザクション
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
// nk2: 引当数(ReserveQty)の再計算が倉庫+SKUで絞り込むため
[KeyDml("nk2", false, [nameof(Id_Soko), nameof(Id_Shohin), nameof(Id_Col), nameof(Id_Siz)])]
[Comment("トランザクション：配分データ 倉庫からの移動指示：日付、配分CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHaibun : BaseDbClass, ITranReserve {
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("配分指示日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 納品日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("納品日")]
	public partial string NouhinDay { get; set; } = string.Empty;
	/// <summary>
	/// 倉庫Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("倉庫CD")]
	[ForeignKey(nameof(MasterTokui), tenType: 0, additionalInfo: "TenType in (0,3,6)")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("得意先CD")]
	[ForeignKey(nameof(MasterTokui), tenType: 0, additionalInfo: "TenType in (0,3,6)")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// 区分（<see cref="EnumHaibun"/>）。どの画面が作った配分指示かを表す。
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("区分")]
	public partial int Kubun { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumHaibun EnKubun {
		get => (EnumHaibun)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	/// 送信フラグ 0:未送信 1:送信中 2:送信済み
	/// <para>
	/// 物流システムへの連携状態。**確定済みかどうかは <see cref="KakuteiDay"/> で判定する**。
	/// 修正可能なのは `SendFlg = 0` かつ `KakuteiDay` が空の行だけ。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("送信FLG")]
	public partial int SendFlg { get; set; }
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 入力JANコード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("JANCODE")]
	public partial string JanCode { get; set; } = string.Empty;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在する色")]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在するサイズ")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量")]
	public partial int Su { get; set; }
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("単価")]
	public partial int Tanka { get; set; }
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	public partial int Kingaku { get; set; }
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代金額")]
	public partial int Jodai { get; set; }
	/// <summary>
	/// 下代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代金額")]
	public partial int Gedai { get; set; }
	/// <summary>
	///	関連No1 = <b>元伝票のId</b>（配分の入力元）。
	/// <para>
	/// 受注配分なら <see cref="Tran12Jyuchu"/>.Id、初回配分なら <see cref="Tran13Hachu"/>.Id。
	/// 在庫からの配分（在庫品配分・取置・移動指示）は元伝票が無いので 0。
	/// システム全体で一貫した「RelateNo1 = 元伝票Id」規約
	/// （Tran03Shiire←発注 / Tran00Uriage←受注 / Tran11IdoIn←積送出庫）に揃えている。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	///	関連No2 = <b>配分確定で作成した伝票のId</b>（配分の出力先）。
	/// <para>
	/// 店舗向けなら <see cref="Tran10IdoOut"/> / <see cref="Tran05Ido"/>.Id、
	/// 得意先向けなら <see cref="Tran00Uriage"/>.Id。未確定は 0。
	/// 確定済み配分の二重伝票作成を防ぐ判定にこの列を使う。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO2")]
	public partial int RelateNo2 { get; set; }
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("明細メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 確定日 yyyyMMdd 8桁の文字列で表現。空文字なら未確定。
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("確定日")]
	public partial string KakuteiDay { get; set; } = string.Empty;
	/// <summary>
	/// 実数量（確定時に実際に出荷・移動した数）。未確定のうちは 0。
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("実数量")]
	public partial int JitsuSu { get; set; }
	/// <summary>
	/// 入力社員Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	[ForeignKey(nameof(MasterShain))]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 入庫済FLG。0=未入庫（引当中） / 1=振り分け後入庫済み（引当解除）。
	/// <para>
	/// この値が0の行の <see cref="Su"/> だけが <see cref="SummaryStock.ReserveQty"/> /
	/// <see cref="SummaryRealStock.ReserveQty"/>（引当数）へ集計される。
	/// 追加・修正・削除、およびこの列の部分更新のたびに、対象の倉庫+SKU の引当数が引き直される。
	/// <see cref="KakuteiDay"/>（配分確定）と <see cref="SendFlg"/>（物流連携）は引当の判定に使わない。
	/// 仕様は `Doc/spec/2026-08-12_phase1_業務仕様決定ドラフト.md` 2.2 / 2.8 を参照する。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnEndFlag))]
	public partial int EndFlag { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnEndFlag {
		get => (EnumYesNo)EndFlag;
		set => EndFlag = (int)value;
	}
}

/// <summary>
/// 配分区分（<see cref="TranHaibun.Kubun"/>）。
/// <para>
/// 0 / 1 は既存の店舗配分入力(ShopHaibunInputViewModel)が使っていた値なので<b>変更しないこと</b>。
/// 2 以降を新しい配分画面へ割り当てている。設計の背景は `.omo/2026-07-31_haibun_design.md` を参照。
/// </para>
/// </summary>
public enum EnumHaibun : int {
	/// <summary>初回配分（発売時に入荷予定を店舗へ振り分ける）。RelateNo1 = 発注Id</summary>
	[Comment("初回配分")]
	Hatsukai = 0,
	/// <summary>在庫配分（倉庫の現在庫を店舗へ振り分ける）。RelateNo1 = 0</summary>
	[Comment("在庫配分")]
	Zaiko = 1,
	/// <summary>受注配分（得意先の受注に対して在庫を割り当てる）。RelateNo1 = 受注Id</summary>
	[Comment("受注配分")]
	Juchu = 2,
	/// <summary>得意先別配分（得意先を軸に商品を振り分ける）。RelateNo1 = 0 または 受注Id</summary>
	[Comment("得意先別配分")]
	Tokui = 3,
	/// <summary>店舗出荷依頼（店舗側から本部倉庫へ出荷を依頼する）。RelateNo1 = 0</summary>
	[Comment("店舗出荷依頼")]
	ShopRequest = 4,
	/// <summary>在庫品配分（滞留在庫などを対象に配分する）。RelateNo1 = 0</summary>
	[Comment("在庫品配分")]
	ZaikoHin = 5,
	/// <summary>取置（特定の得意先・顧客向けに在庫を確保する）。RelateNo1 = 0</summary>
	[Comment("取置")]
	Reservation = 6,
	/// <summary>移動指示（倉庫間の移動を指示する）。RelateNo1 = 0</summary>
	[Comment("移動指示")]
	IdoShiji = 7,
}

// 補充トランザクション
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("トランザクション：補充データ 仕入先への補充発注依頼：日付、補充CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHoju : BaseDbClass {
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("補充指示日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 納品日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("納品日")]
	public partial string NouhinDay { get; set; } = string.Empty;
	/// <summary>
	/// 倉庫Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("倉庫CD")]
	[ForeignKey(nameof(MasterTokui), tenType: 0, additionalInfo: "TenType in (0,3,6)")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入先CD")]
	[ForeignKey(nameof(MasterShiire))]
	public partial long Id_Shiire { get; set; }
	/// <summary>
	/// 区分
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("区分")]
	public partial int Kubun { get; set; }
	/// <summary>
	/// 送信フラグ 0:未送信 1:送信中 2:送信済み
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("送信FLG")]
	public partial int SendFlg { get; set; }
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 入力JANコード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("JANCODE")]
	public partial string JanCode { get; set; } = string.Empty;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在する色")]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在するサイズ")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量")]
	public partial int Su { get; set; }
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("単価")]
	public partial int Tanka { get; set; }
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	public partial int Kingaku { get; set; }
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代金額")]
	public partial int Jodai { get; set; }
	/// <summary>
	/// 下代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代金額")]
	public partial int Gedai { get; set; }
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	///	関連No2
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO2")]
	public partial int RelateNo2 { get; set; }
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("明細メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 確定日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("確定日")]
	public partial string KakuteiDay { get; set; } = string.Empty;
	/// <summary>
	/// 実数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("実数量")]
	public partial int JitsuSu { get; set; }
	/// <summary>
	/// 入力社員Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	[ForeignKey(nameof(MasterShain))]
	public partial long Id_Shain { get; set; }
}
