using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;

public interface ITranDetail {
	public string DenDay { get; set; }
	public long Id_Soko { get; set; }
	public int CalcFlag { get; }
	public List<Tran99Meisai>? Jmeisai { get; set; }
}
public interface ITranIdo {
	public long Id { get; set; }
	public string DenDay { get; set; }
	public long Id_Ido { get; set; }
	public int CalcFlag { get; }
}
public interface ITranSoko {
	public long Id { get; set; }
	public string DenDay { get; set; }
	public long Id_Soko { get; set; }
	public int CalcFlag { get; }
}

/// <summary>
/// Tran系ファイルの出庫・入庫の区分、売上・仕入の区分などの共通的なコードを定義するクラス
/// </summary>
public class TranCalcBase {
	/// <summary>
	/// 在庫、入庫、出庫、移動中のフラグを取得する
	/// </summary>
	/// <param name="tableName"></param>
	/// <returns>在庫、入庫、出庫、移動中のフラグ</returns>
	public static Tuple<int, int, int, int> GetCalcSoko(string tableName, bool invertFlag = false) {
		var ret = new Tuple<int, int, int, int>(0, 0, 0, 0);
		if (tableName == nameof(Tran00Uriage)) {
			ret = new Tuple<int, int, int, int>(-1, 0, 1, 0);
		}
		else if (tableName == nameof(Tran01Tenuri)) {
			ret = new Tuple<int, int, int, int>(-1, 0, 1, 0);
		}
		else if (tableName == nameof(Tran03Shiire)) {
			ret = new Tuple<int, int, int, int>(1, 1, 0, 0);
		}
		else if (tableName == nameof(Tran05Ido)) {
			ret = new Tuple<int, int, int, int>(-1, 0, 1, 0);
		}
		else if (tableName == nameof(Tran10IdoOut)) {
			ret = new Tuple<int, int, int, int>(-1, 0, 1, 0);
		}
		else if (tableName == nameof(Tran11IdoIn)) {
			ret = new Tuple<int, int, int, int>(0, 0, 0, 0);
		}
		// Tran12Jyuchu Tran13Hachu Tran60Tana
		if (invertFlag) {
			var inverted = new Tuple<int, int, int, int>(
				ret.Item1 * -1,
				ret.Item2 * -1,
				ret.Item3 * -1,
				ret.Item4 * -1
			);
			ret = inverted;
		}
		return ret;
	}

	/// <summary>
	/// 移動先基準で在庫計算のためのフラグを取得する。移動中は移動先の在庫に予定として割り当てる
	/// </summary>
	/// <param name="tableName"></param>
	/// <returns>在庫、入庫、出庫、移動中のフラグ</returns>
	public static Tuple<int, int, int, int> GetCalcIdosaki(string tableName, bool invertFlag = false) {
		var ret = new Tuple<int, int, int, int>(0, 0, 0, 0);
		if (tableName == nameof(Tran05Ido)) {
			ret = new Tuple<int, int, int, int>(1, 1, 0, 0);
		}
		else if (tableName == nameof(Tran10IdoOut)) {
			ret = new Tuple<int, int, int, int>(0, 0, 0, 1);
		}
		else if (tableName == nameof(Tran11IdoIn)) {
			ret = new Tuple<int, int, int, int>(1, 1, 0, -1);
		}
		if (invertFlag) {
			var inverted = new Tuple<int, int, int, int>(
				ret.Item1 * -1,
				ret.Item2 * -1,
				ret.Item3 * -1,
				ret.Item4 * -1
			);
			ret = inverted;
		}
		return ret;
	}

}

