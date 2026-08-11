using CommunityToolkit.Mvvm.ComponentModel;
using CvBase.Share;
using Newtonsoft.Json;
using NPoco;

namespace CvBase;

/// <summary>
/// 上代変更区分（<see cref="TranJodai.Kubun"/>）
/// </summary>
public enum EnumJodaiKubun : int {
	/// <summary>プロパー(P)。定価変更。期間は無期限として DayTo="99991231" で表現する</summary>
	Proper = 0,
	/// <summary>セール(S)。期間限定の販売価格</summary>
	Sale = 1,
}

/// <summary>
/// 上代変更の対象系統（<see cref="TranJodai.TaishoType"/>）
/// <para>
/// 全件ワイルドカード(Id_Tenpo=0)の意味が系統ごとに変わるため、対象を表す列が必要になる。
/// 1伝票 = 1系統。店舗と卸を同時に変更する場合は伝票を2本に分ける。
/// </para>
/// </summary>
public enum EnumJodaiTaisho : int {
	/// <summary>店舗用。<see cref="MasterTokui"/>.TenType=6(直営店)。店舗売上・POSに適用</summary>
	Tenpo = 0,
	/// <summary>本部売上用。<see cref="MasterTokui"/>.TenType in (1,3)(卸先・売仕店)。本部売上・受注に適用</summary>
	Honbu = 1,
}

