namespace CvWpfclient.ViewModels._06Uriage;

/// <summary>
/// POS日別精算入力 — **仕様確定待ちのため未実装**（Phase 10 で意図的に保留）。
///
/// <para>【保留理由: 精算結果を保存する場所が無い】</para>
/// <list type="number">
///   <item>
///     日別精算は「店舗×営業日」単位の1レコード（現金実査額・釣銭準備金・過不足・締め時刻・担当者など）だが、
///     それに対応するテーブルがスキーマに無い。
///   </item>
///   <item>
///     計画書は Tran01Tenuri を主テーブルとしていたが、Tran01Tenuri は**1明細=売上伝票**であり、
///     `TranCalcBase.GetCalcSoko(nameof(Tran01Tenuri))` は `(-1, 0, 1, 0)`。
///     つまり精算レコードを Tran01Tenuri として登録すると**架空の売上が立ち在庫まで減る**。流用できない。
///   </item>
///   <item>
///     金種内訳を持つ `Tran01Tenuri.JposPayment`(PosPaymentDetail: CashAmount/CardAmount/OtherAmount/ChangeAmount)
///     は伝票単位のPOS決済内訳であり、日別の実査額とは粒度が違う。
///   </item>
///   <item>
///     旧システム(refer/cvnetclient)でも「精算入力」「精算レポート照会」はどちらも `WindowId=""`（未実装）で、
///     参照できる画面仕様が残っていない。
///   </item>
/// </list>
///
/// <para>【実装に必要な前提条件】</para>
/// <list type="bullet">
///   <item>精算テーブルの新設（例 Tran02Seisan: Id_Tenpo, DenDay, 金種別実査額, 釣銭準備金, 過不足, Id_Shain, 締め時刻）。</item>
///   <item>過不足の算定基準の確定（POS売上合計と突き合わせるのか、レジ内現金の理論残と突き合わせるのか）。</item>
///   <item>「精算レポート照会」と同じ元データを見る画面になるのでセットで設計すること。</item>
/// </list>
///
/// <para>
/// なお「POS決済内訳と売上金額の差額を確認する」だけであれば、
/// 売上金種Viewer(<see cref="UriageCashTypeReportViewModel"/>, Phase 9 実装) が既にその役割を担っている。
/// 本画面が必要なのは**実査額を人が入力して保存する**部分であり、そこが上記の未確定点にあたる。
/// </para>
/// </summary>
public partial class PosDailySeisanInputViewModel : Helpers.BaseViewModel {
}