/// <summary>
/// 共通トランザクション（ヘッダ）
/// <para>
/// V*列（VShain/VSoko など CodeNameView 型の列）は<b>伝票作成時点のマスタ名称を保持する監査値</b>であり、
/// マスタが改名されても伝播しない（意図的な仕様）。現行名称が必要な場合は Id_* から参照先マスタをJOINすること。
/// </para>
/// <para>
/// Master系のV*列は逆に<b>常に現行名称へ同期される</b>（CvDomainLogic/MasterCascadeDb がマスタ更新時に伝播）。
/// つまり「V*列が時点値か現行値か」はテーブル種別（Tran系/Master系）だけで判別できる。
/// 詳細は .omo/20260727_master_vcolumn_sync_design.md を参照。
/// </para>
/// </summary>
public partial class TranAllHeader : BaseDbClass, ITranDetail {
	/// <summary>
	/// 計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("在庫計上日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShain))]
	[OldTableCommentAttr("入力社員CD")]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 社員データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VShain { get; set; } = new();
	/// <summary>
	/// 倉庫キー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 0)]
	[OldTableCommentAttr("倉庫CD")]
	public partial long Id_Soko { get; set; }
	/// <summary>
	/// 倉庫データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VSoko { get; set; } = new();
	/// <summary>
	/// 計算フラグ（1:+ -1:-, 0:計算除外 集計処理で返品を考慮するために使用）
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引区分", "getCalcFlag により算出")]
	public partial int CalcFlag { get; protected set; } = 1;
	/// <summary>
	/// 数量合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("数量合計")]
	public partial int SuTotal { get; set; }
	/// <summary>
	/// 金額合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細金額合計")]
	public partial int KingakuTotal { get; set; }
	/// <summary>
	/// 上代合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代合計")]
	public partial int JodaiTotal { get; set; }
	/// <summary>
	/// 下代合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("下代合計")]
	public partial int GedaiTotal { get; set; }
	/// <summary>
	/// 値引: 合計からの
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("値引1 + 値引2 + 値引3")]
	public partial int Nebiki00Total { get; set; }
	/// <summary>
	/// 値引: 明細積上げ
	/// </summary>
	[ObservableProperty]
	public partial int Nebiki01Meisai { get; set; }
	/// <summary>
	/// ヘッダメモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]

	[OldTableCommentAttr("メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	public partial BaseDetailClass? Jdetail { get; set; }
	/// <summary>
	/// 明細リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(4000)]
	public partial List<Tran99Meisai>? Jmeisai { get; set; }
}

/// <summary>
/// 共通トランザクション（明細）
/// </summary>
[OldTableCommentAttr("HC$tran_tori1", "Tran60Tana は HC$tran_tana1")]
public sealed partial class Tran99Meisai : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("行NO")]
	public partial int No { get; set; }
	/// <summary>
	/// 区分（Max2桁 0:Pプロパー 1:Sセール 2:社販）
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細取引区分")]
	public partial int Kubun { get; set; } = 0;
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 商品CD
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]

	[OldTableCommentAttr("商品CD")]
	public partial string Code_Shohin { get; set; } = string.Empty;
	/// <summary>
	/// 商品名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	[OldTableCommentAttr("明細名称")]
	public partial string Mei_Shohin { get; set; } = string.Empty;
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
	/// カラーCD
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("色CD")]
	public partial string Code_Col { get; set; } = string.Empty;
	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	public partial string Mei_Col { get; set; } = string.Empty;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(DerivedShohinColSiz), additionalInfo: $"{nameof(DerivedShohinColSiz)}に存在するサイズ")]
	public partial long Id_Siz { get; set; }
	/// <summary>
	/// サイズCD
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("サイズCD")]
	public partial string Code_Siz { get; set; } = string.Empty;
	/// <summary>
	/// サイズ名
	/// </summary>
	[ObservableProperty]
	public partial string Mei_Siz { get; set; } = string.Empty;
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
	/// 値引: 合計からの
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細値引")]
	public partial int Nebiki00 { get; set; }
	/// <summary>
	/// 値引: 明細1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細値引1")]
	public partial int Nebiki01 { get; set; }
	/// <summary>
	/// 値引: 明細2
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("小計値引 + 小計値引1")]
	public partial int Nebiki02 { get; set; }
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShain))]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 社員CD
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Code_Shain { get; set; } = string.Empty;
	/// <summary>
	/// 社員名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial string Mei_Shain { get; set; } = string.Empty;
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("明細メモ")]
	public partial string Memo { get; set; } = string.Empty;
}

