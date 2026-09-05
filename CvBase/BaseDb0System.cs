using CommunityToolkit.Mvvm.ComponentModel;
using CvAsset;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;


/// <summary>
/// マスター：システム管理テーブル(Id 1 の1レコードのみ)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[Comment("マスター：システム管理テーブル 会社名、消費税設定など")]
[OldTableCommentAttr("HC$MASTER_SYSKANRI", "HC$MASTER_SYSTAX を含むシステム設定項目")]
public sealed partial class MasterSysman : BaseDbHasAddress {
	/// <summary>
	/// 自社名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("自社名")]
	[Comment("自社名")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// ホームページ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[OldTableCommentAttr("ホームページ")]
	[Comment("ホームページ")]
	public partial string Hp { get; set; } = string.Empty;
	/// <summary>
	/// 自社締め日 1-31,99
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnShimeBi))]
	[OldTableCommentAttr("自社締日")]
	[Comment("自社締め日 1-28、99")]
	public partial int ShimeBi { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumShime EnShimeBi {
		get => (EnumShime)ShimeBi;
		set => ShimeBi = (int)value;
	}
	/// <summary>
	/// 修正有効日数
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("修正有効日数")]
	[Comment("修正有効日数")]
	public partial int ModifyDaysEx { get; set; }
	/// <summary>
	/// 先付有効日数
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("先付有効日数")]
	[Comment("先付有効日数")]
	public partial int ModifyDaysPre { get; set; }
	/// <summary>
	/// 振込先1
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[OldTableCommentAttr("振込先1")]
	[Comment("振込先1")]
	public partial string BankAccount1 { get; set; } = string.Empty;
	/// <summary>
	/// 振込先2
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[OldTableCommentAttr("振込先2")]
	[Comment("振込先2")]
	public partial string BankAccount2 { get; set; } = string.Empty;
	/// <summary>
	/// 振込先3
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[OldTableCommentAttr("振込先3")]
	[Comment("振込先3")]
	public partial string BankAccount3 { get; set; } = string.Empty;
	/// <summary>
	/// 期首年月日
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("期首年月日")]
	[Comment("期首年月日 yyyyMMdd")]
	public partial string FiscalStartDate { get; set; } = "19010101";
	/// <summary>
	/// 消費税率リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("消費税率リスト")]
	public partial List<MasterSysTax>? Jsub { get; set; }
	[ObservableProperty]
	[ColumnSizeDml(14)]
	[OldTableCommentAttr("事業者登録番号", "T+13桁 select 名称 from HC$master_meisho where 名称区分='IBS' and 名称CD='01'")]
	[Comment("事業者登録番号 T+13桁のインボイス登録番号")]
	public partial string TaxRegistrationNumber { get; set; } = string.Empty;
	/// <summary>
	/// 標準倉庫
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui),tenType:0)]
	[Comment("標準倉庫")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 倉庫データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	[Comment("倉庫データ")]
	public partial CodeNameView VSoko { get; set; } = new();
	/// <summary>
	/// 消費税端数処理 0=四捨五入、1=切上、2=切捨
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(EnumRounding))]
	[Comment("消費税端数処理 0=四捨五入、1=切上、2=切捨")]
	public partial int TaxRounding { get; set; } = 0;
	/// <summary>
	/// 原価方式 0=固定原価、1=最終仕入原価、2=総平均原価（原価4項目 詳細設計 §2.5.7）。
	/// 方式変更は設定値だけを変更し、原価履歴とMasterShohin.TankaGenkaは変更適用月からの再計算が成功するまで自動変更しない
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(EnumCostMethod))]
	[Comment("原価方式 0=固定原価、1=最終仕入原価、2=総平均原価")]
	public partial int CostMethod { get; set; } = 0;
}
/// <summary>
/// 消費税率テーブル(Id 1-3)
/// </summary>
[SubTableDefine]
[Comment("マスター：消費税率サブテーブル(Id 1-3) MasterSysman.Jsub にJSONで格納する")]
public sealed partial class MasterSysTax : ObservableObject {
	[ObservableProperty]
	[OldTableCommentAttr("消費税CD")]
	[Comment("消費税CD 1-3の固定連番（MasterSysman.Jsub 内の識別子）")]
	public partial long Id { get; set; }
	/// <summary>
	/// 消費税率 (%) 例:10
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税率")]
	[Comment("消費税率 (%) 例:10")]
	public partial int TaxRate { get; set; }
	/// <summary>
	/// 新消費税開始日(yyyyMMdd)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("新消費税開始日")]
	[Comment("新消費税開始日(yyyyMMdd)")]
	public partial string DateFrom { get; set; } = "19010101";
	/// <summary>
	/// 新消費税率 (%) 例:10
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("新消費税率")]
	[Comment("新消費税率 (%) 例:10")]
	public partial int TaxNewRate { get; set; }
}
/// <summary>
/// 名称テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, [nameof(Kubun), nameof(Code)])]
[KeyDml("nk2", false, [nameof(Kubun), nameof(Odr), nameof(Code)])]
[Comment("マスター：名称テーブル 汎用 区分+名称コード")]
[OldTableCommentAttr("HC$MASTER_MEISHO")]
public sealed partial class MasterMeisho : BaseDbClass, IBaseCodeName {
	/// <summary>
	/// 区分そのものを定義する行の区分。この区分の行の Name が他行の KubunName の元になる。
	/// </summary>
	[Comment("区分そのものを定義する行の区分")]
	public const string KubunIndex = "IDX";
	/// <summary>ブランド区分</summary>
	[Comment("ブランド区分")]
	public const string KubunBrand = "BRD";
	/// <summary>アイテム区分</summary>
	[Comment("アイテム区分")]
	public const string KubunItem = "ITM";
	/// <summary>カラー区分</summary>
	[Comment("カラー区分")]
	public const string KubunColor = "COL";
	/// <summary>サイズ区分</summary>
	[Comment("サイズ区分")]
	public const string KubunSize = "SIZ";
	/// <summary>セール区分</summary>
	[Comment("セール区分")]
	public const string KubunSale = "SLE";
	/// <summary>調整理由区分(CvDomainLogic.ChoseiRiyu参照)</summary>
	[Comment("調整理由区分")]
	public const string KubunChoseiRiyu = "CHR";
	/// <summary>部門区分</summary>
	[Comment("部門区分")]
	public const string KubunBumon = "BMN";
	/// <summary>展示会区分</summary>
	[Comment("展示会区分")]
	public const string KubunTenji = "TNJ";
	/// <summary>メーカー区分</summary>
	[Comment("メーカー区分")]
	public const string KubunMaker = "MKR";
	/// <summary>シーズン区分</summary>
	[Comment("シーズン区分")]
	public const string KubunSeason = "SZN";
	/// <summary>素材区分</summary>
	[Comment("素材区分")]
	public const string KubunMaterial = "SZI";
	/// <summary>原産国区分</summary>
	[Comment("原産国区分")]
	public const string KubunCountry = "GEN";
	/// <summary>区分(生地/付属等)</summary>
	[Comment("区分(生地/付属等)")]
	public const string KubunKiji = "KIJ";
	/// <summary>入金/支払方法区分</summary>
	[Comment("入金/支払方法区分")]
	public const string KubunKin = "KIN";
	/// <summary>商品マスター Jsub(名称リスト)の区分先頭文字(Kb='B01'～'B10')</summary>
	[Comment("商品マスター 名称区分先頭文字")]
	public const char KubunTopShohin = 'B';
	/// <summary>得意先マスター Jsub(名称リスト)の区分先頭文字(Kb='C01'～'C10')</summary>
	[Comment("得意先マスター 名称区分先頭文字")]
	public const char KubunTopTokui = 'C';
	/// <summary>仕入先マスター Jsub(名称リスト)の区分先頭文字(Kb='D01'～'D10')</summary>
	[Comment("仕入先マスター 名称区分先頭文字")]
	public const char KubunTopShiire = 'D';
	/// <summary>社員マスター Jsub(名称リスト)の区分先頭文字(Kb='E01'～'E05')</summary>
	[Comment("社員マスター 名称区分先頭文字")]
	public const char KubunTopShain = 'E';
	/// <summary>エンドカスタマーマスター Jsub(名称リスト)の区分先頭文字(Kb='K01'～'K10')</summary>
	[Comment("エンドカスタマーマスター 名称区分先頭文字")]
	public const char KubunTopEndCustomer = 'K';
	/// <summary>
	/// 区分
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("名称区分")]
	[Comment("区分")]
	public partial string Kubun { get; set; } = string.Empty;
	/// <summary>
	/// 区分名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(40)]
	[Comment("区分名")]
	public partial string KubunName { get; set; } = string.Empty;
	/// <summary>
	/// 名称コード
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("名称CD")]
	[Comment("名称コード")]
	public partial string Code { get; set; } = "";
	/// <summary>
	/// 名称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("名称")]
	[Comment("名称")]
	public partial string Name { get; set; } = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("略称")]
	[Comment("略称")]
	public partial string Ryaku { get; set; } = string.Empty;
	/// <summary>
	/// よみがな
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("カナ")]
	[Comment("よみがな")]
	public partial string Kana { get; set; } = string.Empty;
	/// <summary>
	/// 並び順
	/// </summary>
	[ObservableProperty]
	[Comment("並び順")]
	public partial int Odr { get; set; }
	/// <summary>
	/// 初期データの作成
	/// </summary>
	/// <param name="db"></param>
	/// <returns></returns>
	public static List<MasterMeisho> CreateDefaultData(ExDatabase db) {
		var initData = new List<MasterMeisho>() {
			new MasterMeisho { Kubun = KubunIndex, KubunName = "名称区分", Code = "IDX", Name = "名称区分インデックス", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = KubunIndex, KubunName = "名称区分", Code = "BRD", Name = "ブランド", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = KubunIndex, KubunName = "名称区分", Code = "ITM", Name = "アイテム", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = KubunIndex, KubunName = "名称区分", Code = "COL", Name = "カラー", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = KubunIndex, KubunName = "名称区分", Code = "SIZ", Name = "サイズ", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = KubunIndex, KubunName = "名称区分", Code = "SLE", Name = "セール", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = KubunIndex, KubunName = "名称区分", Code = "CHR", Name = "調整理由", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "BRD", KubunName = "ブランド", Code = "01", Name = "NewBrand", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "ITM", KubunName = "アイテム", Code = "01", Name = "NewItem", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "COL", KubunName = "カラー", Code = "01", Name = "NewColor", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "SIZ", KubunName = "サイズ", Code = "01", Name = "NewSize", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "SLE", KubunName = "セール", Code = "0001", Name = "セール", Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			// 調整理由（在庫強制調整）。コード10〜19=加算(+)/20〜29=減算(−)。ChoseiRiyu.CalcFlag を参照
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "10", Name = "入庫", Odr = 10, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "20", Name = "紛失", Odr = 20, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "21", Name = "盗難", Odr = 21, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "22", Name = "破損", Odr = 22, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "23", Name = "検品ミス", Odr = 23, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
			new MasterMeisho { Kubun = "CHR", KubunName = "調整理由", Code = "29", Name = "その他", Odr = 29, Vdc = Common.GetVdate(), Vdu = Common.GetVdate() },
		};
		var tableCnt = db.GetTableCounts(nameof(MasterMeisho));
		if (tableCnt?.FirstOrDefault()?.Item3 == 0) {
			db.InsertBulk<MasterMeisho>(initData);
			return initData;
		}
		return [];
	}
}