/// <summary>
/// 上代一括変更 伝票
/// <para>
/// 対象店舗(<see cref="Jshop"/>)・対象明細(<see cref="Jmeisai"/>)・抽出条件(<see cref="Jcond"/>)を
/// JSON配列で保持し、物理テーブルはこの1表のみとする。
/// 確定(<see cref="Status"/>=1)すると <see cref="DerivedJodai"/> へ「対象店舗 × 対象明細」の直積が展開され、
/// 実際の価格解決はそちらを引く。展開は <see cref="IDerivedOrigin"/> 経由で
/// CvServer/Services/HandlerDerived が Insert/Update/Delete 時に自動実行する。
/// </para>
/// <para>
/// 商品マスタ(<see cref="MasterShohin"/>.TankaJodai)は<b>書き換えない</b>(オーバーレイ方式)。
/// セール終了で自動的に元価格へ戻り、過去日の再計算も再現できる。
/// </para>
/// <para>
/// V*列(<see cref="VSale"/>/<see cref="VShain"/>)はTran系＝伝票作成時点の名称を保持する監査値であり、
/// マスタが改名されても伝播しない。現行名称が必要な場合は Id_* から参照先マスタをJOINすること。
/// </para>
/// <para>
/// JSON列が大きくなるため、<b>伝票一覧のSQLでは Jcond/Jshop/Jmeisai を SELECT句に含めないこと</b>。
/// 件数表示には <see cref="ShopCnt"/>/<see cref="MeisaiCnt"/>/<see cref="ExpandCnt"/> を使う。
/// 設計の経緯は `.omo/20260811_jodai_table_design_plan.md` を参照。
/// </para>
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("nk1", false, nameof(DenDay))]
[KeyDml("nk2", false, nameof(Id_Sale))]
[Comment("トランザクション：上代一括変更 伝票。対象店舗(Jshop)・対象明細(Jmeisai)・抽出条件(Jcond)をJSON配列で保持")]
public sealed partial class TranJodai : BaseDbClass, IDerivedOrigin {
	/// <summary>
	/// 登録日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("登録日")]
	public partial string DenDay { get; set; } = "19010101";
	/// <summary>
	/// 上代変更区分（<see cref="EnumJodaiKubun"/>）0:プロパー(P) 1:セール(S)
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnKubun))]
	[OldTableCommentAttr("P/S区分")]
	public partial int Kubun { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumJodaiKubun EnKubun {
		get => (EnumJodaiKubun)Kubun;
		set => Kubun = (int)value;
	}
	/// <summary>
	/// 対象系統（<see cref="EnumJodaiTaisho"/>）0:店舗用(TenType=6) 1:本部売上用(TenType in (1,3))
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(EnTaisho))]
	public partial int TaishoType { get; set; }
	[Ignore]
	[JsonIgnore]
	public EnumJodaiTaisho EnTaisho {
		get => (EnumJodaiTaisho)TaishoType;
		set => TaishoType = (int)value;
	}
	/// <summary>
	/// セールCD
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterMeisho), meishoKubun: "SLE")]
	[OldTableCommentAttr("セールCD")]
	public partial long Id_Sale { get; set; }
	/// <summary>
	/// セールデータ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VSale { get; set; } = new();
	/// <summary>
	/// タイトル
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(60)]
	[OldTableCommentAttr("タイトル")]
	public partial string Title { get; set; } = string.Empty;
	/// <summary>
	/// 入力社員Id
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShain))]
	[OldTableCommentAttr("入力者")]
	public partial long Id_Shain { get; set; }
	/// <summary>
	/// 社員データ
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(100)]
	public partial CodeNameView VShain { get; set; } = new();
	/// <summary>
	/// 適用開始日（yyyyMMdd）。店舗個別指定がない場合の既定値
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("開始日")]
	public partial string DayFrom { get; set; } = "19010101";
	/// <summary>
	/// 適用終了日（yyyyMMdd）。プロパー(P)区分は "99991231"（無期限）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("終了日")]
	public partial string DayTo { get; set; } = "99991231";
	/// <summary>
	/// 一括変更方法 0:金額指定 1:率(%)指定
	/// </summary>
	[ObservableProperty]
	public partial int CalcType { get; set; }
	/// <summary>
	/// 変更率(%) CalcType=1 のとき使用
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("率")]
	public partial decimal CalcRate { get; set; }
	/// <summary>
	/// 変更金額 CalcType=0 のとき使用
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("金額")]
	public partial int CalcValue { get; set; }
	/// <summary>
	/// 丸め単位 0:1円 1:10円 2:百円 3:千円
	/// </summary>
	[ObservableProperty]
	public partial int RoundUnit { get; set; }
	/// <summary>
	/// 丸め方法 0:切捨 1:四捨五入 2:切上
	/// </summary>
	[ObservableProperty]
	public partial int RoundType { get; set; }
	/// <summary>
	/// 状態 0:入力中 1:確定(展開済) 2:取消
	/// <para><b>Status=1 の伝票だけが <see cref="DerivedJodai"/> へ展開される</b>（<see cref="DerivedJodai.CreateSql"/>）。</para>
	/// </summary>
	[ObservableProperty]
	public partial int Status { get; set; }
	/// <summary>
	/// 確定日（yyyyMMdd）。空文字なら未確定
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("確定日")]
	public partial string FixDay { get; set; } = string.Empty;
	/// <summary>
	/// 送信フラグ 0:未送信 1:送信中 2:送信済み（店舗POSへの価格配信状態）
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("送信FLG")]
	public partial int SendFlg { get; set; }
	/// <summary>
	/// 対象店舗数（<see cref="Jshop"/>を開かずに一覧表示するための非正規化列）
	/// </summary>
	[ObservableProperty]
	public partial int ShopCnt { get; set; }
	/// <summary>
	/// 対象明細数（<see cref="Jmeisai"/>を開かずに一覧表示するための非正規化列）
	/// </summary>
	[ObservableProperty]
	public partial int MeisaiCnt { get; set; }
	/// <summary>
	/// <see cref="DerivedJodai"/>への展開行数（再展開時の検証用）
	/// </summary>
	[ObservableProperty]
	public partial int ExpandCnt { get; set; }
	/// <summary>
	/// 抽出条件リスト
	/// <para>
	/// どの条件でこの明細群が作られたかを残す。商品追加後の再抽出と監査の説明に必要。
	/// 色・サイズでの絞り込みもここに残る（価格は商品単位なので明細側には持たない）。
	/// </para>
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(ColumnType.Json)]
	public partial List<TranJodaiCond> Jcond { get; set; } = [];
	/// <summary>
	/// 対象店舗リスト（店舗別の適用期間を含む）
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(ColumnType.Json)]
	public partial List<TranJodaiShop> Jshop { get; set; } = [];
	/// <summary>
	/// 対象明細リスト（商品マスタ単位）
	/// </summary>
	[ObservableProperty]
	[SerializedColumn]
	[ColumnSizeDml(ColumnType.Json)]
	public partial List<TranJodaiMeisai> Jmeisai { get; set; } = [];
	/// <summary>
	/// ヘッダメモ
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(200)]
	[OldTableCommentAttr("メモ")]
	public partial string Memo { get; set; } = string.Empty;
	[Ignore]
	public Type DerivedClass => typeof(DerivedJodai);

	/// <summary>
	/// 対象店舗・対象明細の重複を取り除き、行Noと件数列を整える。<b>保存前に必ず呼ぶこと。</b>
	/// <para>
	/// <see cref="DerivedJodai"/> の uk1(Id_Tran, TaishoType, Id_Tenpo, Id_Shohin) はユニークキーなので、
	/// <see cref="Jshop"/> に同じ店舗、<see cref="Jmeisai"/> に同じ商品が重複していると
	/// 展開時に制約違反となり<b>トランザクションごと失敗する</b>（伝票の保存自体が通らない）。
	/// </para>
	/// <para>
	/// 重複時は<b>後の指定を残す</b>（＝最後に入力した内容が有効）。
	/// 期間重複を「後の伝票が勝つ」で解決するのと同じ考え方に揃えている。
	/// 利用者へ知らせたい場合は <see cref="FindDuplicates"/> を先に呼ぶこと。
	/// </para>
	/// </summary>
	/// <returns>取り除いた重複の件数（対象店舗＋対象明細）</returns>
	public int Normalize() {
		var removed = RemoveDuplicates(Jshop, c => c.Id_Tenpo) + RemoveDuplicates(Jmeisai, c => c.Id_Shohin);
		for (var i = 0; i < Jmeisai.Count; i++)
			Jmeisai[i].No = i + 1;
		ShopCnt = Jshop.Count;
		MeisaiCnt = Jmeisai.Count;
		return removed;
	}

	/// <summary>
	/// 重複している対象店舗・対象商品を利用者向けメッセージとして返す（<see cref="Normalize"/>の前の確認用）。
	/// </summary>
	/// <returns>重複が無ければ空リスト</returns>
	public List<string> FindDuplicates() {
		var messages = new List<string>();
		foreach (var group in Jshop.GroupBy(c => c.Id_Tenpo).Where(g => g.Count() > 1)) {
			var first = group.First();
			messages.Add($"対象店舗が重複しています：{first.Code_Tenpo} {first.Mei_Tenpo}（{group.Count()}件）");
		}
		foreach (var group in Jmeisai.GroupBy(c => c.Id_Shohin).Where(g => g.Count() > 1)) {
			var first = group.First();
			messages.Add($"対象商品が重複しています：{first.Code_Shohin} {first.Mei_Shohin}（{group.Count()}件）");
		}
		return messages;
	}

	/// <summary>
	/// キーが重複する要素を後ろから走査して取り除く（＝最後の指定を残す）
	/// </summary>
	static int RemoveDuplicates<T, TKey>(List<T> list, Func<T, TKey> keySelector) {
		var seen = new HashSet<TKey>();
		var removed = 0;
		for (var i = list.Count - 1; i >= 0; i--) {
			if (seen.Add(keySelector(list[i])))
				continue;
			list.RemoveAt(i);
			removed++;
		}
		return removed;
	}
}

