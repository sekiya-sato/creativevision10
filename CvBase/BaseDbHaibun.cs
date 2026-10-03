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
	/// 修正（洗い替えでの削除）できる行の条件。エイリアスなしの列名で書く。
	/// <para>
	/// 配分入力画面が修正対象を読み込む条件と、サーバが保存時に強制する条件
	/// （<c>AllocationRules.IsEditable</c>）を一致させるため、両方がこの定数を基準にする。
	/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step1_共通基盤・確定一本化_詳細設計.md` 2章を参照する。
	/// </para>
	/// </summary>
	public const string EditableWhereSql = "SendFlg = 0 AND EndFlag = 0 AND ifnull(KakuteiDay,'') = ''";
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
	/// 物流システムへの連携状態。**確定済みかどうかは <see cref="EndFlag"/>（と <see cref="KakuteiDay"/>）で判定する**。
	/// 修正可能なのは <see cref="EditableWhereSql"/>（`SendFlg = 0` かつ未完了かつ `KakuteiDay` が空）の行だけで、
	/// 配分保存（<c>HaibunSaveParam</c>）はサーバ側でこの条件を強制する。
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
	/// 受注配分なら <see cref="Tran12Jyuchu"/>.Id、仕入配分なら <see cref="Tran13Hachu"/>.Id。
	/// 在庫からの配分（在庫配分・取置配分）は元伝票が無いので 0。
	/// システム全体で一貫した「RelateNo1 = 元伝票Id」規約
	/// （Tran03Shiire←発注 / Tran00Uriage←受注 / Tran11IdoIn←積送出庫）に揃えている。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	[Comment("関連No1 = 元伝票のId（配分の入力元）。 受注配分なら Tran12Jyuchu.Id、仕入配分なら Tran13Hachu.Id。 在庫からの配分（在庫配分・取置配分）は元伝票が無いので 0。")]
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
	/// <para>
	/// 決定 D8 により確定と伝票作成は同時なので、確定日は生成した伝票の伝票日と同じになる。
	/// 旧2段階方式で「確定済み・未出荷」（確定日あり・<see cref="EndFlag"/>=0）のまま残った行は、
	/// 配分確定で未確定と同じに扱い、確定日を上書きする。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("確定日")]
	[Comment("確定日 yyyyMMdd 8桁の文字列で表現。空文字なら未確定。")]
	public partial string KakuteiDay { get; set; } = string.Empty;
	/// <summary>
	/// 実数量（配分確定で入力した確定数＝実際に出荷・移動した数）。未確定のうちは 0。
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("実数量")]
	[Comment("実数量（確定時に実際に出荷・移動した数）。未確定のうちは 0。")]
	public partial int JitsuSu { get; set; }
	/// <summary>
	/// 欠品数（指示に対して倉庫が出荷できなかった数）。未確定のうちは 0。
	/// <para>
	/// <see cref="Su"/>（指示数）はユーザーが配分入力で設定する。配分確定で確定数を入れると
	/// <see cref="JitsuSu"/>（出荷数）と本列が設定され、<c>Su = JitsuSu + ShortSu</c> が成立し、同時に伝票作成・完了（<see cref="EndFlag"/>=1）となる。
	/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step1_共通基盤・確定一本化_詳細設計.md` 2章を参照する
	/// （旧仕様は `Doc/spec/archive/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 5.1.2）。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("欠品数量")]
	[Comment("欠品数（指示に対して倉庫が出荷できなかった数）。未確定のうちは 0。倉庫から戻されるデータで JitsuSu とともに設定され Su = JitsuSu + ShortSu が成立する。")]
	public partial int ShortSu { get; set; }
	/// <summary>
	/// 入荷済み数（仕入配分＝区分 <see cref="EnumHaibun.Hatsukai"/>(0) だけが使う）。0〜<see cref="Su"/>。
	/// <para>
	/// 発注(<see cref="RelateNo1"/>)×SKU の仕入数から確定済みの配分ぶんを引いた残りを、未完了の仕入配分へ
	/// 配分先の店舗コード順に割り当てた値（<c>ArrivalDb.Recalc</c>）。区分0はこの数だけが引当・確定の対象になる。
	/// 仕入・配分保存・配分確定・全件再集計のたびに再計算する。ほかの区分では使わない（0 のまま）。
	/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step4_仕入配分入力_詳細設計.md` 3章を参照する。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[Comment("入荷済み数（仕入配分=区分0だけが使う）。発注×SKUの仕入数から確定済み分を引いた残りを店舗コード順に割り当てた値で、区分0はこの数だけが引当・確定の対象。")]
	public partial int ArrivedSu { get; set; }
	/// <summary>
	/// 取置の顧客（<see cref="MasterEndCustomer"/>.Id）。取置配分＝区分 <see cref="EnumHaibun.Reservation"/>(6) だけが使い、ほかは 0。
	/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step5_取置配分入力_詳細設計.md` 3章を参照する。
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterEndCustomer))]
	[Comment("取置の顧客Id（MasterEndCustomer.Id）。取置配分(区分6)だけが使い、ほかは0。")]
	public partial long Id_Customer { get; set; }
	/// <summary>
	/// 取置の期限日 yyyyMMdd。取置配分(区分6)だけが使い、ほかは空文字。
	/// 期限日当日までは有効で、翌日に日次タスクが自動取消する（<see cref="EnumHaibunEndReason.Expired"/>）。
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[Comment("取置の期限日 yyyyMMdd。取置配分(区分6)だけが使う。期限日の翌日に自動取消される。")]
	public partial string LimitDay { get; set; } = string.Empty;
	/// <summary>
	/// 完了の理由（<see cref="EnumHaibunEndReason"/>）。未完了と、配分確定で完了した行は 0。
	/// 取置配分(区分6)だけが 1〜3 を使う。
	/// </summary>
	[ObservableProperty]
	[Comment("完了の理由。0=通常(未完了・配分確定) 1=売上変換 2=取消 3=期限切れ。区分6(取置)だけが1〜3を使う。")]
	public partial int EndReason { get; set; }
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
	/// この値が0の行が <see cref="SummaryStock.ReserveQty"/> / <see cref="SummaryRealStock.ReserveQty"/>（引当数）へ集計される。
	/// 数量は、仕入配分(<see cref="EnumHaibun.Hatsukai"/>=0)は入荷済み数 <see cref="ArrivedSu"/>、それ以外は <see cref="Su"/>
	/// （配分再設計 Step 4。以前は区分0を引当対象外としていた）。
	/// 追加・修正・削除、およびこの列の部分更新のたびに、対象の倉庫+SKU の引当数が引き直される。
	/// <see cref="KakuteiDay"/>（配分確定）と <see cref="SendFlg"/>（物流連携）は引当の判定に使わない。
	/// 仕様は `Doc/spec/archive/2026-08-17_旧cvnet比較_仕様決定判断材料.md` 5.2 を参照する。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnEndFlag))]
	[Comment("完了FLG。0=未完了（引当中） / 1=完了（引当解除）。この値が0の行が引当数へ集計される。数量は仕入配分(Kubun=0)は入荷済み数ArrivedSu、それ以外はSu。")]
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
/// 値は既存データの区分なので<b>変更しないこと</b>。画面・帳票の名前は <see cref="HaibunKubunNames"/> を使う。
/// 設計の背景は `.omo/2026-07-31_haibun_design.md` を参照。
/// </para>
/// <para>
/// 引当対象は未完了の全区分。仕入配分(<see cref="Hatsukai"/>=0)は入荷前の振り分けなので、入荷済み数
/// （<see cref="TranHaibun.ArrivedSu"/>）の分だけを引当に積む。区分の整理（0/1/2/6 を使い、3/4/5/7 は廃止）は
/// `Doc/spec/2026-10-03_配分再設計_基本設計.md` 4.2、入荷割当は同 Step4 詳細設計を参照する。
/// </para>
/// </summary>
public enum EnumHaibun : int {
	/// <summary>仕入配分（旧 初回配分。発注の入荷予定を店舗へ振り分ける）。RelateNo1 = 発注Id（必須）。入荷済み数だけが引当対象</summary>
	[Comment("仕入配分（入荷済み数だけが引当対象）")]
	Hatsukai = 0,
	/// <summary>在庫配分（倉庫の現在庫を店舗へ振り分ける）。RelateNo1 = 0</summary>
	[Comment("在庫配分")]
	Zaiko = 1,
	/// <summary>受注配分（得意先の受注に対して在庫を割り当てる）。RelateNo1 = 受注Id</summary>
	[Comment("受注配分")]
	Juchu = 2,
	/// <summary>得意先別配分（得意先を軸に商品を振り分ける）。RelateNo1 = 0 または 受注Id</summary>
	[Comment("得意先別配分（廃止。新規作成不可）")]
	[Obsolete(HaibunKubunNames.ObsoleteMessage)]
	Tokui = 3,
	/// <summary>店舗出荷依頼（店舗側から本部倉庫へ出荷を依頼する）。RelateNo1 = 0</summary>
	[Comment("店舗出荷依頼（廃止。新規作成不可）")]
	[Obsolete(HaibunKubunNames.ObsoleteMessage)]
	ShopRequest = 4,
	/// <summary>在庫品配分（滞留在庫などを対象に配分する）。RelateNo1 = 0</summary>
	[Comment("在庫品配分（廃止。新規作成不可）")]
	[Obsolete(HaibunKubunNames.ObsoleteMessage)]
	ZaikoHin = 5,
	/// <summary>
	/// 取置配分（店舗が一般顧客向けに店舗在庫を確保する）。RelateNo1 = 0、Id_Soko = Id_Tenpo、顧客と期限日が必須。
	/// 配分確定の対象外で、店舗売上への変換・取消・期限切れで完了する（Step 5）
	/// </summary>
	[Comment("取置配分")]
	Reservation = 6,
	/// <summary>移動指示（倉庫間の移動を指示する）。RelateNo1 = 0</summary>
	[Comment("移動指示（廃止。新規作成不可）")]
	[Obsolete(HaibunKubunNames.ObsoleteMessage)]
	IdoShiji = 7,
}