/// <summary>
/// 共通トランザクション（入金/支払ヘッダ）
/// </summary>
public partial class TranKinHeader : BaseDbClass {
	/// <summary>
	/// 計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("在庫計上日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 社員ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShain))]
	[OldTableCommentAttr("入力社員CD")]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 社員データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VShain { get; set; } = new();
	/// <summary>
	/// 取引先キー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), additionalInfo: $"入金は{nameof(MasterTokui)}, 支払は{nameof(MasterShiire)}")]
	[OldTableCommentAttr("取引先CD1  入金であればMasterTokui 支払であればMasterShiire")]
	public partial long Id_Torisaki { get; set; }
	/// <summary>
	/// 取引先データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VTori { get; set; } = new();
	/// <summary>
	/// 金額合計
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("明細金額合計")]
	public partial int KingakuTotal { get; set; }
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("手入力伝票NO")]
	public partial string ManualNo { get; set; } = string.Empty;
	/// <summary>
	/// ヘッダメモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("メモ")]
	public partial string Memo { get; set; } = string.Empty;
	/// <summary>
	/// 明細リスト
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(4000)]
	public partial List<TranKinMeisai>? Jmeisai { get; set; }
}

/// <summary>
/// 入金・支払トランザクション（明細）
/// </summary>
[OldTableCommentAttr("HC$tran_tori1")]
public sealed partial class TranKinMeisai : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("行NO")]
	public partial int No { get; set; }
	/// <summary>
	/// 区分ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "KIN")]
	public partial long Id_Kin { get; set; }
	/// <summary>
	/// 入金・支払CD
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]

	[OldTableCommentAttr("明細取引区分")]
	public partial string Code_Kin { get; set; } = string.Empty;
	/// <summary>
	/// 品名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial string Mei_Kin { get; set; } = string.Empty;
	/// <summary>
	/// 金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	public partial int Kingaku { get; set; }
	/// <summary>
	/// 明細メモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("明細メモ")]
	public partial string Memo { get; set; } = string.Empty;
}

/// <summary>
/// 入金 06 (取引先 売掛-)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, [nameof(Id_Torisaki)])]
[Comment("トランザクション：入金データ 売掛に対する入金")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran06Nyukin : TranKinHeader {
}
/// <summary>
/// 支払 07 (取引先 買掛-)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, [nameof(Id_Torisaki)])]
[Comment("トランザクション：支払データ 買掛に対する支払")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran07Shiharai : TranKinHeader {
}

/// <summary>
/// 棚卸 60 (倉庫 現在値)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, [nameof(Id_Soko)])]
[Comment("トランザクション：棚卸データ 月末あるいは特定日の倉庫現在値")]
[OldTableCommentAttr("HC$tran_tana0")]
public sealed partial class Tran60Tana : TranAllHeader {
	/// <summary>
	/// 棚番
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string TanaNo { get; set; } = string.Empty;
}

/// <summary>
/// 本部売上 00 (倉庫 出)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, nameof(KakeDay))]
[KeyDml("nk3", false, [nameof(Id_Soko)])]
[KeyDml("nk4", false, [nameof(Id_Tokui)])]
[Comment("トランザクション：本部売上データ 得意先に対する売掛計上と倉庫からの出庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran00Uriage : TranAllHeader, ITranSoko {
	/// <summary>
	/// 掛計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("掛計上日")]
	public partial string KakeDay { get; set; } = "19010101";
	/// <summary>
	/// 得意先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	[ForeignKey(nameof(MasterTokui), tenType: 1)]
	public partial long Id_Tokui { get; set; }
	/// <summary>
	/// 得意先データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VTokui { get; set; } = new();
	/// <summary>
	/// 請求フラグ
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsPay))]
	[OldTableCommentAttr("掛計上FLG")]
	public partial int IsPay { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsPay {
		get => (EnumYesNo)IsPay;
		set => IsPay = (int)value;
	}
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	public partial int Kubun { get; set; } = 10;
	partial void OnKubunChanged(int value) {
		// Kubun が変更された後に実行される
		CalcFlag = (value >= 20 && value <= 39) ? -1 : 1;
	}

	[Ignore]
	[JsonIgnore]
	public EnumUri00 EnKubun {
		get => (EnumUri00)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("手入力伝票NO")]
	public partial string ManualNo { get; set; } = string.Empty;
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
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	public partial int Rate { get; set; }
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	public partial int Tax { get; set; }
	/// <summary>
	/// 総合計
	/// </summary>
	[ObservableProperty]
	public partial int Total { get; set; }
	/// <summary>
	/// 納品書発行済FLG。納品書印刷で立て、納品書未発行チェックリストで 0 を抽出する。
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsPrint))]
	public partial int IsPrint { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsPrint {
		get => (EnumYesNo)IsPrint;
		set => IsPrint = (int)value;
	}
}