/// <summary>
/// 上代一括変更 抽出条件（<see cref="TranJodai.Jcond"/>の要素）
/// </summary>
[SubTableDefine]
public sealed partial class TranJodaiCond : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	public partial int No { get; set; }
	/// <summary>
	/// 検索項目（メーカー品番／ブランド／アイテム など）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	[OldTableCommentAttr("検索項目")]
	public partial string Field { get; set; } = string.Empty;
	/// <summary>
	/// 選択項目のコード(FROM)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	public partial string CdFrom { get; set; } = string.Empty;
	/// <summary>
	/// 選択項目のコード(TO)
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(30)]
	public partial string CdTo { get; set; } = string.Empty;
	/// <summary>
	/// 在庫条件 0:在庫無視 1:在庫アリ
	/// </summary>
	[ObservableProperty]
	public partial int ZaikoJoken { get; set; }
	/// <summary>
	/// 展開単位 0:商品
	/// </summary>
	[ObservableProperty]
	public partial int TenkaiTani { get; set; }
}

/// <summary>
/// 上代一括変更 対象店舗（<see cref="TranJodai.Jshop"/>の要素）
/// <para>
/// 画面の「店舗セール期間設定（開始日変更／終了日変更）」により<b>店舗ごとに期間が異なる</b>ため、
/// 期間はヘッダではなくこの要素が持つ値が正となる。
/// </para>
/// </summary>
[SubTableDefine]
public sealed partial class TranJodaiShop : ObservableObject {
	/// <summary>
	/// 対象店舗Id。0 = 全件（その系統の全店舗／全得意先）
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 6, additionalInfo: "TranJodai.TaishoType=1 のときは TenType in (1,3)")]
	[OldTableCommentAttr("店舗CD")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// 店舗CD（時点値）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(16)]
	public partial string Code_Tenpo { get; set; } = string.Empty;
	/// <summary>
	/// 店舗名（時点値）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(80)]
	public partial string Mei_Tenpo { get; set; } = string.Empty;
	/// <summary>
	/// 店舗別の適用開始日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("開始日")]
	public partial string DayFrom { get; set; } = "19010101";
	/// <summary>
	/// 店舗別の適用終了日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("終了日")]
	public partial string DayTo { get; set; } = "99991231";
}