/// <summary>
/// 配分区分の表示名の唯一の出典。画面の選択肢・帳票の SQL（CASE 式）はここから作る。
/// 仕様は `Doc/spec/2026-10-03_配分再設計_Step6_メニュー整理・旧画面削除_詳細設計.md` 6.1。
/// </summary>
public static class HaibunKubunNames {
	/// <summary>廃止区分に付ける <see cref="ObsoleteAttribute"/> の文言</summary>
	public const string ObsoleteMessage = "配分再設計で廃止した区分。新規作成は不可で、既存データの表示のためだけに残す";

	/// <summary>
	/// 区分 → 表示名。廃止区分は既存データが残っていたときの表示のためだけに持つ。
	/// </summary>
	public static readonly IReadOnlyDictionary<int, string> All = new Dictionary<int, string> {
		[0] = "仕入配分",
		[1] = "在庫配分",
		[2] = "受注配分",
		[3] = "得意先別配分(廃止)",
		[4] = "店舗出荷依頼(廃止)",
		[5] = "在庫品配分(廃止)",
		[6] = "取置配分",
		[7] = "移動指示(廃止)",
	};

	/// <summary>倉庫から出荷・移動する配分の区分（出荷系の帳票の絞込に出す。取置と廃止区分は含めない）</summary>
	public static readonly IReadOnlyList<int> ShippingKubun = [0, 1, 2];