public enum EnumUri00 : int {
	Uriage = 10,
	UriSale = 11,
	Henpin = 20,
	HenSale = 21,
	Nebiki = 30,
	Other = 99
}


/// <summary>
/// 店舗売上 01 (倉庫 出)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, [nameof(Id_Soko)])]
[KeyDml("nk3", false, [nameof(Id_Tenpo)])]
[KeyDml("nk4", false, nameof(Id_Customer))]
[Comment("トランザクション：店舗売上データ 店舗に対する売上と店舗(倉庫)からの出庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran01Tenuri : TranAllHeader, ITranSoko {
	[ObservableProperty]
	[ColumnSizeDml(36)]
	public partial string PosClientSaleId { get; set; } = string.Empty;
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(1000)]
	public partial PosPaymentDetail? JposPayment { get; set; }
	/// <summary>
	/// 店舗キー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 6)]
	[OldTableCommentAttr("取引先CD1")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VTenpo { get; set; } = new();
	/// <summary>
	/// 顧客キー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterEndCustomer))]
	[OldTableCommentAttr("顧客TEL")]
	public partial long Id_Customer { get; set; }
	/// <summary>
	/// 顧客データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VCustomer { get; set; } = new();
	/// <summary>
	/// オフライン用顧客CD
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("顧客TEL")]
	public partial string Code_Customer { get; set; } = string.Empty;
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	public partial int Kubun { get; set; } = 10;
	partial void OnKubunChanged(int value) {
		// Kubun が変更された後に実行される
		CalcFlag = (value >= 20 && value <= 39) ? -1 : 1;
	}

	[Ignore]
	[JsonIgnore]
	public EnumUri01 EnKubun {
		get => (EnumUri01)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }

	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	public partial int Rate { get; set; }
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	public partial int Tax { get; set; }
	/// <summary>
	/// 総合計
	/// </summary>
	[ObservableProperty]
	public partial int Total { get; set; }
}

/// <summary>POS会計で受領した金種内訳</summary>
public sealed class PosPaymentDetail {
	public int CashAmount { get; init; }
	public int CardAmount { get; init; }
	public int OtherAmount { get; init; }
	public int ChangeAmount { get; init; }
}
public enum EnumUri01 : int {
	Uriage = 10,
	UriSale = 11,
	Henpin = 20,
	HenSale = 21,
	Other = 99
}

/// <summary>
/// 仕入 03 (倉庫 入)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, nameof(KakeDay))]
[KeyDml("nk3", false, [nameof(Id_Soko)])]
[KeyDml("nk4", false, [nameof(Id_Shiire)])]
[Comment("トランザクション：仕入データ 仕入先に対する買掛計上と倉庫への入庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran03Shiire : TranAllHeader, ITranSoko {
	/// <summary>
	/// 掛計上日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("掛計上日")]
	public partial string KakeDay { get; set; } = "19010101";
	/// <summary>
	/// 仕入先キー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShiire))]
	[OldTableCommentAttr("取引先CD1")]
	public partial long Id_Shiire { get; set; }
	/// <summary>
	/// 仕入先データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VShiire { get; set; } = new();
	/// <summary>
	/// 支払フラグ
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsPay))]
	[OldTableCommentAttr("掛計上FLG")]
	public partial int IsPay { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsPay {
		get => (EnumYesNo)IsPay;
		set => IsPay = (int)value;
	}
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	public partial int Kubun { get; set; } = 10;
	partial void OnKubunChanged(int value) {
		// Kubun が変更された後に実行される
		CalcFlag = (value >= 20 && value <= 39) ? -1 : 1;
	}
	[Ignore]
	[JsonIgnore]
	public EnumShiire EnKubun {
		get => (EnumShiire)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("手入力伝票NO")]
	public partial string ManualNo { get; set; } = string.Empty;
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	public partial int Rate { get; set; }
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	public partial int Tax { get; set; }
	/// <summary>
	/// 総合計
	/// </summary>
	[ObservableProperty]
	public partial int Total { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnIsPrint))]
	public partial int IsPrint { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnIsPrint {
		get => (EnumYesNo)IsPrint;
		set => IsPrint = (int)value;
	}
}
public enum EnumShiire : int {
	Shiire = 10,
	Henpin = 20,
	Nebiki = 30,
	Other = 99
}


