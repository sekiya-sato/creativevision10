using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;

/// <summary>
/// 社員テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Code))]
[Comment("マスター：社員テーブル 店舗、部門などのマスタと紐づく社員情報")]
[OldTableCommentAttr("HC$MASTER_SHAIN")]
public sealed partial class MasterShain : BaseDbClass, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(12)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("社員CD")]
	string code = string.Empty;
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("名前")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string ryaku = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("フリガナ")]
	string kana = string.Empty;
	/// <summary>
	/// メールアドレス
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("メール")]
	string mail = string.Empty;
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("店舗CD")]
	[ForeignKey(nameof(MasterTokui))]
	long id_Tenpo;
	/// <summary>
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTenpo = new();
	/// <summary>
	/// 部門Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("部門")]
	[ForeignKey(nameof(MasterMeisho))]
	long id_Bumon;
	/// <summary>
	/// 部門データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vBumon = new();
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	[OldTableCommentAttr("名称CD01 - 名称CD05")]
	List<MasterGeneralMeisho>? jsub;
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	BaseDetailClass? jdetail;
}

/// <summary>
/// 顧客テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Code))]
[Comment("マスター：顧客テーブル 店頭顧客あるいはEC顧客")]
[OldTableCommentAttr("HC$MASTER_KOKYAKU")]
public sealed partial class MasterEndCustomer : BaseDbHasAddress, IBaseCodeName {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(12)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("顧客CD")]
	string code = string.Empty;
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("顧客名")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string ryaku = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("カナ")]
	string kana = string.Empty;
	/// <summary>
	/// ランク
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("顧客ランク")]
	string rank = string.Empty;
	/// <summary>
	/// 店舗Id
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("店舗CD")]
	long id_Tenpo;
	/// <summary>
	/// 店舗データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTenpo = new();
	/// <summary>
	/// 誕生日 yyyyMMdd
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("誕生日")]
	string birthday = string.Empty;
	/// <summary>
	/// 誕生日 MMdd
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(4)]
	[property: System.ComponentModel.DefaultValue("")]
	string birthNoyear = string.Empty;
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("メモ")]
	string memo = string.Empty;
	/// <summary>
	/// 性別 0=不明 1=男性 2=女性
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnGendar))]
	[OldTableCommentAttr("性別")]
	int gendar = 0;

	[Ignore]
	[JsonIgnore]
	public EnumGendar EnGendar {
		get => (EnumGendar)Gendar;
		set => Gendar = (int)value;
	}
	/// <summary>
	/// ポイント
	/// </summary>
	[ObservableProperty]
	int point;
	/// <summary>
	/// 累計購買回数
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("累計購入数量", "ToDo")]
	int salesCount;
	/// <summary>
	/// 累計購買金額
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("累計購入金額")]
	int salesKingaku;
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	List<MasterGeneralMeisho>? jsub;
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	BaseDetailClass? jdetail;

}

