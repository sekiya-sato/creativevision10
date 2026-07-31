using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Diagnostics;

namespace CvWpfclient.ViewModels._04Juchu;

/*
=== 未実装（仕様確定待ち） 2026-07-31 Phase 5 ===

納品予定表は「受注に対する納品予定日ごとの出荷予定」を出す帳票だが、
cv10 のスキーマには納品予定日を保持する列が存在しないため実装を保留した。
03Hatchu の DeliveryScheduleTableViewModel と同じ理由。

調査結果:
  - Tran12Jyuchu(受注) が持つ日付は DenDay(受注日) のみ。TranAllHeader を含めても他に日付列は無い。
  - 明細 Tran99Meisai にも日付列は無い。
  - NouhinDay(納品日) を持つのは TranHhtData / TranHaibun / TranHojyu の3つだけで、
    いずれも受注とは紐付いていない。

実装するには以下のどれかを先に決める必要がある:
  (A) Tran12Jyuchu（またはその明細）へ納品予定日の列を追加する
      → CvBase のモデル追加 + UpdateDb マイグレーション + 受注入力画面での入力欄追加が必要
  (B) 受注と配分(TranHaibun.NouhinDay)を紐付ける規約を決め、配分側の納品日を予定日として使う
      → TranHaibun.RelateNo1/Kubun の意味付け（Phase 12 の配分系実装）が前提

日付軸を持たない代替は実装済み:
  - 受注残の伝票単位管理 → 「受注残管理表」(JuchuZanKanriTableView)
  - 得意先別の売上予定額 → 「得意先別売上予定表」(TokuiSakiUriageYoteiTableView)
    ※こちらも納品予定日を持てないため「受注日で絞った受注残の合計」として実装している
*/
public partial class NouhinYoteiTableViewModel : Helpers.BaseViewModel {
}