/// <summary>
/// 上代一括変更 対象明細（<see cref="TranJodai.Jmeisai"/>の要素）
/// <para>
/// 価格の粒度は<b>商品マスタ単位</b>。色・サイズ別の価格差は持たない。
/// </para>
/// </summary>
[SubTableDefine]
public sealed partial class TranJodaiMeisai : ObservableObject {
	/// <summary>
	/// 行No
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("行NO")]
	public partial int No { get; set; }
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 商品CD（時点値）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(20)]
	[OldTableCommentAttr("商品CD")]
	public partial string Code_Shohin { get; set; } = string.Empty;
	/// <summary>
	/// 商品名（時点値）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(100)]
	public partial string Mei_Shohin { get; set; } = string.Empty;
	/// <summary>
	/// 変更前上代（時点値）
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("上代")]
	public partial int JodaiOld { get; set; }
	/// <summary>
	/// 新販売価格
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("新販売価格")]
	public partial int JodaiNew { get; set; }
	/// <summary>
	/// 新割引率
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("新割引率")]
	public partial decimal RateOff { get; set; }
	/// <summary>
	/// 税込価格（<b>表示用スナップショット</b>。正は MasterSysman.Jsub の税率と MasterShohin.Id_Tax）
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("税込価格")]
	public partial int PriceInTax { get; set; }
	/// <summary>
	/// 店頭投入日（yyyyMMdd）MasterShohin.DayTento の写し（表示用）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("店頭投入日")]
	public partial string DayTento { get; set; } = "19010101";
	/// <summary>
	/// 変更日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	[OldTableCommentAttr("変更日")]
	public partial string DayChange { get; set; } = "19010101";
	/// <summary>
	/// 状況 0:未確認 1:確認済
	/// </summary>
	[ObservableProperty]
	[OldTableCommentAttr("状況")]
	public partial int Status { get; set; }
}