/// <summary>
/// 移動 05 (倉庫 出, 移動先 入)
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, [nameof(Id_Soko)])]
[KeyDml("nk3", false, [nameof(Id_Ido)])]
[Comment("トランザクション：移動データ(即時) 倉庫からの出庫と移動先への入庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran05Ido : TranAllHeader, ITranIdo, ITranSoko {
	/// <summary>
	/// 移動先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	[ForeignKey(nameof(MasterTokui))]
	public partial long Id_Ido { get; set; }
	/// <summary>
	/// 移動先データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VIdo { get; set; } = new();
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]

	[OldTableCommentAttr("手入力伝票NO")]
	public partial string ManualNo { get; set; } = string.Empty;
}

/// <summary>
/// 積送移動 10 (倉庫 出, 移動先 入) 仮
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, [nameof(Id_Soko)])]
[KeyDml("nk3", false, [nameof(Id_Ido)])]
[Comment("トランザクション：移動データ(積送出庫) 倉庫からの出庫、積送中在庫へ(移動先への入庫予定)")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran10IdoOut : TranAllHeader, ITranIdo, ITranSoko {
	/// <summary>
	/// 移動先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	[ForeignKey(nameof(MasterTokui))]
	public partial long Id_Ido { get; set; }
	/// <summary>
	/// 移動先データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VIdo { get; set; } = new();
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]

	[OldTableCommentAttr("手入力伝票NO")]
	public partial string ManualNo { get; set; } = string.Empty;
}
/// <summary>
/// 積送移動 11 (倉庫 出, 移動先 入) 実
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, [nameof(Id_Soko)])]
[KeyDml("nk3", false, [nameof(Id_Ido)])]
[Comment("トランザクション：移動データ(積送入庫) 積送中在庫(倉庫からの出庫)から移動先への入庫")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran11IdoIn : TranAllHeader, ITranIdo, ITranSoko {
	/// <summary>
	/// 移動先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	[ForeignKey(nameof(MasterTokui))]
	public partial long Id_Ido { get; set; }
	/// <summary>
	/// 移動先データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VIdo { get; set; } = new();
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	/// 手入力No
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("手入力伝票NO")]
	public partial string ManualNo { get; set; } = string.Empty;
}
/// <summary>
/// 受注 12
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk3", false, [nameof(Id_Soko)])]
[KeyDml("nk4", false, [nameof(Id_Tokui)])]
[Comment("トランザクション：受注データ 得意先に対する受注、本部売上になる場合は、本部売上データのRelateNo1に受注データのIdをセット")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran12Jyuchu : TranAllHeader {
	/// <summary>
	/// 得意先キー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 1)]
	[OldTableCommentAttr("取引先CD1")]
	public partial long Id_Tokui { get; set; }
	/// <summary>
	/// 得意先データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VTokui { get; set; } = new();
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	public partial int Kubun { get; set; } = 10;
	partial void OnKubunChanged(int value) {
		// Kubun が変更された後に実行される
		CalcFlag = (value >= 20 && value <= 39) ? -1 : 1;
	}
	[Ignore]
	[JsonIgnore]
	public EnumUri01 EnKubun {
		get => (EnumUri01)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	public partial int Rate { get; set; }
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	public partial int Tax { get; set; }
	/// <summary>
	/// 総合計
	/// </summary>
	[ObservableProperty]
	public partial int Total { get; set; }
}

/// <summary>
/// 発注 13
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk3", false, [nameof(Id_Soko)])]
[KeyDml("nk4", false, [nameof(Id_Shiire)])]
[Comment("トランザクション：発注データ 仕入先に対する発注、仕入になる場合は、仕入データのRelateNo1に発注データのIdをセット")]
[OldTableCommentAttr("HC$tran_tori0")]
public sealed partial class Tran13Hachu : TranAllHeader {
	/// <summary>
	/// 仕入先キー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("取引先CD1")]
	[ForeignKey(nameof(MasterShiire))]
	public partial long Id_Shiire { get; set; }
	/// <summary>
	/// 仕入先データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VShiire { get; set; } = new();
	/// <summary>
	/// 区分（2桁 10-19,20-29,30,99）
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("取引区分")]
	public partial int Kubun { get; set; } = 10;
	partial void OnKubunChanged(int value) {
		// Kubun が変更された後に実行される
		CalcFlag = (value >= 20 && value <= 39) ? -1 : 1;
	}
	[Ignore]
	[JsonIgnore]
	public EnumShiire EnKubun {
		get => (EnumShiire)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	///	関連No1
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("関連伝票NO")]
	public partial int RelateNo1 { get; set; }
	/// <summary>
	/// 掛率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("掛率1")]
	public partial int Rate { get; set; }
	/// <summary>
	/// 消費税
	/// </summary>
	[ObservableProperty]
	public partial int Tax { get; set; }
	/// <summary>
	/// 総合計
	/// </summary>
	[ObservableProperty]
	public partial int Total { get; set; }
}


/// <summary>
/// ハンディターミナルのデータ
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[Comment("トランザクション：ハンディターミナルのデータ")]
public sealed partial class TranHhtData : BaseDbClass {
	/// <summary>
	/// 店舗 文字  8
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string Shop { get; set; } = string.Empty;
	/// <summary>
	/// 日付 文字  8
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 処理区分 文字  2
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(2)]
	public partial string Kubun { get; set; } = string.Empty;
	/// <summary>
	/// 伝票NO	数値	8
	/// </summary>
	[ObservableProperty]
	public partial long DenNo { get; set; }
	/// <summary>
	/// 担当者	文字	6
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(6)]
	public partial string Tanto { get; set; } = string.Empty;
	/// <summary>
	/// 取引先	文字	8
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string Tori { get; set; } = string.Empty;
	/// <summary>
	/// 品番	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Hinban { get; set; } = string.Empty;
	/// <summary>
	/// カラー	文字	8
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string Color { get; set; } = string.Empty;
	/// <summary>
	/// サイズ	文字	8
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string Size { get; set; } = string.Empty;
	/// <summary>
	/// 元上代	数値	8
	/// </summary>
	[ObservableProperty]
	public partial int MotoJodai { get; set; }
	/// <summary>
	/// 上代	数値	8
	/// </summary>
	[ObservableProperty]
	public partial int Jodai { get; set; }
	/// <summary>
	/// 下代	数値	8
	/// </summary>
	[ObservableProperty]
	public partial int Gedai { get; set; }
	/// <summary>
	/// 数量	数値	5
	/// </summary>
	[ObservableProperty]
	public partial int Su { get; set; }
	/// <summary>
	/// 店舗2	文字	8
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string Shop2 { get; set; } = string.Empty;
	/// <summary>
	/// セールFLG	文字	1
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1)]
	public partial string SaleFlg { get; set; } = string.Empty;
	/// <summary>
	/// 棚番	文字	10
	/// </summary>
	[ObservableProperty]
	public partial int TanaNo { get; set; } = 0;
	/// <summary>
	/// 関連伝票NO	数値	8
	/// </summary>
	[ObservableProperty]
	public partial long RelateDenNo { get; set; }
	/// <summary>
	/// 掛率	数値	6.3
	/// </summary>
	[ObservableProperty]
	public partial decimal Kakeritsu { get; set; }
	/// <summary>
	/// 納品日	文字	8
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string NouhinDay { get; set; } = string.Empty;
	/// <summary>
	/// JANコード1	文字	13
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(13)]
	public partial string Jan1 { get; set; } = string.Empty;
	/// <summary>
	/// JANコード2	文字	13
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(13)]
	public partial string Jan2 { get; set; } = string.Empty;
	/// <summary>
	/// 予備03	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi03 { get; set; } = string.Empty;
	/// <summary>
	/// 予備04	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi04 { get; set; } = string.Empty;
	/// <summary>
	/// 予備05	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi05 { get; set; } = string.Empty;

	/// <summary>
	/// 予備06	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi06 { get; set; } = string.Empty;
	/// <summary>
	/// 予備07	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi07 { get; set; } = string.Empty;
	/// <summary>
	/// 予備08	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi08 { get; set; } = string.Empty;
	/// <summary>
	/// 予備09	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi09 { get; set; } = string.Empty;
	/// <summary>
	/// 予備10	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi10 { get; set; } = string.Empty;
	/// <summary>
	/// 予備11	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi11 { get; set; } = string.Empty;
	/// <summary>
	/// 予備12	文字	20
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	public partial string Yobi12 { get; set; } = string.Empty;
	/// <summary>
	/// 入力ファイル名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial string FileName { get; set; } = string.Empty;
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	public partial int LineNo { get; set; }
	/// <summary>
	/// HhtdataからTran系各テーブルへの変換日時(vdu相当の日時データ)
	/// </summary>
	[ObservableProperty]
	public partial long VdCnvDate { get; set; }
}

