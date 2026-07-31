using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Diagnostics;

namespace CvWpfclient.ViewModels._03Hatchu;

/*
=== 未実装（Phase 12 の配分系実装が前提） 2026-07-31 Phase 5 ===

発注配分リストは「発注に対する店舗別配分の結果」を一覧する帳票。
配分データ(TranHaibun)自体はテーブルが存在するが、発注と配分を結び付ける規約が未確定なため保留した。

調査結果:
  - TranHaibun は RelateNo1 / RelateNo2 を持つが、どちらに何の伝票Idを入れるかの定義が無い
    （列コメントも無く、他テーブルの RelateNo1 と違い用途が推測できない）。
  - TranHaibun.Kubun も配分種別（発注配分 / 受注配分 / 在庫配分 / 補充）を区別するはずだが、
    対応する enum が CvBase に存在せず、値の意味が決まっていない。
  - 配分入力画面(HachuHaibunInputView, JuchuHaibunInputView, ZaikoHinHaibunView など)は
    すべて未実装のため、書き込み側の規約もまだ存在しない。

実装順序としては Phase 12（配分系）で
  (1) TranHaibun.Kubun の値定義（enum 追加）
  (2) RelateNo1 に発注/受注伝票Idを入れる規約の確定
  (3) 配分確定フローの実装
を済ませてから、本帳票を実装するのが正しい。
Phase 12 の 12-1「配分確定ロジック確定」の成果を待つこと。

同じ理由で「配分出荷リスト」(04Juchu HaibunShukkaListView) も Phase 12 に置いている。
*/
public partial class HachuHaibunListViewModel : Helpers.BaseViewModel {
}