/// <summary>
/// 適用上代（<see cref="TranJodai"/>の確定分を「対象 × 商品 × 期間」へ展開した派生テーブル）
/// <para>
/// 旧システムの MasterJodai に相当する。伝票から<b>再生成可能</b>なので Derived系とする。
/// 価格解決はこの表を引き、該当行がなければ <see cref="MasterShohin"/>.TankaJodai を使う。
/// </para>
/// <para>
/// 期間の重複は禁止しない（店舗別期間の運用が成り立たなくなるため）。重なった場合は
/// 「個別指定(Id_Tenpo&lt;&gt;0) &gt; 全件(Id_Tenpo=0) → <see cref="Priority"/>の大きい方（＝後の伝票）」で一意に決まる。
/// </para>
/// <para>
/// <b>V*列(CodeNameView)を持たない。</b>Derived系にV*列を追加すると MasterCascadeDb.VRules への登録が必須になり
/// （MasterCascadeDbTests.VRules_CoverAllMasterVColumns が検出）、数万行への伝播UPDATEが発生する。
/// 名称が必要な画面は MasterShohin / MasterTokui をJOINすること（Summary系と同じ方針）。
/// </para>
/// </summary>
[PrimaryKey(nameof(Id), AutoIncrement = true)]
[KeyDml("uk1", true, nameof(Id_Tran), nameof(TaishoType), nameof(Id_Tenpo), nameof(Id_Shohin))]
[KeyDml("nk1", false, nameof(Id_Shohin), nameof(TaishoType), nameof(Id_Tenpo), nameof(DayFrom), nameof(DayTo))]
[KeyDml("nk2", false, nameof(Id_Tran))]
[KeyDml("nk3", false, nameof(DayTo))]
[Comment("派生テーブル：適用上代 TranJodai(確定分)を対象×商品×期間へ展開したもの")]
public partial class DerivedJodai : BaseDbClass, IDerivedClass {
	/// <summary>
	/// 対象系統（<see cref="EnumJodaiTaisho"/>）0:店舗用 1:本部売上用
	/// </summary>
	[ObservableProperty]
	public partial int TaishoType { get; set; }
	/// <summary>
	/// 対象Id（店舗Id または 得意先Id。<see cref="TaishoType"/>による）。0 = その系統の全件
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterTokui), tenType: 6, additionalInfo: "TaishoType=1 のときは TenType in (1,3)")]
	public partial long Id_Tenpo { get; set; }
	/// <summary>
	/// 商品ユニークキー
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(MasterShohin))]
	public partial long Id_Shohin { get; set; }
	/// <summary>
	/// 適用開始日（yyyyMMdd）
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string DayFrom { get; set; } = "19010101";
	/// <summary>
	/// 適用終了日（yyyyMMdd）。プロパー(P)区分は "99991231"
	/// </summary>
	[ObservableProperty]
	[ColumnSizeDml(8)]
	public partial string DayTo { get; set; } = "99991231";
	/// <summary>
	/// 上代変更区分（<see cref="EnumJodaiKubun"/>）0:プロパー 1:セール
	/// </summary>
	[ObservableProperty]
	public partial int Kubun { get; set; }
	/// <summary>
	/// 適用販売価格
	/// </summary>
	[ObservableProperty]
	public partial int Jodai { get; set; }
	/// <summary>
	/// 割引率
	/// </summary>
	[ObservableProperty]
	public partial decimal RateOff { get; set; }
	/// <summary>
	/// 元伝票Id（<see cref="TranJodai"/>.Id）。JSON化した伝票へ戻る唯一の経路
	/// </summary>
	[ObservableProperty]
	[ForeignKey(nameof(TranJodai))]
	public partial long Id_Tran { get; set; }
	/// <summary>
	/// 元明細No（<see cref="TranJodaiMeisai"/>.No）
	/// </summary>
	[ObservableProperty]
	public partial int No { get; set; }
	/// <summary>
	/// 競合解決順位。既定は元伝票Id（＝後から出した伝票が勝つ）
	/// </summary>
	[ObservableProperty]
	public partial long Priority { get; set; }

	/// <summary>
	/// SqlDepends: 確定済み伝票を「対象店舗 × 対象明細」の直積へ展開するSQL
	/// <para>
	/// 期間は店舗別の値を優先し、未設定ならヘッダの既定期間を使う。
	/// Status=1(確定)以外は展開しないので、入力中・取消の伝票では0件になる。
	/// json_extract は不正JSONに例外を投げるため json_valid() でガードする。
	/// </para>
	/// </summary>
	[Ignore]
	public static string CreateSql => @$"
Insert into {nameof(DerivedJodai)}
  (Vdc,Vdu,TaishoType,Id_Tenpo,Id_Shohin,DayFrom,DayTo,Kubun,Jodai,RateOff,Id_Tran,No,Priority)
SELECT
  T.Vdc, T.Vdu,
  T.TaishoType,
  ifnull(json_extract(S.value, '$.Id_Tenpo'), 0),
  ifnull(json_extract(M.value, '$.Id_Shohin'), 0),
  ifnull(nullif(json_extract(S.value, '$.DayFrom'), ''), T.DayFrom),
  ifnull(nullif(json_extract(S.value, '$.DayTo'), ''), T.DayTo),
  T.Kubun,
  ifnull(json_extract(M.value, '$.JodaiNew'), 0),
  ifnull(json_extract(M.value, '$.RateOff'), 0),
  T.Id,
  ifnull(json_extract(M.value, '$.No'), 0),
  T.Id