/// <summary>
/// VULCANデータレイアウト 一次取込用テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(BackupFileName))]
[KeyDml("nk2", false, nameof(VdCnvDate))]
[Comment("トランザクション：VULCANデータレイアウトハンディのデータ")]
public sealed partial class TranVulcanHht : BaseDbClass {
	/// <summary>
	/// VULCANタイプ  1:売上, 2:返品, 3:入庫, 4:出庫, 5:仕入, 6:仕入返品, 7:棚卸, 8:発注, 9:卸売, 10:卸返品, 11:移動, 12:客数
	/// ファイルレイアウト 1桁:1-9,A-Cで表現されているが、数値に変換して格納する
	/// </summary>
	[ObservableProperty]
	public partial int Type0 { get; set; } = 0;

	/// <summary>
	/// HT No  1-999の数値を格納する。VULCANのファイルレイアウトでは3桁の文字列で表現されているが、数値に変換して格納する
	/// </summary>
	[ObservableProperty]
	public partial int HhtNo { get; set; } = 0;
	/// <summary>
	/// SerialNo 1-9999の数値を格納する。VULCANのファイルレイアウトでは5桁の文字列で表現されているが、数値に変換して格納する
	/// </summary>
	[ObservableProperty]
	public partial int Serial { get; set; } = 0;
	/// <summary>
	/// 日付 yyyyMMdd 8桁の文字列で表現
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 店舗 文字  8 前'0'埋め
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string Shop { get; set; } = string.Empty;
	/// <summary>
	/// 担当者	文字	6 前'0'埋め
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(6)]
	public partial string Tanto { get; set; } = string.Empty;
	/// <summary>
	/// 販売区分 1桁 0:プロパー,1:セール, 9:未使用 (入庫と出庫の場合は 0:買取, 1:委託)
	/// </summary>
	[ObservableProperty]
	public partial int HanKubun { get; set; } = 9;
	/// <summary>
	/// 伝票番号 13桁 先頭8桁に0埋で数値を格納する。(売上と返品の場合は顧客CD 13桁、客数の場合は先頭8桁0埋で客数)
	/// </summary>
	[ColumnSizeDml(13)]
	[ObservableProperty]
	public partial string DenNo { get; set; } = string.Empty;
	/// <summary>
	/// JAN 1段目
	/// </summary>
	[ColumnSizeDml(13)]
	[ObservableProperty]
	public partial string Jan1 { get; set; } = string.Empty;
	/// <summary>
	/// JAN 2段目
	/// </summary>
	[ColumnSizeDml(13)]
	[ObservableProperty]
	public partial string Jan2 { get; set; } = string.Empty;

	/// <summary>
	/// 数量 6桁 先頭に'0'か'-'、5桁数値を格納する。
	/// </summary>
	[ObservableProperty]
	public partial int Su { get; set; } = 9;
	/// <summary>
	/// 単価 9桁数値を格納する。
	/// </summary>
	[ObservableProperty]
	public partial int Tanka { get; set; } = 9;
	/// <summary>
	/// 取引先 文字 8 前'0'埋め
	/// </summary>
	[ColumnSizeDml(8)]
	[ObservableProperty]
	public partial string ToriSaki { get; set; } = string.Empty;
	/// <summary>
	/// 掛率 文字 5桁 前'0'埋めで 999.9 を格納する。仕入の場合は発注番号8桁、発注の場合は納品日8桁を格納する。
	/// </summary>
	[ColumnSizeDml(8)]
	[ObservableProperty]
	public partial string KakeRitsu { get; set; } = string.Empty;
	/// <summary>
	/// 1取込ファイルの総件数 5桁数値
	/// </summary>
	[ObservableProperty]
	public partial int TotalCnt { get; set; } = 9;
	/// <summary>
	/// 予備空白	文字	6
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(6)]
	public partial string Filler { get; set; } = string.Empty;
	/// <summary>
	/// バックアップファイル名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial string BackupFileName { get; set; } = string.Empty;
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	public partial int LineNo { get; set; }
	/// <summary>
	/// Local PCのコンピュータ名
	/// </summary>
	[ObservableProperty]
	public partial string? ComputerName { get; set; } = null;
	/// <summary>
	/// Local PCのユーザ名
	/// </summary>
	[ObservableProperty]
	public partial string? UserName { get; set; } = null;
	/// <summary>
	/// HhtdataからTran系各テーブルへの変換日時(vdu相当の日時データ)
	/// </summary>
	[ObservableProperty]
	public partial long VdCnvDate { get; set; }
	/// <summary>
	/// 対象テーブル名
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	public partial string TargetTableName { get; set; } = string.Empty;
	/// <summary>
	/// 対象テーブルの対象レコードID
	/// </summary>
	[ObservableProperty]
	public partial long TargetId { get; set; }
	/// <summary>
	/// 変換エラー内容
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(1000)]
	public partial string ErrorMsg { get; set; } = string.Empty;
}