/// <summary>
/// 商品テーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Code))]
//[KeyDml("njan1", false, "json_extract(Jcolsiz, '$.Jan1')")]
//[KeyDml("njan2", false, "json_extract(Jcolsiz, '$.Jan2')")]
//[KeyDml("njan3", false, "json_extract(Jcolsiz, '$.Jan3')")]
[Comment("マスター：商品テーブル Jcolsiz列に'色CD,サイズCD,JAN1,JAN2,JAN3'の情報を格納")]
[OldTableCommentAttr("HC$MASTER_SHOHIN", "Jcolsiz列は HC$MASTER_SHOHIN_JAN")]
public sealed partial class MasterShohin : BaseDbClass, IBaseCodeName, IDerivedOrigin {
	/// <summary>
	/// コード
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(16)]
	[OldTableCommentAttr("商品CD")]
	string code = "";
	/// <summary>
	/// 名前
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(80)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("商品名")]
	string name = string.Empty;
	/// <summary>
	/// 略称
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("略称")]
	string ryaku = string.Empty;
	/// <summary>
	/// カナ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("旧コード")]
	string kana = string.Empty;
	/// <summary>
	/// ブランド
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("ブランドCD")]
	long id_Brand;
	/// <summary>
	/// ブランドデータ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vBrand = new();
	/// <summary>
	/// アイテム
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("アイテムCD")]
	long id_Item;
	/// <summary>
	/// アイテムデータ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vItem = new();
	/// <summary>
	/// 展示会
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("展示会CD")]
	long id_Tenji;
	/// <summary>
	/// 展示会データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vTenji = new();
	/// <summary>
	/// メーカー
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("メーカーCD")]
	long id_Maker;
	/// <summary>
	/// メーカーデータ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vMaker = new();
	/// <summary>
	/// シーズン
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("シーズンCD")]
	long id_Season;
	/// <summary>
	/// シーズンデータ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vSeason = new();
	/// <summary>
	/// 素材
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("素材CD")]
	long id_Material;
	/// <summary>
	/// 素材データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vMaterial = new();
	/// <summary>
	/// 原産国
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("原産国CD")]
	long id_Country;
	/// <summary>
	/// 原産国データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vCountry = new();
	/// <summary>
	/// 元上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("元上代")]
	int tankaJodaiOrg;
	/// <summary>
	/// 上代
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代")]
	int tankaJodai;
	/// <summary>
	/// 原価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("原価")]
	int tankaGenka;
	/// <summary>
	/// 仕入単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入価格")]
	int tankaShiire;
	/// <summary>
	/// 出荷日(デリバリー)
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[OldTableCommentAttr("デリバリー日")]
	string dayShukka = "19010101";
	/// <summary>
	/// 納品日
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[OldTableCommentAttr("納品日")]
	string dayNohin = "19010101";
	/// <summary>
	/// 店頭投入日
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[OldTableCommentAttr("店頭投入日")]
	string dayTento = "19010101";
	/// <summary>
	/// 消費税No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("消費税CD")]
	long id_Tax;
	/// <summary>
	/// 在庫管理フラグ
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnZaiko))]
	[OldTableCommentAttr("在庫管理FLG")]
	int isZaiko = 1;

	[Ignore]
	[JsonIgnore]
	public EnumYesNo EnZaiko {
		get => (EnumYesNo)IsZaiko;
		set => IsZaiko = (int)value;
	}

	/// <summary>
	/// メーカー品番
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("メーカー品番")]
	string makerHin = string.Empty;
	/// <summary>
	/// 商品サイズ区分
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(8)]
	[OldTableCommentAttr("商品サイズ区分")]
	string sizeKu = "SIZ";
	/// <summary>
	/// 基準倉庫
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("基準倉庫CD")]
	long id_Soko;
	/// <summary>
	/// 倉庫データ
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(100)]
	CodeNameView vSoko = new();
	/// <summary>
	/// メモ
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(120)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("メモ")]
	string memo = string.Empty;
	/// <summary>
	/// 原価リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	[OldTableCommentAttr("JgenkaはHC$MASTER_SHOHIN_GENKAの内容を格納")]
	List<MasterShohinGenka>? jgenka;
	/// <summary>
	/// 色サイズリスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	[OldTableCommentAttr("JcolsizはHC$MASTER_SHOHIN_JANの内容を格納")]
	List<MasterShohinColSiz>? jcolsiz;
	/// <summary>
	/// 品質リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	[OldTableCommentAttr("JgradeはHC$MASTER_SHOHIN_GRADEの内容を格納")]
	List<MasterShohinGrade>? jgrade;
	/// <summary>
	/// 名称リスト
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	[OldTableCommentAttr("名称CD01 - 名称CD10")]
	List<MasterGeneralMeisho>? jsub;
	/// <summary>
	/// 詳細内容
	/// </summary>
	[ObservableProperty]
	[property: SerializedColumn]
	[property: ColumnSizeDml(1000)]
	BaseDetailClass? jdetail;

	[Ignore]
	public Type DerivedClass => typeof(DerivedShohinColSiz);
}


