using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Diagnostics;

namespace CvWpfclient.ViewModels._03Hatchu;

/*
=== 未実装（仕様確定待ち） 2026-07-31 Phase 5 ===

納品予定表は「発注に対する納品予定日ごとの入荷予定」を出す帳票だが、
cv10 のスキーマには納品予定日を保持する列が存在しないため実装を保留した。

調査結果:
  - Tran13Hachu(発注) が持つ日付は DenDay(発注日) のみ。TranAllHeader を含めても他に日付列は無い。
  - 明細 Tran99Meisai にも日付列は無い。
  - NouhinDay(納品日) を持つのは TranHhtData / TranHaibun / TranHojyu の3つだけで、
    いずれも発注とは紐付いていない。

実装するには以下のどれかを先に決める必要がある:
  (A) Tran13Hachu（またはその明細）へ納品予定日の列を追加する
      → CvBase のモデル追加 + UpdateDb マイグレーション + 発注入力画面での入力欄追加が必要
  (B) 発注と配分(TranHaibun.NouhinDay)を紐付ける規約を決め、配分側の納品日を予定日として使う
      → TranHaibun.RelateNo1/Kubun の意味付け（Phase 12 の配分系実装）が前提
  (C) 「納品予定日」を持たず、発注日＋リードタイム（仕入先マスタ等）から算出する
      → リードタイムを保持する列も現状は無い

日付軸を持たない代替として、発注残の一覧は既に実装済みの
「仕入未受リスト」(PendingShiireListView) と「発注残管理表」(HachuZanKanriTableView) で確認できる。
*/
public partial class DeliveryScheduleTableViewModel : Helpers.BaseViewModel {
}
