using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Diagnostics;

namespace CvWpfclient.ViewModels._07Haibun;

/*
=== 未実装（2026-08-17 仕様確定 / 実装待ち） ===

有効在庫問合わせ。旧CV.net【配分】-【有効在庫問合わせ】に相当する。
仕様は Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md 5.1.0 / I9 を参照する。

画面仕様（旧マニュアル13-配分 の11）:
  - 検索条件: ブランド / アイテム / 大分類 / 品番 / 納品日 / 指示日 / 営業担当 を FROM-TO
  - 一覧: 商品CD、上代、ブランド、アイテム、有在(有効在庫数)、引当(総引当商品数計)、在庫(総在庫数)
  - 商品CDをダブルクリックで引当数照会（倉庫×色サイズの展開）

有効在庫 = SummaryRealStock.Su - SummaryRealStock.ReserveQty。
旧は「受注済みで出荷が完了していない数量」も差し引いていたが、CV10 は受注残を引かない
（2026-08-17 決定 I1-y）。引当の源泉は TranHaibun だけである。
*/
public partial class YukoZaikoQueryViewModel : Helpers.BaseViewModel {
}
