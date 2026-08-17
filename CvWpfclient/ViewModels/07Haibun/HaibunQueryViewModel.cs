using CodeShare;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CvAsset;
using CvBase;
using System.Diagnostics;

namespace CvWpfclient.ViewModels._07Haibun;

/*
=== 未実装（2026-08-17 仕様確定 / 実装待ち） ===

配分問合わせ（出庫側）。旧CV.net【配分】-【配分問合わせ】に相当する。
仕様は Doc/spec/2026-08-17_旧cvnet比較_仕様決定判断材料.md 5.1.0 / I9 を参照する。

画面仕様（旧マニュアル13-配分 の9）:
  - 検索条件: 店舗CD / 納品日 / 指示日 / 営業担当 / ブランド / アイテム / 大分類 / 品番 を FROM-TO
  - 表示条件: 原価FLGを表示 / 在庫0を表示
  - 一覧: 商品CD、上代、ブランド、アイテム、引当計（総引当商品数計）
  - 商品CDをダブルクリックすると在庫展開タブを開き、倉庫・得意先を横軸、色サイズを縦軸に配分数を展開する
  - 横計セルのダブルクリックで倉庫別受払表、SKUセルのダブルクリックで品番別受払表へ遷移
  - CSV出力（ファイル名は 商品CD_配分数.CSV）

引当数の定義は SummaryDb.ReserveTargetWhere / ReserveQtySumExpr に集約されている。
  EndFlag=0 かつ Kubun<>0 の行を対象に、未確定は Su、確定済み(KakuteiDay有効)は JitsuSu を積む。
*/
public partial class HaibunQueryViewModel : Helpers.BaseViewModel {
}
