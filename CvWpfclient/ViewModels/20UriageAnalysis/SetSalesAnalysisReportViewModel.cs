using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Diagnostics;

namespace CvWpfclient.ViewModels._20UriageAnalysis;

/*
=== 未実装（仕様確定待ち） 2026-07-31 Phase 6 ===

セット売上分析表は「セット商品（組み合わせ販売）の売れ方」を分析する帳票だが、
cv10 のスキーマにセットを定義するテーブル・列が存在しないため実装を保留した。

調査結果:
  - CvBase 全体に セット / Set / SetJan / IsSet / SetKubun に相当する列・テーブルは0件。
  - MasterShohin は Jan1 / Jan2 / Jan3 を持つが、これはJANコードの複数登録であって
    「どの商品がどのセットを構成するか」を表す構造ではない。
  - 旧クライアントには セットJANマスタ画面 (refer/.../v01Master/SubDlg01SetjanView) があり、
    旧システムにはセット定義マスタが存在したと考えられるが、cv10 へは移行されていない。
    ConvertDb 系にもセット関連の変換処理は無い。

実装するには以下を先に決める必要がある:
  (A) セットの定義方法
      - 専用マスタを作る（セットCD、構成商品、構成数量）→ CvBase にテーブル追加 + UpdateDb
      - あるいは MasterShohin の分類区分（例: アイテム区分の特定値）でセット商品を表す
  (B) 分析の切り口
      - 「セット商品自体の売上」を見るのか
      - 「セット構成品がバラで売れた分との対比」を見るのか
      - 「同一伝票内での併売（バスケット分析）」を見るのか
      呼び名が同じでも要求される集計はまったく別物になる。
  (C) (B) が併売分析だった場合はセット定義そのものが不要になる
      （同一伝票内の商品組み合わせを数えるだけなのでマスタ追加なしで実装できる）。
      つまり (B) の確認が最優先で、それ次第で (A) が要るかどうかが決まる。

旧システムのセットJANマスタの実データと、この帳票の旧出力サンプルがあれば方針が確定できる。
*/
public partial class SetSalesAnalysisReportViewModel : Helpers.BaseViewModel {
}
