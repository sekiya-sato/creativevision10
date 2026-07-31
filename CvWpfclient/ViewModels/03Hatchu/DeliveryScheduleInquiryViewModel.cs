using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Diagnostics;

namespace CvWpfclient.ViewModels._03Hatchu;

/*
=== 未実装（仕様確定待ち） 2026-07-31 Phase 9 ===

納品予定照会は「納品予定日ごとの入荷予定」を画面で確認する照会画面だが、
cv10 のスキーマには納品予定日を保持する列が存在しないため実装を保留した。
帳票版の納品予定表（03Hatchu DeliveryScheduleTableViewModel / 04Juchu NouhinYoteiTableViewModel）と
まったく同じ理由。

調査結果:
  - Tran13Hachu(発注) / Tran12Jyuchu(受注) が持つ日付は DenDay のみ。
  - 明細 Tran99Meisai にも日付列は無い。
  - NouhinDay(納品日) を持つのは TranHhtData / TranHaibun / TranHojyu の3つだけで、
    いずれも発注・受注と紐付いていない。

実装するには以下のどれかを先に決める必要がある:
  (A) Tran13Hachu（またはその明細）へ納品予定日の列を追加する
  (B) 発注と配分(TranHaibun.NouhinDay)を紐付ける規約を決め、配分側の納品日を予定日として使う
      → TranHaibun.RelateNo1/Kubun の意味付け（Phase 12 の配分系実装）が前提
  (C) 発注日＋リードタイム（仕入先マスタ等）から算出する
      → リードタイムを保持する列も現状は無い

日付軸を持たない代替として、発注残の確認は実装済みの
「仕入未受リスト」(PendingShiireListView) と「発注残管理表」(HachuZanKanriTableView) で行える。
どちらも発注日で絞って未入荷数・経過日数を出せる。

なお、この画面を実装する際は照会画面の基底 Helpers.BaseQueryViewModel を継承すること
（同Phaseで実装した StockDifferenceQueryViewModel / ShohinHistoryQueryViewModel が参考になる）。
*/
public partial class DeliveryScheduleInquiryViewModel : Helpers.BaseViewModel {
}