	/// <summary>区分の表示名。未定義の値は数字のまま</summary>
	public static string Name(int kubun) => All.TryGetValue(kubun, out var name) ? name : kubun.ToString(System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>
	/// SQL で区分名を出す CASE 式（SQLite 正典。どの方言でもそのまま動く標準 SQL）。
	/// </summary>
	/// <param name="column">区分の列（例: <c>h.Kubun</c>）</param>
	public static string CaseSql(string column) =>
		$"CASE {column} {string.Join(" ", All.Select(kv => $"WHEN {kv.Key} THEN '{kv.Value}'"))} ELSE cast({column} as text) END";
}

/// <summary>
/// 配分の完了の理由（<see cref="TranHaibun.EndReason"/>）。取置配分(区分6)の終わり方を区別する（決定 D5）。
/// </summary>
public enum EnumHaibunEndReason : int {
	/// <summary>未完了、または配分確定で完了した</summary>
	[Comment("通常")]
	Normal = 0,
	/// <summary>取置を店舗売上へ変換した（<see cref="TranHaibun.RelateNo2"/> = <see cref="Tran01Tenuri"/>.Id）</summary>
	[Comment("売上変換")]
	Converted = 1,
	/// <summary>取置を取り消した</summary>
	[Comment("取消")]
	Cancelled = 2,
	/// <summary>期限切れで自動取消した</summary>
	[Comment("期限切れ")]
	Expired = 3,
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
/// <b>キーを削ってはいけない。</b> <see cref="Kubun"/> は引当数量の判定（区分0は入荷済み数）と伝票の紐付けに使い、
/// <see cref="RelateNo1"/> は元伝票（受注・発注）の特定と受注残の自動完了判定に使う。
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