FROM {nameof(TranJodai)} T, json_each(T.Jshop) S, json_each(T.Jmeisai) M
WHERE T.Status = 1 AND json_valid(T.Jshop) AND json_valid(T.Jmeisai)
";
	[Ignore]
	public static string InsertSql => CreateSql + " AND T.Id = @0";
	[Ignore]
	public static string DeleteSql => $"Delete from {nameof(DerivedJodai)} where Id_Tran = @0";

	/// <summary>
	/// SqlDepends: 適用上代を1件解決するスカラサブクエリ断片。該当行がなければ NULL を返す。
	/// <para>
	/// 引数はいずれも<b>SQL式</b>（列参照 "s.Id_Shohin" / パラメータ "@0" / リテラル "'20260811'" のいずれでも可）。
	/// 優先順位は「個別指定 &gt; 全件 → Priority(＝後の伝票) → Id」。
	/// </para>
	/// </summary>
	/// <param name="shohinExpr">商品Idの式</param>
	/// <param name="taishoExpr">対象系統の式（<see cref="EnumJodaiTaisho"/>）</param>
	/// <param name="tenpoExpr">店舗Id／得意先Idの式。0 なら全件行のみ該当</param>
	/// <param name="dayExpr">判定日(yyyyMMdd)の式</param>
	public static string ResolveSql(string shohinExpr, string taishoExpr, string tenpoExpr, string dayExpr) => @$"(
    SELECT dj.Jodai FROM {nameof(DerivedJodai)} dj
     WHERE dj.Id_Shohin = {shohinExpr}
       AND dj.TaishoType = {taishoExpr}
       AND dj.Id_Tenpo IN ({tenpoExpr}, 0)
       AND {dayExpr} BETWEEN dj.DayFrom AND dj.DayTo
     ORDER BY (dj.Id_Tenpo <> 0) DESC, dj.Priority DESC, dj.Id DESC
     LIMIT 1)";

	/// <summary>
	/// SqlDepends: 最終的な上代を返すSQL断片。適用上代がなければ商品マスタの上代を使う。
	/// <para>集計SQLへ埋め込む用途。<c>shohinAlias</c> は結合済みの <see cref="MasterShohin"/> の別名。</para>
	/// </summary>
	public static string FinalJodaiSql(string shohinExpr, string taishoExpr, string tenpoExpr, string dayExpr, string shohinAlias = "sh")
		=> $"ifnull({ResolveSql(shohinExpr, taishoExpr, tenpoExpr, dayExpr)}, ifnull({shohinAlias}.TankaJodai,0))";

	/// <summary>
	/// SqlDepends: <b>倉庫軸</b>（在庫評価など）で適用上代を解決するスカラサブクエリ断片。該当行がなければ NULL を返す。
	/// <para>
	/// 在庫の <c>Id_Soko</c> は倉庫(TenType=0)のことも直営店(TenType=6)のこともあるため、
	/// 「その倉庫が直営店なら店頭価格、そうでなければ本部基準」を1本のSQLで表す。
	/// 優先順位は 店舗系の当該店舗 &gt; 本部売上系の全件 &gt; （呼び出し側で）マスタ定価。
	/// 倉庫の場合は店舗系の行が一致しないので自然に本部基準へ落ちる。
	/// </para>
	/// </summary>
	/// <param name="shohinExpr">商品Idの式</param>
	/// <param name="sokoExpr">倉庫Id／店舗Idの式</param>
	/// <param name="dayExpr">判定日(yyyyMMdd)の式</param>
	public static string ResolveSokoSql(string shohinExpr, string sokoExpr, string dayExpr) => @$"(
    SELECT dj.Jodai FROM {nameof(DerivedJodai)} dj
     WHERE dj.Id_Shohin = {shohinExpr}
       AND ((dj.TaishoType = {(int)EnumJodaiTaisho.Tenpo} AND dj.Id_Tenpo = {sokoExpr})
         OR (dj.TaishoType = {(int)EnumJodaiTaisho.Honbu} AND dj.Id_Tenpo = 0))
       AND {dayExpr} BETWEEN dj.DayFrom AND dj.DayTo
     ORDER BY (dj.TaishoType = {(int)EnumJodaiTaisho.Tenpo}) DESC, dj.Priority DESC, dj.Id DESC
     LIMIT 1)";

	/// <summary>
	/// SqlDepends: 倉庫軸での最終的な上代。適用上代がなければ商品マスタの上代を使う。
	/// </summary>
	public static string FinalJodaiSokoSql(string shohinExpr, string sokoExpr, string dayExpr, string shohinAlias = "sh")
		=> $"ifnull({ResolveSokoSql(shohinExpr, sokoExpr, dayExpr)}, ifnull({shohinAlias}.TankaJodai,0))";

	/// <summary>
	/// SqlDepends: 「今日」を yyyyMMdd で返すSQL式（判定日の既定値）
	/// </summary>
	public static string TodaySql => "strftime('%Y%m%d','now','localtime')";
}