/* ToDo: 未作成テーブル(上代、原価)

// 上代一括変更の伝票データ
[Comment("トランザクション：上代一括変更、伝票No、登録日、日付Frm-To、セールCD(区分'S01')、タイトル、[店舗CD] [商品CD、色サイズ、掛率、上代]")]
public sealed partial class TranJodai : BaseDbClass {
}

[Comment("派生テーブル：商品上代テーブル：商品Id、店舗Id、日付Fm-To、上代、元TranId")
public sealed partial class DerivedJodai : BaseDbClass {
}
			派生上代テーブルとマスタから上代を決定する
			   SELECT COALESCE(d.Jodai, m.Jodai) AS FinalJodai
				FROM MasterShohin m
				LEFT JOIN DerivedShohin d ON m.Id = d.Id_Shohin 
				    AND d.Id_Tenpo = 10
				    AND 20260415 BETWEEN d.DayFrom AND d.DayTo
				WHERE m.Id = 1;

// 原価変更の伝票データ
[Comment("トランザクション：原価変更、伝票No、登録日、評価区分(評価替、その他)、[商品CD、OFF率、(上代、掛率、原価)、新原価]")]
public sealed partial class TranGenka : BaseDbClass {
}
*/


/* ToDo: 未作成テーブル(配分)
[Comment("トランザクション：配分データ：日付、配分CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHaibun : BaseDbClass {
}
[Comment("派生テーブル：配分明細：日付、倉庫Id、商品Id、色サイズ、予定数量、実数量、完了FLG、元伝票Id")]
public sealed partial class DerivedHaibun : BaseDbClass {
}
 */

/* ToDo: 未作成テーブル(補充)
[Comment("トランザクション：補充データ：日付、配分CD、倉庫Id、[商品Id、色サイズ、予定数量、実数量、完了FLG]")]
public sealed partial class TranHojyu : BaseDbClass {
}
 */

/* ToDo: 未作成テーブル(集計)
[Comment("集計テーブル：売掛データ：年月、得意先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryUrikake : BaseDbClass {
}
[Comment("集計テーブル：請求データ：年月+締日、得意先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryUriSei : BaseDbClass {
}
[Comment("集計テーブル：買掛データ：年月、仕入先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryKaikake : BaseDbClass {
}
[Comment("集計テーブル：支払データ：年月、仕入先Id、前月残、当月残、売上、入金")]
public sealed partial class SummaryKaiShi : BaseDbClass {
}
 */

/* ToDo: 未作成テーブル(顧客)
[Comment("ベースポイントトランク別ポイント、ボーナスポイント")]
public sealed partial class MasterPointRank : BaseDbClass {
}
[Comment("ポイント履歴テーブル：日付、顧客Id、取得ポイント、使用ポイント、残")]
public sealed partial class TranPointRireki : BaseDbClass {
}
 */

