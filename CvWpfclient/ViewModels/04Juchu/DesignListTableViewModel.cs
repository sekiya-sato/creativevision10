using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Diagnostics;

namespace CvWpfclient.ViewModels._04Juchu;

/*
=== 未実装（仕様確定待ち） 2026-07-31 Phase 5 ===

絵型一覧表は商品の絵型（デザイン画・商品画像）を並べたカタログ帳票だが、
cv10 のスキーマに画像を保持する列・ファイルパスを保持する列が一切存在しないため保留した。

調査結果:
  - CvBase 全体を検索して image / 画像 / 絵型 / picture / photo に相当する列は0件。
  - MasterShohin にも画像パス・画像ファイル名・BLOB 列は無い。
  - qfm 側は image 要素で「itemN からファイルパスを受け取る」ことは可能
    （MasterPrintBarcode 系で実績あり。サーバから参照できる配置が必要）。
    つまり出力側の手段はあるが、渡すべきパスの供給元が無い。

実装するには以下を先に決める必要がある:
  (A) 画像の保管方法（サーバ上のファイル共有 / DBのBLOB / 外部URL）
  (B) 商品と画像の対応付け（MasterShohin へパス列を追加するか、命名規則で品番から導くか）
  (C) 画像が無い商品の扱い（代替画像 / 空欄 / 行を出さない）
  (D) 1ページあたりの面数と用紙（カタログ体裁）

(B) を「品番＝ファイル名」の命名規則で解決する場合はスキーマ変更なしで実装できるが、
その規則自体がユーザー環境の運用に依存するため、確認なしに決めるべきではない。

展示会向けの近い帳票としてスワッチ系（TenjiSwatchView / SwatchDataCreateView /
SwatchDataBulkCreateView・Phase 13）があり、そちらも同じ画像の所在問題を抱える見込み。
まとめて仕様を確認するのが効率的。
*/
public partial class DesignListTableViewModel : Helpers.BaseViewModel {
}