/// <summary>
/// 商品色サイズJANテーブル
/// </summary>
[NoCreate]
[OldTableCommentAttr("HC$MASTER_SHOHIN_JAN")]
public sealed partial class MasterShohinColSiz : BaseDbClass {
	/// <summary>
	/// 色
	/// </summary>
	[ObservableProperty]
	long id_Col;
	/// <summary>
	/// カラーCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("色CD")]
	string code_Col = string.Empty;
	/// <summary>
	/// カラー名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string mei_Col = string.Empty;
	/// <summary>
	/// サイズ
	/// </summary>
	[ObservableProperty]
	long id_Siz;
	/// <summary>
	/// サイズCD
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("サイズCD")]
	string code_Siz = string.Empty;
	/// <summary>
	/// サイズ名
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(100)]
	[property: System.ComponentModel.DefaultValue("")]
	string mei_Siz = string.Empty;
	/// <summary>
	/// JANコード1
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("JANコード1")]
	string jan1 = string.Empty;
	/// <summary>
	/// JANコード2
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("JANコード2")]
	string jan2 = string.Empty;
	/// <summary>
	/// JANコード3
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(20)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("JANコード3")]
	string jan3 = string.Empty;
}
/// <summary>
/// 品質テーブル
/// </summary>
[NoCreate]
[OldTableCommentAttr("HC$MASTER_SHOHIN_GRADE")]
public sealed partial class MasterShohinGrade : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("行NO")]
	int no;
	/// <summary>
	/// 品質
	/// </summary>
	[ObservableProperty]
	[property: ColumnSizeDml(40)]
	[property: System.ComponentModel.DefaultValue("")]
	[OldTableCommentAttr("品質")]
	string hinshitu = string.Empty;
	/// <summary>
	/// ％
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("パーセント")]
	int percent;
}
/// <summary>
/// 原価テーブル
/// </summary>
[NoCreate]
[OldTableCommentAttr("HC$MASTER_SHOHIN_GENKA")]
public sealed partial class MasterShohinGenka : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("行NO")]
	int no;
	/// <summary>
	/// 原価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("原価")]
	int tankaGenka;
	/// <summary>
	/// 仕入単価
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("仕入価格")]
	int tankaShiire;
}

/// <summary>
/// 設定フラグテーブル
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uq1", true, nameof(Name))]
[Comment("マスター：設定フラグテーブル name と val の組で設定情報を表す")]
[OldTableCommentAttr("HC$MASTER_CONFIG")]
public sealed partial class MasterConfig : BaseDbClass {
	[ObservableProperty]
	[OldTableCommentAttr("カテゴリ")]
	string category = string.Empty;
	[ObservableProperty]
	[OldTableCommentAttr("フラグ名")]
	string name = string.Empty;
	[ObservableProperty]
	[OldTableCommentAttr("値")]
	string val = string.Empty;
	[ObservableProperty]
	[OldTableCommentAttr("リスト")]
	string example = string.Empty;
	[ObservableProperty]
	[OldTableCommentAttr("MEMO")]
	string memo = string.Empty;
}
/// <summary>
/// ハンディターミナル用のテーブル、HHTマスター作成時のみ必要
/// </summary>
[NoCreate]
public sealed partial class MasterHht : ObservableObject {
	/// <summary>
	/// 1 - 3	識別フラグ	SIR, SOK, TAN, TOK のいずれか
	/// </summary>
	[ObservableProperty]
	string kubun = string.Empty;
	/// <summary>
	/// 4 - 11	コード	8桁（社員は6桁+スペース2桁）のゼロ埋めコード
	/// </summary>
	[ObservableProperty]
	string code = string.Empty;
	/// <summary>
	/// 12 - 51	名称1	SJIS 40byteの名称（略称/カナ/名称、TRANSLATE済み）
	/// </summary>
	[ObservableProperty]
	string name = string.Empty;
	/// <summary>
	/// 52 - 91	名称2	SJIS 40byteの名称（略称、またはスペース）
	/// </summary>
	[ObservableProperty]
	string nameOpt = string.Empty;
	/// <summary>
	/// 92	終端符号	アスタリスク * 
	/// </summary>
	[ObservableProperty]
	string eol = string.Empty;
}
