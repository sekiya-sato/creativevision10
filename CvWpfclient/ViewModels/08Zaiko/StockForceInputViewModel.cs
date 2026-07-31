namespace CvWpfclient.ViewModels._08Zaiko;

/// <summary>
/// 在庫強制調整入力 — **仕様確定待ちのため未実装**（Phase 10 で意図的に保留）。
///
/// <para>【保留理由: 調整を保存する場所が無い】</para>
/// <list type="number">
///   <item>
///     `SummaryRealStock` は**導出テーブル**である。`CvDomainLogic/SummaryDb.CalcSummaryRealStock()` が
///     `DELETE FROM SummaryRealStock` の後に `SummaryStock` から作り直すため、
///     この画面から直接 `SummaryRealStock.Su` を書き換えても**次の在庫再計算で消える**。
///   </item>
///   <item>
///     `SummaryStock` も Tran テーブルからの集計結果（`CalcSummaryStockTrn`）で、
///     調整分だけを保持し続ける仕組みが無い。
///   </item>
///   <item>
///     `SummaryStock.AdjustQty`（調整数）という列は存在するが、
///     **リポジトリ内に書き込み側が1つも無い**（読むのは 倉庫別受払表 / 商品別受払表 / 全社受払表 の3帳票のみ）。
///     つまり「調整をどう記録するか」が未定のまま列だけある状態。
///   </item>
///   <item>
///     在庫を動かす他の伝票は必ず Tran テーブル + `TranCalcBase.GetCalcSoko()` のフラグ経由で計上される。
///     調整も同じ枠組みに乗せるのが筋だが、調整用の Tran テーブルが存在しない。
///   </item>
/// </list>
///
/// <para>【実装に必要な前提条件（どちらかを決める必要がある）】</para>
/// <list type="bullet">
///   <item>
///     案A: 調整用 Tran テーブル（例 Tran61Chosei）を新設し、`TranCalcBase.GetCalcSoko` に
///     `(1, 0, 0, 0)` 相当のフラグと、`CreateSummaryStockSql` の AdjustQty への加算を追加する。
///     監査が残り、在庫再計算でも消えない。**旧システムに「在庫強制調整実績表」があることから、
///     調整は伝票として残す設計だったと推測できる**（実績表を出すには元データが必要）。
///   </item>
///   <item>
///     案B: 既存の Tran05Ido / Tran03Shiire に調整用の区分を足して代用する。
///     テーブルは増えないが「移動でも仕入でもない在庫増減」が混ざるため集計側の条件分岐が増える。
///   </item>
/// </list>
///
/// <para>
/// どちらも CvBase のスキーマ変更 + CvDomainLogic の集計SQL変更を伴う（クライアントだけでは閉じない）。
/// 併せて「在庫強制調整実績表」も同じ元データを見る画面になるので、セットで設計する必要がある。
/// </para>
/// </summary>
public partial class StockForceInputViewModel : Helpers.BaseViewModel {
}
