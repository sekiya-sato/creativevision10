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
	[Comment("日付 yyyyMMdd 8桁の文字列で表現")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 納品日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("納品日")]
	[Comment("納品日 yyyyMMdd 8桁の文字列で表現")]
	public partial string NouhinDay { get; set; } = string.Empty;
	/// <summary>
	/// 倉庫Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("倉庫CD")]
	[ForeignKey(nameof(MasterTokui), tenType: 0, additionalInfo: "TenType in (0,3,6)")]
	[Comment("倉庫Id")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("得意先CD")]
	[ForeignKey(nameof(MasterTokui), tenType: 0, additionalInfo: "TenType in (0,3,6)")]
	[Comment("店舗Id")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// 区分（<see cref="EnumHaibun"/>）。どの画面が作った配分指示かを表す。
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("区分")]
	[Comment("区分（EnumHaibun）。どの画面が作った配分指示かを表す。")]
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
	[Comment("送信フラグ 0:未送信 1:送信中 2:送信済み 物流システムへの連携状態。確定済みかどうかは KakuteiDay で判定する。 修正可能なのは SendFlg = 0 かつ KakuteiDay が空の行だけ。")]
	public partial int SendFlg { get; set; }
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	[Comment("商品ユニークキー")]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 入力JANコード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("JANCODE")]
	[Comment("入力JANコード")]
	public partial string JanCode { get; set; } = string.Empty;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在する色")]
	[Comment("色")]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在するサイズ")]
	[Comment("サイズ")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量")]
	[Comment("数量")]
	public partial int Su { get; set; }
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("単価")]
	[Comment("単価")]
	public partial int Tanka { get; set; }
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	[Comment("金額")]
	public partial int Kingaku { get; set; }
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代金額")]
	[Comment("上代")]
	public partial int Jodai { get; set; }
	/// <summary>
	/// 下代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代金額")]
	[Comment("下代")]
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
	[Comment("関連No1 = 元伝票のId（配分の入力元）。 受注配分なら Tran12Jyuchu.Id、初回配分なら Tran13Hachu.Id。 在庫からの配分（在庫品配分・取置・移動指示）は元伝票が無いので 0。")]
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
	[Comment("関連No2 = 配分確定で作成した伝票のId（配分の出力先）。 店舗向けなら Tran10IdoOut / Tran05Ido.Id、 得意先向けなら Tran00Uriage.Id。未確定は 0。")]
	public partial int RelateNo2 { get; set; }
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("明細メモ")]
	[Comment("明細メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 確定日 yyyyMMdd 8桁の文字列で表現。空文字なら未確定。
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("確定日")]
	[Comment("確定日 yyyyMMdd 8桁の文字列で表現。空文字なら未確定。")]
	public partial string KakuteiDay { get; set; } = string.Empty;
	/// <summary>
	/// 実数量（確定時に実際に出荷・移動した数）。未確定のうちは 0。
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("実数量")]
	[Comment("実数量（確定時に実際に出荷・移動した数）。未確定のうちは 0。")]
	public partial int JitsuSu { get; set; }
	/// <summary>
	/// 欠品数（指示に対して倉庫が出荷できなかった数）。未確定のうちは 0。
	/// <para>
	/// <see cref="Su"/>（指示数）はユーザーが配分入力で設定し、倉庫へ送信される。倉庫から戻されるデータで
	/// <see cref="JitsuSu"/>（出荷数）と本列が設定され、<c>Su = JitsuSu + ShortSu</c> が成立する。
	/// この状態かつ <see cref="KakuteiDay"/> に有効な日付があるものを確定とみなす。完了は <see cref="EndFlag"/>=1。
	/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 5.1.2 を参照する。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("欠品数量")]
	[Comment("欠品数（指示に対して倉庫が出荷できなかった数）。未確定のうちは 0。倉庫から戻されるデータで JitsuSu とともに設定され Su = JitsuSu + ShortSu が成立する。")]
	public partial int ShortSu { get; set; }
	/// <summary>
	/// 入力社員Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	[ForeignKey(nameof(MasterShain))]
	[Comment("入力社員Id")]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 入庫済FLG。0=未入庫（引当中） / 1=振り分け後入庫済み（引当解除）。
	/// <para>
	/// この値が0で、かつ <see cref="Kubun"/> が <see cref="EnumHaibun.Hatsukai"/>(0) 以外の行の
	/// <see cref="Su"/> だけが <see cref="SummaryStock.ReserveQty"/> /
	/// <see cref="SummaryRealStock.ReserveQty"/>（引当数）へ集計される。
	/// 初回配分は入荷前の振り分けであり現物を押さえないため引当対象外とする。
	/// 追加・修正・削除、およびこの列の部分更新のたびに、対象の倉庫+SKU の引当数が引き直される。
	/// <see cref="KakuteiDay"/>（配分確定）と <see cref="SendFlg"/>（物流連携）は引当の判定に使わない。
	/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 5.2 を参照する。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnEndFlag))]
	[Comment("入庫済FLG。0=未入庫（引当中） / 1=振り分け後入庫済み（引当解除）。 この値が0で、かつ Kubun が 0:初回配分 以外の行の Su だけが SummaryStock.ReserveQty / SummaryRealStock.ReserveQty（引当数）へ集計される。")]
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
/// <para>
/// <b>引当対象は <see cref="Hatsukai"/>(0) 以外のすべて</b>。判定は <c>Kubun != 0</c> の一点で行う。
/// 初回配分は入荷前に入荷予定を振り分けるものであり、現物在庫を押さえないため引当数へ算入しない。
/// 仕様は `Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 5.2 を参照する。
/// </para>
/// </summary>
public enum EnumHaibun : int {
	/// <summary>初回配分（発売時に入荷予定を店舗へ振り分ける）。RelateNo1 = 発注Id。<b>引当対象外</b></summary>
	[Comment("初回配分（引当対象外）")]
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

/// <summary>
/// 配分の<b>仮想ヘッダ</b>キー（決定 I5）。
/// <para>
/// <see cref="TranHaibun"/> は明細行（1行=1SKU）のまま持ち、ヘッダは実テーブルを作らずこのキーで括る。
/// キーは <see cref="DenDay"/>（配分指示日）+ <see cref="NouhinDay"/>（納品日）+
/// <see cref="Id_Soko"/>（出庫元倉庫）+ <see cref="Id_Tenpo"/>（出荷先）+ <see cref="Kubun"/>（区分）+
/// <see cref="RelateNo1"/>（元伝票Id）の6列で、旧CV.netの配分伝票NO（1出庫元 ⇒ 1出荷先）と同じ括りになる。
/// </para>
/// <para>
/// <b>キーを削ってはいけない。</b> <see cref="Kubun"/> は引当対象の判定そのもの
/// （<c>Kubun &lt;&gt; 0</c> が引当対象）で、<see cref="RelateNo1"/> は受注・受注残の自動完了判定に使う。
/// この2列を落とすと1ヘッダから元伝票を特定できなくなる。
/// </para>
/// <para>
/// 出荷処理（<c>ShippingDb.CreateShippingSlips</c>）が 1キー=1伝票 で出荷売上／移動出庫を作る。
/// 配分データメンテ・出荷指示明細書印刷・納入一覧表のヘッダ表示もこのキーが単位になる。
/// 構造化（ヘッダ実テーブル化）の検討経緯は
/// `Doc/spec/archive/2026-08-24_TranHaibun_ヘッダ明細構造化_調査.md` を参照する。
/// </para>
/// </summary>
public readonly record struct HaibunHeaderKey(string DenDay, string NouhinDay, long Id_Soko, long Id_Tenpo, int Kubun, int RelateNo1) {
	/// <summary>配分明細行から仮想ヘッダキーを作る</summary>
	public static HaibunHeaderKey From(TranHaibun row) =>
		new(row.DenDay, row.NouhinDay, row.Id_Soko, row.Id_Tenpo, row.Kubun, row.RelateNo1);

	/// <summary>配分区分（<see cref="EnumHaibun"/>）</summary>
	public EnumHaibun EnKubun => (EnumHaibun)Kubun;

	/// <summary>キーの列名。SQLの GROUP BY / ORDER BY をこのキーと必ず一致させるために使う</summary>
	public static readonly string[] KeyColumns = [
		nameof(TranHaibun.DenDay), nameof(TranHaibun.NouhinDay), nameof(TranHaibun.Id_Soko),
		nameof(TranHaibun.Id_Tenpo), nameof(TranHaibun.Kubun), nameof(TranHaibun.RelateNo1),
	];

	/// <summary>
	/// キー列をSQLの句へ展開する（例: <c>"h.DenDay, h.NouhinDay, ..."</c>）。
	/// </summary>
	/// <param name="alias"><see cref="TranHaibun"/> の別名。空文字なら修飾しない</param>
	public static string KeyColumnsSql(string alias = "") {
		var prefix = string.IsNullOrEmpty(alias) ? string.Empty : $"{alias}.";
		return string.Join(", ", KeyColumns.Select(c => $"{prefix}{c}"));
	}
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
	[Comment("日付 yyyyMMdd 8桁の文字列で表現")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 納品日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("納品日")]
	[Comment("納品日 yyyyMMdd 8桁の文字列で表現")]
	public partial string NouhinDay { get; set; } = string.Empty;
	/// <summary>
	/// 倉庫Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("倉庫CD")]
	[ForeignKey(nameof(MasterTokui), tenType: 0, additionalInfo: "TenType in (0,3,6)")]
	[Comment("倉庫Id")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入先CD")]
	[ForeignKey(nameof(MasterShiire))]
	[Comment("店舗Id")]
	public partial long Id_Shiire { get; set; }
	/// <summary>
	/// 区分
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("区分")]
	[Comment("区分")]
	public partial int Kubun { get; set; }
	/// <summary>
	/// 送信フラグ 0:未送信 1:送信中 2:送信済み
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("送信FLG")]
	[Comment("送信フラグ 0:未送信 1:送信中 2:送信済み")]
	public partial int SendFlg { get; set; }
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	[Comment("商品ユニークキー")]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 入力JANコード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("JANCODE")]
	[Comment("入力JANコード")]
	public partial string JanCode { get; set; } = string.Empty;
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在する色")]
	[Comment("色")]
	public partial long Id_Col { get; set; }
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在するサイズ")]
	[Comment("サイズ")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// 数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量")]
	[Comment("数量")]
	public partial int Su { get; set; }
	/// <summary>
	/// 単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("単価")]
	[Comment("単価")]
	public partial int Tanka { get; set; }
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	[Comment("金額")]
	public partial int Kingaku { get; set; }
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代金額")]
	[Comment("上代")]
	public partial int Jodai { get; set; }
	/// <summary>
	/// 下代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代金額")]
	[Comment("下代")]
	public partial int Gedai { get; set; }
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	[Comment("関連No1")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	///	関連No2
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO2")]
	[Comment("関連No2")]
	public partial int RelateNo2 { get; set; }
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("明細メモ")]
	[Comment("明細メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 確定日 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("確定日")]
	[Comment("確定日 yyyyMMdd 8桁の文字列で表現")]
	public partial string KakuteiDay { get; set; } = string.Empty;
	/// <summary>
	/// 実数量
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("実数量")]
	[Comment("実数量")]
	public partial int JitsuSu { get; set; }
	/// <summary>
	/// 入力社員Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("入力社員CD")]
	[ForeignKey(nameof(MasterShain))]
	[Comment("入力社員Id")]
	public partial long Id_Shain { get; set; }
}
